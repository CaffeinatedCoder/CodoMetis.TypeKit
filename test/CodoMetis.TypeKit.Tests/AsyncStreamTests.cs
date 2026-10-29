namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>Choose</c>, <c>SequenceAsync</c> and <c>TraverseAsync</c> over an <see cref="IAsyncEnumerable{T}"/>:
/// in order, the caller's token reaching the stream's enumerator, nothing read after the first error,
/// and the enumerator disposed however the reading ends.
/// </summary>
/// <remarks>
/// Every case runs over a stream whose elements arrive at once and over one whose elements each wait,
/// since an async iterator that never waits completes every step synchronously and hides a step that
/// resumes in the wrong place.
/// </remarks>
public sealed class AsyncStreamTests
{
    public static TheoryData<bool> Waits => [false, true];

    private static Option<int> Even(int n) => n % 2 == 0 ? Option.Some(n) : Option.None();

    private static Result<int, string> Ok(int value) => Result<int, string>.Success(value);

    private static Result<int, string> Fail(string error) => Result<int, string>.Error(error);

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task Choose_keeps_the_values_of_the_options_that_hold_one_in_order(bool waits)
    {
        var options = new TrackedStream<Option<int>>(waits, Option.Some(1), Option.None(), Option.Some(3), default, Option.Some(5));

        (await options.Choose().ToListAsync(TestContext.Current.CancellationToken)).ShouldBe([1, 3, 5]);
        (await new TrackedStream<Option<int>>(waits).Choose().ToListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        options.Disposed.ShouldBeTrue();
    }

    /// <summary>A <c>default</c> option is <c>None</c>, so a stream holding one answers what the same sequence does.</summary>
    [Theory]
    [MemberData(nameof(Waits))]
    public async Task Choose_over_a_stream_answers_what_it_answers_over_the_same_sequence(bool waits)
    {
        Option<int>[] options = [default, Option.Some(2), Option.None(), default, Option.Some(4)];
        int[] numbers = [1, 2, 3, 4, 5, 6];

        (await new TrackedStream<Option<int>>(waits, options).Choose().ToListAsync(TestContext.Current.CancellationToken)).ShouldBe(options.Choose().ToList());
        (await new TrackedStream<int>(waits, numbers).Choose(Even).ToListAsync(TestContext.Current.CancellationToken)).ShouldBe(numbers.Choose(Even).ToList());
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task Choose_with_a_selector_keeps_what_it_answers_Some_for_and_reads_null_elements(bool waits)
    {
        (await new TrackedStream<int>(waits, 1, 2, 3, 4, 5, 6).Choose(Even).ToListAsync(TestContext.Current.CancellationToken)).ShouldBe([2, 4, 6]);
        (await new TrackedStream<string?>(waits, "Annie", null, "Bo").Choose(nickname => nickname.ToOption()).ToListAsync(TestContext.Current.CancellationToken)).ShouldBe(["Annie", "Bo"]);
        (await new TrackedStream<int?>(waits, 3, null, 5).Choose(floor => floor.ToOption()).ToListAsync(TestContext.Current.CancellationToken)).ShouldBe([3, 5]);
    }

    /// <summary>An async LINQ operator reads its source when it is enumerated, not when it is called, and only as far as the caller reads.</summary>
    [Theory]
    [MemberData(nameof(Waits))]
    public async Task Choose_is_lazy_and_disposes_the_stream_when_the_caller_stops(bool waits)
    {
        var numbers = new TrackedStream<int>(waits, 1, 2, 3, 4, 5, 6);

        var chosen = numbers.Choose(Even);
        numbers.Enumerations.ShouldBe(0, "nothing is read until the result is enumerated");

        await foreach (var value in chosen)
        {
            value.ShouldBe(2);
            break;
        }

        numbers.Read.ShouldBe(2, "the stream is read only as far as the first value found");
        numbers.Disposed.ShouldBeTrue("a break disposes the stream's enumerator");
    }

    /// <summary>The arguments are checked when the call is made, as LINQ's are, not when the stream is first read.</summary>
    [Fact]
    public void Choose_checks_its_arguments_before_the_stream_is_read()
    {
        Should.Throw<ArgumentNullException>(() => ((IAsyncEnumerable<int>)null!).Choose(Even)).ParamName.ShouldBe("source");
        Should.Throw<ArgumentNullException>(() => ((IAsyncEnumerable<Option<int>>)null!).Choose()).ParamName.ShouldBe("options");
        Should.Throw<ArgumentNullException>(() => new TrackedStream<int>(false, 1).Choose((Func<int, Option<int>>)null!)).ParamName.ShouldBe("selector");
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task Choose_hands_the_token_of_WithCancellation_and_of_GetAsyncEnumerator_to_the_stream(bool waits)
    {
        using var cancellation = new CancellationTokenSource();
        var numbers = new TrackedStream<int>(waits, 1, 2);
        var options = new TrackedStream<Option<int>>(waits, Option.Some(1));

        await foreach (var _ in numbers.Choose(Even).WithCancellation(cancellation.Token)) { }
        numbers.Token.ShouldBe(cancellation.Token);

        await foreach (var _ in options.Choose().WithCancellation(cancellation.Token)) { }
        options.Token.ShouldBe(cancellation.Token);

        var held = new TrackedStream<int>(waits, 1, 2, 3, 4);
        await using (var enumerator = held.Choose(Even).GetAsyncEnumerator(cancellation.Token))
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            held.Disposed.ShouldBeFalse();
        }

        held.Token.ShouldBe(cancellation.Token);
        held.Disposed.ShouldBeTrue("disposing the chosen values disposes the stream's enumerator");
    }

    /// <summary>A stream that honours its token stops the chosen values when the caller cancels.</summary>
    [Theory]
    [MemberData(nameof(Waits))]
    public async Task Choose_stops_when_the_caller_cancels_a_stream_that_honours_its_token(bool waits)
    {
        using var cancellation = new CancellationTokenSource();
        var numbers = new TrackedStream<int>(waits, 2, 4, 6) { OnRead = n => { if (n == 4) cancellation.Cancel(); }, HonoursToken = true };
        var seen = new List<int>();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var value in numbers.Choose(Even).WithCancellation(cancellation.Token)) seen.Add(value);
        });

        seen.ShouldBe([2, 4]);
        numbers.Disposed.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task Choose_disposes_the_stream_when_the_selector_throws(bool waits)
    {
        var numbers = new TrackedStream<int>(waits, 1, 2, 3);

        await Should.ThrowAsync<FormatException>(() => numbers.Choose(n => n == 2 ? throw new FormatException() : Option.Some(n)).ToListAsync(TestContext.Current.CancellationToken).AsTask());

        numbers.Read.ShouldBe(2);
        numbers.Disposed.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task SequenceAsync_collects_every_value_in_order(bool waits)
    {
        var values = await new TrackedStream<Result<int, string>>(waits, Ok(1), Ok(2), Ok(3)).SequenceAsync(TestContext.Current.CancellationToken);
        values.Match(list => list, _ => []).ShouldBe([1, 2, 3]);

        var none = await new TrackedStream<Result<int, string>>(waits).SequenceAsync(TestContext.Current.CancellationToken);
        none.Match(list => list.Count, _ => -1).ShouldBe(0);

        ((bool)await new TrackedStream<Result<string>>(waits, Result<string>.Success(), Result<string>.Success()).SequenceAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        ((bool)await new TrackedStream<Result<string>>(waits).SequenceAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task SequenceAsync_answers_the_first_error_reads_nothing_after_it_and_disposes_the_stream(bool waits)
    {
        var results = new TrackedStream<Result<int, string>>(waits, Ok(1), Fail("x"), Fail("y"), Ok(4));

        (await results.SequenceAsync(TestContext.Current.CancellationToken)).Match(_ => "", error => error).ShouldBe("x");
        results.Read.ShouldBe(2);
        results.Disposed.ShouldBeTrue();

        var commands = new TrackedStream<Result<string>>(waits, Result<string>.Success(), Result<string>.Error("refused"), default);

        (await commands.SequenceAsync(TestContext.Current.CancellationToken)).TryGetError(out var refused).ShouldBeTrue();
        refused.ShouldBe("refused");
        commands.Read.ShouldBe(2, "the uninitialized result after the error is never read");
        commands.Disposed.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task SequenceAsync_refuses_an_uninitialized_result_and_disposes_the_stream(bool waits)
    {
        var results = new TrackedStream<Result<int, string>>(waits, Ok(1), default, Ok(3));
        var commands = new TrackedStream<Result<string>>(waits, Result<string>.Success(), default);

        (await Should.ThrowAsync<InvalidOperationException>(() => results.SequenceAsync())).Message.ShouldContain("never initialized");
        (await Should.ThrowAsync<InvalidOperationException>(() => commands.SequenceAsync())).Message.ShouldContain("never initialized");
        results.Disposed.ShouldBeTrue();
        commands.Disposed.ShouldBeTrue();
    }

    /// <summary>
    /// The token reaches the stream's enumerator and is checked before each result is read, so a stream
    /// that ignores its token still stops: the cancellation propagates rather than becoming an error.
    /// </summary>
    [Theory]
    [MemberData(nameof(Waits))]
    public async Task SequenceAsync_hands_the_token_to_the_stream_and_stops_once_cancelled(bool waits)
    {
        using var cancellation = new CancellationTokenSource();
        var results = new TrackedStream<Result<int, string>>(waits, Ok(1), Ok(2), Ok(3)) { OnRead = r => { if (r == Ok(2)) cancellation.Cancel(); } };

        await Should.ThrowAsync<OperationCanceledException>(() => results.SequenceAsync(cancellation.Token));
        results.Token.ShouldBe(cancellation.Token);
        results.Read.ShouldBe(2, "nothing is read after the token was cancelled");
        results.Disposed.ShouldBeTrue();

        using var commandCancellation = new CancellationTokenSource();
        var commands = new TrackedStream<Result<string>>(waits, Result<string>.Success(), Result<string>.Success(), Result<string>.Success());
        commands.OnRead = _ => { if (commands.Read == 2) commandCancellation.Cancel(); };

        await Should.ThrowAsync<OperationCanceledException>(() => commands.SequenceAsync(commandCancellation.Token));
        commands.Token.ShouldBe(commandCancellation.Token);
        commands.Read.ShouldBe(2);
        commands.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task SequenceAsync_with_a_cancelled_token_reads_nothing()
    {
        var results = new TrackedStream<Result<int, string>>(false, Ok(1));
        var commands = new TrackedStream<Result<string>>(false, Result<string>.Success());

        await Should.ThrowAsync<OperationCanceledException>(() => results.SequenceAsync(new CancellationToken(canceled: true)));
        await Should.ThrowAsync<OperationCanceledException>(() => commands.SequenceAsync(new CancellationToken(canceled: true)));
        results.Read.ShouldBe(0);
        commands.Read.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task TraverseAsync_awaits_each_operation_in_order_and_collects_the_values(bool waits)
    {
        var order = new List<string>();
        var texts = new TrackedStream<string>(waits, "1", "2", "3") { OnRead = text => order.Add($"read {text}") };

        async Task<Result<int, string>> Parse(string text)
        {
            order.Add($"start {text}");
            await Task.Yield();
            order.Add($"end {text}");
            return int.TryParse(text, out var n) ? Ok(n) : Fail(text);
        }

        var parsed = await texts.TraverseAsync(Parse);

        parsed.Match(values => values, _ => []).ShouldBe([1, 2, 3]);
        order.ShouldBe(["read 1", "start 1", "end 1", "read 2", "start 2", "end 2", "read 3", "start 3", "end 3"], "one element at a time, its operation finished before the next is read");
        texts.Disposed.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task TraverseAsync_stops_reading_after_an_error_and_disposes_the_stream(bool waits)
    {
        var started = new List<string>();
        var texts = new TrackedStream<string>(waits, "1", "x", "y", "4");

        Task<Result<int, string>> Parse(string text)
        {
            started.Add(text);
            return Task.FromResult(int.TryParse(text, out var n) ? Ok(n) : Fail(text));
        }

        (await texts.TraverseAsync(Parse)).Match(_ => "", error => error).ShouldBe("x");
        started.ShouldBe(["1", "x"]);
        texts.Read.ShouldBe(2);
        texts.Disposed.ShouldBeTrue();

        var sent = new List<int>();
        var numbers = new TrackedStream<int>(waits, 1, 2, 3, 4);

        Task<Result<string>> Send(int n)
        {
            if (n == 3) return Task.FromResult(Result<string>.Error($"refused {n}"));

            sent.Add(n);
            return Task.FromResult(Result<string>.Success());
        }

        (await numbers.TraverseAsync(Send)).TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe("refused 3");
        sent.ShouldBe([1, 2]);
        numbers.Read.ShouldBe(3);
        numbers.Disposed.ShouldBeTrue();

        ((bool)await new TrackedStream<int>(waits, 1, 2).TraverseAsync(Send)).ShouldBeTrue();
        ((bool)await new TrackedStream<int>(waits).TraverseAsync(Send)).ShouldBeTrue();
        (await new TrackedStream<string>(waits).TraverseAsync(Parse)).Match(list => list.Count, _ => -1).ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task TraverseAsync_disposes_the_stream_when_the_selector_throws(bool waits)
    {
        var numbers = new TrackedStream<int>(waits, 1, 2, 3);
        var commands = new TrackedStream<int>(waits, 1, 2, 3);

        await Should.ThrowAsync<FormatException>(() => numbers.TraverseAsync(n => n == 2 ? throw new FormatException() : Task.FromResult(Ok(n))));
        await Should.ThrowAsync<FormatException>(() => commands.TraverseAsync((n, _) => n == 2 ? throw new FormatException() : Task.FromResult(Result<string>.Success()), CancellationToken.None));

        numbers.Read.ShouldBe(2);
        numbers.Disposed.ShouldBeTrue();
        commands.Read.ShouldBe(2);
        commands.Disposed.ShouldBeTrue();
    }

    /// <summary>
    /// The token is handed to the stream and to each operation, and checked before each element is read
    /// and before each operation: once cancelled, nothing more is read or started, and the cancellation
    /// propagates rather than becoming an error.
    /// </summary>
    [Theory]
    [MemberData(nameof(Waits))]
    public async Task TraverseAsync_hands_the_token_on_and_stops_once_cancelled(bool waits)
    {
        using var cancellation = new CancellationTokenSource();
        var numbers = new TrackedStream<int>(waits, 1, 2, 3);
        var started = new List<int>();

        Task<Result<int, string>> Load(int n, CancellationToken token)
        {
            token.ShouldBe(cancellation.Token, "the operation receives the caller's token");
            started.Add(n);
            if (n == 2) cancellation.Cancel();
            return Task.FromResult(Ok(n));
        }

        await Should.ThrowAsync<OperationCanceledException>(() => numbers.TraverseAsync(Load, cancellation.Token));
        numbers.Token.ShouldBe(cancellation.Token, "the stream's enumerator receives the caller's token");
        started.ShouldBe([1, 2]);
        numbers.Read.ShouldBe(2, "the third element is not read after the token was cancelled");
        numbers.Disposed.ShouldBeTrue();

        using var commandCancellation = new CancellationTokenSource();
        var commands = new TrackedStream<int>(waits, 1, 2, 3) { OnRead = n => { if (n == 2) commandCancellation.Cancel(); } };
        started.Clear();

        Task<Result<string>> Send(int n, CancellationToken token)
        {
            token.ShouldBe(commandCancellation.Token);
            started.Add(n);
            return Task.FromResult(Result<string>.Success());
        }

        await Should.ThrowAsync<OperationCanceledException>(() => commands.TraverseAsync(Send, commandCancellation.Token));
        commands.Token.ShouldBe(commandCancellation.Token);
        started.ShouldBe([1], "the command for an element read after the token was cancelled is not started");
        commands.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task TraverseAsync_with_a_cancelled_token_reads_and_starts_nothing()
    {
        var numbers = new TrackedStream<int>(false, 1);
        var started = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => numbers.TraverseAsync((n, _) => { started++; return Task.FromResult(Ok(n)); }, new CancellationToken(canceled: true)));
        await Should.ThrowAsync<OperationCanceledException>(() => numbers.TraverseAsync((_, _) => { started++; return Task.FromResult(Result<string>.Success()); }, new CancellationToken(canceled: true)));

        started.ShouldBe(0);
        numbers.Read.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Waits))]
    public async Task TraverseAsync_with_a_token_that_is_not_cancelled_runs_every_operation(bool waits)
    {
        var values = await new TrackedStream<int>(waits, 1, 2).TraverseAsync((n, _) => Task.FromResult(Ok(n * 10)), CancellationToken.None);
        values.Match(list => list, _ => []).ShouldBe([10, 20]);

        var command = await new TrackedStream<int>(waits, 1, 2).TraverseAsync((_, _) => Task.FromResult(Result<string>.Success()), CancellationToken.None);
        ((bool)command).ShouldBeTrue();

        var refused = new TrackedStream<int>(waits, 1, 2, 3);
        (await refused.TraverseAsync((n, _) => Task.FromResult(n == 2 ? Fail("two") : Ok(n)), CancellationToken.None)).Match(_ => "", error => error).ShouldBe("two");
        refused.Read.ShouldBe(2);
        refused.Disposed.ShouldBeTrue();
    }

    /// <summary>The calls in a consumer's compilation, nullable annotations on, beside <c>System.Linq.AsyncEnumerable</c>'s operators: no warning and no ambiguity.</summary>
    [Theory]
    [InlineData("IAsyncEnumerable<string?> nicknames", "nicknames.Choose(nickname => nickname.ToOption())")]
    [InlineData("IAsyncEnumerable<int?> floors", "floors.Where(floor => floor > 0).Choose(floor => floor.ToOption()).Select(floor => floor * 10)")]
    [InlineData("IAsyncEnumerable<int> numbers", "numbers.Select(n => n > 0 ? Option.Some(n) : Option.None<int>()).Choose()")]
    [InlineData("IAsyncEnumerable<Result<int, string>> results", "results.SequenceAsync()")]
    [InlineData("IAsyncEnumerable<Result<string>> results", "results.Where(result => true).SequenceAsync(CancellationToken.None)")]
    [InlineData("IAsyncEnumerable<string> texts", "texts.TraverseAsync(text => Task.FromResult(Result<int, string>.Success(text.Length)))")]
    [InlineData("IAsyncEnumerable<string> texts", "texts.TraverseAsync(async (text, token) => { await Task.Delay(1, token); return Result<string>.Success(); }, CancellationToken.None)")]
    [InlineData("int[] numbers", "numbers.Choose(n => Option.Some(n))")]
    [InlineData("int[] numbers", "numbers.TraverseAsync(n => Task.FromResult(Result<int, string>.Success(n)))")]
    public void The_stream_members_compile_without_a_warning_beside_the_async_LINQ_operators(string parameter, string call) =>
        ConsumerCompilation.Of($$"""
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using CodoMetis.TypeKit;

            public static class Checks
            {
                public static object Run({{parameter}}) => {{call}};
            }
            """).Problems().ShouldBeEmpty();

    /// <summary>
    /// A stream that records what its enumerator was given and how far it was read, and whether it was
    /// disposed. It ignores its token unless told to honour it, as many producers do.
    /// </summary>
    private sealed class TrackedStream<T>(bool waits, params T[] elements) : IAsyncEnumerable<T>
    {
        public CancellationToken Token { get; private set; }

        public int Enumerations { get; private set; }

        /// <summary>How many elements were handed out.</summary>
        public int Read { get; private set; }

        public bool Disposed { get; private set; }

        public Action<T>? OnRead { get; set; }

        public bool HonoursToken { get; init; }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Enumerations++;

            return Enumerate(cancellationToken);
        }

        private async IAsyncEnumerator<T> Enumerate(CancellationToken cancellationToken)
        {
            try
            {
                foreach (var element in elements)
                {
                    if (waits) await Task.Yield();
                    if (HonoursToken) cancellationToken.ThrowIfCancellationRequested();

                    Read++;
                    OnRead?.Invoke(element);
                    yield return element;
                }
            }
            finally
            {
                Disposed = true;
            }
        }
    }
}
