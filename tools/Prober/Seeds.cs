using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// Proves that each probe can fail (decision 100). A probe's registry entry names a seeded product defect:
/// a patch against the product files (the replay's <c>include</c>, <c>src</c> here), an environment toggle,
/// or both, and a sentence saying what it breaks. This applies each seed in a scratch worktree of its own,
/// builds, replays that probe there, and counts the seed caught only where a check ends as a violation that the
/// unseeded probe does not have in the same row for the same obligation and that no harness error raised, and, for a
/// seed naming an obligation, only a check naming that obligation (a row without checks is one check, <c>claim</c>).
/// Nothing it runs is recorded as evidence: a seeded run proves the probe,
/// not the product. Never in the checkout and never in the replay's worktree; one build per seed, so it runs
/// locally, not in CI. A missed seed, a probe without one, a seed that no longer applies and a seeded build
/// that fails all fail the command.
/// </summary>
internal static class Seeds
{
    /// <summary>How one seed ended; <paramref name="Obligation"/> is the claim's obligation it breaks, or null for a seed that proves the probe alone.</summary>
    internal sealed record Outcome(string Probe, string Claim, string? Obligation, string Seed, string Result, int Violations, int Rows, string Detail);

    /// <summary>
    /// Every seed of a registry entry, each with the obligation it names: the one `seed` (its `obligation`, if
    /// any) and each of `seeds` (decision 104). An entry without either has none.
    /// </summary>
    internal static List<(string? Obligation, JsonObject Seed)> SeedsOf(JsonObject entry)
    {
        var result = new List<(string?, JsonObject)>();
        if (entry["seed"] is JsonObject one) result.Add((Files.Str(one["obligation"]) is { Length: > 0 } o ? o : null, one));
        foreach (var seed in (entry["seeds"] as JsonArray ?? []).OfType<JsonObject>())
            result.Add((Files.Str(seed["obligation"]) is { Length: > 0 } o ? o : null, seed));
        return result;
    }

    private static string Named(string probe, string? obligation) => obligation is null ? probe : $"{probe}/{obligation}";

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
        var seedsOf = states.ToDictionary(s => s.Probe, s => SeedsOf(registry[s.Probe]), StringComparer.Ordinal);
        // A probe without a seed owes one per obligation its claim lists (decision 104), each an outcome of its own;
        // under a claim without obligations, one.
        var listed = ledger.Claims().ToDictionary(c => Files.Str(c["id"]), Claim.ObligationIds, StringComparer.Ordinal);
        List<(string? Obligation, JsonNode? Seed)> Owed(Ledger.ProbeState s) =>
            seedsOf[s.Probe].Count > 0 ? [.. seedsOf[s.Probe].Select(x => (x.Obligation, (JsonNode?)x.Seed))]
            : listed.GetValueOrDefault(s.Claim) is { } ids && ids is not [Claim.ImplicitObligation] ? [.. ids.Select(id => ((string?)id, (JsonNode?)null))]
            : [(null, null)];
        ctx.Out.WriteLine($"prober seeds: {ledger.Branch} at {head[..7]}: {seedsOf.Values.Sum(x => x.Count)} seeds of {states.Count} probes, in {worktree}");
        if (a.Flag("dry-run"))
        {
            foreach (var s in states)
            {
                foreach (var (obligation, seed) in Owed(s)) ctx.Out.WriteLine($"  {Named(s.Probe, obligation)}: {Describe(seed)}");
            }
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
        var environment = ProbeRun.Environment(ctx, adapter, home);
        var results = Directory.CreateTempSubdirectory("prober-seeds-").FullName;
        var outcomes = new List<Outcome>();
        try
        {
            // The baseline: the checks each probe already fails without a seed, by row and obligation. From the ledger
            // where its evidence is current, else from an unseeded run here, so a seed is credited only with violations
            // of its own.
            var baseline = new Dictionary<string, HashSet<(int Row, string Obligation)>>(StringComparer.Ordinal);
            var unseeded = new List<Ledger.ProbeState>();
            foreach (var s in states)
            {
                if (!s.Stale && Files.ReadJson(ledger.EvidencePath(s.Probe)) is JsonObject evidence)
                    baseline[s.Probe] = [.. (evidence["rows"] as JsonObject ?? []).Where(kv => kv.Value is JsonObject)
                        .SelectMany(kv => Ledger.ChecksOf((JsonObject)kv.Value!).Where(c => c.Category == "violation")
                                                .Select(c => (int.Parse(kv.Key, CultureInfo.InvariantCulture), c.Obligation)))];
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
                    baseline[s.Probe] = Violating(rows);
                    ctx.Out.WriteLine($"    {s.Probe}: {rows.Count} rows, {baseline[s.Probe].Select(x => x.Row).Distinct().Count()} violations unseeded");
                }
            }

            foreach (var s in states)
            {
                // A seed missing is an outcome "no seed"; a probe with several runs each under its obligation.
                foreach (var (obligation, seed) in Owed(s))
                {
                    var outcome = One(ledger, config, adapter, worktree, head, include, environment, results, s, obligation, seed, baseline[s.Probe]);
                    outcomes.Add(outcome);
                    ctx.Out.WriteLine($"  {Named(s.Probe, obligation)} ({s.Claim}): {outcome.Result}{(outcome.Detail.Length > 0 ? ", " + outcome.Detail : "")}");
                }
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
                ["probe"] = o.Probe, ["claim"] = o.Claim, ["obligation"] = o.Obligation, ["seed"] = o.Seed, ["result"] = o.Result,
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
                               Dictionary<string, string?> environment, string results, Ledger.ProbeState s, string? obligation, JsonNode? seed,
                               HashSet<(int Row, string Obligation)> baseline)
    {
        Outcome End(string result, string detail, int violations = 0, int rows = 0) => new(s.Probe, s.Claim, obligation, Describe(seed), result, violations, rows, detail);

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
            // A check that broke the harness says nothing about the claim, even where it also returned a failure; a
            // seed naming an obligation is caught only by a check naming it (decision 104), a seed naming none by any.
            var fresh = rows.SelectMany(kv => kv.Value.Checks.Select(c => (Row: kv.Key, Check: c)))
                            .Where(x => x.Check.CleanViolation && !baseline.Contains((x.Row, x.Check.Obligation))).OrderBy(x => x.Row).ToList();
            var own = fresh.Where(x => obligation is null || x.Check.Obligation == obligation).ToList();
            var caughtRows = own.Select(x => x.Row).Distinct().Count();
            var tally = string.Join(", ", rows.Values.GroupBy(r => r.Category, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"));
            var broken = rows.Values.Count(r => r.HarnessError);
            var harness = broken > 0 ? $"; {broken} ended in a harness error, which is no catch" : "";
            // Violations by checks of other obligations are no catch of this one; say whose they were, a row without checks being `claim`'s.
            var others = fresh.Count > own.Count
                ? $"; {fresh.Select(x => x.Row).Distinct().Count()} violate by a check naming {string.Join(", ", fresh.Select(x => x.Check.Obligation).Distinct().Order(StringComparer.Ordinal))}, not {obligation}"
                : "";
            if (own.Count == 0) return End("missed", $"no row violates that did not already ({rows.Count} rows: {(tally.Length > 0 ? tally : "none ran")}{harness}{others}; build {Seconds(buildSeconds)})", 0, rows.Count);
            var example = own[0];
            var named = example.Check.Obligation == Claim.ImplicitObligation && rows[example.Row].Checks.Count == 1 ? "" : " " + example.Check.Obligation;
            return End("caught", $"{caughtRows} of {rows.Count} rows violate; e.g. #{example.Row}{named}: {Cut(example.Check.Reason)}", caughtRows, rows.Count);
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
        // The obligations each claim lists (or its one implicit obligation), and the seeds that name each.
        var obligations = ledger.Claims().ToDictionary(c => Files.Str(c["id"]), Claim.ObligationIds, StringComparer.Ordinal);
        var seeded = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (probe, entry) in ledger.Registry())
        {
            if (entry["retired"] is JsonValue retired && retired.TryGetValue<bool>(out var r) && r) continue;
            var claim = Files.Str(entry["claim"]);
            var seeds = SeedsOf(entry);
            if (seeds.Count == 0) { problems.Add($"{probe}: no seed, its registry entry names none"); continue; }
            foreach (var (obligation, seed) in seeds)
            {
                if (Validate(ledger, include, seed, ledger.Top) is { Refused: { } refused, Detail: var detail })
                    problems.Add($"{Named(probe, obligation)}: {refused}, {detail}");
                var ids = obligations.GetValueOrDefault(claim) ?? [Claim.ImplicitObligation];
                // A seed naming no obligation proves the probe; it counts for the claim's one implicit obligation only.
                var counts = obligation ?? (ids is [Claim.ImplicitObligation] ? Claim.ImplicitObligation : null);
                if (counts is null) continue;
                if (!ids.Contains(counts)) { problems.Add($"{Named(probe, obligation)}: names obligation {counts}, which claim {claim} does not list"); continue; }
                (seeded.TryGetValue(claim, out var set) ? set : seeded[claim] = new(StringComparer.Ordinal)).Add(counts);
            }
        }
        // Every listed obligation of a claim that has a probe is seeded by some probe of that claim (decision 104);
        // the implicit obligation is covered by the rule above that every probe has a seed.
        var claimed = ledger.Registry().Values.Select(e => Files.Str(e["claim"])).ToHashSet(StringComparer.Ordinal);
        foreach (var (claim, ids) in obligations.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (ids is [Claim.ImplicitObligation] || !claimed.Contains(claim)) continue;
            foreach (var id in ids.Where(id => !(seeded.GetValueOrDefault(claim)?.Contains(id) ?? false)))
                problems.Add($"{claim}: obligation {id} has no seed in any of its probes");
        }
        return problems;
    }

    /// <summary>The (row, obligation) of every check that ended as a violation, clean or not: what a seed's violations are measured against.</summary>
    private static HashSet<(int Row, string Obligation)> Violating(Dictionary<int, ProbeRun.RowOutcome> rows) =>
        [.. rows.SelectMany(kv => kv.Value.Checks.Where(c => c.Category == "violation").Select(c => (kv.Key, c.Obligation)))];

    /// <summary>Runs one probe in every one of its variants and reads how each row ended.</summary>
    private static Dictionary<int, ProbeRun.RowOutcome> RunProbe(IProbeAdapter adapter, string worktree, Ledger.ProbeState s, Dictionary<string, string?> environment,
                                                                 JsonObject? seedEnv, string results, string tag)
    {
        var rows = new Dictionary<int, ProbeRun.RowOutcome>();
        var index = 0;
        foreach (var variant in s.Variants.OfType<JsonObject>())
        {
            var name = $"{s.Probe}-{tag}-{index}";
            var (_, outcomes) = ProbeRun.Once(adapter, worktree, s.Filter, results, name, name + ".cobertura.xml", ProbeRun.Variant(environment, variant, seedEnv));
            foreach (var (idx, outcome) in outcomes) rows[idx] = outcome;
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

    private static string FirstLine(Ran ran) => ProbeRun.FirstLine(ran);

    private static string FirstLine(string text) => text.Trim().Split('\n')[0].Trim();
}
