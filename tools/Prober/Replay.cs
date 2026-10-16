using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// Replays the written probes with no model in the loop. Per commit it runs only the probes whose identity or
/// footprint changed; <c>--all</c> runs every probe, retired ones included (the release run). Each probe runs
/// once per process variant, and the replay records each probe in registry order whatever order the runs
/// finished in: per-row evidence (rows expected, rows executed, each row's outcome and applied settings), the
/// result, and the fresh footprint. <c>--no-footprint</c> runs without coverage and records no footprint, for a
/// state nobody keeps (a pull request's); only then do the variants of every selected probe run
/// <c>--parallel N</c> at a time (the machine's cores by default), since the coverage collector rewrites the
/// assemblies on disk and two hosts from one output directory corrupt each other (KB-025).
/// <c>--family F</c> replays the probes whose claim's first surface the
/// replay configuration's <c>families</c> puts under F, so a matrix of jobs can split the corpus and
/// <c>merge</c> join their states; it refuses while any probe's surface is in no family, so a matrix cannot
/// quietly leave a probe out. It works in one warm worktree kept between runs and moved to HEAD without force,
/// so only what changed is rebuilt. A dependency input that changed stales every probe, and the axes are
/// derived again. Only one round runs at a time.
/// </summary>
internal static class Replay
{
    /// <summary>How this replay was asked to run, recorded in replay-last.json for <c>merge</c> to read.</summary>
    internal sealed record Options(bool All, string? Family, int Parallel, bool Footprint);

    public static int Run(Context ctx, Args a, IProbeAdapter? adapter = null)
    {
        var ledger = new Ledger(ctx, a.Get("branch"));
        var config = ledger.AxesBlock()["replay"] as JsonObject ?? throw new Refusal("the axes file's block has no \"replay\" configuration");
        adapter ??= new DotNetAdapter(config);
        var home = Home(ctx, ledger);
        if (a.Flag("where"))
        {
            ctx.Out.WriteLine($"replay:  {Path.Combine(home, "wt")}\nexplore: {Path.Combine(home, "explore")}\nseeds:   {Path.Combine(home, "seeds")}");
            ctx.Out.WriteLine($"remove:  {RemoveCommand(home)}");
            return 0;
        }

        // With a footprint the coverage collector instruments the assemblies on disk, so two hosts from one
        // output directory corrupt each other (KB-025): one at a time, whatever --parallel says.
        var footprint = !a.Flag("no-footprint");
        var asked = Parallelism(a.Get("parallel"));
        var options = new Options(a.Flag("all"), a.Get("family"), footprint ? 1 : asked, footprint);
        var states = ledger.ProbeStates(options.All);
        var scope = options.Family is null ? states : OfFamily(config, ledger, states, options.Family);
        var todo = options.All ? scope : [.. scope.Where(s => s.Replay)];
        var skipped = scope.Where(s => !todo.Contains(s)).Select(s => s.Probe).ToList();
        var head = ledger.Head;
        ctx.Out.WriteLine($"prober replay: {ledger.Branch} at {head[..7]}: {todo.Count} of {scope.Count} probes to replay" +
                          $"{(options.Family is null ? "" : $" (family {options.Family})")}, " +
                          (footprint ? "one at a time (the coverage collector rewrites the assemblies on disk)" : $"{options.Parallel} at a time, no footprint"));
        if (a.Flag("dry-run")) { foreach (var s in todo) ctx.Out.WriteLine($"  {s.Probe}"); return 0; }

        Directory.CreateDirectory(home);
        var holder = a.Get("holder") ?? $"replay-{System.Environment.ProcessId}";
        // Under a round's lock (--holder) the replay takes nothing: re-locking would tie the round's liveness to
        // this shorter-lived process. On its own it locks with its own process as the owner.
        if (a.Get("holder") is null && !RoundLock.Take(ledger, holder, "replay", System.Environment.ProcessId).Taken)
        {
            RoundLock.OweReplay(ledger);
            ctx.Out.WriteLine($"prober replay: skipped, the ledger is locked by another round; its unlock will say a replay is owed");
            return 0;
        }
        try
        {
            using var beat = new RoundLock.Heartbeat(ledger, holder);
            using var held = HoldWorktree(home);
            if (held is null)
            {
                ctx.Out.WriteLine($"prober replay: skipped, another branch's replay in this checkout is using {home}; run `prober replay` when it ends");
                return 0;
            }
            var axes = Rederive(ledger, adapter, config, Path.Combine(home, "enumerate"), force: options.All);
            if (axes is not null)
            {
                if (axes["error"] is { } error) ctx.Out.WriteLine($"  axes: {Files.Str(error)}");
                else
                {
                    ctx.Out.WriteLine($"  axes: re-derived ({(axes["added"] as JsonArray)!.Count} added, {(axes["removed"] as JsonArray)!.Count} removed)");
                    foreach (var x in Files.Strings(axes["added"])) ctx.Out.WriteLine($"    + {x}");
                    foreach (var x in Files.Strings(axes["removed"])) ctx.Out.WriteLine($"    - {x}");
                }
            }
            if (todo.Count == 0)
            {
                Summary(ledger, head, warmth: null, buildSeconds: 0, todo, skipped, failed: [], seconds: 0, axes, options);
                return 0;
            }
            return Probes(ctx, ledger, adapter, config, home, Path.GetFullPath(a.Get("worktree") ?? Path.Combine(home, "wt")), head, todo, skipped, axes, options);
        }
        finally
        {
            if (a.Get("holder") is null) RoundLock.Release(ledger, holder);
        }
    }

    /// <summary>How many probe processes run at once: <c>--parallel N</c>, else one per core.</summary>
    internal static int Parallelism(string? given)
    {
        if (given is null) return Math.Max(1, System.Environment.ProcessorCount);
        if (!int.TryParse(given, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1) throw new Refusal("--parallel takes a whole number of 1 or more", 2);
        return n;
    }

    /// <summary>
    /// The probes of one family: those whose claim's first surface the replay configuration's <c>families</c>
    /// (name to surfaces) puts under it. Every probe of the registry must have a family, or a matrix of jobs by
    /// family would never run the one that has none; so this refuses for the whole registry, not only the family
    /// asked for.
    /// </summary>
    internal static List<Ledger.ProbeState> OfFamily(JsonObject config, Ledger ledger, List<Ledger.ProbeState> states, string family)
    {
        var families = config["families"] as JsonObject ?? throw new Refusal("the replay configuration names no families; add \"families\" (a name to the surfaces it holds) to the axes file's replay block", 2);
        if (families[family] is not JsonArray) throw new Refusal($"no family {family}; the replay configuration has {string.Join(", ", families.Select(kv => kv.Key))}", 2);
        var bySurface = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, surfaces) in families)
            foreach (var surface in Files.Strings(surfaces))
            {
                if (bySurface.TryGetValue(surface, out var other) && other != name) throw new Refusal($"the surface {surface} is in two families, {other} and {name}", 2);
                bySurface[surface] = name;
            }
        var firstSurface = ledger.Claims().ToDictionary(c => Files.Str(c["id"]), c => Files.Strings(c["surfaces"]).FirstOrDefault() ?? "", StringComparer.Ordinal);
        var result = new List<Ledger.ProbeState>();
        foreach (var s in states)
        {
            var surface = firstSurface.GetValueOrDefault(s.Claim) ?? "";
            if (!bySurface.TryGetValue(surface, out var f))
                throw new Refusal($"{s.Probe}: the first surface of its claim {s.Claim} ({(surface.Length > 0 ? surface : "none")}) is in no family of the replay configuration, so a replay by family would never run it");
            if (f == family) result.Add(s);
        }
        return result;
    }

    /// <summary>Derives the generated axes again when a dependency input or the toolchain changed, or always when forced.</summary>
    private static JsonObject? Rederive(Ledger ledger, IProbeAdapter adapter, JsonObject config, string work, bool force)
    {
        if (config["enumerate"] is not JsonObject enumerate) return null;
        var stampInput = Files.Canonical(new JsonArray(
            new JsonArray([.. Files.Strings(enumerate["inputs"]).Select(p => (JsonNode?)(Git.ObjectId(ledger.Top, p) ?? ""))]),
            ledger.Toolchain().DeepClone()));
        var stamp = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stampInput)));
        var path = Path.Combine(ledger.Memory, "axes-generated.json");
        var old = Files.ReadJson(path) as JsonObject ?? [];
        if (!force && Files.Str(old["inputsStamp"]) == stamp) return null;
        var (generated, error) = adapter.DeriveAxes(ledger.Top, work);
        if (generated is null) return new JsonObject { ["error"] = error };
        generated["inputsStamp"] = stamp;
        static SortedSet<string> Settings(JsonObject o) =>
            new((o["axes"] as JsonObject ?? []).SelectMany(kv => Files.Strings(kv.Value).Select(v => $"{kv.Key}={v}")).Concat(Files.Strings(o["needsValues"])), StringComparer.Ordinal);
        var before = Settings(old); var after = Settings(generated);
        Files.WriteJson(path, generated);
        return new JsonObject
        {
            ["added"] = new JsonArray([.. after.Except(before, StringComparer.Ordinal).Select(x => (JsonNode)x)]),
            ["removed"] = new JsonArray([.. before.Except(after, StringComparer.Ordinal).Select(x => (JsonNode)x)]),
            ["versions"] = generated["versions"]?.DeepClone(),
        };
    }

    /// <summary>One process variant of a probe, run: what it was asked, what it answered, and where its rows, coverage and logs went.</summary>
    private sealed record VariantRun(JsonObject Variant, int Index, Ran Ran, double Seconds, string OutDir, string Coverage, string LogDir);

    /// <summary>
    /// Starts every variant of a probe, each on a thread of its own that waits its turn at the gate, so the
    /// variants of all selected probes share the <c>--parallel</c> slots in the order they were started.
    /// </summary>
    private static List<Task<VariantRun>> Start(IProbeAdapter adapter, string worktree, string results, IReadOnlyDictionary<string, string?> environment,
                                                Ledger.ProbeState s, SemaphoreSlim gate, bool coverage)
    {
        var pending = new List<Task<VariantRun>>();
        var index = 0;
        foreach (var variant in s.Variants.OfType<JsonObject>())
        {
            var i = index++;
            var coverageFile = coverage ? $"{s.Probe}-{i}.cobertura.xml" : "";
            var outDir = Path.Combine(results, $"{s.Probe}-{i}-rows");
            // PROBE_LOG_DIR: where the run may leave its full logs; a failed run's are kept in the state.
            var logDir = Path.Combine(results, $"{s.Probe}-{i}-log");
            var env = new Dictionary<string, string?>(environment, StringComparer.Ordinal) { ["PROBE_OUT"] = outDir, ["PROBE_LOG_DIR"] = logDir };
            foreach (var (k, v) in variant) env[k] = Files.Str(v);
            pending.Add(Task.Factory.StartNew(() =>
            {
                gate.Wait();
                try
                {
                    var clock = Stopwatch.StartNew();
                    var ran = adapter.RunProbe(worktree, s.Filter, results, coverageFile, env);
                    return new VariantRun(variant, i, ran, clock.Elapsed.TotalSeconds, outDir, coverageFile, logDir);
                }
                finally { gate.Release(); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        }
        return pending;
    }

    private static int Probes(Context ctx, Ledger ledger, IProbeAdapter adapter, JsonObject config, string home, string worktree, string head,
                              List<Ledger.ProbeState> todo, List<string> skipped, JsonObject? axes, Options options)
    {
        var clock = Stopwatch.StartNew();
        string warmth;
        if (!Directory.Exists(worktree))
        {
            var added = Git.Run(ledger.Root, "worktree", "add", "--detach", worktree, head);
            if (added.ExitCode != 0) { ctx.Error.WriteLine(added.Stderr); return 2; }
            warmth = "cold";
        }
        else
        {
            // No force: files the commits did not change keep their mtimes, so the build stays incremental.
            if (Git.Run(worktree, "checkout", "--detach", head).ExitCode != 0) Git.Run(worktree, "reset", "--hard", head);
            warmth = "warm";
        }

        var probeDir = Overlay(ledger, config, worktree);

        var environment = new Dictionary<string, string?>(ctx.Environment, StringComparer.Ordinal);
        foreach (var (k, v) in adapter.Environment(home)) environment[k] = v;
        var results = Directory.CreateTempSubdirectory("prober-replay-").FullName;

        var buildClock = Stopwatch.StartNew();
        var build = adapter.Build(worktree, environment);
        var buildSeconds = buildClock.Elapsed.TotalSeconds;
        ctx.Out.WriteLine($"  build ({warmth}): {buildSeconds.ToString("F1", CultureInfo.InvariantCulture)} s, {FirstLine(build)}");
        if (build.ExitCode != 0) { ctx.Out.WriteLine(build.Stdout); return 2; }

        // Every variant of every probe is started now and runs when a slot frees; the probes are then read,
        // reported and recorded one by one in registry order, each once all its variants have ended.
        using var gate = new SemaphoreSlim(options.Parallel);
        var started = todo.Select(s => Start(adapter, worktree, results, environment, s, gate, options.Footprint)).ToList();

        var failed = new List<string>();
        foreach (var (s, pending) in todo.Zip(started))
        {
            var variants = pending.Select(t => t.GetAwaiter().GetResult()).OrderBy(v => v.Index).ToList();
            var claimRows = Files.ReadRows(Path.Combine(probeDir, "rows", s.Claim + ".json"));
            var evidence = new JsonObject { ["variants"] = new JsonArray(), ["rows"] = new JsonObject() };
            var result = "pass"; var seconds = 0.0; var reports = new List<string>();
            foreach (var v in variants)
            {
                seconds += v.Seconds;
                var label = v.Variant.Count > 0 ? " [" + string.Join(", ", v.Variant.Select(kv => $"{kv.Key}={Files.Str(kv.Value)}")) + "]" : "";
                ctx.Out.WriteLine($"  {s.Probe}{label}: {FirstLine(v.Ran)} ({v.Seconds.ToString("F1", CultureInfo.InvariantCulture)} s)");
                if (v.Ran.ExitCode != 0) { result = "fail"; ctx.Out.WriteLine(string.Join('\n', v.Ran.Stdout.Split('\n').Skip(1).Take(11))); }
                // A failed run's full logs, kept under the state's logs/ for triage: in CI the state is the artifact.
                if (v.Ran.ExitCode != 0 && Directory.Exists(v.LogDir))
                    foreach (var file in Directory.EnumerateFiles(v.LogDir))
                    {
                        var kept = ledger.P(Path.Combine("logs", $"{s.Probe}-{v.Index}-{Path.GetFileName(file)}"));
                        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                        File.Copy(file, kept, overwrite: true);
                        ctx.Out.WriteLine($"    log: {kept}");
                    }
                if (v.Coverage.Length > 0) reports.AddRange(Directory.EnumerateFiles(results, v.Coverage, SearchOption.AllDirectories));

                // Execution evidence: the rows this variant should run, the rows it did, how each ended and the
                // settings that applied. A row missing here, or recorded without its settings, earns nothing.
                var only = Files.Str(v.Variant["PROBE_ONLY"]);
                var expected = new JsonArray(); var executed = new SortedSet<int>();
                for (var i = 0; i < claimRows.Count; i++)
                    if (Selected(Files.StringMap(claimRows[i]?["row"] ?? claimRows[i]), only)) expected.Add(i);
                if (Directory.Exists(v.OutDir))
                    foreach (var file in Directory.EnumerateFiles(v.OutDir, "*.jsonl"))
                        foreach (var line in Files.ReadLines(file))
                        {
                            var idx = (int)Files.Num(line["idx"]);
                            executed.Add(idx);
                            (evidence["rows"] as JsonObject)![idx.ToString(CultureInfo.InvariantCulture)] = new JsonObject
                            {
                                ["category"] = Files.Str(line["category"], "violation"), ["applied"] = line["applied"]?.DeepClone() ?? new JsonObject(),
                                ["reasons"] = new JsonArray([.. Files.Strings(line["reasons"]).Take(6).Select(x => (JsonNode)x)]),
                                ["findings"] = new JsonArray([.. Files.Strings(line["findings"]).Select(x => (JsonNode)x)]),
                            };
                        }
                (evidence["variants"] as JsonArray)!.Add(new JsonObject
                {
                    ["env"] = v.Variant.DeepClone(), ["expected"] = expected, ["executed"] = new JsonArray([.. executed.Select(x => (JsonNode)x)]),
                });
                var missing = expected.Select(x => (int)Files.Num(x)).Where(x => !executed.Contains(x)).ToList();
                if (missing.Count > 0) ctx.Out.WriteLine($"    {missing.Count} expected row(s) did not run: [{string.Join(", ", missing.Take(10))}]");
            }
            foreach (var stale in ledger.StaleSkips(evidence)) { result = "fail"; ctx.Out.WriteLine($"    {stale}"); }
            if (result == "fail") failed.Add(s.Probe);
            var evidencePath = Path.Combine(results, $"{s.Probe}-evidence.json");
            Files.WriteJson(evidencePath, evidence);
            var categories = (evidence["rows"] as JsonObject)!.GroupBy(kv => Files.Str(kv.Value!["category"]), StringComparer.Ordinal)
                                                               .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}");
            ctx.Out.WriteLine("    outcomes: " + string.Join(", ", categories));
            var record = new StringWriter();
            Program.Run(new Context(ledger.Top, ctx.Environment, record, ctx.Error),
                ["record-run", "--branch", ledger.Branch, "--probe", s.Probe, "--result", result,
                 "--seconds", seconds.ToString("F1", CultureInfo.InvariantCulture), "--evidence", evidencePath]);
            if (record.ToString().Trim() is { Length: > 0 } said) ctx.Out.WriteLine("    " + said);
            // The footprint is what ran, pass or fail: a probe failing for an open finding is skipped like any
            // other until its footprint changes (a fix touches it). Without one the probe stays stale, which is
            // right for a state nobody keeps.
            if (!options.Footprint) ctx.Out.WriteLine("    footprint: not recorded (--no-footprint)");
            else if (reports.Count > 0)
            {
                var fp = new StringWriter(); var fpError = new StringWriter();
                Program.Run(new Context(ledger.Top, ctx.Environment, fp, fpError),
                    ["footprint", "--branch", ledger.Branch, "--probe", s.Probe, "--root", worktree, "--cobertura", .. reports,
                     "--include", .. (Files.Strings(config["include"]).ToArray() is { Length: > 0 } inc ? inc : ["src"]),
                     "--compile-time", .. Files.Strings(config["compileTimeFootprint"])]);
                ctx.Out.WriteLine($"    footprint: {(fp.ToString() + fpError.ToString()).Trim()}");
            }
            else ctx.Error.WriteLine("    footprint: no coverage report found; the probe stays stale");
        }

        Summary(ledger, head, warmth, buildSeconds, todo, skipped, failed, clock.Elapsed.TotalSeconds, axes, options);
        ctx.Out.WriteLine($"prober replay: {todo.Count - failed.Count} passed, {failed.Count} failed, {clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s in all");
        try { Directory.Delete(results, recursive: true); } catch (IOException) { }
        return failed.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// replay-last.json: what this replay did, and how it was asked to run. <c>selected</c> and <c>skipped</c>
    /// together are the probes in its scope (its family, or all), so <c>merge</c> can tell a probe no leg saw.
    /// </summary>
    private static void Summary(Ledger ledger, string head, string? warmth, double buildSeconds, List<Ledger.ProbeState> todo, List<string> skipped,
                                List<string> failed, double seconds, JsonObject? axes, Options options)
    {
        static JsonArray Names(IEnumerable<string> names) => new([.. names.Select(x => (JsonNode)x)]);
        Files.WriteJson(ledger.P("replay-last.json"), new JsonObject
        {
            ["branch"] = ledger.Branch, ["commit"] = head, ["build"] = warmth, ["build_seconds"] = Math.Round(buildSeconds, 1),
            ["replayed"] = todo.Count, ["failed"] = Names(failed), ["seconds"] = Math.Round(seconds, 1), ["axes"] = axes?.DeepClone(),
            ["all"] = options.All, ["family"] = options.Family, ["parallel"] = options.Parallel, ["footprint"] = options.Footprint,
            ["selected"] = Names(todo.Select(s => s.Probe)), ["skipped"] = Names(skipped),
        });
    }

    /// <summary>
    /// The cache directory of this checkout's scratch worktrees (<c>wt</c>, <c>seeds</c>, <c>explore</c>, <c>enumerate</c>) under
    /// PROBE_CACHE_DIR, else ~/.cache/probe-replay: the checkout's directory name and a hash of its path. A linked worktree
    /// has one of its own, so sessions in two checkouts never reset each other's copy; the main checkout is keyed on
    /// <see cref="Ledger.Root"/>, as it was before, so its warm copy stays where it is.
    /// </summary>
    internal static string Home(Context ctx, Ledger ledger)
    {
        var checkout = ledger.Linked ? ledger.Top : ledger.Root;
        var key = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(checkout)))[..8];
        return Path.Combine(ctx.Env("PROBE_CACHE_DIR") ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".cache", "probe-replay"),
                            $"{Path.GetFileName(checkout)}-{key}");
    }

    /// <summary>
    /// The command that removes this checkout's cache, its worktrees first (for <c>git worktree list</c>) and then the
    /// directory, every path literal: removing a linked worktree leaves its cache behind.
    /// </summary>
    internal static string RemoveCommand(string home)
    {
        // Forward slashes on Windows, which git and the sh of Git for Windows both read.
        static string Quoted(string path) => "'" + (Path.DirectorySeparatorChar == '\\' ? path.Replace('\\', '/') : path).Replace("'", "'\\''") + "'";
        var worktrees = new[] { "wt", "seeds", "explore" }.Select(w => Path.Combine(home, w)).Where(w => File.Exists(Path.Combine(w, ".git")));
        return string.Join("; ", worktrees.Select(w => $"git worktree remove --force {Quoted(w)}").Append($"rm -rf {Quoted(home)}"));
    }

    /// <summary>
    /// Holds this checkout's replay worktree for one replay: the lock is per branch, the worktree per checkout, so two
    /// branches' replays in one checkout (a branch switched while the post-commit replay runs) would otherwise share it.
    /// An OS lock on <c>replay.lock</c> beside it, released by the OS when its process ends; null when another holds it.
    /// </summary>
    internal static FileStream? HoldWorktree(string home)
    {
        Directory.CreateDirectory(home);
        try { return new FileStream(Path.Combine(home, "replay.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    /// <summary>Copies the corpus into the worktree's probe directory: tracked, overlaid by the branch's unpublished staging; rows unpacked.</summary>
    internal static string Overlay(Ledger ledger, JsonObject config, string worktree)
    {
        var probeDir = Path.Combine(worktree, Files.Str(config["probeDir"]));
        Directory.CreateDirectory(Path.Combine(probeDir, "rows"));
        var wanted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in new[] { ledger.CorpusDir, ledger.StagingDir })
        {
            foreach (var sub in new[] { "harness", "probes" })
                if (Directory.Exists(Path.Combine(source, sub)))
                    foreach (var f in Directory.EnumerateFiles(Path.Combine(source, sub), "*.cs")) wanted[Path.GetFileName(f)] = f;
            if (Directory.Exists(Path.Combine(source, "rows")))
                foreach (var f in Directory.EnumerateFiles(Path.Combine(source, "rows")).Where(f => f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".json.gz", StringComparison.Ordinal)))
                    rows[Path.GetFileName(f).Replace(".gz", "", StringComparison.Ordinal)] = f;
        }
        foreach (var existing in Directory.EnumerateFiles(probeDir)) if (!wanted.ContainsKey(Path.GetFileName(existing))) File.Delete(existing);
        foreach (var (name, src) in wanted)
        {
            var dst = Path.Combine(probeDir, name);
            if (!File.Exists(dst) || !File.ReadAllBytes(dst).AsSpan().SequenceEqual(File.ReadAllBytes(src))) File.Copy(src, dst, overwrite: true);
        }
        foreach (var (name, src) in rows)
        {
            var data = src.EndsWith(".gz", StringComparison.Ordinal) ? Files.Gunzip(src) : File.ReadAllBytes(src);
            var dst = Path.Combine(probeDir, "rows", name);
            if (!File.Exists(dst) || !File.ReadAllBytes(dst).AsSpan().SequenceEqual(data)) File.WriteAllBytes(dst, data);
        }
        return probeDir;
    }

    /// <summary>The harness's PROBE_ONLY filter, replicated: the rows a variant is expected to run.</summary>
    internal static bool Selected(IReadOnlyDictionary<string, string> row, string only)
    {
        if (only.Length == 0) return true;
        foreach (var condition in only.Split(','))
        {
            var neq = condition.IndexOf("!=", StringComparison.Ordinal);
            if (neq >= 0) { if (row.TryGetValue(condition[..neq], out var v) && v == condition[(neq + 2)..]) return false; }
            else
            {
                var eq = condition.IndexOf('=');
                if (!row.TryGetValue(condition[..eq], out var v) || v != condition[(eq + 1)..]) return false;
            }
        }
        return true;
    }

    private static string FirstLine(Ran ran) => ran.Stdout.Split('\n')[0].Trim() is { Length: > 0 } line ? line : $"exit {ran.ExitCode}";
}
