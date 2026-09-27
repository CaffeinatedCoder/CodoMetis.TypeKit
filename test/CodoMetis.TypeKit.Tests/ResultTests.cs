using System.Reflection;

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

    /// <summary>
    /// The error branch of <c>Match</c> receives the error. The only overload took a parameterless
    /// <c>onError</c>, so <c>TryGetError</c> was the only way to the error, while the type's own
    /// documentation promised <c>Match</c> too. Through reflection, so this test compiles without the
    /// overload and fails on its absence rather than on a build error.
    /// </summary>
    [Fact]
    public void Match_hands_the_error_to_its_error_branch()
    {
        var match = typeof(Result<string>).GetMethods()
                                          .SingleOrDefault(method => method is { Name: nameof(Result<>.Match), IsGenericMethodDefinition: true }
                                                                  && method.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                                         ?.MakeGenericMethod(typeof(string));

        match.ShouldNotBeNull("Result<TError> has no Match whose error branch receives the error");

        Func<string>         onSuccess = () => "success";
        Func<string, string> onError   = error => $"error: {error}";

        match.Invoke(Result<string>.Error("boom"), [onSuccess, onError]).ShouldBe("error: boom");
        match.Invoke(Result<string>.Success(), [onSuccess, onError]).ShouldBe("success");
    }

    /// <summary>
    /// <c>notnull</c> is only an annotation: <c>Error(null!)</c> produced an error whose
    /// <c>TryGetError</c> handed out null despite <c>[NotNullWhen(true)]</c>. The implicit
    /// conversions and <c>MapError</c> go through <c>Error</c>, so they refuse it too.
    /// </summary>
    [Fact]
    public void Error_refuses_null()
    {
        Should.Throw<ArgumentNullException>(() => Result<string>.Error(null!));
        Should.Throw<ArgumentNullException>(() => (Result<string>)(string)null!);
        Should.Throw<ArgumentNullException>(() => Result<string>.Error("boom").MapError(_ => (string)null!));
    }

    /// <summary>Crossing layers: the error is reshaped, and a success passes without the mapping being called.</summary>
    [Fact]
    public void MapError_transforms_only_an_error()
    {
        Result<string>.Error("boom").MapError(e => e.Length).TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe(4);

        var calls = 0;
        ((bool)Result<string>.Success().MapError(e => { calls++; return e.Length; })).ShouldBeTrue();
        calls.ShouldBe(0);
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
    /// <c>Result.Success()</c>, from a method typed <see cref="Result{TError}"/>.
    /// </summary>
    [Fact]
    public void An_error_value_and_the_success_marker_convert_implicitly()
    {
        Result<string> fromError = "boom";
        fromError.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("boom");

        Result<string> fromMarker = Result.Success();
        ((bool)fromMarker).ShouldBeTrue();
    }

    /// <summary>
    /// <c>return Result.Error(fault);</c> from a method typed <see cref="Result{TError}"/> did not
    /// compile, while the valued shape accepts it. Through reflection, so this test compiles without
    /// the conversion and fails on its absence rather than on a build error.
    /// </summary>
    [Fact]
    public void The_error_marker_converts_implicitly_too()
    {
        var conversion = typeof(Result<string>).GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, [typeof(Error<string>)]);

        conversion.ShouldNotBeNull("Result<TError> has no implicit conversion from the Result.Error(error) marker");

        var converted = (Result<string>)conversion.Invoke(null, [Result.Error("boom")])!;
        converted.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("boom");
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
