namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// The valued <see cref="Result{T,TError}"/>: the outcome of a parse or a query, and the shape the
/// <see cref="Result"/> markers and extension members exist to build and compose. What happens to
/// an uninitialized one is in <see cref="UninitializedResultTests"/>.
/// </summary>
public sealed class ResultWithValueTests
{
    [Fact]
    public void Success_carries_the_value_and_error_the_error()
    {
        Result<int, string>.Success(5).TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(5);

        Result<int, string>.Error("boom").TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("boom");
    }

    [Fact]
    public void Match_routes_the_value_or_the_error()
    {
        Result<int, string>.Success(21).Match(x => x   * 2, _ => -1).ShouldBe(42);
        Result<int, string>.Error("boom").Match(x => x * 2, e => e.Length).ShouldBe(4);
    }

    /// <summary>The overload that substitutes a default deliberately drops the error.</summary>
    [Fact]
    public void Match_with_a_default_provider_substitutes_on_error()
    {
        Result<int, string>.Error("boom").Match(x => x * 2, () => -1).ShouldBe(-1);
    }

    /// <summary>
    /// A query that produced a value, then a command that produces none: the value is spent in the
    /// command, whose own error or success is what remains. An error skips the command.
    /// </summary>
    [Fact]
    public void Bind_to_an_error_only_result_spends_the_value_in_the_command()
    {
        var seen = new List<int>();

        Result<string> sent = Result<int, string>.Success(21).Bind(x => { seen.Add(x); return Result.Success(); });
        ((bool)sent).ShouldBeTrue();

        Result<string> declined = Result<int, string>.Success(21).Bind(_ => Result.Error("declined"));
        declined.TryGetError(out var reason).ShouldBeTrue();
        reason.ShouldBe("declined");

        Result<string> skipped = Result<int, string>.Error("boom").Bind(x => { seen.Add(x); return Result.Success(); });
        skipped.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("boom");

        seen.ShouldBe([21]);
    }

    /// <summary>Crossing layers: the error is reshaped, and a success passes without the mapping being called.</summary>
    [Fact]
    public void MapError_transforms_only_an_error()
    {
        Result<int, string>.Error("boom").MapError(e => e.Length).TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe(4);

        var calls = 0;
        Result<int, string>.Success(21).MapError(e => { calls++; return e.Length; }).TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(21);
        calls.ShouldBe(0);
    }

    [Fact]
    public void Map_transforms_only_a_success()
    {
        Result<int, string>.Success(21).Map(x => x * 2).TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(42);

        var calls = 0;
        Result<int, string>.Error("boom").Map(_ => calls++).TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("boom");
        calls.ShouldBe(0);
    }

    [Fact]
    public void Bind_chains_on_success_and_short_circuits_on_error()
    {
        Result<int, string>.Success(21)
                           .Bind(x => Result<int, string>.Success(x * 2))
                           .TryGetValue(out var value, out _)
                           .ShouldBeTrue();
        value.ShouldBe(42);

        var calls = 0;
        Result<int, string>.Error("boom").Bind(x =>
        {
            calls++;
            return Result<int, string>.Success(x);
        });
        calls.ShouldBe(0);
    }

    [Fact]
    public void Tap_sees_the_value_only_on_success()
    {
        var seen = new List<int>();

        Result<int, string>.Success(5).Tap(seen.Add);
        Result<int, string>.Error("boom").Tap(seen.Add);

        seen.ShouldBe([5]);
    }

    [Fact]
    public void TapError_sees_the_error_only_on_error()
    {
        var seen = new List<string>();

        Result<int, string>.Success(5).TapError(seen.Add).TryGetValue(out var value, out _).ShouldBeTrue();
        Result<int, string>.Error("boom").TapError(seen.Add).TryGetValue(out _, out var error).ShouldBeFalse();

        value.ShouldBe(5);
        error.ShouldBe("boom");
        seen.ShouldBe(["boom"]);
    }

    [Fact]
    public void Ensure_turns_a_value_that_fails_the_rule_into_the_given_error()
    {
        Result<int, string>.Success(5).Ensure(x => x > 0, "negative").ShouldBe(Result<int, string>.Success(5));
        Result<int, string>.Success(-5).Ensure(x => x > 0, "negative").ShouldBe(Result<int, string>.Error("negative"));

        var calls = 0;
        Result<int, string>.Error("boom").Ensure(_ => ++calls > 0, "negative").ShouldBe(Result<int, string>.Error("boom"));
        calls.ShouldBe(0);
    }

    /// <summary>As <c>Option.ToResult</c>: a null error is refused whether or not it is needed.</summary>
    [Fact]
    public void Ensure_refuses_a_null_error_on_either_branch()
    {
        Should.Throw<ArgumentNullException>(() => Result<int, string>.Success(5).Ensure(_ => true, null!)).ParamName.ShouldBe("error");
        Should.Throw<ArgumentNullException>(() => Result<int, string>.Error("boom").Ensure(_ => true, null!)).ParamName.ShouldBe("error");
    }

    [Fact]
    public async Task TapAsync_sees_the_value_only_on_success()
    {
        var seen = new List<int>();

        await Result<int, string>.Success(5).TapAsync(x =>
        {
            seen.Add(x);
            return Task.CompletedTask;
        });
        await Result<int, string>.Error("boom").TapAsync(x =>
        {
            seen.Add(x);
            return Task.CompletedTask;
        });

        seen.ShouldBe([5]);
    }

    /// <summary>
    /// The conversions call sites actually write: returning a bare value, <c>Result.Success(x)</c> or
    /// <c>Result.Error(e)</c> from a method typed <see cref="Result{T,TError}"/>.
    /// </summary>
    [Fact]
    public void A_value_a_success_marker_and_an_error_marker_convert_implicitly()
    {
        Result<int, string> fromValue = 5;
        fromValue.TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(5);

        Result<int, string> fromSuccess = Result.Success(7);
        fromSuccess.TryGetValue(out var successValue, out _).ShouldBeTrue();
        successValue.ShouldBe(7);

        Result<int, string> fromError = Result.Error("boom");
        fromError.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("boom");
    }

    [Fact]
    public void Success_converts_to_true_and_error_to_false()
    {
        ((bool)Result<int, string>.Success(1)).ShouldBeTrue();
        ((bool)Result<int, string>.Error("boom")).ShouldBeFalse();
    }

    /// <summary><c>Select</c> exists so a result composes in query syntax like an option does.</summary>
    [Fact]
    public void Select_enables_query_syntax()
    {
        var doubled = from x in Result<int, string>.Success(21) select x * 2;

        doubled.TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(42);
    }

    /// <summary>
    /// A second <c>from</c> needs the two-selector <c>SelectMany</c>: each step can use every earlier
    /// value, and the first error ends the query.
    /// </summary>
    [Fact]
    public void Query_syntax_chains_results_that_depend_on_each_other()
    {
        static Result<int, string> Positive(int x) => x > 0 ? x : Result.Error("not positive");

        var sum =
            from a in Positive(2)
            from b in Positive(a + 1)
            select a + b;

        sum.ShouldBe(Result<int, string>.Success(5));
        (from a in Positive(2) from b in Positive(-a) select a + b).ShouldBe(Result<int, string>.Error("not positive"));

        var calls = 0;
        (from a in Positive(-1) from b in Counted(a) select a + b).ShouldBe(Result<int, string>.Error("not positive"));
        calls.ShouldBe(0);

        Result<int, string> Counted(int x)
        {
            calls++;
            return x;
        }
    }

    [Fact]
    public void SelectMany_refuses_an_uninitialized_result_from_its_selector()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            Result<int, string>.Success(1).SelectMany(_ => default(Result<int, string>), (a, b) => a + b));

        exception.Message.ShouldContain("never initialized");
    }

    /// <summary>Dropping the error is the point: the caller keeps only presence.</summary>
    [Fact]
    public void ToOption_keeps_the_value_and_drops_the_error()
    {
        Result<int, string>.Success(5).ToOption().ShouldBe(Option.Some(5));
        Result<int, string>.Error("boom").ToOption().IsNone().ShouldBeTrue();
    }

    [Fact]
    public void FirstOrError_and_LastOrError_pick_ends_or_report_the_given_error()
    {
        new[] { 1, 2, 3 }.FirstOrError(x => x > 1, "missing").TryGetValue(out var first, out _).ShouldBeTrue();
        first.ShouldBe(2);

        new[] { 1, 2, 3 }.LastOrError(x => x > 1, "missing").TryGetValue(out var last, out _).ShouldBeTrue();
        last.ShouldBe(3);

        new[] { 1, 2, 3 }.FirstOrError(x => x > 9, "missing").TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("missing");
    }

    /// <summary>As <c>Option.ToResult</c>: the error was checked only when nothing matched.</summary>
    [Fact]
    public void FirstOrError_and_LastOrError_refuse_a_null_error_whether_or_not_something_matches()
    {
        Should.Throw<ArgumentNullException>(() => new[] { 1 }.FirstOrError(_ => true, (string)null!)).ParamName.ShouldBe("error");
        Should.Throw<ArgumentNullException>(() => new[] { 1 }.LastOrError(_ => true, (string)null!)).ParamName.ShouldBe("error");
        Should.Throw<ArgumentNullException>(() => new[] { 1 }.FirstOrError(_ => false, (string)null!)).ParamName.ShouldBe("error");
        Should.Throw<ArgumentNullException>(() => new[] { 1 }.LastOrError(_ => false, (string)null!)).ParamName.ShouldBe("error");
    }

    [Fact]
    public void AsEnumerable_yields_the_value_once_or_nothing()
    {
        Result<int, string>.Success(5).AsEnumerable().ShouldHaveSingleItem().ShouldBe(5);
        Result<int, string>.Error("boom").AsEnumerable().ShouldBeEmpty();
    }

    /// <summary>
    /// The type parameters are <c>notnull</c>, as <see cref="Option{T}"/>'s are, which is only an
    /// annotation. <c>Success(null!)</c> and <c>Error(null!)</c> produced results whose
    /// <c>TryGetValue</c> handed out null despite <c>[NotNullWhen]</c>. So did the markers and a
    /// <c>Map</c> whose selector returned null.
    /// </summary>
    [Fact]
    public void Success_and_Error_refuse_null()
    {
        Should.Throw<ArgumentNullException>(() => Result<string, string>.Success(null!));
        Should.Throw<ArgumentNullException>(() => Result<int, string>.Error(null!));
        Should.Throw<ArgumentNullException>(() => Result.Success<string>(null!));
        Should.Throw<ArgumentNullException>(() => Result.Error<string>(null!));
        Should.Throw<ArgumentNullException>(() => Result<int, string>.Success(1).Map(_ => (string)null!));
        Should.Throw<ArgumentNullException>(() => Result<int, string>.Error("boom").MapError(_ => (string)null!));
    }

    [Fact]
    public void Results_compare_by_value()
    {
        Result<int, string>.Success(1).ShouldBe(Result<int, string>.Success(1));
        Result<int, string>.Success(1).ShouldNotBe(Result<int, string>.Success(2));
        Result<int, string>.Error("boom").ShouldBe(Result<int, string>.Error("boom"));
        Result<int, string>.Success(1).ShouldNotBe(Result<int, string>.Error("boom"));
    }
}
