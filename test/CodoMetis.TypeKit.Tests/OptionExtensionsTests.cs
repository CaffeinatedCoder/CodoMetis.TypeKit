namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// The composition surface around <see cref="Option{T}"/>: the LINQ vocabulary, the zips, and the
/// conversions in and out of nullables, sequences and results.
/// </summary>
public sealed class OptionExtensionsTests
{
    /// <summary><c>Select</c>/<c>Where</c> exist so an option composes in query syntax.</summary>
    [Fact]
    public void Query_syntax_maps_and_filters()
    {
        (from x in Option.Some(21) where x > 20 select x * 2).ShouldBe(Option.Some(42));
        (from x in Option.Some(21) where x > 21 select x * 2).IsNone().ShouldBeTrue();
    }

    [Fact]
    public void SelectMany_flattens_like_bind()
    {
        Option.Some(2).SelectMany(x => Option.Some(x * 10)).ShouldBe(Option.Some(20));
        Option.Some(2).SelectMany(_ => Option.None<int>()).IsNone().ShouldBeTrue();
        Option.None<int>().SelectMany(x => Option.Some(x)).IsNone().ShouldBeTrue();
    }

    [Fact]
    public void Zip_combines_only_when_every_side_has_a_value()
    {
        Option.Some(1).Zip(Option.Some(2), (a, b) => a + b).ShouldBe(Option.Some(3));
        Option.Some(1).Zip(Option.None<int>(), (a, b) => a + b).IsNone().ShouldBeTrue();
        Option.None<int>().Zip(Option.Some(2), (a, b) => a + b).IsNone().ShouldBeTrue();
    }

    /// <summary>
    /// The top of the arity ladder, pinned so the mechanically repeated overloads between two and
    /// six stay on the same all-or-none rule.
    /// </summary>
    [Fact]
    public void The_widest_zip_follows_the_same_all_or_none_rule()
    {
        Option.Some(1)
              .Zip(Option.Some(2), Option.Some(3), Option.Some(4), Option.Some(5), Option.Some(6),
                   (a, b, c, d, e, f) => a + b + c + d + e + f)
              .ShouldBe(Option.Some(21));

        Option.Some(1)
              .Zip(Option.Some(2), Option.Some(3), Option.None<int>(), Option.Some(5), Option.Some(6),
                   (a, b, c, d, e, f) => a + b + c + d + e + f)
              .IsNone()
              .ShouldBeTrue();
    }

    /// <summary>
    /// The bridge from "not there" to "and that is an error": absence gains the caller's error
    /// value, in both the valued and the error-only shape.
    /// </summary>
    [Fact]
    public void ToResult_turns_none_into_the_given_error()
    {
        Option.Some(21).ToResult(x => x * 2, "missing").TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(42);

        Option.None<int>().ToResult(x => x * 2, "missing").TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("missing");

        ((bool)Option.Some(21).ToResult("missing")).ShouldBeTrue();
        Option.None<int>().ToResult("missing").TryGetError(out var bare).ShouldBeTrue();
        bare.ShouldBe("missing");
    }

    [Fact]
    public void OrDefault_and_OrNull_unwrap_with_the_type_shaped_fallback()
    {
        Option.Some(5).OrDefault().ShouldBe(5);
        Option.None<int>().OrDefault().ShouldBe(0);

        Option.Some("value").OrNull().ShouldBe("value");
        Option.None<string>().OrNull().ShouldBeNull();
    }

    [Fact]
    public void FirstOrNone_and_LastOrNone_distinguish_ends_and_absence()
    {
        new[] { 1, 2, 3 }.FirstOrNone(x => x > 1).ShouldBe(Option.Some(2));
        new[] { 1, 2, 3 }.LastOrNone(x => x > 1).ShouldBe(Option.Some(3));
        new[] { 1, 2, 3 }.FirstOrNone(x => x > 9).IsNone().ShouldBeTrue();
        Array.Empty<int>().LastOrNone(_ => true).IsNone().ShouldBeTrue();
    }

    [Fact]
    public void TryCast_filters_by_runtime_type()
    {
        object boxed = 42;

        boxed.TryCast<int>().ShouldBe(Option.Some(42));
        boxed.TryCast<string>().IsNone().ShouldBeTrue();
        ((object?)null).TryCast<string>().IsNone().ShouldBeTrue();
    }

    [Fact]
    public void ToOption_lifts_nullables_of_both_shapes()
    {
        ((int?)5).ToOption().ShouldBe(Option.Some(5));
        ((int?)null).ToOption().IsNone().ShouldBeTrue();

        ((string?)"value").ToOption().ShouldBe(Option.Some("value"));
        ((string?)null).ToOption().IsNone().ShouldBeTrue();
    }
}
