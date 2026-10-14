namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>SingleOrNone</c> and <c>ElementAtOrNone</c>, the options CMTK0009 has to offer for
/// <c>SingleOrDefault</c> and <c>ElementAtOrDefault</c>, and <c>Transpose</c>/<c>Traverse</c> between
/// an option and a result, for an optional input that is validated when present.
/// </summary>
public sealed class OptionLookupTests
{
    private enum Fault { Refused }

    [Fact]
    public void SingleOrNone_answers_the_only_element_or_None_and_refuses_more_than_one()
    {
        new[] { 7 }.SingleOrNone().ShouldBe(Option.Some(7));
        Array.Empty<int>().SingleOrNone().ShouldBe(Option.None<int>());
        new[] { 1, 2, 3 }.SingleOrNone(x => x == 2).ShouldBe(Option.Some(2));
        new[] { 1, 2, 3 }.SingleOrNone(x => x > 5).ShouldBe(Option.None<int>());

        // More than one is a defect, not absence: as SingleOrDefault, it throws.
        Should.Throw<InvalidOperationException>(() => new[] { 1, 2 }.SingleOrNone()).Message.ShouldContain("FirstOrNone");
        Should.Throw<InvalidOperationException>(() => new[] { 1, 2, 3 }.SingleOrNone(x => x > 1));
    }

    [Fact]
    public void ElementAtOrNone_answers_the_element_or_None_past_either_end()
    {
        IReadOnlyList<int> list = [10, 20, 30];
        IEnumerable<int> sequence = Enumerable.Range(1, 3).Select(x => x * 10);

        list.ElementAtOrNone(1).ShouldBe(Option.Some(20));
        sequence.ElementAtOrNone(2).ShouldBe(Option.Some(30));
        list.ElementAtOrNone(3).ShouldBe(Option.None<int>());
        sequence.ElementAtOrNone(3).ShouldBe(Option.None<int>());
        list.ElementAtOrNone(-1).ShouldBe(Option.None<int>());
        sequence.ElementAtOrNone(-1).ShouldBe(Option.None<int>());
        list.ElementAtOrNone(^1).ShouldBe(Option.Some(30));
        sequence.ElementAtOrNone(^3).ShouldBe(Option.Some(10));
        sequence.ElementAtOrNone(^1).ShouldBe(Option.Some(30));
        list.ElementAtOrNone(^4).ShouldBe(Option.None<int>());
        sequence.ElementAtOrNone(^4).ShouldBe(Option.None<int>());
        sequence.ElementAtOrNone(^0).ShouldBe(Option.None<int>());
        sequence.ElementAtOrNone(new Index(1)).ShouldBe(Option.Some(20));
    }

    /// <summary>On a query, a negative index is <c>None</c> without a query: <c>Skip(-1)</c> skips nothing and would answer the first element.</summary>
    [Fact]
    public void On_a_query_they_answer_as_on_a_sequence()
    {
        var query = new[] { 10, 20, 30 }.AsQueryable();

        query.SingleOrNone(x => x == 20).ShouldBe(Option.Some(20));
        query.SingleOrNone(x => x > 99).ShouldBe(Option.None<int>());
        Should.Throw<InvalidOperationException>(() => query.SingleOrNone());
        query.ElementAtOrNone(2).ShouldBe(Option.Some(30));
        query.ElementAtOrNone(3).ShouldBe(Option.None<int>());
        query.ElementAtOrNone(-1).ShouldBe(Option.None<int>());
    }

    [Fact]
    public void Transpose_turns_an_optional_result_inside_out()
    {
        Option.None<Result<int, Fault>>().Transpose().ShouldBe(Result<Option<int>, Fault>.Success(Option.None<int>()));
        Option.Some(Result<int, Fault>.Success(3)).Transpose().ShouldBe(Result<Option<int>, Fault>.Success(Option.Some(3)));
        Option.Some(Result<int, Fault>.Error(Fault.Refused)).Transpose().ShouldBe(Result<Option<int>, Fault>.Error(Fault.Refused));
    }

    [Fact]
    public void Transpose_turns_a_result_of_an_option_inside_out()
    {
        Result<Option<int>, Fault>.Success(Option.None<int>()).Transpose().ShouldBe(Option.None<Result<int, Fault>>());
        Result<Option<int>, Fault>.Success(Option.Some(3)).Transpose().ShouldBe(Option.Some(Result<int, Fault>.Success(3)));
        Result<Option<int>, Fault>.Error(Fault.Refused).Transpose().ShouldBe(Option.Some(Result<int, Fault>.Error(Fault.Refused)));
    }

    [Fact]
    public void Traverse_validates_an_optional_input_only_when_present()
    {
        static Result<string, Fault> Create(string text) => text.Length > 0 ? Result<string, Fault>.Success(text) : Result<string, Fault>.Error(Fault.Refused);

        Option.Some("a").Traverse(Create).ShouldBe(Result<Option<string>, Fault>.Success(Option.Some("a")));
        Option.Some("").Traverse(Create).ShouldBe(Result<Option<string>, Fault>.Error(Fault.Refused));
        Option.None<string>().Traverse<string, string, Fault>(_ => throw new InvalidOperationException("called on None"))
              .ShouldBe(Result<Option<string>, Fault>.Success(Option.None<string>()));
    }

    /// <summary>An uninitialized result inside an option is a bug at the site that made it, refused as everywhere else.</summary>
    [Fact]
    public void An_uninitialized_result_inside_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => Option.Some(default(Result<int, Fault>)).Transpose());
        Should.Throw<InvalidOperationException>(() => Option.Some(1).Traverse(_ => default(Result<int, Fault>)));
        Should.Throw<InvalidOperationException>(() => default(Result<Option<int>, Fault>).Transpose());
    }

    /// <summary>A method group infers both type arguments, the shape the README shows.</summary>
    [Fact]
    public void Traverse_infers_from_a_method_group() =>
        ConsumerCompilation.Of(
            """
            using CodoMetis.TypeKit;

            enum PhoneFault { Malformed }

            readonly record struct Phone(string Text)
            {
                public static Result<Phone, PhoneFault> Create(string text) => new Phone(text);
            }

            static class Consumer
            {
                static Result<Option<Phone>, PhoneFault> Read(string? phone) => phone.ToOption().Traverse(Phone.Create);
            }
            """).Problems().ShouldBeEmpty();
}
