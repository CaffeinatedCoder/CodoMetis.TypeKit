using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodoMetis.TypeKit.Oracles;

/// <summary>
/// Whether an oracle caught a seed: the rule <c>oracles seeds</c> applies, compiled into <c>tools/Oracles</c> and
/// linked into <c>CodoMetis.TypeKit.Oracles.Tests</c> (<c>SeedVerdictTests</c>), so the two cannot disagree
/// (design/adr/0112, design/adr/0117).
/// </summary>
/// <remarks>
/// <para>
/// A seed is caught when its oracle passes without it and fails with it, in a test of its own. Each oracle class is
/// run twice with the same filter in the same environment: once on a copy of the tree with no seed (the baseline),
/// once with the seed planted, and each run is <see cref="Read"/> from the exit code of <c>dotnet test</c> and its
/// TRX. A run that did not reach the tests' own code is an <see cref="Run.Error"/>, and an error on either side makes
/// the pair ERROR, never CAUGHT: a dead fixture fails every test, with or without a seed.
/// </para>
/// <para>
/// What the TRX holds, measured with xunit.v3 4.0.2 on Microsoft Testing Platform (2026-10-29): a class, collection
/// or assembly fixture that fails to construct or to initialize fails every test that uses it, each with xunit's own
/// message, <c>Collection fixture type 'T' threw in InitializeAsync</c>, the fixture's exception below it
/// (<see cref="IsSetupFailure"/>). A fixture that throws in <c>Dispose</c> fails no test and is counted only as an
/// <c>error</c> in the TRX's counters. A test host that crashes writes no TRX and exits with a code that is not
/// Microsoft Testing Platform's 0 (passed) or 2 (a test failed). A failure in the test's own constructor or
/// <c>InitializeAsync</c> is written like one in its body, the exception's message and the stack trace below the
/// test's code, so it counts as the test's: the oracles build what they assert on there.
/// </para>
/// <para>
/// A sweep reports through <c>Candidates.Verdict</c>: a header line, then one line per failing row, where a
/// row that matches an unfiled candidate starts with <see cref="KnownMarker"/>. A failed test catches the
/// seed when at least one of its rows is not a known candidate (<see cref="Catches"/>). Counting a test as known
/// whenever its message held the marker anywhere read a seed that broke a row beside a known one as missed, as
/// CodoMetis.ValueRanges found (its #55). A baseline whose only failures are known candidates passes.
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

    /// <summary>The exit codes of a run that reached its tests: Microsoft Testing Platform's success and "a test failed".</summary>
    private static readonly int[] RanExitCodes = [0, 2];

    /// <summary>The TRX counters of a result outside any test, such as a fixture's <c>Dispose</c>, or of a run cut short.</summary>
    private static readonly string[] ErrorCounters = ["error", "aborted", "timeout", "disconnected"];

    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>
    /// xunit's own messages for a test it could not set up, every one in xunit.v3.core 4.0.2 (its fixture kind is
    /// <c>Assembly</c>, <c>Collection</c> or <c>Class</c>).
    /// </summary>
    private static readonly Regex SetupMessage = new(
        @"\A(\w+ fixture type '[^']*' (threw in |had one or more unresolved constructor arguments|may only define a single public constructor)"
        + @"|The following constructor parameters did not have matching fixture data"
        + @"|Cannot create type '[^']*' due to missing constructor arguments)",
        RegexOptions.CultureInvariant);

    /// <summary>One run of one oracle class, read from the exit code of <c>dotnet test</c> and its TRX.</summary>
    /// <param name="Executed">The tests that ran, passed or failed; a skipped test is neither.</param>
    /// <param name="Failed">The tests that failed.</param>
    /// <param name="Caught">The failed tests with a row that is not a known candidate.</param>
    /// <param name="Example">The first test in <paramref name="Caught"/>, or empty.</param>
    /// <param name="Error">Why the run never reached the tests' own code, or <see langword="null"/> when it did.</param>
    public sealed record Run(int Executed, int Failed, int Caught, string Example, string? Error);

    /// <summary>Whether a failed test's message holds at least one row that is not a known candidate.</summary>
    public static bool Catches(string message)
    {
        var lines = message.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (!lines.Any(IsKnown)) return true;

        // A Verdict message: the first line is its header, the last may say how many rows were cut.
        return lines.Skip(1).Any(line => !IsKnown(line) && !line.StartsWith(MorePrefix, StringComparison.Ordinal));
    }

    /// <summary>Whether a failed test's message is xunit's own for a fixture or a constructor it could not set up.</summary>
    public static bool IsSetupFailure(string message) => SetupMessage.IsMatch(message);

    /// <summary>Reads one run of <paramref name="oracle"/>: the exit code of <c>dotnet test</c> and its TRX, if it wrote one.</summary>
    public static Run Read(int exitCode, XDocument? trx, string oracle)
    {
        if (trx is null) return Failure($"no test results written: the test host exited {exitCode}");
        if (!RanExitCodes.Contains(exitCode)) return Failure($"the test host exited {exitCode}, not 0 or 2");

        var counters = trx.Descendants(Trx + "Counters").FirstOrDefault();
        var outside = ErrorCounters.Select(name => (Name: name, Count: (int?)counters?.Attribute(name) ?? 0)).Where(counter => counter.Count > 0).ToList();
        if (outside.Count > 0) return Failure($"the run counts {string.Join(", ", outside.Select(counter => $"{counter.Count} {counter.Name}"))} outside any test (a fixture's Dispose, a host cut short)");

        var results = trx.Descendants(Trx + "UnitTestResult")
                         .Select(result => (Outcome: (string?)result.Attribute("outcome"), Test: (string?)result.Attribute("testName") ?? "?", Message: result.Descendants(Trx + "Message").FirstOrDefault()?.Value ?? ""))
                         .Where(result => result.Outcome is "Passed" or "Failed")
                         .ToList();
        if (results.Count == 0) return Failure($"no test of {oracle} ran");

        var failed = results.Where(result => result.Outcome == "Failed").ToList();
        var setup = failed.Where(result => IsSetupFailure(result.Message)).ToList();
        if (setup.Count > 0) return Failure($"{setup.Count} of {results.Count} tests failed in setup, not in the test: {setup[0].Message.Split('\n')[0].Trim()}");

        var caught = failed.Where(result => Catches(result.Message)).ToList();
        return new Run(results.Count, failed.Count, caught.Count, caught.Count > 0 ? caught[0].Test : "", Error: null);

        static Run Failure(string error) => new(0, 0, 0, "", error);
    }

    /// <summary>
    /// The verdict on one seed and one oracle: <c>CAUGHT</c>, <c>MISSED</c> or <c>ERROR</c>, padded to eight
    /// characters, then what was seen.
    /// </summary>
    public static string Judge(Run baseline, Run seeded)
    {
        if (baseline.Error is not null) return $"ERROR   without the seed, {baseline.Error}";
        if (baseline.Caught > 0) return $"ERROR   {baseline.Caught} of {baseline.Executed} tests fail without the seed, e.g. {baseline.Example}";
        if (seeded.Error is not null) return $"ERROR   with the seed, {seeded.Error}";
        if (seeded.Caught > 0) return $"CAUGHT  {seeded.Caught} of {seeded.Executed} tests fail, e.g. {seeded.Example}";
        return seeded.Failed == 0
            ? $"MISSED  all {seeded.Executed} tests pass with the defect planted"
            : $"MISSED  {seeded.Failed} of {seeded.Executed} tests fail, every row a known candidate";
    }

    private static bool IsKnown(string line) => line.StartsWith(KnownMarker, StringComparison.Ordinal);
}
