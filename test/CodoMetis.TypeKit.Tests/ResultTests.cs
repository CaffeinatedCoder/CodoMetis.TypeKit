namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// The error-only <see cref="Result{TError}"/>: the outcome of a command that produces no value.
/// What happens to an uninitialized one is in <see cref="UninitializedResultTests"/>.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void Success_converts_to_true_and_error_to_false()
    {
        ((bool)Result<string>.Success()).ShouldBeTrue();
        ((bool)Result<string>.Error("boom")).ShouldBeFalse();
    }

    [Fact]
    public void An_error_is_retrievable_and_success_yields_none()
    {
        Result<string>.Error("boom").TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("boom");

        Result<string>.Success().TryGetError(out _).ShouldBeFalse();
    }

    [Fact]
    public void Match_routes_by_state()
    {
        Result<string>.Success().Match(() => "success", () => "error").ShouldBe("success");
        Result<string>.Error("boom").Match(() => "success", () => "error").ShouldBe("error");
    }

    [Fact]
    public void Map_on_success_carries_the_produced_value()
    {
        Result<string>.Success().Map(() => 42).TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public void Map_on_error_carries_the_error_and_never_runs_the_selector()
    {
        var calls = 0;

        Result<string>.Error("boom").Map(() => calls++).TryGetValue(out _, out var error).ShouldBeFalse();

        error.ShouldBe("boom");
        calls.ShouldBe(0);
    }

    [Fact]
    public void Bind_chains_on_success_and_short_circuits_on_error()
    {
        Result<string>.Success().Bind(() => Result<string>.Error("late")).TryGetError(out var late).ShouldBeTrue();
        late.ShouldBe("late");

        var calls = 0;
        Result<string>.Error("early").Bind(() => { calls++; return Result<string>.Success(); });
        calls.ShouldBe(0);
    }

    [Fact]
    public void Tap_runs_only_on_success()
    {
        var calls = 0;

        Result<string>.Success().Tap(() => calls++);
        Result<string>.Error("boom").Tap(() => calls++);

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task TapAsync_runs_only_on_success()
    {
        var calls = 0;

        await Result<string>.Success().TapAsync(() => { calls++; return Task.CompletedTask; });
        await Result<string>.Error("boom").TapAsync(() => { calls++; return Task.CompletedTask; });

        calls.ShouldBe(1);
    }

    /// <summary>
    /// The conversions call sites actually write: returning a bare error value, or
    /// <c>Result.Ok()</c>, from a method typed <see cref="Result{TError}"/>.
    /// </summary>
    [Fact]
    public void An_error_value_and_the_success_marker_convert_implicitly()
    {
        Result<string> fromError = "boom";
        fromError.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("boom");

        Result<string> fromMarker = Result.Ok();
        ((bool)fromMarker).ShouldBeTrue();
    }

    [Fact]
    public void Results_compare_by_value()
    {
        Result<string>.Success().ShouldBe(Result<string>.Success());
        Result<string>.Error("a").ShouldBe(Result<string>.Error("a"));
        Result<string>.Error("a").ShouldNotBe(Result<string>.Error("b"));
        Result<string>.Success().ShouldNotBe(default(Result<string>));
    }
}
