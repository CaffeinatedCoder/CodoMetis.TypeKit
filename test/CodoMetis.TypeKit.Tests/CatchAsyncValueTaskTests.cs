using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>CatchAsync</c> over a call that returns a <c>ValueTask</c>: the same outcomes as the <c>Task</c>
/// forms, and the overloads' lower priority, which keeps every <c>async</c> lambda on the <c>Task</c> form
/// it bound to before they existed (decision 110).
/// </summary>
public sealed class CatchAsyncValueTaskTests
{
    private static readonly InvalidOperationException Boom = new("boom");

    private static readonly TaskCanceledException Timeout = new("timed out", new TimeoutException());

    private static async ValueTask<int> ThrowsLaterAsync()
    {
        await Task.Yield();
        throw Boom;
    }

    private static async ValueTask ThrowsLaterCommandAsync()
    {
        await Task.Yield();
        throw Boom;
    }

    private static async ValueTask<int> AnswersLaterAsync()
    {
        await Task.Yield();
        return 7;
    }

    private static async ValueTask DoesLaterAsync() => await Task.Yield();

    [Fact]
    public async Task Every_form_answers_the_value_or_a_success()
    {
        (await Result.CatchAsync(AnswersLaterAsync)).Match(value => value, _ => -1).ShouldBe(7);
        (await Result.CatchAsync(() => new ValueTask<int>(7))).Match(value => value, _ => -1).ShouldBe(7);
        ((bool)await Result.CatchAsync(DoesLaterAsync)).ShouldBeTrue();
        (await Result.CatchAsync<int, InvalidOperationException>(AnswersLaterAsync)).Match(value => value, _ => -1).ShouldBe(7);
        ((bool)await Result.CatchAsync<InvalidOperationException>(DoesLaterAsync)).ShouldBeTrue();
    }

    [Fact]
    public async Task Every_form_catches_an_exception_thrown_after_the_first_await()
    {
        (await Result.CatchAsync(ThrowsLaterAsync)).TryGetValue(out _, out var valued).ShouldBeFalse();
        valued.ShouldBeSameAs(Boom);
        (await Result.CatchAsync(ThrowsLaterCommandAsync)).TryGetError(out var command).ShouldBeTrue();
        command.ShouldBeSameAs(Boom);
        (await Result.CatchAsync<int, InvalidOperationException>(ThrowsLaterAsync)).TryGetValue(out _, out var narrowed).ShouldBeFalse();
        narrowed.ShouldBeSameAs(Boom);
        (await Result.CatchAsync<InvalidOperationException>(ThrowsLaterCommandAsync)).TryGetError(out var narrowedCommand).ShouldBeTrue();
        narrowedCommand.ShouldBeSameAs(Boom);
    }

    [Fact]
    public async Task Every_form_catches_an_exception_thrown_before_any_task()
    {
        static ValueTask<int> ThrowsForAValue() => throw Boom;
        static ValueTask ThrowsForACommand() => throw Boom;

        (await Result.CatchAsync(ThrowsForAValue)).TryGetValue(out _, out var valued).ShouldBeFalse();
        valued.ShouldBeSameAs(Boom);
        (await Result.CatchAsync(ThrowsForACommand)).TryGetError(out var command).ShouldBeTrue();
        command.ShouldBeSameAs(Boom);
        (await Result.CatchAsync<int, InvalidOperationException>(ThrowsForAValue)).TryGetValue(out _, out var narrowed).ShouldBeFalse();
        narrowed.ShouldBeSameAs(Boom);
        (await Result.CatchAsync<InvalidOperationException>(ThrowsForACommand)).TryGetError(out var narrowedCommand).ShouldBeTrue();
        narrowedCommand.ShouldBeSameAs(Boom);
    }

    [Fact]
    public async Task A_narrowed_form_lets_any_other_exception_propagate()
    {
        (await Should.ThrowAsync<InvalidOperationException>(() => Result.CatchAsync<int, FormatException>(ThrowsLaterAsync))).ShouldBeSameAs(Boom);
        (await Should.ThrowAsync<InvalidOperationException>(() => Result.CatchAsync<FormatException>(ThrowsLaterCommandAsync))).ShouldBeSameAs(Boom);
    }

    [Fact]
    public async Task A_cancellation_passes_through_every_form()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        async ValueTask<int> Read() { await Task.Delay(10, cancellation.Token); return 1; }
        async ValueTask Write() => await Task.Delay(10, cancellation.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync(Read));
        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync(Write));
        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync<int, Exception>(Read));
        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync<Exception>(Write));
    }

    /// <summary>A cancellation over a <see cref="TimeoutException"/> is how <c>HttpClient</c> reports its timeout: the error, as the <c>Task</c> forms answer it.</summary>
    [Fact]
    public async Task A_timeout_is_the_error_in_every_form()
    {
        async ValueTask<int> Read() { await Task.Yield(); throw Timeout; }
        async ValueTask Write() { await Task.Yield(); throw Timeout; }

        (await Result.CatchAsync(Read)).TryGetValue(out _, out var valued).ShouldBeFalse();
        valued.ShouldBeSameAs(Timeout);
        (await Result.CatchAsync(Write)).TryGetError(out var command).ShouldBeTrue();
        command.ShouldBeSameAs(Timeout);
        (await Result.CatchAsync<int, OperationCanceledException>(Read)).TryGetValue(out _, out var narrowed).ShouldBeFalse();
        narrowed.ShouldBeSameAs(Timeout);
        (await Result.CatchAsync<TaskCanceledException>(Write)).TryGetError(out var narrowedCommand).ShouldBeTrue();
        narrowedCommand.ShouldBeSameAs(Timeout);
    }

    [Fact]
    public async Task A_cancellation_that_Result_wraps_passes_through_every_form()
    {
        var cancelled = Task.FromCanceled<int>(new CancellationToken(canceled: true));

        async ValueTask<int> Read() { await Task.Yield(); return cancelled.Result; }
        async ValueTask Write() { await Task.Yield(); cancelled.Wait(); }

        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync(Read));
        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync(Write));
        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync<int, AggregateException>(Read));
        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync<AggregateException>(Write));
    }

    [Fact]
    public async Task A_null_value_and_a_task_of_a_task_are_refused_rather_than_caught()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync(() => new ValueTask<string>((string)null!)));
        await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync<string, FormatException>(() => new ValueTask<string>((string)null!)));

        (await Should.ThrowAsync<ArgumentException>(() => Result.CatchAsync(() => new ValueTask<Task<int>>(Task.FromResult(1))))).Message.ShouldContain("Await the inner task");
        await Should.ThrowAsync<ArgumentException>(() => Result.CatchAsync<ValueTask<int>, InvalidOperationException>(() => new ValueTask<ValueTask<int>>(new ValueTask<int>(1))));
    }

    /// <summary>
    /// An <c>async</c> lambda converts to a delegate over either task type, and without the
    /// <c>ValueTask</c> forms' lower priority every one of them was ambiguous (CS0121, decision 30). Each
    /// call here compiles and binds to the form it names; the <c>Task</c> rows are what bound before the
    /// <c>ValueTask</c> forms existed.
    /// </summary>
    [Theory]
    [InlineData("Result.CatchAsync(async () => { await Task.Yield(); return 1; })", "Task")]
    [InlineData("Result.CatchAsync(async () => await FetchAsync())", "Task")]
    [InlineData("Result.CatchAsync(async () => await FetchValueAsync())", "Task")]
    [InlineData("Result.CatchAsync(async () => { await DoAsync(); })", "Task")]
    [InlineData("Result.CatchAsync(async () => await DoValueAsync())", "Task")]
    [InlineData("Result.CatchAsync<int, InvalidOperationException>(async () => await FetchAsync())", "Task")]
    [InlineData("Result.CatchAsync<InvalidOperationException>(async () => await DoAsync())", "Task")]
    [InlineData("Result.CatchAsync<int>(() => throw new Exception())", "Task")]
    [InlineData("Result.CatchAsync<int, InvalidOperationException>(() => throw new Exception())", "Task")]
    [InlineData("Result.CatchAsync(() => FetchAsync())", "Task")]
    [InlineData("Result.CatchAsync(FetchAsync)", "Task")]
    [InlineData("Result.CatchAsync(DoAsync)", "Task")]
    [InlineData("Result.CatchAsync(() => FetchValueAsync())", "ValueTask")]
    [InlineData("Result.CatchAsync(FetchValueAsync)", "ValueTask")]
    [InlineData("Result.CatchAsync(() => DoValueAsync())", "ValueTask")]
    [InlineData("Result.CatchAsync(DoValueAsync)", "ValueTask")]
    [InlineData("Result.CatchAsync<int, InvalidOperationException>(FetchValueAsync)", "ValueTask")]
    [InlineData("Result.CatchAsync<InvalidOperationException>(() => DoValueAsync())", "ValueTask")]
    public void A_call_binds_to_the_form_of_its_task_and_an_async_lambda_to_the_Task_form(string call, string task)
    {
        var compilation = ConsumerCompilation.Of($$"""
            using System;
            using System.Threading.Tasks;
            using CodoMetis.TypeKit;

            static class Consumer
            {
                static Task<int> FetchAsync() => Task.FromResult(1);
                static Task DoAsync() => Task.CompletedTask;
                static ValueTask<int> FetchValueAsync() => new(1);
                static ValueTask DoValueAsync() => ValueTask.CompletedTask;
                static object Call() => {{call}};
            }
            """);

        compilation.ErrorIds().ShouldBeEmpty(compilation.Problems());

        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>()
                             .Single(node => node.Expression.ToString().StartsWith("Result.CatchAsync", StringComparison.Ordinal));
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(invocation, TestContext.Current.CancellationToken).Symbol!;

        ((INamedTypeSymbol)method.Parameters.Single().Type).DelegateInvokeMethod!.ReturnType.Name.ShouldBe(task, call);
    }
}
