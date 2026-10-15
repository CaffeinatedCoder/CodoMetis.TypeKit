using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// Replays the written probes with no model in the loop. Per commit it runs only the probes whose identity or
/// footprint changed; <c>--all</c> runs every probe, retired ones included (the release run). Each runs alone,
/// once per process variant, with coverage; the replay records per-row evidence (rows expected, rows executed,
/// each row's outcome and applied settings), the result, and the fresh footprint. It works in one warm
/// worktree kept between runs and moved to HEAD without force, so only what changed is rebuilt. A dependency
/// input that changed stales every probe, and the axes are derived again. Only one round runs at a time.
/// </summary>
internal static class Replay
{
    public static int Run(Context ctx, Args a)
    {
        var ledger = new Ledger(ctx, a.Get("branch"));
        var config = ledger.AxesBlock()["replay"] as JsonObject ?? throw new Refusal("the axes file's block has no \"replay\" configuration");
        IProbeAdapter adapter = new DotNetAdapter(config);
        var key = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(ledger.Root)))[..8];
        var home = Path.Combine(ctx.Env("PROBE_CACHE_DIR") ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".cache", "probe-replay"),
                                $"{Path.GetFileName(ledger.Root)}-{key}");
        if (a.Flag("where")) { ctx.Out.WriteLine($"replay:  {Path.Combine(home, "wt")}\nexplore: {Path.Combine(home, "explore")}"); return 0; }

        var states = ledger.ProbeStates(a.Flag("all"));
        var todo = a.Flag("all") ? states : [.. states.Where(s => s.Replay)];
        var head = ledger.Head;
        ctx.Out.WriteLine($"prober replay: {ledger.Branch} at {head[..7]}: {todo.Count} of {states.Count} probes to replay");
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
            var axes = Rederive(ledger, adapter, config, Path.Combine(home, "enumerate"), force: a.Flag("all"));
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
                Files.WriteJson(ledger.P("replay-last.json"), new JsonObject { ["branch"] = ledger.Branch, ["commit"] = head, ["replayed"] = 0, ["failed"] = new JsonArray(), ["axes"] = axes?.DeepClone() });
                return 0;
            }
            return Probes(ctx, ledger, adapter, config, home, Path.GetFullPath(a.Get("worktree") ?? Path.Combine(home, "wt")), head, todo, axes);
        }
        finally
        {
            if (a.Get("holder") is null) RoundLock.Release(ledger, holder);
        }
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

    private static int Probes(Context ctx, Ledger ledger, IProbeAdapter adapter, JsonObject config, string home, string worktree, string head,
                              List<Ledger.ProbeState> todo, JsonObject? axes)
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

        // The corpus: tracked, overlaid by the branch's unpublished staging; rows unpacked.
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

        var environment = new Dictionary<string, string?>(ctx.Environment, StringComparer.Ordinal);
        foreach (var (k, v) in adapter.Environment(home)) environment[k] = v;
        var results = Directory.CreateTempSubdirectory("prober-replay-").FullName;

        var buildClock = Stopwatch.StartNew();
        var build = adapter.Build(worktree, environment);
        var buildSeconds = buildClock.Elapsed.TotalSeconds;
        ctx.Out.WriteLine($"  build ({warmth}): {buildSeconds.ToString("F1", CultureInfo.InvariantCulture)} s, {FirstLine(build)}");
        if (build.ExitCode != 0) { ctx.Out.WriteLine(build.Stdout); return 2; }

        var failed = new List<string>();
        foreach (var s in todo)
        {
            var claimRows = Files.ReadRows(Path.Combine(probeDir, "rows", s.Claim + ".json"));
            var evidence = new JsonObject { ["variants"] = new JsonArray(), ["rows"] = new JsonObject() };
            var result = "pass"; var seconds = 0.0; var reports = new List<string>();
            var index = 0;
            foreach (var variant in s.Variants.OfType<JsonObject>())
            {
                var coverage = $"{s.Probe}-{index}.cobertura.xml";
                var outDir = Path.Combine(results, $"{s.Probe}-{index}-rows");
                var env = new Dictionary<string, string?>(environment, StringComparer.Ordinal) { ["PROBE_OUT"] = outDir };
                foreach (var (k, v) in variant) env[k] = Files.Str(v);
                var probeClock = Stopwatch.StartNew();
                var ran = adapter.RunProbe(worktree, s.Filter, results, coverage, env);
                seconds += probeClock.Elapsed.TotalSeconds;
                var label = variant.Count > 0 ? " [" + string.Join(", ", variant.Select(kv => $"{kv.Key}={Files.Str(kv.Value)}")) + "]" : "";
                ctx.Out.WriteLine($"  {s.Probe}{label}: {FirstLine(ran)} ({probeClock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s)");
                if (ran.ExitCode != 0) { result = "fail"; ctx.Out.WriteLine(string.Join('\n', ran.Stdout.Split('\n').Skip(1).Take(11))); }
                reports.AddRange(Directory.EnumerateFiles(results, coverage, SearchOption.AllDirectories));

                // Execution evidence: the rows this variant should run, the rows it did, how each ended and the
                // settings that applied. A row missing here, or recorded without its settings, earns nothing.
                var only = Files.Str(variant["PROBE_ONLY"]);
                var expected = new JsonArray(); var executed = new SortedSet<int>();
                for (var i = 0; i < claimRows.Count; i++)
                    if (Selected(Files.StringMap(claimRows[i]?["row"] ?? claimRows[i]), only)) expected.Add(i);
                if (Directory.Exists(outDir))
                    foreach (var file in Directory.EnumerateFiles(outDir, "*.jsonl"))
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
                    ["env"] = variant.DeepClone(), ["expected"] = expected, ["executed"] = new JsonArray([.. executed.Select(x => (JsonNode)x)]),
                });
                var missing = expected.Select(x => (int)Files.Num(x)).Where(x => !executed.Contains(x)).ToList();
                if (missing.Count > 0) ctx.Out.WriteLine($"    {missing.Count} expected row(s) did not run: [{string.Join(", ", missing.Take(10))}]");
                index++;
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
            // other until its footprint changes (a fix touches it).
            if (reports.Count > 0)
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

        Files.WriteJson(ledger.P("replay-last.json"), new JsonObject
        {
            ["branch"] = ledger.Branch, ["commit"] = head, ["build"] = warmth, ["build_seconds"] = Math.Round(buildSeconds, 1),
            ["replayed"] = todo.Count, ["failed"] = new JsonArray([.. failed.Select(x => (JsonNode)x)]),
            ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1), ["axes"] = axes?.DeepClone(),
        });
        ctx.Out.WriteLine($"prober replay: {todo.Count - failed.Count} passed, {failed.Count} failed, {clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s in all");
        try { Directory.Delete(results, recursive: true); } catch (IOException) { }
        return failed.Count > 0 ? 1 : 0;
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
