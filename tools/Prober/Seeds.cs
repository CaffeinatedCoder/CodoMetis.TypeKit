using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// Proves that each probe can fail (decision 100). A probe's registry entry names a seeded product defect:
/// a patch against the product files (the replay's <c>include</c>, <c>src</c> here), an environment toggle,
/// or both, and a sentence saying what it breaks. This applies each seed in a scratch worktree of its own,
/// builds, replays that probe there, and counts the seed caught only where a row ends as a violation that
/// the unseeded probe does not have and that no harness error raised. Nothing it runs is recorded as evidence: a seeded run proves the probe,
/// not the product. Never in the checkout and never in the replay's worktree; one build per seed, so it runs
/// locally, not in CI. A missed seed, a probe without one, a seed that no longer applies and a seeded build
/// that fails all fail the command.
/// </summary>
internal static class Seeds
{
    /// <summary>How one seed ended.</summary>
    internal sealed record Outcome(string Probe, string Claim, string Seed, string Result, int Violations, int Rows, string Detail);

    public static int Run(Context ctx, Args a, IProbeAdapter? adapter = null)
    {
        var ledger = new Ledger(ctx, a.Get("branch"));
        var config = ledger.AxesBlock()["replay"] as JsonObject ?? throw new Refusal("the axes file's block has no \"replay\" configuration");
        adapter ??= new DotNetAdapter(config);
        var registry = ledger.Registry();
        var states = ledger.ProbeStates(includeRetired: false).Where(s => !s.Retired).ToList();
        if (a.Get("probe") is { } only)
        {
            states = [.. states.Where(s => s.Probe == only)];
            if (states.Count == 0) throw new Refusal($"no registered probe {only} that is not retired", 2);
        }
        var head = ledger.Head;
        var home = Replay.Home(ctx, ledger);
        var worktree = Path.Combine(home, "seeds");
        ctx.Out.WriteLine($"prober seeds: {ledger.Branch} at {head[..7]}: {states.Count} probes, in {worktree}");
        if (a.Flag("dry-run"))
        {
            foreach (var s in states) ctx.Out.WriteLine($"  {s.Probe}: {Describe(registry[s.Probe]["seed"])}");
            return 0;
        }

        var clock = Stopwatch.StartNew();
        Directory.CreateDirectory(home);
        if (!Directory.Exists(worktree))
        {
            var added = Git.Run(ledger.Root, "worktree", "add", "--detach", worktree, head);
            if (added.ExitCode != 0) throw new Refusal("could not add the scratch worktree: " + added.Stderr.Trim());
        }
        var include = Files.Strings(config["include"]).ToArray() is { Length: > 0 } inc ? inc : ["src"];
        var environment = new Dictionary<string, string?>(ctx.Environment, StringComparer.Ordinal);
        foreach (var (k, v) in adapter.Environment(home)) environment[k] = v;
        var results = Directory.CreateTempSubdirectory("prober-seeds-").FullName;
        var outcomes = new List<Outcome>();
        try
        {
            // The baseline: the rows each probe already fails without a seed. From the ledger where its evidence is
            // current, else from an unseeded run here, so a seed is credited only with violations of its own.
            var baseline = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            var unseeded = new List<Ledger.ProbeState>();
            foreach (var s in states)
            {
                if (!s.Stale && Files.ReadJson(ledger.EvidencePath(s.Probe)) is JsonObject evidence)
                    baseline[s.Probe] = [.. (evidence["rows"] as JsonObject ?? []).Where(kv => Files.Str(kv.Value?["category"]) == "violation")
                                                                                .Select(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))];
                else unseeded.Add(s);
            }
            ctx.Out.WriteLine($"  baseline: {states.Count - unseeded.Count} from the ledger, {unseeded.Count} to run unseeded");
            if (unseeded.Count > 0)
            {
                Reset(worktree, head);
                Replay.Overlay(ledger, config, worktree);
                var build = Timed(() => adapter.Build(worktree, environment), out var seconds);
                ctx.Out.WriteLine($"  unseeded build: {Seconds(seconds)}, {FirstLine(build)}");
                if (build.ExitCode != 0) { ctx.Out.WriteLine(build.Stdout); throw new Refusal("the unseeded build failed; no seed can be judged"); }
                foreach (var s in unseeded)
                {
                    var rows = RunProbe(adapter, worktree, s, environment, null, results, "base");
                    baseline[s.Probe] = [.. rows.Where(kv => kv.Value.Category == "violation").Select(kv => kv.Key)];
                    ctx.Out.WriteLine($"    {s.Probe}: {rows.Count} rows, {baseline[s.Probe].Count} violations unseeded");
                }
            }

            foreach (var s in states)
            {
                var outcome = One(ledger, config, adapter, worktree, head, include, environment, results, s, registry[s.Probe]["seed"], baseline[s.Probe]);
                outcomes.Add(outcome);
                ctx.Out.WriteLine($"  {s.Probe} ({s.Claim}): {outcome.Result}{(outcome.Detail.Length > 0 ? ", " + outcome.Detail : "")}");
            }
        }
        finally
        {
            // The worktree is left at HEAD, every seed undone, for the next run to build incrementally.
            Reset(worktree, head);
            try { Directory.Delete(results, recursive: true); } catch (IOException) { }
        }

        Files.WriteJson(ledger.P("seeds-last.json"), new JsonObject
        {
            ["schemaVersion"] = Files.SchemaVersion, ["branch"] = ledger.Branch, ["commit"] = head,
            ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
            ["seeds"] = new JsonArray([.. outcomes.Select(o => (JsonNode)new JsonObject
            {
                ["probe"] = o.Probe, ["claim"] = o.Claim, ["seed"] = o.Seed, ["result"] = o.Result,
                ["violations"] = o.Violations, ["rows"] = o.Rows, ["detail"] = o.Detail,
            })]),
        });
        var caught = outcomes.Count(o => o.Result == "caught");
        var counts = outcomes.GroupBy(o => o.Result, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}");
        ctx.Out.WriteLine($"prober seeds: {string.Join(", ", counts)}, {Seconds(clock.Elapsed.TotalSeconds)} in all");
        return caught == outcomes.Count ? 0 : 1;
    }

    /// <summary>Applies one probe's seed, builds, runs the probe, and undoes the seed.</summary>
    private static Outcome One(Ledger ledger, JsonObject config, IProbeAdapter adapter, string worktree, string head, string[] include,
                               Dictionary<string, string?> environment, string results, Ledger.ProbeState s, JsonNode? seed, HashSet<int> baseline)
    {
        Outcome End(string result, string detail, int violations = 0, int rows = 0) => new(s.Probe, s.Claim, Describe(seed), result, violations, rows, detail);

        Reset(worktree, head);
        Replay.Overlay(ledger, config, worktree);
        var (refused, detail, patch, env) = Validate(ledger, include, seed, worktree);
        if (refused is not null) return End(refused, detail);
        if (patch is not null)
        {
            var ran = Git.Run(worktree, "apply", "--whitespace=nowarn", patch);
            if (ran.ExitCode != 0) return End("does not apply", FirstLine(ran.Stderr));
        }
        try
        {
            var seeded = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            foreach (var (k, v) in env ?? []) seeded[k] = Files.Str(v);
            var build = Timed(() => adapter.Build(worktree, seeded), out var buildSeconds);
            if (build.ExitCode != 0) return End("build failed", $"the seeded build: {FirstLine(build)}");
            var rows = RunProbe(adapter, worktree, s, environment, env, results, "seed");
            // A row that broke the harness says nothing about the claim's check, even where it also returned a failure.
            var own = rows.Where(kv => kv.Value.Category == "violation" && !kv.Value.HarnessError && !baseline.Contains(kv.Key)).OrderBy(kv => kv.Key).ToList();
            var tally = string.Join(", ", rows.Values.GroupBy(r => r.Category, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"));
            var broken = rows.Values.Count(r => r.HarnessError);
            var harness = broken > 0 ? $"; {broken} ended in a harness error, which is no catch" : "";
            if (own.Count == 0) return End("missed", $"no row violates that did not already ({rows.Count} rows: {(tally.Length > 0 ? tally : "none ran")}{harness}; build {Seconds(buildSeconds)})", 0, rows.Count);
            var example = own[0];
            return End("caught", $"{own.Count} of {rows.Count} rows violate; e.g. #{example.Key}: {Cut(example.Value.Reason)}", own.Count, rows.Count);
        }
        finally
        {
            if (patch is not null) Git.Run(worktree, "apply", "-R", "--whitespace=nowarn", patch);
        }
    }

    /// <summary>
    /// Why a seed cannot be applied in <paramref name="worktree"/> (a result and a detail), or a null result with
    /// the patch's full path and the environment toggle to apply. A seed names its defect and a patch, an
    /// environment toggle or both; a patch exists in the corpus, touches only the product files and applies.
    /// </summary>
    private static (string? Refused, string Detail, string? Patch, JsonObject? Env) Validate(Ledger ledger, string[] include, JsonNode? seed, string worktree)
    {
        if (seed is not JsonObject entry) return ("no seed", "its registry entry names none", null, null);
        var patch = Files.Str(entry["patch"]);
        var env = entry["env"] as JsonObject;
        if (Files.Str(entry["defect"]).Length == 0 || (patch.Length == 0 && env is not { Count: > 0 }))
            return ("no seed", "a seed names its defect and a patch or an environment toggle", null, null);
        if (patch.Length == 0) return (null, "", null, env);

        var file = Path.GetFullPath(ledger.CorpusPath(patch));
        if (!File.Exists(file)) return ("does not apply", $"{patch} is not in the corpus", null, null);
        var touched = Git.Run(worktree, "apply", "--numstat", file);
        if (touched.ExitCode != 0) return ("does not apply", FirstLine(touched.Stderr), null, null);
        var outside = touched.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')[^1].Trim())
                             .Where(path => !include.Any(p => path.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal))).ToList();
        if (outside.Count > 0) return ("does not apply", $"it patches {string.Join(", ", outside)}, outside the product files ({string.Join(", ", include)})", null, null);
        var check = Git.Run(worktree, "apply", "--check", file);
        return check.ExitCode != 0 ? ("does not apply", FirstLine(check.Stderr), null, null) : (null, "", file, env);
    }

    /// <summary>
    /// What `seeds` would refuse before building anything, against this checkout: a probe without a seed, or a
    /// seed that does not apply. No build, so the tests run it on every build and a probe registered without a
    /// seed, or a product change that leaves a seed behind, fails there and not only at the next local run.
    /// </summary>
    internal static List<string> Problems(Ledger ledger)
    {
        var config = ledger.AxesBlock()["replay"] as JsonObject ?? [];
        var include = Files.Strings(config["include"]).ToArray() is { Length: > 0 } inc ? inc : ["src"];
        var problems = new List<string>();
        foreach (var (probe, entry) in ledger.Registry())
        {
            if (entry["retired"] is JsonValue retired && retired.TryGetValue<bool>(out var r) && r) continue;
            if (Validate(ledger, include, entry["seed"], ledger.Top) is { Refused: { } refused, Detail: var detail })
                problems.Add($"{probe}: {refused}, {detail}");
        }
        return problems;
    }

    /// <summary>A row's outcome under one run: its category, the first reason given, and whether any reason was a harness error.</summary>
    internal sealed record RowOutcome(string Category, string Reason, bool HarnessError);

    /// <summary>Runs one probe in every one of its variants and reads how each row ended.</summary>
    private static Dictionary<int, RowOutcome> RunProbe(IProbeAdapter adapter, string worktree, Ledger.ProbeState s, Dictionary<string, string?> environment,
                                                        JsonObject? seedEnv, string results, string tag)
    {
        var rows = new Dictionary<int, RowOutcome>();
        var index = 0;
        foreach (var variant in s.Variants.OfType<JsonObject>())
        {
            var outDir = Path.Combine(results, $"{s.Probe}-{tag}-{index}-rows");
            var env = new Dictionary<string, string?>(environment, StringComparer.Ordinal) { ["PROBE_OUT"] = outDir };
            foreach (var (k, v) in variant) env[k] = Files.Str(v);
            foreach (var (k, v) in seedEnv ?? []) env[k] = Files.Str(v);
            adapter.RunProbe(worktree, s.Filter, results, $"{s.Probe}-{tag}-{index}.cobertura.xml", env);
            if (Directory.Exists(outDir))
                foreach (var file in Directory.EnumerateFiles(outDir, "*.jsonl"))
                    foreach (var line in Files.ReadLines(file))
                    {
                        var category = Files.Str(line["category"], "violation");
                        var reasons = Files.Strings(line["reasons"]).ToList();
                        var reason = reasons.FirstOrDefault(r => r.StartsWith(category + ":", StringComparison.Ordinal)) ?? Files.Str(line["note"]);
                        var harnessError = category == "harness-error" || reasons.Any(r => r.StartsWith("harness-error:", StringComparison.Ordinal));
                        rows[(int)Files.Num(line["idx"])] = new RowOutcome(category, reason, harnessError);
                    }
            index++;
        }
        return rows;
    }

    /// <summary>Puts the scratch worktree's tracked files back at HEAD; the copied corpus, untracked, stays.</summary>
    private static void Reset(string worktree, string head)
    {
        if (Git.Run(worktree, "checkout", "-q", "--detach", head).ExitCode != 0 && Git.Run(worktree, "reset", "-q", "--hard", head).ExitCode != 0)
            throw new Refusal($"could not move the scratch worktree {worktree} to {head[..7]}");
        Git.Run(worktree, "reset", "-q", "--hard", head);
    }

    private static string Describe(JsonNode? seed) =>
        seed is JsonObject o
            ? string.Join(" + ", new[] { Files.Str(o["patch"]), o["env"] is JsonObject env ? string.Join(",", env.Select(kv => $"{kv.Key}={Files.Str(kv.Value)}")) : "" }.Where(x => x.Length > 0))
            : "(none)";

    private static Ran Timed(Func<Ran> run, out double seconds)
    {
        var clock = Stopwatch.StartNew();
        var ran = run();
        seconds = clock.Elapsed.TotalSeconds;
        return ran;
    }

    private static string Seconds(double seconds) => seconds.ToString("F1", CultureInfo.InvariantCulture) + " s";

    private static string Cut(string s) => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) is var line && line.Length > 160 ? line[..160] + "..." : line;

    private static string FirstLine(Ran ran) => ran.Stdout.Split('\n')[0].Trim() is { Length: > 0 } line ? line : $"exit {ran.ExitCode}";

    private static string FirstLine(string text) => text.Trim().Split('\n')[0].Trim();
}
