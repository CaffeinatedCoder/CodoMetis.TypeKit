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
    /// The collapsing overload is how a valued pipeline hands its outcome to a caller that only
    /// wants pass/fail: the value is spent, the error may be reshaped on the way out.
    /// </summary>
    [Fact]
    public void Match_can_collapse_to_an_error_only_result()
    {
        var collapsed = Result<int, string>.Success(21).Match(_ => Result.Ok(), e => e + "!");
        ((bool)collapsed).ShouldBeTrue();

        var failed = Result<int, string>.Error("boom").Match(_ => Result.Ok(), e => e + "!");
        failed.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("boom!");
    }

    /// <summary>
    /// "Spent" means the success callback runs: it is where the value is consumed, so skipping it
    /// would silently drop whatever it does with the value.
    /// </summary>
    [Fact]
    public void Collapsing_runs_the_success_callback_with_the_value()
    {
        var seen = new List<int>();

        Result<int, string>.Success(21).Match(x => { seen.Add(x); return Result.Ok(); }, e => e);
        Result<int, string>.Error("boom").Match(x => { seen.Add(x); return Result.Ok(); }, e => e);

        seen.ShouldBe([21]);
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
    public void Bind_accepts_a_plain_ok_marker()
    {
        Result<int, string>.Success(21).Bind(x => Result.Ok(x * 2)).TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(42);
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
    /// The conversions call sites actually write: returning a bare value, <c>Result.Ok(x)</c> or
    /// <c>Result.Error(e)</c> from a method typed <see cref="Result{T,TError}"/>.
    /// </summary>
    [Fact]
    public void A_value_an_ok_marker_and_an_error_marker_convert_implicitly()
    {
        Result<int, string> fromValue = 5;
        fromValue.TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(5);

        Result<int, string> fromOk = Result.Ok(7);
        fromOk.TryGetValue(out var okValue, out _).ShouldBeTrue();
        okValue.ShouldBe(7);

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

    [Fact]
    public void AsEnumerable_yields_the_value_once_or_nothing()
    {
        Result<int, string>.Success(5).AsEnumerable().ShouldHaveSingleItem().ShouldBe(5);
        Result<int, string>.Error("boom").AsEnumerable().ShouldBeEmpty();
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
