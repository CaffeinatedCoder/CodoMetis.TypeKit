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
    /// form over a value-returning call would discard the value, and a <c>ValueTask</c>'s exception with
    /// it. That is a compile error naming the valued form; a statement lambda is a command.
    /// </summary>
    [Theory]
    [InlineData("Result.Catch<FormatException>(() => int.Parse(text))", "CS0619")]
    [InlineData("Result.Catch<FormatException>(() => Items.Remove(text))", "CS0619")]
    [InlineData("Result.Catch<FormatException>(() => { Items.Remove(text); })", "")]
    [InlineData("Result.Catch<FormatException>(() => Items.Clear())", "")]
    [InlineData("Result.Catch<int, FormatException>(() => int.Parse(text))", "")]
    public void A_one_type_argument_Catch_over_a_value_is_a_compile_error_naming_the_valued_form(string call, string error)
    {
        var compilation = ConsumerCompilation.Of($$"""
            using System;
            using System.Collections.Generic;
            using CodoMetis.TypeKit;

            static class Consumer
            {
                static readonly List<string> Items = [];
                static object Call(string text) => {{call}};
            }
            """);

        compilation.ErrorIds().ShouldBe(error.Length == 0 ? [] : [error], compilation.Problems());
        if (error.Length > 0) compilation.Problems().ShouldContain("Catch<T, TException>");
    }

    /// <summary>
    /// A delegate that returns a <c>Task</c>, an <c>async</c> lambda among them, is a compile error whose message
    /// names <c>CatchAsync</c>: its exception would be thrown after <c>Catch</c> had returned a success.
    /// </summary>
    [Theory]
    [InlineData("Result.Catch(() => Task.FromResult(1))")]
    [InlineData("Result.Catch(() => Task.CompletedTask)")]
    [InlineData("Result.Catch(async () => await Task.Yield())")]
    [InlineData("Result.Catch<InvalidOperationException>(() => Task.CompletedTask)")]
    public void Catch_over_a_task_is_a_compile_error_naming_CatchAsync(string call)
    {
        var compilation = ConsumerCompilation.Of($$"""
            using System;
            using System.Threading.Tasks;
            using CodoMetis.TypeKit;

            static class Consumer
            {
                static object Call() => {{call}};
            }
            """);

        compilation.ErrorIds().ShouldBe(["CS0619"], compilation.Problems());
        compilation.Problems().ShouldContain("Use CatchAsync.");
    }

    /// <summary>
    /// A task's type spelled as the value, or inferred where no overload can name it (a
    /// <c>ValueTask</c>), would make the task's exception arrive after a success was returned. It is
    /// refused before the delegate runs, and the message names <c>CatchAsync</c>; <c>CatchAsync</c>
    /// whose task's value is another task is refused the same way.
    /// </summary>
    [Fact]
    public async Task A_task_as_the_value_is_refused_before_the_delegate_runs()
    {
        var ran = false;
        Task<int> Fails() { ran = true; return Task.FromException<int>(Boom); }

        Should.Throw<ArgumentException>(() => Result.Catch<Task<int>, InvalidOperationException>(Fails)).Message.ShouldContain("Use CatchAsync.");
        Should.Throw<ArgumentException>(() => Result.Catch<Task<int>>(Fails));
        Should.Throw<ArgumentException>(() => Result.Catch<Task, InvalidOperationException>(Fails));
        Should.Throw<ArgumentException>(() => Result.Catch(() => { ran = true; return new ValueTask<int>(1); }));
        Should.Throw<ArgumentException>(() => Result.Catch(() => { ran = true; return ValueTask.CompletedTask; }));
        (await Should.ThrowAsync<ArgumentException>(() => Result.CatchAsync(() => Task.FromResult(Fails())))).Message.ShouldContain("Await the inner task");
        await Should.ThrowAsync<ArgumentException>(() => Result.CatchAsync<Task<int>, InvalidOperationException>(() => Task.FromResult(Fails())));

        ran.ShouldBeFalse();
    }

    /// <summary>
    /// A delegate that returns no task is refused before anything is awaited, in every overload, as a
    /// delegate that returns a null value is: it was an error holding the NullReferenceException the
    /// await threw. A delegate that throws before it returns a task is still that exception as the error.
    /// </summary>
    [Fact]
    public async Task CatchAsync_refuses_a_delegate_that_returns_no_task_and_keeps_one_that_throws_first()
    {
        (await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync<int>(() => null!))).ParamName.ShouldBe("action");
        (await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync(() => null!))).ParamName.ShouldBe("action");
        (await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync<int, InvalidOperationException>(() => null!))).ParamName.ShouldBe("action");
        (await Should.ThrowAsync<ArgumentNullException>(() => Result.CatchAsync<InvalidOperationException>(() => null!))).ParamName.ShouldBe("action");

        // Method groups, since a lambda that only throws converts to the valued and the command form alike.
        static Task<int> ThrowsForAValue() => throw new InvalidOperationException("before any task");
        static Task ThrowsForACommand() => throw new InvalidOperationException("before any task");

        var thrown = await Result.CatchAsync(ThrowsForAValue);
        thrown.Match(_ => "", error => error.Message).ShouldBe("before any task");

        var command = await Result.CatchAsync<InvalidOperationException>(ThrowsForACommand);
        command.Match(() => "", error => error.Message).ShouldBe("before any task");
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

    /// <summary>
    /// <c>CatchAsync</c> keeps <c>await</c>'s semantics: the error is what
    /// <c>try { await task; } catch (Exception e)</c> catches, so for a task that faulted with several
    /// exceptions, as <c>Task.WhenAll</c>'s does, the first of them, and a narrowed form looks at that one
    /// alone. The task's <see cref="Task.Exception"/> holds them all, which is what a caller holds the
    /// task for where every failure matters.
    /// </summary>
    [Fact]
    public async Task CatchAsync_over_a_task_with_several_exceptions_answers_the_one_await_throws()
    {
        var first = new InvalidOperationException("first");
        var second = new FormatException("second");

        var both = Task.WhenAll(Task.FromException(first), Task.FromException(second));
        Exception? awaited = null;
        try { await both; }
        catch (Exception exception) { awaited = exception; }

        (await Result.CatchAsync(() => both)).TryGetError(out var error).ShouldBeTrue();
        error.ShouldBeSameAs(first);
        error.ShouldBeSameAs(awaited);
        both.Exception!.InnerExceptions.ShouldBe([first, second], "every failure is on the task");

        var values = Task.WhenAll(Task.FromException<int>(first), Task.FromException<int>(second));
        (await Result.CatchAsync(() => values)).TryGetValue(out _, out var valued).ShouldBeFalse();
        valued.ShouldBeSameAs(first);
        (await Result.CatchAsync<int[], InvalidOperationException>(() => values)).TryGetValue(out _, out var narrowed).ShouldBeFalse();
        narrowed.ShouldBeSameAs(first);
        (await Should.ThrowAsync<InvalidOperationException>(() => Result.CatchAsync<int[], FormatException>(() => values))).ShouldBeSameAs(first, "the second exception is never looked at");
        (await Result.CatchAsync<InvalidOperationException>(() => both)).TryGetError(out var command).ShouldBeTrue();
        command.ShouldBeSameAs(first);
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

    /// <summary>
    /// HttpClient reports its <c>Timeout</c> elapsing as a <see cref="TaskCanceledException"/> over a
    /// <see cref="TimeoutException"/>: the call failed, and nobody cancelled it. A handler that waits
    /// for its token leaves the timeout as the only way out, so this does not race.
    /// </summary>
    [Fact]
    public async Task CatchAsync_answers_an_HttpClient_timeout_as_the_error()
    {
        using var client = new HttpClient(new WaitingHandler()) { Timeout = TimeSpan.FromMilliseconds(1) };

        var fetched = await Result.CatchAsync(() => client.GetByteArrayAsync(new Uri("http://timeout.test/")));

        fetched.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBeOfType<TaskCanceledException>().InnerException.ShouldBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task Every_overload_answers_a_timeout_as_the_error()
    {
        Result.Catch(int () => throw Timeout).TryGetValue(out _, out var valued).ShouldBeFalse();
        valued.ShouldBeSameAs(Timeout);
        Result.Catch(void () => throw Timeout).TryGetError(out var command).ShouldBeTrue();
        command.ShouldBeSameAs(Timeout);
        Result.Catch<int, OperationCanceledException>(() => throw Timeout).TryGetValue(out _, out var narrowed).ShouldBeFalse();
        narrowed.ShouldBeSameAs(Timeout);
        Result.Catch<TaskCanceledException>(void () => throw Timeout).TryGetError(out var narrowedCommand).ShouldBeTrue();
        narrowedCommand.ShouldBeSameAs(Timeout);

        (await Result.CatchAsync(TimesOutLaterAsync)).TryGetValue(out _, out var later).ShouldBeFalse();
        later.ShouldBeSameAs(Timeout);
        (await Result.CatchAsync(TimesOutLaterCommandAsync)).TryGetError(out var laterCommand).ShouldBeTrue();
        laterCommand.ShouldBeSameAs(Timeout);
        (await Result.CatchAsync<int, OperationCanceledException>(TimesOutLaterAsync)).TryGetValue(out _, out var laterNarrowed).ShouldBeFalse();
        laterNarrowed.ShouldBeSameAs(Timeout);
        (await Result.CatchAsync<OperationCanceledException>(TimesOutLaterCommandAsync)).TryGetError(out var laterNarrowedCommand).ShouldBeTrue();
        laterNarrowedCommand.ShouldBeSameAs(Timeout);
    }

    /// <summary>A timeout is still the exception it is: a narrowed catch of another type lets it propagate.</summary>
    [Fact]
    public async Task A_narrowed_catch_of_another_type_lets_a_timeout_propagate()
    {
        Should.Throw<TaskCanceledException>(() => Result.Catch<int, HttpRequestException>(() => throw Timeout));
        await Should.ThrowAsync<TaskCanceledException>(() => Result.CatchAsync<int, HttpRequestException>(TimesOutLaterAsync));
    }

    /// <summary>Only a timeout is the error: a cancellation over any other inner exception, or none, still passes through.</summary>
    [Fact]
    public async Task A_cancellation_that_is_no_timeout_still_passes_through()
    {
        var cancelled = new OperationCanceledException("cancelled", new IOException("the stream closed"));

        Should.Throw<OperationCanceledException>(() => Result.Catch(int () => throw cancelled));
        Should.Throw<OperationCanceledException>(() => Result.Catch(void () => throw new TaskCanceledException()));
        await Should.ThrowAsync<OperationCanceledException>(() => Result.CatchAsync(async Task<int> () => { await Task.Yield(); throw cancelled; }));
    }

    private static Task<int> Cancelled => Task.FromCanceled<int>(new CancellationToken(canceled: true));

    private static Task CancelledCommand => Task.FromCanceled(new CancellationToken(canceled: true));

    private static void WaitsForACancelledTask() => CancelledCommand.Wait();

    private static int ReadsACancelledTask() => Cancelled.Result;

    /// <summary>
    /// <c>Task.Wait()</c> and <c>.Result</c> throw a cancelled task's cancellation inside an
    /// <see cref="AggregateException"/>. One that holds nothing but cancellations is a cancellation, and
    /// passes through every overload as it was thrown: it was the error, so <c>Recover</c> ran on a
    /// cancelled call.
    /// </summary>
    [Fact]
    public async Task A_cancellation_that_Wait_or_Result_wraps_passes_through_every_overload()
    {
        Should.Throw<AggregateException>(() => Result.Catch(() => Cancelled.Result)).InnerException.ShouldBeOfType<TaskCanceledException>();
        Should.Throw<AggregateException>(() => Result.Catch(() => CancelledCommand.Wait())).InnerException.ShouldBeOfType<TaskCanceledException>();
        Should.Throw<AggregateException>(() => Result.Catch<int, AggregateException>(() => Cancelled.Result));
        Should.Throw<AggregateException>(() => Result.Catch<int, Exception>(() => Cancelled.Result));
        Should.Throw<AggregateException>(() => Result.Catch<AggregateException>(() => CancelledCommand.Wait()));
        Should.Throw<AggregateException>(() => Result.Catch<Exception>(() => CancelledCommand.Wait()));

        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync(async () => { await Task.Yield(); return Cancelled.Result; }));
        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync(async () => { await Task.Yield(); CancelledCommand.Wait(); }));
        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync<int, AggregateException>(async () => { await Task.Yield(); return Cancelled.Result; }));
        await Should.ThrowAsync<AggregateException>(() => Result.CatchAsync<AggregateException>(async () => { await Task.Yield(); CancelledCommand.Wait(); }));
    }

    /// <summary>Nothing catches and rethrows it: the instance thrown arrives, with the stack of its throw.</summary>
    [Fact]
    public void A_wrapped_cancellation_arrives_as_it_was_thrown()
    {
        var wrapped = new AggregateException(new OperationCanceledException(), new AggregateException(new TaskCanceledException()));

        Should.Throw<AggregateException>(() => Result.Catch(int () => throw wrapped)).ShouldBeSameAs(wrapped, "nested aggregates of cancellations are a cancellation too");
        Should.Throw<AggregateException>(() => Result.Catch(void () => throw wrapped)).ShouldBeSameAs(wrapped);

        Should.Throw<AggregateException>(() => Result.Catch(ReadsACancelledTask)).StackTrace!.ShouldContain(nameof(ReadsACancelledTask), Case.Sensitive, "the stack is the original throw's");
        Should.Throw<AggregateException>(() => Result.Catch(WaitsForACancelledTask)).StackTrace!.ShouldContain(nameof(WaitsForACancelledTask), Case.Sensitive, "the stack is the original throw's");
    }

    /// <summary>
    /// A cancellation beside a failure, as <c>Task.WaitAll</c> throws for one task cancelled and one
    /// faulted, is not a cancellation alone: the aggregate is the error, as it was thrown.
    /// </summary>
    [Fact]
    public async Task An_aggregate_that_mixes_a_cancellation_with_a_failure_is_the_error()
    {
        Result.Catch(() => Task.WaitAll(CancelledCommand, Task.FromException(Boom))).TryGetError(out var waited).ShouldBeTrue();
        waited.ShouldBeOfType<AggregateException>().InnerExceptions.Select(inner => inner.GetType()).ShouldBe([typeof(TaskCanceledException), typeof(InvalidOperationException)]);

        var mixed = new AggregateException(new OperationCanceledException(), Boom);

        Result.Catch(int () => throw mixed).TryGetValue(out _, out var valued).ShouldBeFalse();
        valued.ShouldBeSameAs(mixed);
        Result.Catch(void () => throw mixed).TryGetError(out var command).ShouldBeTrue();
        command.ShouldBeSameAs(mixed);
        Result.Catch<int, AggregateException>(() => throw mixed).TryGetValue(out _, out var narrowed).ShouldBeFalse();
        narrowed.ShouldBeSameAs(mixed);
        Result.Catch<AggregateException>(void () => throw mixed).TryGetError(out var narrowedCommand).ShouldBeTrue();
        narrowedCommand.ShouldBeSameAs(mixed);

        (await Result.CatchAsync(async Task<int> () => { await Task.Yield(); throw mixed; })).TryGetValue(out _, out var later).ShouldBeFalse();
        later.ShouldBeSameAs(mixed);
        (await Result.CatchAsync(async () => { await Task.Yield(); throw mixed; })).TryGetError(out var laterCommand).ShouldBeTrue();
        laterCommand.ShouldBeSameAs(mixed);
    }

    /// <summary>
    /// A timeout is the error wrapped or not (decision 46). An aggregate that holds one, alone or beside
    /// cancellations, is not a cancellation alone, and is the error as it was thrown.
    /// </summary>
    [Fact]
    public async Task A_wrapped_timeout_is_the_error()
    {
        Result.Catch(() => Task.FromException<int>(Timeout).Result).TryGetValue(out _, out var read).ShouldBeFalse();
        read.ShouldBeOfType<AggregateException>().InnerException.ShouldBeSameAs(Timeout);

        var beside = new AggregateException(new OperationCanceledException(), Timeout);
        Result.Catch(void () => throw beside).TryGetError(out var command).ShouldBeTrue();
        command.ShouldBeSameAs(beside);
        Result.Catch<int, AggregateException>(() => throw beside).TryGetValue(out _, out var narrowed).ShouldBeFalse();
        narrowed.ShouldBeSameAs(beside);
        (await Result.CatchAsync(async Task<int> () => { await Task.Yield(); throw beside; })).TryGetValue(out _, out var later).ShouldBeFalse();
        later.ShouldBeSameAs(beside);
    }

    /// <summary>
    /// <c>.Result</c> and <c>Wait()</c> on a cancelled task throw a new <see cref="TaskCanceledException"/>
    /// naming the task, without the exception the task was cancelled with (measured on .NET 10): for an
    /// <see cref="HttpClient"/> call whose <c>Timeout</c> elapsed, the <see cref="TimeoutException"/> is gone
    /// from what is thrown. It is still the task's, and the timeout is the error, as <c>CatchAsync</c> answers it.
    /// </summary>
    [Fact]
    public void An_HttpClient_timeout_read_through_Result_is_the_error()
    {
        using var client = new HttpClient(new WaitingHandler()) { Timeout = TimeSpan.FromMilliseconds(1) };

        var fetched = Result.Catch(() => client.GetByteArrayAsync(new Uri("http://timeout.test/")).Result);

        fetched.TryGetValue(out _, out var error).ShouldBeFalse();
        var thrown = error.ShouldBeOfType<AggregateException>().InnerException.ShouldBeOfType<TaskCanceledException>();
        thrown.InnerException.ShouldBeNull("the runtime's own exception for a cancelled task holds no inner exception");

        Should.Throw<AggregateException>(() => Result.Catch(() => client.GetByteArrayAsync(new Uri("http://timeout.test/"), new CancellationToken(canceled: true)).Result));
    }

    private static readonly TaskCanceledException Timeout = new("The request was canceled due to the configured HttpClient.Timeout elapsing.", new TimeoutException());

    private static async Task<int> TimesOutLaterAsync()
    {
        await Task.Yield();
        throw Timeout;
    }

    private static async Task TimesOutLaterCommandAsync()
    {
        await Task.Yield();
        throw Timeout;
    }

    /// <summary>Answers nothing until its token is cancelled, which only the client's timeout does.</summary>
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The client's timeout is the only way out.");
        }
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
    /// nothing it throws, and given to the command form <c>Catch&lt;TException&gt;</c> would be
    /// discarded, an <c>async</c> lambda as <c>async void</c>. The overloads over a task-returning
    /// delegate are more specific, so the compiler picks them, and they are errors that name
    /// <c>CatchAsync</c>. A <c>ValueTask</c> has no such overload, since it would break a build that works
    /// today (decision 110): the command form's refusal of any value covers it, and the valued form
    /// refuses it at run time.
    /// </summary>
    [Theory]
    [InlineData("Result.Catch(() => FetchAsync())")]
    [InlineData("Result.Catch(FetchAsync)")]
    [InlineData("Result.Catch(() => DoAsync())")]
    [InlineData("Result.Catch(DoAsync)")]
    [InlineData("Result.Catch(async () => await FetchAsync())")]
    [InlineData("Result.Catch(async () => await DoAsync())")]
    [InlineData("Result.Catch<InvalidOperationException>(() => FetchAsync())")]
    [InlineData("Result.Catch<InvalidOperationException>(FetchAsync)")]
    [InlineData("Result.Catch<InvalidOperationException>(() => DoAsync())")]
    [InlineData("Result.Catch<InvalidOperationException>(DoAsync)")]
    [InlineData("Result.Catch<InvalidOperationException>(async () => await DoAsync())")]
    [InlineData("Result.Catch<InvalidOperationException>(async () => { await DoAsync(); })")]
    [InlineData("Result.Catch<InvalidOperationException>(() => DoValueAsync())")]
    [InlineData("Result.Catch<InvalidOperationException>(() => FetchValueAsync())")]
    public void A_task_returning_delegate_given_to_Catch_is_a_compile_error_naming_CatchAsync(string call)
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

        compilation.ErrorIds().ShouldBe(["CS0619"], compilation.Problems());
        compilation.Problems().ShouldContain("CatchAsync");
    }

    /// <summary>
    /// The valued form with the value's type spelled cannot take a task at all: the compiler's own
    /// conversion error, with no overload to make it compile. A refusal overload there would make a
    /// lambda that only throws, or returns <c>null!</c>, ambiguous.
    /// </summary>
    [Theory]
    [InlineData("Result.Catch<int, InvalidOperationException>(() => FetchAsync())")]
    [InlineData("Result.Catch<int, InvalidOperationException>(FetchAsync)")]
    [InlineData("Result.Catch<int, InvalidOperationException>(async () => await FetchAsync())")]
    [InlineData("Result.Catch<int, InvalidOperationException>(() => FetchValueAsync())")]
    public void A_task_given_to_the_valued_Catch_with_its_value_type_spelled_does_not_compile(string call)
    {
        var compilation = ConsumerCompilation.Of($$"""
            using System;
            using System.Threading.Tasks;
            using CodoMetis.TypeKit;

            static class Consumer
            {
                static Task<int> FetchAsync() => Task.FromResult(1);
                static ValueTask<int> FetchValueAsync() => new(1);
                static object Call() => {{call}};
            }
            """);

        compilation.ErrorIds().ShouldNotBeEmpty(compilation.Problems());
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
    [InlineData("Result.Catch<InvalidOperationException>(() => throw new Exception())", "CS0121")]
    [InlineData("Result.Catch<InvalidOperationException>(void () => throw new Exception())", "")]
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
