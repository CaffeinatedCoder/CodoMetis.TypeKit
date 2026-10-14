namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>Map</c> and <c>Traverse</c> over a <see cref="Patch{T}"/>: a field that carried a value is
/// mapped or validated, and a clear or an absent field passes through as it is. A patch over what the
/// request sent becomes a patch over the domain's type, with the validation's own fault.
/// </summary>
public sealed class PatchCombinationTests
{
    private static Result<int, string> Positive(int n) => n > 0 ? n : Result.Error($"{n} is not positive");

    private static string Show<T>(Patch<T> patch) where T : notnull => patch.Match(value => $"Value({value})", () => "Null", () => "Absent");

    [Fact]
    public void Map_changes_the_value_and_keeps_a_clear_and_an_absence()
    {
        Show(Patch.Value(2).Map(n => n * 10)).ShouldBe("Value(20)");
        Show(Patch.Null<int>().Map(n => n * 10)).ShouldBe("Null");
        Show(Patch.Absent<int>().Map(n => n * 10)).ShouldBe("Absent");

        Show(Patch.Value("4").Map(int.Parse)).ShouldBe("Value(4)");
    }

    [Fact]
    public void Map_runs_the_selector_for_a_value_only()
    {
        var calls = 0;

        Patch.Null<int>().Map(n => ++calls);
        Patch.Absent<int>().Map(n => ++calls);
        calls.ShouldBe(0);

        Patch.Value(1).Map(n => ++calls);
        calls.ShouldBe(1);
    }

    /// <summary>A clear is the Null state, never a null value: a selector that answers null is refused, as <c>Option.Map</c>'s is.</summary>
    [Fact]
    public void Map_refuses_a_selector_that_returns_null()
    {
        var refusal = Should.Throw<ArgumentNullException>(() => Patch.Value(1).Map(_ => (string)null!));

        refusal.ParamName.ShouldBe("selector");
        refusal.Message.ShouldContain("Patch.Null()");
    }

    [Fact]
    public void Traverse_validates_a_value_and_keeps_its_fault()
    {
        Result<Patch<int>, string> valid = Patch.Value(3).Traverse(Positive);
        valid.Match(Show, error => error).ShouldBe("Value(3)");

        Result<Patch<int>, string> refused = Patch.Value(-3).Traverse(Positive);
        refused.Match(Show, error => error).ShouldBe("-3 is not positive");
    }

    [Fact]
    public void Traverse_passes_a_clear_and_an_absence_through_as_successes()
    {
        var calls = 0;
        Result<int, string> Counted(int n) { calls++; return Positive(n); }

        Patch.Null<int>().Traverse(Counted).Match(Show, error => error).ShouldBe("Null");
        Patch.Absent<int>().Traverse(Counted).Match(Show, error => error).ShouldBe("Absent");
        calls.ShouldBe(0, "there is nothing to validate");
    }

    /// <summary>The usual selector is a value object's <c>Create</c>, a method group over the wrapped type.</summary>
    [Fact]
    public void Traverse_takes_a_factory_as_a_method_group()
    {
        static Result<string, string> Trimmed(string text) => text.Trim().Length > 0 ? text.Trim() : Result.Error("blank");

        Patch.Value(" a ").Traverse(Trimmed).Match(Show, error => error).ShouldBe("Value(a)");
        Patch.Value("  ").Traverse(Trimmed).Match(Show, error => error).ShouldBe("blank");
    }
}
