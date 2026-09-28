namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// Several results into one: <c>Zip</c> over a fixed number, <c>Sequence</c> and <c>Traverse</c>
/// over a sequence. All of them stop at the first error; none collects errors.
/// </summary>
public sealed class ResultCombinationTests
{
    private static Result<int, string> Ok(int value) => Result<int, string>.Success(value);

    private static Result<int, string> Fail(string error) => Result<int, string>.Error(error);

    [Fact]
    public void Zip_combines_the_values_of_successes()
    {
        Ok(1).Zip(Ok(2), (a, b) => a + b).Match(x => x, _ => -1).ShouldBe(3);
        Ok(1).Zip(Ok(2), Ok(3), (a, b, c) => a + b + c).Match(x => x, _ => -1).ShouldBe(6);
        Ok(1).Zip(Ok(2), Ok(3), Ok(4), (a, b, c, d) => a + b + c + d).Match(x => x, _ => -1).ShouldBe(10);
        Ok(1).Zip(Ok(2), Ok(3), Ok(4), Ok(5), (a, b, c, d, e) => a + b + c + d + e).Match(x => x, _ => -1).ShouldBe(15);
        Ok(1).Zip(Ok(2), Ok(3), Ok(4), Ok(5), Ok(6), (a, b, c, d, e, f) => a + b + c + d + e + f).Match(x => x, _ => -1).ShouldBe(21);
    }

    /// <summary>Each position in turn holds the first error, with a later one behind it.</summary>
    [Fact]
    public void Zip_reports_the_first_error_in_argument_order_without_combining()
    {
        var calls = 0;
        int Sum(int a, int b, int c, int d, int e, int f) { calls++; return a + b + c + d + e + f; }

        for (var position = 0; position < 6; position++)
        {
            var arguments = Enumerable.Range(0, 6)
                                      .Select(i => i < position ? Ok(i) : i == position ? Fail($"first@{i}") : Fail($"later@{i}"))
                                      .ToArray();

            var zipped = arguments[0].Zip(arguments[1], arguments[2], arguments[3], arguments[4], arguments[5], Sum);

            zipped.Match(_ => "", e => e).ShouldBe($"first@{position}");
        }

        Fail("a").Zip(Fail("b"), (x, y) => x + y).Match(_ => "", e => e).ShouldBe("a");
        Ok(1).Zip(Fail("b"), Fail("c"), (x, y, z) => x + y + z).Match(_ => "", e => e).ShouldBe("b");
        calls.ShouldBe(0);
    }

    [Fact]
    public void Zip_combines_values_of_different_types()
    {
        var line = Result<string, int>.Success("book").Zip(Result<long, int>.Success(3L), (name, count) => $"{count} x {name}");

        line.Match(x => x, _ => "").ShouldBe("3 x book");
    }

    [Fact]
    public void Sequence_collects_every_value_in_order()
    {
        var all = new[] { Ok(3), Ok(1), Ok(2) }.Sequence();

        all.Match(values => values, _ => []).ShouldBe([3, 1, 2]);
    }

    [Fact]
    public void Sequence_of_nothing_is_an_empty_success()
    {
        Array.Empty<Result<int, string>>().Sequence().Match(values => values.Count, _ => -1).ShouldBe(0);
        ((bool)Array.Empty<Result<string>>().Sequence()).ShouldBeTrue();
    }

    /// <summary>A lazy sequence is not enumerated past the first error.</summary>
    [Fact]
    public void Sequence_stops_at_the_first_error()
    {
        var produced = new List<int>();

        IEnumerable<Result<int, string>> Lazy()
        {
            for (var i = 0; i < 5; i++)
            {
                produced.Add(i);
                yield return i == 2 ? Fail($"at {i}") : i == 3 ? Fail("later") : Ok(i);
            }
        }

        Lazy().Sequence().Match(_ => "", e => e).ShouldBe("at 2");
        produced.ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void Sequence_of_commands_is_the_first_error_or_a_success()
    {
        ((bool)new[] { Result<string>.Success(), Result<string>.Success() }.Sequence()).ShouldBeTrue();

        var failed = new[] { Result<string>.Success(), Result<string>.Error("first"), Result<string>.Error("later") }.Sequence();
        failed.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("first");
    }

    [Fact]
    public void Traverse_applies_an_operation_that_may_fail_to_every_element()
    {
        var parsed = new[] { "1", "2", "3" }.Traverse(text => int.TryParse(text, out var n) ? Ok(n) : Fail(text));

        parsed.Match(values => values, _ => []).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Traverse_stops_calling_the_selector_at_the_first_error()
    {
        var seen = new List<string>();

        var parsed = new[] { "1", "x", "y", "4" }.Traverse(text =>
        {
            seen.Add(text);
            return int.TryParse(text, out var n) ? Ok(n) : Fail(text);
        });

        parsed.Match(_ => "", e => e).ShouldBe("x");
        seen.ShouldBe(["1", "x"]);
    }

    [Fact]
    public void Traverse_with_a_command_runs_until_one_fails()
    {
        var sent = new List<int>();

        Result<string> Send(int n)
        {
            if (n == 3) return Result.Error($"refused {n}");

            sent.Add(n);
            return Result.Success();
        }

        new[] { 1, 2, 3, 4 }.Traverse(Send).TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("refused 3");
        sent.ShouldBe([1, 2]);

        ((bool)new[] { 1, 2 }.Traverse(Send)).ShouldBeTrue();
    }

    /// <summary>A method group such as a value object's <c>Create</c> is the usual selector.</summary>
    [Fact]
    public void Traverse_takes_a_factory_as_a_method_group()
    {
        static Result<int, string> Positive(int n) => n > 0 ? n : Result.Error($"{n} is not positive");

        new[] { 1, 2 }.Traverse(Positive).Match(values => values.Count, _ => -1).ShouldBe(2);
        new[] { 1, -2 }.Traverse(Positive).Match(_ => "", e => e).ShouldBe("-2 is not positive");
    }

    [Fact]
    public void A_null_sequence_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => ((IEnumerable<Result<int, string>>)null!).Sequence()).ParamName.ShouldBe("results");
        Should.Throw<ArgumentNullException>(() => ((IEnumerable<Result<string>>)null!).Sequence()).ParamName.ShouldBe("results");
        Should.Throw<ArgumentNullException>(() => ((IEnumerable<int>)null!).Traverse(Ok)).ParamName.ShouldBe("source");
    }

    [Fact]
    public async Task A_null_pending_result_is_refused()
    {
        (await Should.ThrowAsync<ArgumentNullException>(() => ((Task<Result<int, string>>)null!).MapAsync(x => x))).ParamName.ShouldBe("task");
        (await Should.ThrowAsync<ArgumentNullException>(() => ((Task<Result<string>>)null!).TapAsync(() => { }))).ParamName.ShouldBe("task");
    }
}
