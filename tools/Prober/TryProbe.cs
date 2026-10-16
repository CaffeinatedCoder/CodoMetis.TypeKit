using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// <c>try</c>: the writer's loop for one probe. It moves the replay's worktree to HEAD, overlays the corpus and
/// the branch's staging there as the replay does, builds once through the adapter (not with <c>--no-build</c>),
/// and runs the one probe in each of its registry variants; <c>--rows</c> keeps the rows given, run through the
/// harness's <c>PROBE_ROWS</c> one stretch of consecutive rows at a time, within the rows each variant's
/// <c>PROBE_ONLY</c> selects. It prints each run's first line (the verdict's) and how each row ended, from the
/// jsonl the harness writes. It records nothing, no evidence, footprint or run, so nothing under the state
/// directory changes, and takes no lock: a replay running at the same time shares the worktree. Its exit code is
/// the probe's: the first run's that failed, else 0; 1 when the build fails.
/// </summary>
internal static class TryProbe
{
    public static int Run(Context ctx, Args a, IProbeAdapter? adapter = null)
    {
        var ledger = new Ledger(ctx, a.Get("branch"));
        var config = ledger.AxesBlock()["replay"] as JsonObject ?? throw new Refusal("the axes file's block has no \"replay\" configuration");
        adapter ??= new DotNetAdapter(config);
        var probe = a.Need("probe");
        if (!ledger.Registry().TryGetValue(probe, out var entry))
            throw new Refusal($"no probe {probe} in the registry, tracked or staged; `prober new` stages one", 2);
        var claim = Files.Str(entry["claim"]);
        var filter = Files.Str(entry["filter"]);

        // What each variant runs: the rows its PROBE_ONLY selects, those of --rows among them, in stretches.
        var probeRows = ledger.RowsOf(probe);
        var asked = Indices(a.Get("rows"), probeRows.Count);
        var plan = new List<(JsonObject Variant, List<int> Rows, List<string?> Stretches)>();
        foreach (var variant in Ledger.VariantsOf(entry).OfType<JsonObject>())
        {
            var only = Files.Str(variant["PROBE_ONLY"]);
            var selected = Enumerable.Range(0, probeRows.Count).Where(i => Replay.Selected(Files.StringMap(probeRows[i]?["row"] ?? probeRows[i]), only)).ToList();
            if (asked is null) { plan.Add((variant, selected, [null])); continue; }
            var rows = selected.Where(asked.Contains).ToList();
            if (rows.Count > 0) plan.Add((variant, rows, Stretches(rows)));
        }
        if (asked is not null && asked.Except(plan.SelectMany(p => p.Rows)).ToList() is { Count: > 0 } unrun)
            throw new Refusal($"no variant of {probe} runs row(s) {string.Join(", ", unrun)}: each variant's PROBE_ONLY leaves them out", 2);

        var home = Replay.Home(ctx, ledger);
        var worktree = Path.Combine(home, "wt");
        var noBuild = a.Flag("no-build");
        if (noBuild && !Directory.Exists(worktree)) throw new Refusal($"--no-build: the replay worktree {worktree} does not exist yet, so nothing is built; run without it", 2);
        Directory.CreateDirectory(home);
        var head = ledger.Head;
        var warmth = ProbeRun.Ready(ledger, worktree, head);
        Replay.Overlay(ledger, config, worktree);
        var environment = ProbeRun.Environment(ctx, adapter, home);
        ctx.Out.WriteLine($"prober try: {probe} ({claim}) at {head[..7]} in {worktree}; nothing is recorded");
        if (!noBuild)
        {
            var clock = Stopwatch.StartNew();
            var build = adapter.Build(worktree, environment);
            ctx.Out.WriteLine($"  build ({warmth}): {Seconds(clock.Elapsed.TotalSeconds)}, {ProbeRun.FirstLine(build)}");
            if (build.ExitCode != 0) { ctx.Out.WriteLine(build.Stdout); return 1; }
        }

        // The full logs of each run, kept until the next try; the results and the rows' jsonl are thrown away.
        var logs = Path.Combine(home, "try-logs");
        try { if (Directory.Exists(logs)) Directory.Delete(logs, recursive: true); } catch (IOException) { }
        var results = Directory.CreateTempSubdirectory("prober-try-").FullName;
        var exit = 0;
        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var index = 0;
            foreach (var (variant, rows, stretches) in plan)
            {
                var label = variant.Count > 0 ? " [" + string.Join(", ", variant.Select(kv => $"{kv.Key}={Files.Str(kv.Value)}")) + "]" : "";
                foreach (var stretch in stretches)
                {
                    var name = $"{probe}-try-{index++}";
                    var env = ProbeRun.Variant(environment, variant);
                    if (stretch is not null) env["PROBE_ROWS"] = stretch;
                    env["PROBE_LOG_DIR"] = Path.Combine(logs, name);
                    var clock = Stopwatch.StartNew();
                    var (ran, outcomes) = ProbeRun.Once(adapter, worktree, filter, results, name, "", env);
                    ctx.Out.WriteLine($"  {probe}{label}{(stretch is null ? "" : $" rows {stretch}")}: {ProbeRun.FirstLine(ran)} (exit {ran.ExitCode}, {Seconds(clock.Elapsed.TotalSeconds)})");
                    foreach (var i in rows.Where(i => stretch is null || Within(i, stretch)))
                    {
                        var outcome = outcomes.GetValueOrDefault(i);
                        var category = outcome?.Category ?? "did not run";
                        tally[category] = tally.GetValueOrDefault(category) + 1;
                        ctx.Out.WriteLine($"    #{i} {category}{(outcome is { Category: not "pass", Reason.Length: > 0 } ? ": " + Cut(Unprefixed(outcome.Reason, category)) : "")}");
                    }
                    if (ran.ExitCode != 0 && Directory.Exists(env["PROBE_LOG_DIR"])) ctx.Out.WriteLine($"    log: {env["PROBE_LOG_DIR"]}");
                    if (ran.ExitCode != 0 && exit == 0) exit = ran.ExitCode;
                }
            }
        }
        finally
        {
            try { Directory.Delete(results, recursive: true); } catch (IOException) { }
        }
        ctx.Out.WriteLine($"prober try: {probe}: {(tally.Count == 0 ? "no rows" : string.Join(", ", tally.Select(kv => $"{kv.Key} {kv.Value}")))}; exit {exit}");
        return exit;
    }

    /// <summary>The row indices of <c>--rows</c> (<c>i,j,...</c>, <c>a-b</c> for a stretch), or null for every row; refuses one the claim does not have.</summary>
    internal static SortedSet<int>? Indices(string? given, int count)
    {
        if (given is null) return null;
        var result = new SortedSet<int>();
        foreach (var part in given.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-');
            if (bounds.Length > 2 || !bounds.All(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                throw new Refusal($"--rows takes row indices, i,j,... or a-b: not {part}", 2);
            var lo = int.Parse(bounds[0], CultureInfo.InvariantCulture); var hi = int.Parse(bounds[^1], CultureInfo.InvariantCulture);
            for (var i = lo; i <= hi; i++) result.Add(i);
        }
        if (result.Count == 0) throw new Refusal("--rows takes row indices, i,j,... or a-b", 2);
        if (result.Max >= count) throw new Refusal($"--rows {result.Max}: the claim has {count} row(s), 0 to {count - 1}", 2);
        return result;
    }

    /// <summary>The consecutive stretches of sorted rows, as the harness's PROBE_ROWS spells one: <c>lo-hi</c>.</summary>
    internal static List<string?> Stretches(List<int> rows)
    {
        var result = new List<string?>();
        for (var i = 0; i < rows.Count;)
        {
            var j = i;
            while (j + 1 < rows.Count && rows[j + 1] == rows[j] + 1) j++;
            result.Add(string.Create(CultureInfo.InvariantCulture, $"{rows[i]}-{rows[j]}"));
            i = j + 1;
        }
        return result;
    }

    private static bool Within(int row, string stretch)
    {
        var bounds = stretch.Split('-');
        return row >= int.Parse(bounds[0], CultureInfo.InvariantCulture) && row <= int.Parse(bounds[1], CultureInfo.InvariantCulture);
    }

    /// <summary>A reason without the category the harness puts in front of it (<c>violation:...</c>), which the line already names.</summary>
    private static string Unprefixed(string reason, string category) => reason.StartsWith(category + ":", StringComparison.Ordinal) ? reason[(category.Length + 1)..] : reason;

    private static string Seconds(double seconds) => seconds.ToString("F1", CultureInfo.InvariantCulture) + " s";

    private static string Cut(string s) => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) is var line && line.Length > 160 ? line[..160] + "..." : line;
}
