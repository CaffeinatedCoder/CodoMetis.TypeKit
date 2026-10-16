using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// What <c>replay</c>, <c>seeds</c> and <c>try</c> share to run a probe: the worktree moved to HEAD, the
/// environment every build and run gets, one run of a probe in one process, and how each row of it ended, read
/// from the jsonl the harness writes under <c>PROBE_OUT</c>.
/// </summary>
internal static class ProbeRun
{
    /// <summary>
    /// A row's outcome under one run: its category, the first reason given, whether any reason of the row or of a check
    /// of it was a harness error, and its checks. A row that ran no check is one check of the obligation <c>claim</c>.
    /// </summary>
    internal sealed record RowOutcome(string Category, string Reason, bool HarnessError, IReadOnlyList<CheckOutcome> Checks);

    /// <summary>
    /// One check of a row under one run: its obligation, category and first reason, and whether a harness error broke it
    /// or its row outside any check. Only a clean violation, one with no harness error, says the check fired (decision 103).
    /// </summary>
    internal sealed record CheckOutcome(string Obligation, string Category, string Reason, bool HarnessError)
    {
        public bool CleanViolation => Category == "violation" && !HarnessError;
    }

    /// <summary>
    /// Adds the worktree at <paramref name="head"/> ("cold"), or moves it there without force ("warm"): files the
    /// commits did not change keep their mtimes, so the build stays incremental.
    /// </summary>
    public static string Ready(Ledger ledger, string worktree, string head)
    {
        if (!Directory.Exists(worktree))
        {
            var added = Git.Run(ledger.Root, "worktree", "add", "--detach", worktree, head);
            if (added.ExitCode != 0) throw new Refusal("could not add the worktree: " + added.Stderr.Trim(), 2);
            return "cold";
        }
        if (Git.Run(worktree, "checkout", "--detach", head).ExitCode != 0) Git.Run(worktree, "reset", "--hard", head);
        return "warm";
    }

    /// <summary>The process's environment with what the adapter adds for every build and run of the probes.</summary>
    public static Dictionary<string, string?> Environment(Context ctx, IProbeAdapter adapter, string home)
    {
        var environment = new Dictionary<string, string?>(ctx.Environment, StringComparer.Ordinal);
        foreach (var (k, v) in adapter.Environment(home)) environment[k] = v;
        return environment;
    }

    /// <summary>The environment of one variant: the base, then the variant's own settings, then the extra ones (a seed's toggle).</summary>
    public static Dictionary<string, string?> Variant(IReadOnlyDictionary<string, string?> environment, JsonObject variant, JsonObject? extra = null)
    {
        var env = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
        foreach (var (k, v) in variant) env[k] = Files.Str(v);
        foreach (var (k, v) in extra ?? []) env[k] = Files.Str(v);
        return env;
    }

    /// <summary>
    /// Runs the probe the filter selects once, with its rows written to <c>{results}/{name}-rows</c>, and reads how
    /// each row ended. <paramref name="coverageFile"/> empty asks for no coverage.
    /// </summary>
    public static (Ran Ran, Dictionary<int, RowOutcome> Rows) Once(IProbeAdapter adapter, string worktree, string filter, string results, string name,
                                                                     string coverageFile, Dictionary<string, string?> env)
    {
        var outDir = Path.Combine(results, name + "-rows");
        env["PROBE_OUT"] = outDir;
        var ran = adapter.RunProbe(worktree, filter, results, coverageFile, env);
        return (ran, Outcomes(outDir));
    }

    /// <summary>How each row ended, from every jsonl under <paramref name="outDir"/>; a line without a category is a violation.</summary>
    public static Dictionary<int, RowOutcome> Outcomes(string outDir)
    {
        var rows = new Dictionary<int, RowOutcome>();
        if (!Directory.Exists(outDir)) return rows;
        foreach (var file in Directory.EnumerateFiles(outDir, "*.jsonl"))
            foreach (var line in Files.ReadLines(file))
            {
                var category = Files.Str(line["category"], "violation");
                var reasons = Files.Strings(line["reasons"]).ToList();
                var reason = reasons.FirstOrDefault(r => r.StartsWith(category + ":", StringComparison.Ordinal)) ?? Files.Str(line["note"]);
                static bool Broken(IEnumerable<string> reasons) => reasons.Any(r => r.StartsWith("harness-error:", StringComparison.Ordinal));
                var checks = new List<CheckOutcome>();
                if (line["checks"] is JsonArray { Count: > 0 } ran)
                {
                    // The row's own harness error (outside any check) breaks every check of it; a check's breaks that check.
                    var rowBroken = Broken(reasons);
                    foreach (var c in ran.OfType<JsonObject>())
                    {
                        var cc = Files.Str(c["category"], "violation");
                        var cr = Files.Strings(c["reasons"]).ToList();
                        checks.Add(new CheckOutcome(Files.Str(c["obligation"]), cc, cr.FirstOrDefault(r => r.StartsWith(cc + ":", StringComparison.Ordinal)) ?? Files.Str(c["notes"]),
                                                    rowBroken || cc == "harness-error" || Broken(cr)));
                    }
                }
                else checks.Add(new CheckOutcome(Claim.ImplicitObligation, category, reason, category == "harness-error" || Broken(reasons)));
                var harnessError = Broken(reasons) || checks.Any(c => c.HarnessError);
                rows[(int)Files.Num(line["idx"])] = new RowOutcome(category, reason, harnessError, checks);
            }
        return rows;
    }

    /// <summary>The first line of a run's output (the verdict's), or its exit code where it printed nothing.</summary>
    public static string FirstLine(Ran ran) => ran.Stdout.Split('\n')[0].Trim() is { Length: > 0 } line ? line : $"exit {ran.ExitCode}";
}
