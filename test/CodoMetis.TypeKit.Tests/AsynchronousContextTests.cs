using System.Reflection;
using System.Runtime.CompilerServices;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// Where the callbacks of the asynchronous members run, and that nothing resumes on the caller's
/// <see cref="SynchronizationContext"/> (decision 62).
/// </summary>
/// <remarks>
/// <para>
/// Every await in the package is <c>ConfigureAwait(false)</c>. A callback therefore runs where the task
/// it waited for completed: at once, on the caller's thread and context, when that task had already
/// completed, and without the caller's context, wherever the task completed, when it was still pending.
/// In a UI or a Blazor component the second is the one that breaks, by timing, so it is documented and
/// pinned here, member by member: a change to it is a decision, not an accident.
/// </para>
/// <para>
/// The caller's context here never runs what is posted to it, as a thread blocked on the chain
/// (<c>.Result</c>, <c>.Wait()</c>, <c>GetAwaiter().GetResult()</c>) never does. A member that resumed on it
/// would hang that caller, which is why <c>ConfigureAwait(false)</c> stays; such a post fails here at once.
/// The cases are written by hand, and the completeness test holds them to every public member of the
/// assembly that returns a task or an <see cref="IAsyncEnumerable{T}"/>, whose cases read it to the end.
/// </para>
/// </remarks>
public sealed class AsynchronousContextTests
{
    private static readonly Option<int> Some = Option.Some(1);

    private static readonly Result<string> Ok     = Result<string>.Success();
    private static readonly Result<string> Failed = Result<string>.Error("e");

    private static readonly Result<int, string> Value = Result<int, string>.Success(1);
    private static readonly Result<int, string> Error = Result<int, string>.Error("e");

    private static readonly Dictionary<string, Func<Probe, Task>> Cases = new()
    {
        ["Result.MapAsync(Task<Result<T, TError>>, Func<T, TResult>)"]                       = p => p.First(Value).MapAsync(x => p.Seen(x)),
        ["Result.MapAsync(Task<Result<T, TError>>, Func<T, Task<TResult>>)"]                 = p => p.First(Value).MapAsync(x => p.Later(p.Seen(x))),
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Result<TResult, TError>>)"]      = p => p.First(Value).BindAsync(x => p.Seen(Value)),
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Task<Result<TResult, TError>>>)"] = p => p.First(Value).BindAsync(x => p.Later(p.Seen(Value))),
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Result<TError>>)"]               = p => p.First(Value).BindAsync(x => p.Seen(Ok)),
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Task<Result<TError>>>)"]         = p => p.First(Value).BindAsync(x => p.Later(p.Seen(Ok))),
        ["Result.MapErrorAsync(Task<Result<T, TError>>, Func<TError, TNewError>)"]           = p => p.First(Error).MapErrorAsync(e => p.Seen(e.Length)),
        ["Result.RecoverAsync(Task<Result<T, TError>>, Func<TError, Result<T, TError>>)"]    = p => p.First(Error).RecoverAsync(e => p.Seen(Value)),
        ["Result.RecoverAsync(Task<Result<T, TError>>, Func<TError, Task<Result<T, TError>>>)"] = p => p.First(Error).RecoverAsync(e => p.Later(p.Seen(Value))),
        ["Result.EnsureAsync(Task<Result<T, TError>>, Func<T, Boolean>, TError)"]            = p => p.First(Value).EnsureAsync(x => p.Seen(true), "e"),
        ["Result.TapErrorAsync(Task<Result<T, TError>>, Action<TError>)"]                    = p => p.First(Error).TapErrorAsync(e => p.Seen(e)),
        ["Result.TapErrorAsync(Task<Result<T, TError>>, Func<TError, Task>)"]                = p => p.First(Error).TapErrorAsync(e => p.Later(p.Seen(e))),
        ["Result.TapAsync(Task<Result<T, TError>>, Action<T>)"]                              = p => p.First(Value).TapAsync(x => p.Seen(x)),
        ["Result.TapAsync(Task<Result<T, TError>>, Func<T, Task>)"]                          = p => p.First(Value).TapAsync(x => p.Later(p.Seen(x))),

        ["Result.MapAsync(Task<Result<TError>>, Func<TResult>)"]                     = p => p.First(Ok).MapAsync(() => p.Seen(1)),
        ["Result.MapAsync(Task<Result<TError>>, Func<Task<TResult>>)"]               = p => p.First(Ok).MapAsync(() => p.Later(p.Seen(1))),
        ["Result.BindAsync(Task<Result<TError>>, Func<Result<TError>>)"]             = p => p.First(Ok).BindAsync(() => p.Seen(Ok)),
        ["Result.BindAsync(Task<Result<TError>>, Func<Task<Result<TError>>>)"]       = p => p.First(Ok).BindAsync(() => p.Later(p.Seen(Ok))),
        ["Result.MapErrorAsync(Task<Result<TError>>, Func<TError, TNewError>)"]      = p => p.First(Failed).MapErrorAsync(e => p.Seen(e.Length)),
        ["Result.RecoverAsync(Task<Result<TError>>, Func<TError, Result<TError>>)"]  = p => p.First(Failed).RecoverAsync(e => p.Seen(Ok)),
        ["Result.RecoverAsync(Task<Result<TError>>, Func<TError, Task<Result<TError>>>)"] = p => p.First(Failed).RecoverAsync(e => p.Later(p.Seen(Ok))),
        ["Result.TapErrorAsync(Task<Result<TError>>, Action<TError>)"]               = p => p.First(Failed).TapErrorAsync(e => p.Seen(e)),
        ["Result.TapErrorAsync(Task<Result<TError>>, Func<TError, Task>)"]           = p => p.First(Failed).TapErrorAsync(e => p.Later(p.Seen(e))),
        ["Result.TapAsync(Task<Result<TError>>, Action)"]                            = p => p.First(Ok).TapAsync(() => p.Seen(0)),
        ["Result.TapAsync(Task<Result<TError>>, Func<Task>)"]                        = p => p.First(Ok).TapAsync(() => p.Later(p.Seen(0))),

        // The first element's operation is what the second element's selector waits for.
        ["Result.TraverseAsync(IEnumerable<T>, Func<T, Task<Result<TResult, TError>>>)"] =
            p => new[] { 1, 2 }.TraverseAsync(x => x == 1 ? p.First(Value) : p.Later(p.Seen(Value))),
        ["Result.TraverseAsync(IEnumerable<T>, Func<T, CancellationToken, Task<Result<TResult, TError>>>, CancellationToken)"] =
            p => new[] { 1, 2 }.TraverseAsync((x, _) => x == 1 ? p.First(Value) : p.Later(p.Seen(Value)), CancellationToken.None),
        ["Result.TraverseAsync(IEnumerable<T>, Func<T, Task<Result<TError>>>)"] =
            p => new[] { 1, 2 }.TraverseAsync(x => x == 1 ? p.First(Ok) : p.Later(p.Seen(Ok))),
        ["Result.TraverseAsync(IEnumerable<T>, Func<T, CancellationToken, Task<Result<TError>>>, CancellationToken)"] =
            p => new[] { 1, 2 }.TraverseAsync((x, _) => x == 1 ? p.First(Ok) : p.Later(p.Seen(Ok)), CancellationToken.None),

        // A stream's first element is what the rest wait for, so the member's own await of the stream is
        // the one that would resume on the caller's context; a selector runs as each element arrives.
        ["Result.TraverseAsync(IAsyncEnumerable<T>, Func<T, Task<Result<TResult, TError>>>)"] =
            p => p.Stream(1, 2).TraverseAsync(x => x == 1 ? p.First(Value) : p.Later(p.Seen(Value))),
        ["Result.TraverseAsync(IAsyncEnumerable<T>, Func<T, CancellationToken, Task<Result<TResult, TError>>>, CancellationToken)"] =
            p => p.Stream(1, 2).TraverseAsync((x, _) => x == 1 ? p.First(Value) : p.Later(p.Seen(Value)), CancellationToken.None),
        ["Result.TraverseAsync(IAsyncEnumerable<T>, Func<T, Task<Result<TError>>>)"] =
            p => p.Stream(1, 2).TraverseAsync(x => x == 1 ? p.First(Ok) : p.Later(p.Seen(Ok))),
        ["Result.TraverseAsync(IAsyncEnumerable<T>, Func<T, CancellationToken, Task<Result<TError>>>, CancellationToken)"] =
            p => p.Stream(1, 2).TraverseAsync((x, _) => x == 1 ? p.First(Ok) : p.Later(p.Seen(Ok)), CancellationToken.None),
        ["Result.SequenceAsync(IAsyncEnumerable<Result<T, TError>>, CancellationToken)"] = p => p.Stream(Value, Value).SequenceAsync(),
        ["Result.SequenceAsync(IAsyncEnumerable<Result<TError>>, CancellationToken)"]    = p => p.Stream(Ok, Ok).SequenceAsync(),
        ["Option.Choose(IAsyncEnumerable<Option<T>>)"]                                   = p => Drain(p.Stream(Some, Some).Choose()),
        ["SequenceOptionExtensions.Choose(IAsyncEnumerable<T>, Func<T, Option<TResult>>)"] = p => Drain(p.Stream(1, 2).Choose(x => p.Seen(Some))),

        // The delegate runs at once, on the caller's context; the task it returns is awaited without it.
        ["Result.CatchAsync<T>(Func<Task<T>>)"]             = p => Result.CatchAsync(() => p.Later(p.Seen(1))),
        ["Result.CatchAsync(Func<Task>)"]                   = p => Result.CatchAsync(() => (Task)p.Later(p.Seen(1))),
        ["Result.CatchAsync<T, TException>(Func<Task<T>>)"] = p => Result.CatchAsync<int, InvalidOperationException>(() => p.Later(p.Seen(1))),
        ["Result.CatchAsync<TException>(Func<Task>)"]       = p => Result.CatchAsync<InvalidOperationException>(() => (Task)p.Later(p.Seen(1))),
        ["Result.CatchAsync<T>(Func<ValueTask<T>>)"]             = p => Result.CatchAsync(() => new ValueTask<int>(p.Later(p.Seen(1)))),
        ["Result.CatchAsync(Func<ValueTask>)"]                   = p => Result.CatchAsync(() => new ValueTask(p.Later(p.Seen(1)))),
        ["Result.CatchAsync<T, TException>(Func<ValueTask<T>>)"] = p => Result.CatchAsync<int, InvalidOperationException>(() => new ValueTask<int>(p.Later(p.Seen(1)))),
        ["Result.CatchAsync<TException>(Func<ValueTask>)"]       = p => Result.CatchAsync<InvalidOperationException>(() => new ValueTask(p.Later(p.Seen(1)))),

        ["Option.MapAsync(Task<Option<T>>, Func<T, TResult>)"]               = p => p.First(Some).MapAsync(x => p.Seen(x)),
        ["Option.MapAsync(Task<Option<T>>, Func<T, Task<TResult>>)"]         = p => p.First(Some).MapAsync(x => p.Later(p.Seen(x))),
        ["Option.BindAsync(Task<Option<T>>, Func<T, Option<TResult>>)"]      = p => p.First(Some).BindAsync(x => p.Seen(Some)),
        ["Option.BindAsync(Task<Option<T>>, Func<T, Task<Option<TResult>>>)"] = p => p.First(Some).BindAsync(x => p.Later(p.Seen(Some))),
        ["Option.FilterAsync(Task<Option<T>>, Func<T, Boolean>)"]            = p => p.First(Some).FilterAsync(x => p.Seen(true)),
        ["Option.TapAsync(Task<Option<T>>, Action<T>)"]                      = p => p.First(Some).TapAsync(x => p.Seen(x)),
        ["Option.TapAsync(Task<Option<T>>, Func<T, Task>)"]                  = p => p.First(Some).TapAsync(x => p.Later(p.Seen(x))),
        ["Option.ToResultAsync(Task<Option<T>>, TError)"]                    = p => p.First(Some).ToResultAsync("e"),

        // On a known result or option the callback runs at once; what it returns is awaited without the context.
        ["Option<T>.MapAsync(Func<T, Task<TResult>>)"]          = p => Some.MapAsync(x => p.Later(p.Seen(x))),
        ["Option<T>.BindAsync(Func<T, Task<Option<TResult>>>)"] = p => Some.BindAsync(x => p.Later(p.Seen(Some))),
        ["Option<T>.TapAsync(Func<T, Task>)"]                   = p => Some.TapAsync(x => p.Later(p.Seen(x))),
        ["Result.TraverseAsync(Option<T>, Func<T, Task<Result<TResult, TError>>>)"] = p => Some.TraverseAsync(x => p.Later(p.Seen(Value))),
        ["Result.TraverseAsync(Option<T>, Func<T, CancellationToken, Task<Result<TResult, TError>>>, CancellationToken)"] =
            p => Some.TraverseAsync((x, _) => p.Later(p.Seen(Value)), CancellationToken.None),
        ["Result.TraverseAsync(Patch<T>, Func<T, Task<Result<TResult, TError>>>)"] = p => Patch.Value(1).TraverseAsync(x => p.Later(p.Seen(Value))),
        ["Result.TraverseAsync(Patch<T>, Func<T, CancellationToken, Task<Result<TResult, TError>>>, CancellationToken)"] =
            p => Patch.Value(1).TraverseAsync((x, _) => p.Later(p.Seen(Value)), CancellationToken.None),

        ["Result<TError>.TapAsync(Func<Task>)"]                          = p => Ok.TapAsync(() => p.Later(p.Seen(0))),
        ["Result<TError>.TapErrorAsync(Func<TError, Task>)"]             = p => Failed.TapErrorAsync(e => p.Later(p.Seen(e))),
        ["Result<TError>.MapAsync(Func<Task<TResult>>)"]                 = p => Ok.MapAsync(() => p.Later(p.Seen(1))),
        ["Result<TError>.BindAsync(Func<Task<Result<TError>>>)"]         = p => Ok.BindAsync(() => p.Later(p.Seen(Ok))),
        ["Result<TError>.RecoverAsync(Func<TError, Task<Result<TError>>>)"] = p => Failed.RecoverAsync(e => p.Later(p.Seen(Ok))),

        ["Result<T, TError>.TapAsync(Func<T, Task>)"]                               = p => Value.TapAsync(x => p.Later(p.Seen(x))),
        ["Result<T, TError>.TapErrorAsync(Func<TError, Task>)"]                     = p => Error.TapErrorAsync(e => p.Later(p.Seen(e))),
        ["Result<T, TError>.MapAsync(Func<T, Task<TResult>>)"]                      = p => Value.MapAsync(x => p.Later(p.Seen(x))),
        ["Result<T, TError>.BindAsync(Func<T, Task<Result<TResult, TError>>>)"]     = p => Value.BindAsync(x => p.Later(p.Seen(Value))),
        ["Result<T, TError>.BindAsync(Func<T, Task<Result<TError>>>)"]              = p => Value.BindAsync(x => p.Later(p.Seen(Ok))),
        ["Result<T, TError>.RecoverAsync(Func<TError, Task<Result<T, TError>>>)"]   = p => Error.RecoverAsync(e => p.Later(p.Seen(Value))),
    };

    /// <summary>Members that take no callback: only that nothing resumes on the caller's context is checked.</summary>
    private static readonly HashSet<string> WithoutCallback =
    [
        "Option.ToResultAsync(Task<Option<T>>, TError)",
        "Result.SequenceAsync(IAsyncEnumerable<Result<T, TError>>, CancellationToken)",
        "Result.SequenceAsync(IAsyncEnumerable<Result<TError>>, CancellationToken)",
        "Option.Choose(IAsyncEnumerable<Option<T>>)",
    ];

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task A_callback_runs_where_the_task_it_waited_for_completed_and_nothing_resumes_on_the_caller_s_context(string member)
    {
        foreach (var pending in new[] { true, false })
        {
            var mode = pending ? "the task it waits for still pending" : "the task it waits for already completed";
            var probe = new Probe(pending);
            var caller = new BlockedContext();

            var chain = Start(Cases[member], probe, caller);
            probe.Open();

            var first = await Task.WhenAny(chain, caller.Posted, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

            caller.Posts.ShouldBe(0, $"{member}, {mode}, resumed on the caller's context, which a caller blocked on the chain never runs: it hangs.");
            first.ShouldBeSameAs(chain, $"{member}, {mode}, did not complete.");
            await chain;

            if (WithoutCallback.Contains(member)) continue;

            probe.Called.ShouldBeTrue($"{member}, {mode}: the case never reached its callback.");
            if (probe.Waited)
                probe.Context.ShouldNotBeSameAs(caller, $"{member}: a callback after a pending task runs where that task completed, without the caller's context.");
            else
                probe.Context.ShouldBeSameAs(caller, $"{member}: a callback with nothing pending before it runs at once, on the caller's context.");
        }
    }

    /// <summary>The documented case, spelled out: a selector after a pending result does not see the caller's context, and the caller's own await returns to it.</summary>
    [Fact]
    public async Task A_selector_after_a_pending_result_runs_without_the_caller_s_context_and_the_caller_s_await_returns_to_it()
    {
        var caller = new RunningContext();
        SynchronizationContext? selector = null;
        SynchronizationContext? after = null;

        await caller.Run(async () =>
        {
            var gate = new TaskCompletionSource<Result<int, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var chain = gate.Task.MapAsync(x => { selector = SynchronizationContext.Current; return x + 1; });
            _ = Task.Run(() => gate.SetResult(Value));

            (await chain).Match(x => x, _ => -1).ShouldBe(2);
            after = SynchronizationContext.Current;
        });

        selector.ShouldNotBeSameAs(caller, "the selector ran on the caller's context");
        after.ShouldBeSameAs(caller, "the caller's await captures its own context, whatever the package does");
    }

    [Fact]
    public void Every_public_member_that_returns_a_task_or_a_stream_has_a_case()
    {
        var members = AsynchronousMembers().ToList();

        members.Count.ShouldBeGreaterThanOrEqualTo(50);
        members.Except(Cases.Keys).ShouldBeEmpty("These public members return a task or a stream and have no case here.");
        Cases.Keys.Except(members).ShouldBeEmpty("These cases name no public member of the assembly.");
    }

    /// <summary>Reads a stream to the end without capturing the caller's context, as a member's own awaits must not.</summary>
    private static async Task Drain<T>(IAsyncEnumerable<T> stream)
    {
        await foreach (var _ in stream.ConfigureAwait(false)) { }
    }

    private static Task Start(Func<Probe, Task> chain, Probe probe, SynchronizationContext caller)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(caller);
        try
        {
            return chain(probe);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static IEnumerable<string> AsynchronousMembers()
    {
        var methods =
            from type in typeof(Option<>).Assembly.GetExportedTypes()
            where !type.Name.Contains('<') && type.GetCustomAttribute<CompilerGeneratedAttribute>() is null
            from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            where typeof(Task).IsAssignableFrom(method.ReturnType) || method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
            where method.GetCustomAttribute<ObsoleteAttribute>() is not { IsError: true }
            select (Type: type, Method: method, Signature: $"({string.Join(", ", method.GetParameters().Select(p => Describe(p.ParameterType)))})");

        // Overloads that differ only in their type parameters (CatchAsync<T> beside CatchAsync<T, TException>)
        // are keyed with them, as in NullDelegateTests.
        var byKey = methods.ToLookup(m => $"{Describe(m.Type)}.{m.Method.Name}{m.Signature}");

        return from overloads in byKey
               from m in overloads
               let name = overloads.Count() > 1 && m.Method.IsGenericMethod
                   ? $"{m.Method.Name}<{string.Join(", ", m.Method.GetGenericArguments().Select(a => a.Name))}>"
                   : m.Method.Name
               select $"{Describe(m.Type)}.{name}{m.Signature}";
    }

    private static string Describe(Type type) =>
        type.IsByRef ? Describe(type.GetElementType()!)
      : type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>"
      : type.Name;

    /// <summary>
    /// Hands a case the task its callback waits for, pending or completed, and records where the callback ran.
    /// Every task a callback returns stays pending until <see cref="Open"/>, so the await after it suspends.
    /// </summary>
    private sealed class Probe(bool pending)
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Whether the case waited for a task that was still pending before its callback.</summary>
        public bool Waited { get; private set; }

        public bool Called { get; private set; }

        public SynchronizationContext? Context { get; private set; }

        public Task<T> First<T>(T value)
        {
            Waited = pending;
            return pending ? Later(value) : Task.FromResult(value);
        }

        public async Task<T> Later<T>(T value)
        {
            await gate.Task.ConfigureAwait(false);
            return value;
        }

        /// <summary>A stream whose first element is <see cref="First{T}"/>'s task, the second following it at once.</summary>
        public async IAsyncEnumerable<T> Stream<T>(T first, T second)
        {
            yield return await First(first).ConfigureAwait(false);
            yield return second;
        }

        public T Seen<T>(T value)
        {
            Called = true;
            Context = SynchronizationContext.Current;
            return value;
        }

        public void Open() => gate.SetResult();
    }

    /// <summary>A caller blocked on the chain: what is posted to its context never runs.</summary>
    private sealed class BlockedContext : SynchronizationContext
    {
        private readonly TaskCompletionSource posted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int posts;

        public int Posts => Volatile.Read(ref posts);

        public Task Posted => posted.Task;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref posts);
            posted.TrySetResult();
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("Nothing sends to the caller's context.");
    }

    /// <summary>A caller's context that runs what is posted to it, on the thread pool, as itself.</summary>
    private sealed class RunningContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                d(state);
            });

        /// <summary>Starts <paramref name="body"/> on this context, as a component's event handler starts on its renderer's.</summary>
        public Task Run(Func<Task> body)
        {
            var started = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ => started.SetResult(body()), null);

            return started.Task.Unwrap();
        }
    }
}
