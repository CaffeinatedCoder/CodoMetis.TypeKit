using System.Text.Json;

namespace CodoMetis.TypeKit.Oracles;

/// <summary>
/// The known disagreements of an oracle, read from <c>test/oracles/candidates.json</c>, and the verdict a sweep
/// reaches once its failures are matched against them (design/adr/0112).
/// </summary>
/// <remarks>
/// <para>
/// An oracle sweep collects one line per disagreement and hands them here instead of asserting the list empty.
/// A line that matches no candidate fails. A line that matches a candidate whose issue is still empty fails too,
/// labelled <c>[known candidate: family]</c>, so nothing is quarantined before it is filed. A line that matches a
/// candidate with an issue is written as <c>SKIPPED [known candidate: family, issue]</c> and does not fail. A
/// candidate that no line matched fails with <c>candidate fixed: remove the entry</c>, so the registry cannot
/// outlive the defect it recorded.
/// </para>
/// <para>
/// A candidate matches a failure line when the line contains the candidate's <c>row</c> text (ordinal): the stable
/// prefix of the sweep's own line, the operands and the operation, never the actual value, which changes with the fix.
/// This file is linked into every test project that holds an oracle rather than referenced.
/// </para>
/// </remarks>
public static class Candidates
{
    /// <summary>
    /// One known disagreement: the oracle class, the row's stable text, its family and the issue that tracks it.
    /// <paramref name="Method"/> scopes the row to one test method of the class.
    /// </summary>
    public sealed record Candidate(string Oracle, string Row, string Family, string Issue, string? Method = null);

    private static readonly Lazy<IReadOnlyList<Candidate>> All_ = new(Load);

    /// <summary>Every registered candidate.</summary>
    public static IReadOnlyList<Candidate> All => All_.Value;

    /// <summary>Candidates registered for <paramref name="oracle"/>.</summary>
    public static IReadOnlyList<Candidate> For(string oracle) =>
        [.. All.Where(candidate => string.Equals(candidate.Oracle, oracle, StringComparison.Ordinal))];

    /// <summary>
    /// Passes when every failure line is a filed candidate and every candidate of the oracle still fails;
    /// otherwise fails with <paramref name="header"/> followed by the offending lines.
    /// </summary>
    /// <param name="oracle">The oracle class, as registered in <c>test/oracles/oracles.json</c>.</param>
    /// <param name="failures">The sweep's disagreements, one line each.</param>
    /// <param name="header">The first line of the failure message, naming the sweep.</param>
    /// <param name="shown">How many lines of the failure message to show.</param>
    /// <param name="method">The calling test method; a candidate scoped to another method is not this sweep's.</param>
    public static void Verdict(string oracle, IReadOnlyList<string> failures, string header, int shown = 25, string? method = null) =>
        Judge([.. For(oracle).Where(candidate => candidate.Method is null || method is null || string.Equals(candidate.Method, method, StringComparison.Ordinal))],
              failures, header, shown);

    /// <summary><see cref="Verdict"/> over the candidates given directly, as <c>SeedVerdictTests</c> drives it.</summary>
    internal static void Judge(IReadOnlyList<Candidate> known, IReadOnlyList<string> failures, string header, int shown = 25)
    {
        var failing = new List<string>();
        var unfiled = new List<string>();
        var matched = new HashSet<Candidate>();

        foreach (var line in failures)
        {
            var candidate = known.FirstOrDefault(c => line.Contains(c.Row, StringComparison.Ordinal));
            if (candidate is null)
            {
                failing.Add(OneLine(line));
                continue;
            }

            matched.Add(candidate);
            if (candidate.Issue.Length == 0)
                unfiled.Add($"{SeedVerdict.KnownMarker} {candidate.Family}] {OneLine(line.TrimStart())}");
            else
                Console.WriteLine($"SKIPPED [known candidate: {candidate.Family}, {candidate.Issue}] {OneLine(line.TrimStart())}");
        }

        foreach (var fixedCandidate in known.Where(candidate => !matched.Contains(candidate)))
            failing.Add($"candidate fixed: remove the entry for '{fixedCandidate.Row}' ({fixedCandidate.Family}) from test/oracles/candidates.json");

        // Known rows last, so a message cut short hides them first (SeedVerdict).
        failing.AddRange(unfiled);
        if (failing.Count == 0) return;

        Assert.Fail(
            $"{OneLine(header)} ({failing.Count} failures):" + Environment.NewLine
          + string.Join(Environment.NewLine, failing.Take(shown))
          + (failing.Count > shown ? $"{Environment.NewLine}{SeedVerdict.MorePrefix}{failing.Count - shown} more" : ""));
    }

    /// <summary>A row as one line, line breaks shown as ⏎: the message is one row per line, and SeedVerdict reads it so.</summary>
    private static string OneLine(string text) =>
        text.Contains('\n') || text.Contains('\r') ? text.Replace("\r\n", "⏎").Replace('\n', '⏎').Replace('\r', '⏎') : text;

    private static IReadOnlyList<Candidate> Load()
    {
        var path = Path.Combine(RepositoryRoot(), "test", "oracles", "candidates.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<Candidate>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidOperationException($"'{path}' holds no array.");
    }

    private static string RepositoryRoot()
    {
        const string marker = "CodoMetis.TypeKit.slnx";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, marker))) return directory.FullName;
        }

        throw new InvalidOperationException($"Could not find '{marker}' above '{AppContext.BaseDirectory}'.");
    }
}
