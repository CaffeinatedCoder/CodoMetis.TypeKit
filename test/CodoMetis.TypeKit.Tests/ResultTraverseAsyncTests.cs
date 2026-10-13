namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>TraverseAsync</c>: an asynchronous operation that may fail, applied to every element one at a
/// time and in order, stopping at the first error, so the next operation is never started after one
/// failed. Operations that may all run at once are <c>Task.WhenAll</c> and <c>Sequence()</c>.
/// </summary>
public sealed class ResultTraverseAsyncTests
{
    private static Result<int, string> Ok(int value) => Result<int, string>.Success(value);

    private static Result<int, string> Fail(string error) => Result<int, string>.Error(error);

    [Fact]
    public async Task TraverseAsync_awaits_each_operation_in_order_and_collects_the_values()
    {
        var order = new List<string>();

        async Task<Result<int, string>> Parse(string text)
        {
            order.Add($"start {text}");
            await Task.Yield();
            order.Add($"end {text}");
            return int.TryParse(text, out var n) ? Ok(n) : Fail(text);
        }

        var parsed = await new[] { "1", "2", "3" }.TraverseAsync(Parse);

        parsed.Match(values => values, _ => []).ShouldBe([1, 2, 3]);
        order.ShouldBe(["start 1", "end 1", "start 2", "end 2", "start 3", "end 3"], "one operation at a time, each finished before the next starts");
    }

    [Fact]
    public async Task TraverseAsync_stops_before_the_next_operation_after_an_error()
    {
        var started = new List<string>();

        Task<Result<int, string>> Parse(string text)
        {
            started.Add(text);
            return Task.FromResult(int.TryParse(text, out var n) ? Ok(n) : Fail(text));
        }

        var parsed = await new[] { "1", "x", "y", "4" }.TraverseAsync(Parse);

        parsed.Match(_ => "", error => error).ShouldBe("x");
        started.ShouldBe(["1", "x"]);
    }

    [Fact]
    public async Task TraverseAsync_with_a_command_runs_until_one_fails()
    {
        var sent = new List<int>();

        Task<Result<string>> Send(int n)
        {
            if (n == 3) return Task.FromResult(Result<string>.Error($"refused {n}"));

            sent.Add(n);
            return Task.FromResult(Result<string>.Success());
        }

        (await new[] { 1, 2, 3, 4 }.TraverseAsync(Send)).TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("refused 3");
        sent.ShouldBe([1, 2]);

        ((bool)await new[] { 1, 2 }.TraverseAsync(Send)).ShouldBeTrue();
        ((bool)await Array.Empty<int>().TraverseAsync(Send)).ShouldBeTrue();
    }

    [Fact]
    public async Task TraverseAsync_over_an_empty_sequence_is_an_empty_success()
    {
        var values = await Array.Empty<string>().TraverseAsync(text => Task.FromResult(Ok(text.Length)));

        values.Match(list => list.Count, _ => -1).ShouldBe(0);
    }

    /// <summary>
    /// The token is handed to each operation and checked before each: once cancelled, the next
    /// operation is not started, and the cancellation propagates rather than becoming an error.
    /// </summary>
    [Fact]
    public async Task TraverseAsync_hands_the_token_on_and_stops_once_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new List<int>();

        Task<Result<int, string>> Load(int n, CancellationToken token)
        {
            token.ShouldBe(cancellation.Token, "the operation receives the caller's token");
            started.Add(n);
            if (n == 2) cancellation.Cancel();
            return Task.FromResult(Ok(n));
        }

        await Should.ThrowAsync<OperationCanceledException>(() => new[] { 1, 2, 3 }.TraverseAsync(Load, cancellation.Token));
        started.ShouldBe([1, 2], "the third operation is not started after the token was cancelled");

        using var commandCancellation = new CancellationTokenSource();

        Task<Result<string>> Send(int n, CancellationToken token)
        {
            token.ShouldBe(commandCancellation.Token);
            started.Add(n);
            if (n == 2) commandCancellation.Cancel();
            return Task.FromResult(Result<string>.Success());
        }

        started.Clear();
        await Should.ThrowAsync<OperationCanceledException>(() => new[] { 1, 2, 3 }.TraverseAsync(Send, commandCancellation.Token));
        started.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task TraverseAsync_with_a_token_that_is_not_cancelled_runs_every_operation()
    {
        var values = await new[] { 1, 2 }.TraverseAsync((n, _) => Task.FromResult(Ok(n * 10)), CancellationToken.None);
        values.Match(list => list, _ => []).ShouldBe([10, 20]);

        var command = await new[] { 1, 2 }.TraverseAsync((_, _) => Task.FromResult(Result<string>.Success()), CancellationToken.None);
        ((bool)command).ShouldBeTrue();

        var refused = await new[] { 1, 2 }.TraverseAsync((n, _) => Task.FromResult(n == 2 ? Fail("two") : Ok(n)), CancellationToken.None);
        refused.Match(_ => "", error => error).ShouldBe("two");
    }

    [Fact]
    public async Task TraverseAsync_with_a_cancelled_token_starts_nothing()
    {
        var started = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => new[] { 1 }.TraverseAsync((n, _) => { started++; return Task.FromResult(Ok(n)); }, new CancellationToken(canceled: true)));
        started.ShouldBe(0);
    }
}
