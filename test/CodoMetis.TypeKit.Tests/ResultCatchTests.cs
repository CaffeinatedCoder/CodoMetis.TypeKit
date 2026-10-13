namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>Result.Catch</c>: the one place an exception becomes data. Measured first in
/// <c>spikes/ResultCatch</c>; each claim there is a test here.
/// </summary>
public sealed class ResultCatchTests
{
    private static readonly InvalidOperationException Boom = new("boom");

    private static int Throws() => throw Boom;

    private static async Task<int> ThrowsLaterAsync()
    {
        await Task.Yield();
        throw Boom;
    }

    [Fact]
    public void Catch_infers_the_value_type_and_answers_the_value()
    {
        Result<int, Exception> parsed = Result.Catch(() => int.Parse("42"));

        parsed.TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public void Catch_answers_the_exception_thrown_as_the_instance_thrown()
    {
        var caught = Result.Catch(int () => throw Boom);

        caught.TryGetValue(out _, out var error).ShouldBeFalse();
        ReferenceEquals(error, Boom).ShouldBeTrue("nothing wraps the exception");
    }

    [Fact]
    public void Catch_of_an_action_is_the_value_less_result()
    {
        Result<Exception> acted = Result.Catch(() => { });
        ((bool)acted).ShouldBeTrue();

        Result<Exception> failed = Result.Catch(() => { Throws(); });
        failed.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBeSameAs(Boom);
    }

    [Fact]
    public void A_narrowed_Catch_catches_that_type_and_lets_any_other_propagate()
    {
        Result<int, FormatException> refused = Result.Catch<int, FormatException>(() => int.Parse("x"));
        refused.TryGetValue(out _, out var format).ShouldBeFalse();
        format.ShouldBeOfType<FormatException>();

        var other = Should.Throw<InvalidOperationException>(() => Result.Catch<int, FormatException>(Throws));
        other.ShouldBeSameAs(Boom);
        other.StackTrace!.ShouldContain(nameof(Throws), Case.Sensitive, "the stack is the original throw's");

        Result<FormatException> failedCommand = Result.Catch<FormatException>(() => { int.Parse("x"); });
        failedCommand.TryGetError(out _).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => Result.Catch<FormatException>(() => { Throws(); }));
    }

    /// <summary>
    /// A lambda with an expression body converts to <see cref="Action"/>, so the one-type-argument
    /// form over a value-returning call is the value-less overload, and the value is discarded. The
    /// valued form spells both type arguments; the README says so.
    /// </summary>
    [Fact]
    public void A_one_type_argument_Catch_over_a_value_is_the_value_less_overload()
    {
        var bound = Result.Catch<FormatException>(() => int.Parse("42"));

        bound.ShouldBeOfType<Result<FormatException>>();
    }

    [Fact]
    public void A_cancellation_is_not_an_outcome()
    {
        Should.Throw<OperationCanceledException>(() => Result.Catch(int () => throw new OperationCanceledException()));
        Should.Throw<OperationCanceledException>(() => Result.Catch(void () => throw new OperationCanceledException()));
        Should.Throw<OperationCanceledException>(() => Result.Catch<int, Exception>(int () => throw new OperationCanceledException()));
        Should.Throw<OperationCanceledException>(() => Result.Catch<Exception>(void () => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task CatchAsync_catches_an_exception_thrown_after_the_first_await()
    {
        var later = await Result.CatchAsync(ThrowsLaterAsync);
        later.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBeSameAs(Boom);

        var command = await Result.CatchAsync(async () => { await Task.Yield(); Throws(); });
        command.TryGetError(out _).ShouldBeTrue();

        var narrowed = await Result.CatchAsync<int, InvalidOperationException>(ThrowsLaterAsync);
        narrowed.TryGetValue(out _, out var typed).ShouldBeFalse();
        typed.ShouldBeSameAs(Boom);

        var narrowedCommand = await Result.CatchAsync<InvalidOperationException>(async () => { await Task.Yield(); Throws(); });
        narrowedCommand.TryGetError(out _).ShouldBeTrue();

        var completed = await Result.CatchAsync(async () => { await Task.Yield(); return 7; });
        completed.TryGetValue(out var value, out _).ShouldBeTrue();
        value.ShouldBe(7);
    }

    [Fact]
    public async Task CatchAsync_lets_a_cancelled_token_through_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync(async () => { await Task.Delay(10, cts.Token); return 1; }));
        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync(() => Task.Delay(10, cts.Token)));
        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync<int, Exception>(async () => { await Task.Delay(10, cts.Token); return 1; }));
        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync<Exception>(() => Task.Delay(10, cts.Token)));
    }

    /// <summary>A success over null is the delegate's defect, not an outcome, and is not caught.</summary>
    [Fact]
    public async Task A_delegate_that_returns_null_is_refused_rather_than_caught()
    {
        Should.Throw<ArgumentNullException>(() => Result.Catch(() => (string)null!));
        Should.Throw<ArgumentNullException>(() => Result.Catch<string, FormatException>(() => null!));
        await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync(() => Task.FromResult((string)null!)));
        await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync<string, FormatException>(() => Task.FromResult((string)null!)));
    }

    [Fact]
    public void The_combinators_are_unchanged_so_a_throwing_selector_propagates()
    {
        Should.Throw<InvalidOperationException>(() => Result.Catch(() => 1).Map(_ => Throws()));
        Should.Throw<InvalidOperationException>(() => Result.Catch(() => 1).Bind<int>(_ => Throws()));
    }

    /// <summary>
    /// A delegate returning a task given to <c>Catch</c> would bind <c>T</c> to the task and catch
    /// nothing it throws. The overload over a task-returning delegate is more specific, so the
    /// compiler picks it, and it is an error that names <c>CatchAsync</c>.
    /// </summary>
    [Theory]
    [InlineData("Result.Catch(() => FetchAsync())")]
    [InlineData("Result.Catch(FetchAsync)")]
    [InlineData("Result.Catch(() => DoAsync())")]
    [InlineData("Result.Catch(DoAsync)")]
    public void A_task_returning_delegate_given_to_Catch_is_a_compile_error_naming_CatchAsync(string call)
    {
        var compilation = ConsumerCompilation.Of($$"""
            using System.Threading.Tasks;
            using CodoMetis.TypeKit;

            static class Consumer
            {
                static Task<int> FetchAsync() => Task.FromResult(1);
                static Task DoAsync() => Task.CompletedTask;
                static object Call() => {{call}};
            }
            """);

        compilation.ErrorIds().ShouldBe(["CS0619"], compilation.Problems());
        compilation.Problems().ShouldContain("CatchAsync");
    }

    /// <summary>
    /// The cost of the trap: a lambda that only throws has no return type. With a type argument it is
    /// ambiguous between <c>Func&lt;T&gt;</c> and the trap's <c>Func&lt;Task&lt;T&gt;&gt;</c>; without
    /// one, the compiler prefers the non-void delegate for an expression body and lands on the trap.
    /// Stating the return type resolves both, and the call sites of real code have one.
    /// </summary>
    [Theory]
    [InlineData("Result.Catch<int>(() => throw new Exception())", "CS0121")]
    [InlineData("Result.Catch<int>(int () => throw new Exception())", "")]
    [InlineData("Result.Catch(() => throw new Exception())", "CS0619")]
    [InlineData("Result.Catch(void () => throw new Exception())", "")]
    public void A_lambda_that_only_throws_states_its_return_type(string call, string error)
    {
        var compilation = ConsumerCompilation.Of($$"""
            using System;
            using CodoMetis.TypeKit;

            static class Consumer
            {
                static object Call() => {{call}};
            }
            """);

        compilation.ErrorIds().ShouldBe(error.Length == 0 ? [] : [error], compilation.Problems());
    }
}
