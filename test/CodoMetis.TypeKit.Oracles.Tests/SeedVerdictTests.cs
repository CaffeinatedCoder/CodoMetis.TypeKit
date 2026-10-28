namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// The rule <c>oracles seeds</c> judges a failed test by (<see cref="SeedVerdict"/>), fed the messages
/// <see cref="Candidates.Verdict"/> writes: a seed is caught when one row of a failed test is not a known candidate,
/// whatever else shares its message.
/// </summary>
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

    private static string MessageOf(IReadOnlyList<Candidates.Candidate> known, IReadOnlyList<string> failures, int shown = 25) =>
        Should.Throw<Exception>(() => Candidates.Judge(known, failures, "a sweep", shown)).Message;
}
