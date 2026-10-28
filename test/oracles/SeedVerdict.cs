namespace CodoMetis.TypeKit.Oracles;

/// <summary>
/// Whether a test that failed with a seed planted caught it: the rule <c>oracles seeds</c> applies to each
/// failed test, compiled into <c>tools/Oracles</c> and linked into <c>CodoMetis.TypeKit.Oracles.Tests</c>
/// (<c>SeedVerdictTests</c>), so the two cannot disagree (design/adr/0112).
/// </summary>
/// <remarks>
/// <para>
/// A sweep reports through <c>Candidates.Verdict</c>: a header line, then one line per failing row, where a
/// row that matches an unfiled candidate starts with <see cref="KnownMarker"/>. A failed test catches the
/// seed when at least one of its rows is not a known candidate. Counting a test as known whenever its
/// message held the marker anywhere read a seed that broke a row beside a known one as missed, as
/// CodoMetis.ValueRanges found (its #55).
/// </para>
/// <para>
/// <c>Candidates.Verdict</c> lists the other rows before the known ones, so a message cut short
/// (<c>… and N more</c>) hides known rows first: when every row shown is known, the hidden ones are too.
/// A message with no known row at all is any other assertion, and catches.
/// </para>
/// </remarks>
public static class SeedVerdict
{
    /// <summary>The prefix <c>Candidates.Verdict</c> gives a row that matches an unfiled candidate.</summary>
    public const string KnownMarker = "[known candidate:";

    /// <summary>The line <c>Candidates.Verdict</c> ends a cut-short message with.</summary>
    public const string MorePrefix = "… and ";

    /// <summary>Whether a failed test's message holds at least one row that is not a known candidate.</summary>
    public static bool Catches(string message)
    {
        var lines = message.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (!lines.Any(IsKnown)) return true;

        // A Verdict message: the first line is its header, the last may say how many rows were cut.
        return lines.Skip(1).Any(line => !IsKnown(line) && !line.StartsWith(MorePrefix, StringComparison.Ordinal));
    }

    private static bool IsKnown(string line) => line.StartsWith(KnownMarker, StringComparison.Ordinal);
}
