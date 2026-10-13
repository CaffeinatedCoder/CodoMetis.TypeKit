namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>Choose</c>: the values of the options in a sequence, or of the options a selector answers,
/// in order and without the <c>None</c>s. A filter and a map in one, lazy as a LINQ operator is.
/// </summary>
public sealed class ChooseTests
{
    private static Option<int> Even(int n) => n % 2 == 0 ? Option.Some(n) : Option.None();

    [Fact]
    public void Choose_keeps_the_values_of_the_options_that_hold_one_in_order()
    {
        Option<int>[] options = [Option.Some(1), Option.None(), Option.Some(3), Option.None(), Option.Some(5)];

        options.Choose().ShouldBe([1, 3, 5]);
        Array.Empty<Option<int>>().Choose().ShouldBeEmpty();
        new[] { Option.None<int>() }.Choose().ShouldBeEmpty();
    }

    [Fact]
    public void Choose_with_a_selector_keeps_what_it_answers_Some_for()
    {
        new[] { 1, 2, 3, 4, 5, 6 }.Choose(Even).ShouldBe([2, 4, 6]);
        new[] { 1, 3 }.Choose(Even).ShouldBeEmpty();
        Array.Empty<int>().Choose(Even).ShouldBeEmpty();

        var customers = new Dictionary<int, string> { [1] = "Ann", [3] = "Bo" };
        new[] { 1, 2, 3 }.Choose(id => customers.GetValueOrNone(id)).ShouldBe(["Ann", "Bo"]);
    }

    /// <summary>A LINQ operator reads its source when it is enumerated, not when it is called, and only as far as the caller reads.</summary>
    [Fact]
    public void Choose_is_lazy()
    {
        var seen = new List<int>();

        IEnumerable<int> Counted()
        {
            for (var n = 1; n <= 10; n++)
            {
                seen.Add(n);
                yield return n;
            }
        }

        var chosen = Counted().Choose(Even);
        seen.ShouldBeEmpty("nothing is read until the result is enumerated");

        chosen.First().ShouldBe(2);
        seen.ShouldBe([1, 2], "the source is read only as far as the first value found");
    }

    /// <summary>The arguments are checked when the call is made, as LINQ's are, not when the sequence is first read.</summary>
    [Fact]
    public void Choose_checks_its_arguments_before_the_sequence_is_read()
    {
        Should.Throw<ArgumentNullException>(() => ((IEnumerable<int>)null!).Choose(Even)).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => ((IEnumerable<Option<int>>)null!).Choose()).ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() => new[] { 1 }.Choose((Func<int, Option<int>>)null!)).ParamName.ShouldBe("selector");
    }
}
