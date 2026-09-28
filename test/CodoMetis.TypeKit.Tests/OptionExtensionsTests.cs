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

    /// <summary>
    /// A second <c>from</c> needs the two-selector <c>SelectMany</c>; with only the one-selector form,
    /// the query did not compile. A later step sees every earlier value.
    /// </summary>
    [Fact]
    public void Query_syntax_chains_options_that_depend_on_each_other()
    {
        var towns = new Dictionary<string, string> { ["ada"] = "London" };

        var greeting =
            from name in Option.Some("ada")
            from town in towns.GetValueOrNone(name)
            where town.Length > 0
            select $"{name} from {town}";

        greeting.ShouldBe(Option.Some("ada from London"));

        (from name in Option.Some("bob") from town in towns.GetValueOrNone(name) select town).IsNone().ShouldBeTrue();

        var calls = 0;
        (from name in Option.None<string>() from town in Counted(name) select town).IsNone().ShouldBeTrue();
        calls.ShouldBe(0);

        Option<string> Counted(string name)
        {
            calls++;
            return Option.Some(name);
        }
    }

    [Fact]
    public void GetValueOrNone_is_TryGetValue_as_an_option()
    {
        IReadOnlyDictionary<int, string> names = new Dictionary<int, string> { [1] = "one" };

        names.GetValueOrNone(1).ShouldBe(Option.Some("one"));
        names.GetValueOrNone(2).IsNone().ShouldBeTrue();
        new Dictionary<int, string> { [1] = "one" }.GetValueOrNone(1).ShouldBe(Option.Some("one"));
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
    /// The bridge from "not there" to "and that is an error": the value is kept, and absence gains
    /// the caller's error.
    /// </summary>
    [Fact]
    public void ToResult_keeps_the_value_and_turns_none_into_the_given_error()
    {
        Option.Some(21).ToResult("missing").TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(21);

        Option.None<int>().ToResult("missing").TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("missing");
    }

    /// <summary>A null error is refused whatever the option holds, not first on a <c>None</c>.</summary>
    [Fact]
    public void ToResult_refuses_a_null_error_on_either_branch()
    {
        Should.Throw<ArgumentNullException>(() => Option.Some(1).ToResult((string)null!));
        Should.Throw<ArgumentNullException>(() => Option.None<int>().ToResult((string)null!));
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

    /// <summary>A list and a lazy sequence take different paths to the last element.</summary>
    [Fact]
    public void FirstOrNone_and_LastOrNone_without_a_predicate_take_the_ends()
    {
        new[] { 1, 2, 3 }.FirstOrNone().ShouldBe(Option.Some(1));
        new[] { 1, 2, 3 }.LastOrNone().ShouldBe(Option.Some(3));
        Enumerable.Range(1, 3).Select(x => x * 10).LastOrNone().ShouldBe(Option.Some(30));

        Array.Empty<int>().FirstOrNone().IsNone().ShouldBeTrue();
        Array.Empty<int>().LastOrNone().IsNone().ShouldBeTrue();
        Enumerable.Empty<int>().Select(x => x).LastOrNone().IsNone().ShouldBeTrue();
    }

    /// <summary>A null element is refused as <c>Option.Some(null)</c> is, rather than reported as a value.</summary>
    [Fact]
    public void FirstOrNone_and_LastOrNone_refuse_a_null_element()
    {
        Should.Throw<ArgumentNullException>(() => new string[] { null! }.FirstOrNone());
        Should.Throw<ArgumentNullException>(() => new string[] { null! }.LastOrNone());
        Should.Throw<ArgumentNullException>(() => new[] { "a", null! }.Select(x => x).LastOrNone());
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
