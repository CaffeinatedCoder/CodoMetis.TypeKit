namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="Option{T}"/> itself: presence, routing and composition. The conversion and LINQ
/// surface has its own class in <see cref="OptionExtensionsTests"/>.
/// </summary>
public sealed class OptionTests
{
    [Fact]
    public void Some_has_a_value_and_none_does_not()
    {
        Option.Some(5).IsSome().ShouldBeTrue();
        Option.Some(5).IsNone().ShouldBeFalse();
        Option.None<int>().IsSome().ShouldBeFalse();
        Option.None<int>().IsNone().ShouldBeTrue();
    }

    [Fact]
    public void Match_routes_by_presence()
    {
        Option.Some(21).Match(x => x    * 2, () => -1).ShouldBe(42);
        Option.None<int>().Match(x => x * 2, () => -1).ShouldBe(-1);
    }

    [Fact]
    public void Map_transforms_only_a_present_value()
    {
        Option.Some(21).Map(x => x * 2).ShouldBe(Option.Some(42));

        var calls = 0;
        Option.None<int>().Map(_ => calls++).IsNone().ShouldBeTrue();
        calls.ShouldBe(0);
    }

    [Fact]
    public void Bind_chains_and_short_circuits()
    {
        Option.Some(2).Bind(x => Option.Some(x * 10)).ShouldBe(Option.Some(20));
        Option.Some(2).Bind(_ => Option.None<int>()).IsNone().ShouldBeTrue();

        var calls = 0;
        Option.None<int>().Bind(x =>
        {
            calls++;
            return Option.Some(x);
        });
        calls.ShouldBe(0);
    }

    [Fact]
    public void Filter_keeps_only_what_passes()
    {
        Option.Some(5).Filter(x => x > 1).ShouldBe(Option.Some(5));
        Option.Some(5).Filter(x => x > 9).IsNone().ShouldBeTrue();
        Option.None<int>().Filter(_ => true).IsNone().ShouldBeTrue();
    }

    [Fact]
    public void Coalesce_substitutes_only_for_none()
    {
        Option.Some(5).Coalesce(9).ShouldBe(5);
        Option.None<int>().Coalesce(9).ShouldBe(9);
    }

    [Fact]
    public void TryGetValue_unwraps_by_presence()
    {
        Option.Some(5).TryGetValue(out var value).ShouldBeTrue();
        value.ShouldBe(5);

        Option.None<int>().TryGetValue(out _).ShouldBeFalse();
    }

    [Fact]
    public void Tap_sees_only_a_present_value()
    {
        var seen = new List<int>();

        Option.Some(5).Tap(seen.Add);
        Option.None<int>().Tap(seen.Add);

        seen.ShouldBe([5]);
    }

    [Fact]
    public void AsEnumerable_yields_the_value_once_or_nothing()
    {
        Option.Some(5).AsEnumerable().ShouldHaveSingleItem().ShouldBe(5);
        Option.None<int>().AsEnumerable().ShouldBeEmpty();
    }

    [Fact]
    public void Options_compare_by_value()
    {
        Option.Some(1).ShouldBe(Option.Some(1));
        Option.Some(1).ShouldNotBe(Option.Some(2));
        Option.Some(1).ShouldNotBe(Option.None<int>());
        Option.None<int>().ShouldBe(Option.None<int>());
    }

    /// <summary>
    /// The analyzer forbids writing an option default, but one still arises structurally: an array
    /// slot, a zeroed field. "No value" is exactly what a zeroed option encodes, so it degrades to
    /// <c>None</c> and composes. Unlike <see cref="Result{TError}"/> it needs no uninitialized state.
    /// </summary>
    [Fact]
    public void A_default_option_is_none()
    {
        default(Option<int>).ShouldBe(Option.None<int>());
        default(Option<string>).IsNone().ShouldBeTrue();
    }

    /// <summary>
    /// <c>notnull</c> is an annotation the runtime does not enforce: <c>Some(null!)</c> produced an
    /// option that reported a value and handed null out of <c>TryGetValue</c>, despite
    /// <c>[NotNullWhen(true)]</c>. So did a <c>Map</c> whose selector returned null.
    /// </summary>
    [Fact]
    public void Some_refuses_null()
    {
        Should.Throw<ArgumentNullException>(() => Option.Some<string>(null!));
        Should.Throw<ArgumentNullException>(() => Option.Some("x").Map(_ => (string)null!));
    }
}
