using System.Xml.Linq;

namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// The rule <c>oracles seeds</c> judges a seed by (<see cref="SeedVerdict"/>): a seed is caught when its oracle passes
/// without it and one of its tests fails with it, with a row that is not a known candidate, whatever else shares its
/// message, and never by a failure xunit wrote for a fixture it could not set up or by a run that did not reach the
/// tests (design/adr/0117).
/// </summary>
/// <remarks>
/// The TRX documents here have the shape xunit.v3 4.0.2 writes through <c>--report-xunit-trx</c>, measured on
/// 2026-10-29 with one class per kind of failure: the messages are xunit's own, a fixture's <c>Dispose</c> that throws
/// is a counter and no result, a crashed host writes no file.
/// </remarks>
public sealed class SeedVerdictTests
{
    private static readonly Candidates.Candidate Unfiled = new("AnyOracle", "row 1:", "a known disagreement", Issue: "");

    [Fact]
    public void A_message_with_a_known_row_and_another_row_catches() =>
        SeedVerdict.Catches(MessageOf([Unfiled], ["row 1: expected 1, got 2", "row 2: expected 1, got 3"])).ShouldBeTrue();

    [Fact]
    public void A_message_whose_every_row_is_known_does_not_catch() =>
        SeedVerdict.Catches(MessageOf([Unfiled], ["row 1: expected 1, got 2"])).ShouldBeFalse();

    /// <summary>The other rows come first, so a message cut short still shows one; known rows are what is cut.</summary>
    [Fact]
    public void A_message_cut_short_still_shows_the_row_that_catches()
    {
        var message = MessageOf([Unfiled], [.. Enumerable.Range(0, 30).Select(i => $"row 1: known variant {i}"), "row 2: expected 1, got 3"], shown: 5);

        message.ShouldContain("… and 26 more");
        SeedVerdict.Catches(message).ShouldBeTrue();
    }

    [Fact]
    public void A_message_cut_short_whose_every_row_is_known_does_not_catch()
    {
        var message = MessageOf([Unfiled], [.. Enumerable.Range(0, 30).Select(i => $"row 1: known variant {i}")], shown: 5);

        message.ShouldContain("… and 25 more");
        SeedVerdict.Catches(message).ShouldBeFalse();
    }

    /// <summary>A known row whose text spans lines is still one known row: its continuation has no marker and read as a caught row.</summary>
    [Fact]
    public void A_known_row_spanning_lines_does_not_catch()
    {
        var unfiled = new Candidates.Candidate("AnyOracle", "text {1,", "a known disagreement", Issue: "");
        var message = MessageOf([unfiled], ["text {1,\n2}: expected {1,2}, refused"]);

        SeedVerdict.Catches(message).ShouldBeFalse(message);
        message.ShouldContain("{1,⏎2}");
    }

    /// <summary>A seed that makes a known row agree changes what the sweep answers, which is a catch.</summary>
    [Fact]
    public void A_candidate_fixed_catches() => SeedVerdict.Catches(MessageOf([Unfiled], [])).ShouldBeTrue();

    [Theory]
    [InlineData("Shouldly.ShouldAssertException: value should be 1 but was 2")]
    [InlineData("Assert.Fail: the text '[known candidate: x]' appears inside a line, not as a row")]
    public void Any_other_assertion_catches(string message) => SeedVerdict.Catches(message).ShouldBeTrue();

    [Fact]
    public void A_sweep_with_no_failure_and_no_candidate_passes() =>
        Should.NotThrow(() => Candidates.Judge([], [], "a sweep"));

    /// <summary>What SQL Server 2022 under QEMU wrote for every test of a SQL Server oracle (#151).</summary>
    private const string DeadSqlServer =
        "Collection fixture type 'CodoMetis.TypeKit.EntityFrameworkCore.Tests.SqlServer' threw in InitializeAsync\n" +
        "---- DotNet.Testcontainers.Containers.ContainerNotRunningException : Container is not running.";

    // Before Passing: static fields initialize in the order they are written.
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private static readonly SeedVerdict.Run Passing = Read(Result("Passed"), Result("Passed"));

    /// <summary>The defect of #151: a dead fixture failed every test with and without the seed, and the seed read as caught.</summary>
    [Fact]
    public void A_fixture_that_fails_to_initialize_is_an_error_with_or_without_the_seed()
    {
        var dead = Read(Result("Failed", DeadSqlServer), Result("Failed", DeadSqlServer));

        dead.Error.ShouldNotBeNull().ShouldContain("2 of 2 tests failed in setup");
        SeedVerdict.Judge(dead, dead).ShouldStartWith("ERROR   without the seed");
        SeedVerdict.Judge(Passing, dead).ShouldStartWith("ERROR   with the seed");
    }

    /// <summary>A seed that breaks a fixture is not credited either: only a test of the oracle's own catches.</summary>
    [Fact]
    public void A_fixture_failure_beside_a_caught_test_is_an_error()
    {
        var seeded = Read(Result("Failed", "Shouldly.ShouldAssertException : value should be 1 but was 2"), Result("Failed", DeadSqlServer));

        SeedVerdict.Judge(Passing, seeded).ShouldStartWith("ERROR   with the seed");
    }

    /// <summary>Every message xunit.v3.core 4.0.2 holds for a test it could not set up.</summary>
    [Theory]
    [InlineData(DeadSqlServer)]
    [InlineData("Class fixture type 'Probe.ThrowingCtorFixture' threw in its constructor\n---- System.InvalidOperationException : fixture ctor threw")]
    [InlineData("Class fixture type 'Probe.ThrowingInitFixture' threw in InitializeAsync\n---- System.InvalidOperationException : threw")]
    [InlineData("Assembly fixture type 'Probe.Fixture' threw in InitializeAsync\n---- System.Exception : threw")]
    [InlineData("Collection fixture type 'Probe.Fixture' had one or more unresolved constructor arguments: Probe.Other other")]
    [InlineData("Class fixture type 'Probe.Fixture' may only define a single public constructor.")]
    [InlineData("The following constructor parameters did not have matching fixture data: SqlServer server")]
    [InlineData("Cannot create type 'Probe.Fixture' due to missing constructor arguments: Probe.Other")]
    public void A_message_xunit_writes_for_a_fixture_it_could_not_set_up_is_setup(string message) =>
        SeedVerdict.IsSetupFailure(message).ShouldBeTrue();

    /// <summary>An assertion, an exception from the code a test calls, and a sweep's verdict are the test's own failures.</summary>
    [Theory]
    [InlineData("Shouldly.ShouldAssertException : [Fact] public void Shouldly_fails1\n    should be\n2\n    but was\n1")]
    [InlineData("Assert.Equal() Failure: Values differ\nExpected: 1\nActual:   2")]
    [InlineData("System.InvalidOperationException : product threw")]
    [InlineData("a sweep: 3 of 170 rows disagree\n(shape: TptRoot, kind: \"ushort\"): the key reads back as 0")]
    [InlineData("row 1: the Collection fixture type 'X' threw in InitializeAsync, quoted by a test")]
    public void A_failure_in_the_test_itself_is_not_setup(string message) => SeedVerdict.IsSetupFailure(message).ShouldBeFalse();

    [Fact]
    public void An_oracle_that_fails_without_the_seed_is_an_error()
    {
        var failing = Read(Result("Passed"), Result("Failed", "Assert.Equal() Failure: Values differ"));

        SeedVerdict.Judge(failing, failing).ShouldBe("ERROR   1 of 2 tests fail without the seed, e.g. Probe.Oracle.T1");
    }

    [Fact]
    public void A_test_failing_only_with_the_seed_catches() =>
        SeedVerdict.Judge(Passing, Read(Result("Passed"), Result("Failed", "Assert.Equal() Failure: Values differ")))
                   .ShouldBe("CAUGHT  1 of 2 tests fail, e.g. Probe.Oracle.T1");

    [Fact]
    public void An_oracle_that_passes_with_the_seed_misses_it() =>
        SeedVerdict.Judge(Passing, Passing).ShouldBe("MISSED  all 2 tests pass with the defect planted");

    /// <summary>An unfiled candidate fails its sweep without a seed; the baseline passes, and the seed is judged by its other rows.</summary>
    [Fact]
    public void A_baseline_failing_only_on_known_candidates_passes()
    {
        var known = Read(Result("Passed"), Result("Failed", MessageOf([Unfiled], ["row 1: expected 1, got 2"])));
        var seeded = Read(Result("Passed"), Result("Failed", MessageOf([Unfiled], ["row 1: expected 1, got 2", "row 2: expected 1, got 3"])));

        known.Error.ShouldBeNull();
        SeedVerdict.Judge(known, known).ShouldBe("MISSED  1 of 2 tests fail, every row a known candidate");
        SeedVerdict.Judge(known, seeded).ShouldStartWith("CAUGHT");
    }

    /// <summary>A fixture whose Dispose throws fails no test; the TRX counts it as an error outside any result.</summary>
    [Fact]
    public void An_error_counted_outside_any_test_is_an_error() =>
        SeedVerdict.Read(2, Trx([Result("Passed")], error: 1), "Probe.Oracle").Error.ShouldNotBeNull().ShouldContain("1 error outside any test");

    /// <summary>A host that crashes writes no TRX; one that exits otherwise than 0 or 2 did not finish its tests.</summary>
    [Theory]
    [InlineData(134, false)]
    [InlineData(7, true)]
    [InlineData(3, true)]
    public void A_host_that_did_not_finish_is_an_error(int exitCode, bool wroteResults) =>
        SeedVerdict.Read(exitCode, wroteResults ? Trx([Result("Failed", "Assert.Equal() Failure: Values differ")]) : null, "Probe.Oracle")
                   .Error.ShouldNotBeNull().ShouldContain($"exited {exitCode}");

    [Fact]
    public void A_run_whose_every_test_is_skipped_is_an_error() =>
        SeedVerdict.Read(0, Trx([Result("NotExecuted"), Result("NotExecuted")]), "Probe.Oracle").Error.ShouldBe("no test of Probe.Oracle ran");

    private static SeedVerdict.Run Read(params XElement[] results) =>
        SeedVerdict.Read(results.Any(result => (string?)result.Attribute("outcome") == "Failed") ? 2 : 0, Trx(results), "Probe.Oracle");

    private static XElement Result(string outcome, string? message = null) =>
        new(Ns + "UnitTestResult", new XAttribute("testName", "?"), new XAttribute("outcome", outcome),
            message is null ? null : new XElement(Ns + "Output", new XElement(Ns + "ErrorInfo", new XElement(Ns + "Message", message), new XElement(Ns + "StackTrace", "   at Probe.Oracle.T()"))));

    private static XDocument Trx(IReadOnlyList<XElement> results, int error = 0)
    {
        var named = results.Select(result => new XElement(result)).ToList();
        for (var i = 0; i < named.Count; i++) named[i].SetAttributeValue("testName", $"Probe.Oracle.T{i}");

        int Count(string outcome) => named.Count(result => (string?)result.Attribute("outcome") == outcome);
        return new XDocument(new XElement(Ns + "TestRun",
            new XElement(Ns + "Results", named),
            new XElement(Ns + "ResultSummary", new XAttribute("outcome", Count("Failed") > 0 || error > 0 ? "Failed" : "Completed"),
                new XElement(Ns + "Counters",
                    new XAttribute("total", named.Count), new XAttribute("executed", Count("Passed") + Count("Failed")),
                    new XAttribute("passed", Count("Passed")), new XAttribute("failed", Count("Failed")), new XAttribute("error", error),
                    new XAttribute("timeout", 0), new XAttribute("aborted", 0), new XAttribute("notExecuted", Count("NotExecuted")), new XAttribute("disconnected", 0)))));
    }

    private static string MessageOf(IReadOnlyList<Candidates.Candidate> known, IReadOnlyList<string> failures, int shown = 25) =>
        Should.Throw<Exception>(() => Candidates.Judge(known, failures, "a sweep", shown)).Message;
}
