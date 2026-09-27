using CodoMetis.TypeKit.Generators.Probes;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>That <c>TryFrom</c> is derived from <c>Create</c> rather than merely present beside it.</summary>
/// <remarks>
/// A build proves the generated member exists; it cannot prove the two factories agree, which is the
/// whole claim. Every case compares the generated answer with the hand-written one on the same input
/// rather than with a literal, so a generation that dropped the trimming or inverted the branch
/// fails even though it compiles.
/// </remarks>
public sealed class ValidatedValueTests
{
    public static TheoryData<string> Inputs => ["ABC", "  ABC  ", "ABCDEF", "", "   ", "AB", "abc", " aB ", "​ABC"];

    [Theory]
    [MemberData(nameof(Inputs))]
    public void The_generated_TryFrom_is_Create_with_the_fault_dropped(string input) =>
        ProbeCode.TryFrom(input).ShouldBe(ProbeCode.Create(input).ToOption());

    /// <summary><c>Create</c> trims, so <c>TryFrom</c> trims: there is only one implementation of the rule.</summary>
    [Fact]
    public void The_generated_TryFrom_carries_the_normalisation_Create_applies()
    {
        ProbeCode.TryFrom("  ABC  ").TryGetValue(out var code).ShouldBeTrue();
        code.Value.ShouldBe("ABC");
    }

    [Theory]
    [InlineData("", ProbeCodeFault.Blank)]
    [InlineData("   ", ProbeCodeFault.Blank)]
    [InlineData("AB", ProbeCodeFault.TooShort)]
    [InlineData("abc", ProbeCodeFault.NotUpperCase)]
    public void Create_names_the_rule_that_refused_the_value(string input, ProbeCodeFault expected)
    {
        ProbeCode.Create(input).TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBe(expected);
    }

    /// <summary>
    /// A hand-written <c>TryFrom</c> suppresses the generated one rather than colliding with it.
    /// <see cref="ProbeOverride"/>'s refuses everything, so the assertion cannot pass by accident.
    /// </summary>
    [Fact]
    public void A_hand_written_TryFrom_wins_over_the_generated_one()
    {
        ProbeOverride.Create("ABC").TryGetValue(out _, out _).ShouldBeTrue();
        ProbeOverride.TryFrom("ABC").IsNone().ShouldBeTrue();
    }
}

/// <summary>
/// That <c>FromKnownGood</c> is <c>Create</c> with the fault carried into a throw, and that the throw
/// names the caller's expression rather than the caller's value.
/// </summary>
public sealed class KnownGoodFactoryTests
{
    [Fact]
    public void FromKnownGood_answers_what_Create_accepted()
    {
        ProbeCode.Create("  ABC  ").TryGetValue(out var accepted, out _).ShouldBeTrue();

        ProbeCode.FromKnownGood("  ABC  ").ShouldBe(accepted);
    }

    [Fact]
    public void The_throw_names_the_type_and_the_rule_that_refused_the_value()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => ProbeCode.FromKnownGood("AB"));

        thrown.Message.ShouldContain(nameof(ProbeCode));
        thrown.Message.ShouldContain(nameof(ProbeCodeFault.TooShort));
    }

    /// <summary>
    /// <see cref="ProbeCode"/>'s <c>FromKnownGood</c> is generated in the probes assembly and called
    /// from this one, so the caller-expression substitution has to survive into metadata. The quotes
    /// are the assertion: they are in the source text of the argument, not in the value.
    /// </summary>
    [Fact]
    public void The_throw_carries_the_caller_expression_across_an_assembly_boundary()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => ProbeCode.FromKnownGood("ab"));

        thrown.Message.ShouldContain("\"ab\"");
    }

    [Fact]
    public void The_throw_carries_the_expression_and_not_the_value_behind_it()
    {
        var secret = "a-refused-secret";

        var thrown = Should.Throw<InvalidOperationException>(() => ProbeCode.FromKnownGood(secret));

        thrown.Message.ShouldContain(nameof(secret));
        thrown.Message.ShouldNotContain("a-refused-secret");
    }
}
