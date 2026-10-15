using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// One exploration round (explore or full) under caps the model cannot overrun, run locally only: never in
/// CI, which needs no model. The wall clock is enforced here (the process tree is stopped at the deadline
/// plus a grace), the claim cap and the deadline by `rows`, which refuses new work, and the spend by
/// `--max-budget-usd`. The round always ends cleanly: partial lines repaired, caps cleared, lock released.
/// </summary>
internal static class Round
{
    public static int Run(Context ctx, Args a)
    {
        var mode = a.Positional.FirstOrDefault() ?? throw new Refusal("round takes explore or full", 2);
        if (mode is not ("explore" or "full")) throw new Refusal("round takes explore or full", 2);
        var minutes = a.Get("minutes") is { Length: > 0 } m ? double.Parse(m, CultureInfo.InvariantCulture) : mode == "explore" ? 10 : 30;
        int? claims = a.Get("claims") is { Length: > 0 } c ? int.Parse(c, CultureInfo.InvariantCulture) : mode == "explore" ? 2 : null;
        var grace = double.Parse(ctx.Env("PROBE_GRACE") ?? "60", CultureInfo.InvariantCulture);
        var ledger = new Ledger(ctx, a.Get("branch"));
        var holder = $"round-{mode}-{System.Environment.ProcessId}";

        if (!RoundLock.Take(ledger, holder, mode, System.Environment.ProcessId).Taken)
        {
            ctx.Out.WriteLine("prober round: the ledger is locked by another round");
            return 1;
        }
        var clock = Stopwatch.StartNew();
        string outcome = "completed"; int? exit = null; JsonObject usage = []; var report = "";
        string roundEnded = "", repaired = "", unlocked;
        try
        {
            Program.Run(new Context(ledger.Top, ctx.Environment, TextWriter.Null, ctx.Error),
                ["round-start", "--branch", ledger.Branch, "--holder", holder, "--minutes", minutes.ToString(CultureInfo.InvariantCulture),
                 .. (claims is { } n ? new[] { "--claims", n.ToString(CultureInfo.InvariantCulture) } : [])]);
            var caps = $"{minutes.ToString(CultureInfo.InvariantCulture)} minutes" + (claims is { } k ? $" and {k} claim(s)" : "");
            var prompt =
                $"Mode: {mode}. Repository {ledger.Top}, branch {ledger.Branch}, base {a.Get("base") ?? "main"}.\n" +
                $"This round runs under caps enforced outside you ({caps}). The round runner holds the lock as `{holder}`: do not lock, " +
                $"unlock or start a round yourself, and pass `--holder {holder}` to `prober replay`. `prober rows` refuses new work once " +
                "the time is spent or the claim cap is reached; when it refuses, start nothing new: record what ran, hand the corpus off " +
                "to staging, and report. The process is stopped at the deadline whatever you are doing, so record each claim's results " +
                "as soon as its rows have run.";
            var argv = new List<string> { "-p", prompt, "--agent", "prober", "--permission-mode", "auto", "--output-format", "json" };
            if (a.Get("usd") is { Length: > 0 } usd) argv.AddRange(["--max-budget-usd", usd]);
            var env = new Dictionary<string, string?>(ctx.Environment, StringComparer.Ordinal) { ["PROBE_HOLDER"] = holder };

            using (new RoundLock.Heartbeat(ledger, holder))
            {
                var ran = Proc.Run(FindClaude(ctx), argv, ledger.Top, env, TimeSpan.FromSeconds(minutes * 60 + grace));
                exit = ran.ExitCode;
                if (ran.ExitCode == -1) outcome = "stopped at the cap";
                else
                {
                    try
                    {
                        if (JsonNode.Parse(ran.Stdout) is JsonObject result)
                        {
                            foreach (var key in new[] { "total_cost_usd", "num_turns", "duration_ms", "usage", "subtype" }) usage[key] = result[key]?.DeepClone();
                            report = Files.Str(result["result"]);
                        }
                    }
                    catch (JsonException) { report = ran.Stdout.Length > 4000 ? ran.Stdout[^4000..] : ran.Stdout; }
                    if (ran.ExitCode != 0) outcome = "failed";
                    if ((ran.Stdout + ran.Stderr).Contains("Not logged in", StringComparison.Ordinal))
                    {
                        outcome = "not logged in";
                        report = "The headless claude CLI is not authenticated. Log it in once (`claude`, then /login, or ANTHROPIC_API_KEY), or start the round from a Claude session with the /probe skill, which caps it with `prober round-start` and stops it at the deadline.";
                    }
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            outcome = "no claude executable"; report = "No claude executable was found; set CLAUDE_BIN. Exploration needs a model and runs locally only.";
        }
        finally
        {
            var w = new StringWriter();
            Program.Run(new Context(ledger.Top, ctx.Environment, w, ctx.Error), ["repair", "--branch", ledger.Branch]); repaired = w.ToString().Trim();
            w = new StringWriter();
            Program.Run(new Context(ledger.Top, ctx.Environment, w, ctx.Error), ["round-end", "--branch", ledger.Branch, "--holder", holder]); roundEnded = w.ToString().Trim();
            w = new StringWriter();
            Program.Run(new Context(ledger.Top, ctx.Environment, w, ctx.Error), ["unlock", "--branch", ledger.Branch, "--holder", holder]); unlocked = w.ToString().Trim();
        }
        if (unlocked.Contains("replay was skipped", StringComparison.Ordinal))
            Replay.Run(new Context(ledger.Top, ctx.Environment, TextWriter.Null, ctx.Error), new Args(["--branch", ledger.Branch], new HashSet<string>(StringComparer.Ordinal)));
        var status = new StringWriter();
        Program.Run(new Context(ledger.Top, ctx.Environment, status, ctx.Error), ["status", "--branch", ledger.Branch]);
        var summary = new JsonObject
        {
            ["mode"] = mode, ["branch"] = ledger.Branch,
            ["caps"] = new JsonObject { ["minutes"] = minutes, ["claims"] = claims, ["usd"] = a.Get("usd") },
            ["outcome"] = outcome, ["exit"] = exit, ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1), ["usage"] = usage,
            ["round"] = roundEnded, ["repair"] = repaired, ["status"] = status.ToString().Split('\n')[0].Trim(),
        };
        var full = (JsonObject)summary.DeepClone(); full["report"] = report;
        Files.WriteJson(ledger.P("round-last.json"), full);
        ctx.Out.WriteLine(summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (report.Length > 0) ctx.Out.WriteLine("\n" + report);
        return outcome == "completed" ? 0 : 1;
    }

    /// <summary>$CLAUDE_BIN, else claude on PATH, else the desktop app's bundled copy.</summary>
    private static string FindClaude(Context ctx)
    {
        if (ctx.Env("CLAUDE_BIN") is { } bin) return bin;
        foreach (var dir in (ctx.Env("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var name in new[] { "claude", "claude.exe" })
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
        var bundled = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude-code");
        if (Directory.Exists(bundled))
        {
            var found = Directory.EnumerateFiles(bundled, "claude", SearchOption.AllDirectories)
                                 .Where(p => p.Contains("Contents/MacOS", StringComparison.Ordinal)).Order(StringComparer.Ordinal).LastOrDefault();
            if (found is not null) return found;
        }
        throw new System.ComponentModel.Win32Exception("no claude executable");
    }
}
