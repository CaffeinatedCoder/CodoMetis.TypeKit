namespace CodoMetis.TypeKit.Analyzers.Tests.Variants;

/// <summary>A call that returns a task whose drop CMTK0003 judges.</summary>
/// <param name="Name">What the call is, as a test case names it.</param>
/// <param name="Call">The call as written on the run method's parameters.</param>
/// <param name="Static">The same call in the lowered static form, or <see langword="null"/> where it has none.</param>
/// <param name="NullConditional">The same call through <c>?.</c>, or <see langword="null"/> where its receiver is a struct.</param>
/// <param name="Returns">The type the call returns, which a <c>??</c>'s held operand is declared as.</param>
/// <param name="Method">The method the message names.</param>
/// <param name="Outcome">What the message says is dropped.</param>
/// <param name="AwaitDrops">
/// Whether awaiting it drops something too: a task of a <c>Result</c> completes with the result, which the
/// statement then drops; an assertion completes once it has passed, with nothing left to see.
/// </param>
/// <param name="IsAssertion">Whether it is an assertion of CodoMetis.TypeKit.Testing, whose combination the message names as a <c>Task</c>.</param>
/// <param name="Group">A sequence of the calls made through a method group, or <see langword="null"/> where the call takes more than its receiver.</param>
public sealed record PendingCall(string Name, string Call, string? Static, string? NullConditional, string Returns, string Method, string Outcome, bool AwaitDrops, bool IsAssertion = true, string? Group = null)
{
    public override string ToString() => Name;
}

/// <summary>A call on a task that hands on the task it is called on, or none.</summary>
/// <param name="Name">What the call is, as a test case names it.</param>
/// <param name="Suffix">The call as written after the task.</param>
/// <param name="KeepsTask">Whether what it returns is still a task, as <c>WaitAsync</c>'s is and <c>ConfigureAwait</c>'s awaitable is not.</param>
public sealed record TaskPassThrough(string Name, string Suffix, bool KeepsTask = true)
{
    public override string ToString() => Name;
}

/// <summary>Where the dropped call stands in the statement's expression.</summary>
/// <param name="Name">What the form is, as a test case names it.</param>
/// <param name="Expression">
/// The expression: <c>{call}</c> is the call, <c>{w}</c> the pass-through, and <c>[[</c> <c>]]</c> enclose a
/// branch's call, which a diagnostic reports on its own. A form without them is the statement's own call,
/// reported whole.
/// </param>
/// <param name="NeedsPassThrough">Whether the form is a statement only with a pass-through: a bare <c>?:</c>, <c>??</c> or <c>switch</c> is CS0201.</param>
/// <param name="Combinator">
/// The <c>Task</c> method that combines the calls, which the message names in place of the call's, or
/// <see langword="null"/>. A <c>WhenAll</c> of results drops a collection of them, and a <c>WhenAny</c> one
/// result (#159).
/// </param>
/// <param name="AwaitDrops">Whether awaiting the combination of assertions still drops a failure, which <c>WhenAny</c>'s does; <see langword="null"/> where the call decides.</param>
/// <param name="Silent">Whether nothing is dropped in any use: the statement combines no task it made.</param>
/// <param name="Value">What the expression is: a task, or what <c>Task.WaitAny</c> and <c>Task.WaitAll</c> return, which loses the tasks in every use.</param>
/// <param name="AssertionsObserved">Whether an assertion's failure is seen all the same: <c>Task.WaitAll</c> throws it.</param>
/// <param name="AssertionsOnly">
/// Whether only an assertion is written in the form: where a <c>Result</c>'s would be a statement of its own, or a
/// combinator inside another, which only an assertion's failure passes through (#165).
/// </param>
/// <param name="Lazy">Whether the sequence runs nothing until a loop reads it, so that dropping it drops nothing.</param>
public sealed record DropForm(
    string Name,
    string Expression,
    bool NeedsPassThrough = false,
    string? Combinator = null,
    bool? AwaitDrops = null,
    bool Silent = false,
    FormValue Value = FormValue.Task,
    bool AssertionsObserved = false,
    bool AssertionsOnly = false,
    bool Lazy = false)
{
    public bool IsBranch => Expression.Contains("[[", StringComparison.Ordinal);

    public override string ToString() => Name;
}

/// <summary>What a form's expression is.</summary>
public enum FormValue
{
    /// <summary>A task, or what a pass-through makes of one.</summary>
    Task,

    /// <summary>The index <c>Task.WaitAny</c> returns.</summary>
    Index,

    /// <summary>Nothing: <c>Task.WaitAll</c> returns <c>void</c>.</summary>
    Nothing,

    /// <summary>A sequence of the tasks, read with <c>foreach</c>: a <c>Select</c>, which runs nothing until it is read.</summary>
    Sequence,

    /// <summary>A sequence of the tasks, read with <c>await foreach</c>: <c>Task.WhenEach</c>'s.</summary>
    AsyncSequence,
}

/// <summary>What the statement does with the expression.</summary>
/// <param name="Name">What the use is, as a test case names it.</param>
/// <param name="Statement"><c>{e}</c> is the expression, and <c>&lt;&lt;</c> <c>&gt;&gt;</c> enclose what a diagnostic reports for the statement's own call.</param>
/// <param name="Drops">Whether the use drops the task: <see langword="null"/> where it drops what the task completes with, which only <see cref="PendingCall.AwaitDrops"/> holds anything of.</param>
/// <param name="Async">Whether the use needs an <c>async</c> method.</param>
/// <param name="NeedsTask">Whether the use needs a task: it converts it to a plain <c>Task</c> or calls <c>Wait</c> on it.</param>
/// <param name="Chains">Whether the use writes a call after the expression, which binds inside a <c>?.</c>.</param>
/// <param name="IsStatement">Whether the expression is a statement, so that one of no value can be written there.</param>
/// <param name="Loops">The sequence a loop reads the expression as, or <see langword="null"/> for a use that is no loop.</param>
public sealed record TaskUse(string Name, string Statement, bool? Drops, bool Async = false, bool NeedsTask = false, bool Chains = false, bool IsStatement = false, FormValue? Loops = null)
{
    public override string ToString() => Name;
}

/// <summary>
/// The variant catalogue for CMTK0003's dropped task (design/adr/0095): every way a statement can leave a
/// call's task unobserved, and the uses that keep it.
/// </summary>
/// <remarks>
/// <para>
/// Four axes. The call: every assertion of CodoMetis.TypeKit.Testing on a <c>Task</c> and a <c>ValueTask</c>,
/// those that return a plain <c>Task</c> and those that return a <c>Task&lt;T&gt;</c>, beside a call that returns a
/// task of a <c>Result</c>, whose drop the rule judged first. The pass-through: none, and every overload of
/// <c>ConfigureAwait</c> and <c>WaitAsync</c>, one chained onto another. The form: the call alone, in the lowered
/// static form, through <c>?.</c>, as a branch of a <c>?:</c>, a <c>??</c> and a <c>switch</c> expression, and
/// handed on by <c>Task.WhenAll</c>, <c>Task.WhenAny</c> or <c>Task.Run</c> as arguments, an array, a collection
/// expression, a <c>Select</c> of a lambda, an <c>async</c> lambda or a method group, beside a held task and as a
/// branch, and given to <c>Task.WaitAny</c> or <c>Task.WaitAll</c>, against held tasks, an assertion discarded
/// where it was made and one whose failure a <c>catch</c> swallows. The use: dropped as a statement or as a
/// lambda's body, awaited, converted to a plain <c>Task</c> and waited for with <c>Wait</c>, against discarded,
/// held and returned.
/// </para>
/// <para>
/// Both calls go through the one path, so they agree on every form: #152 found the assertions alone silent
/// through the pass-throughs of a plain <c>Task</c> and through the branches, #157 through the combinators,
/// #159 a <c>WhenAny</c> of results dropped unawaited, #165 a <c>WhenAny</c> inside a <c>WhenAll</c> or a <c>Run</c>,
/// awaited, and #166 a <c>catch</c> that throws again, of another exception or filtered by <c>false</c>.
/// </para>
/// </remarks>
public static class DroppedTaskShapes
{
    /// <summary>The run method's parameters, which the calls and the forms name.</summary>
    public const string Parameters =
        "Service service, Service? maybe, bool choose, CancellationToken token, "
      + "Task<Option<int>> option, ValueTask<Option<int>> optionValue, "
      + "Task<Result<int, Fault>> result, ValueTask<Result<int, Fault>> resultValue, "
      + "Task<Result<Fault>> command, ValueTask<Result<Fault>> commandValue, "
      + "Task<Patch<int>> patch, ValueTask<Patch<int>> patchValue, "
      + "Task<Fault<Fault, Other>> composed, ValueTask<Fault<Fault, Other>> composedValue";

    /// <summary>The declarations the parameters name, a file of their own.</summary>
    public const string Declarations =
        """
        using System.Threading.Tasks;
        using CodoMetis.TypeKit;

        public enum Fault { Refused }

        public enum Other { Late }

        public sealed class Service
        {
            public Task<Result<int, Fault>> LoadAsync() => Task.FromResult<Result<int, Fault>>(1);
        }

        """;

    public static IReadOnlyList<PendingCall> Calls { get; } =
    [
        .. Assertion("ShouldBeNone", "OptionAssertions", "option", "", "Task"),
        .. Assertion("ShouldBeSuccess", "ResultAssertions", "command", "", "Task", "command"),
        .. Assertion("ShouldBeError", "ResultAssertions", "result", "Fault.Refused", "Task", "expected-value"),
        .. Assertion("ShouldBeError", "ResultAssertions", "command", "Fault.Refused", "Task", "command expected-value"),
        .. Assertion("ShouldBeAbsent", "PatchAssertions", "patch", "", "Task"),
        .. Assertion("ShouldBeNull", "PatchAssertions", "patch", "", "Task"),
        .. Assertion("ShouldBeSome", "OptionAssertions", "option", "", "Task<int>"),
        .. Assertion("ShouldBeSuccess", "ResultAssertions", "result", "", "Task<int>"),
        .. Assertion("ShouldBeError", "ResultAssertions", "result", "", "Task<Fault>"),
        .. Assertion("ShouldBeError", "ResultAssertions", "command", "", "Task<Fault>", "command"),
        .. Assertion("ShouldBeValue", "PatchAssertions", "patch", "", "Task<int>"),
        .. Assertion("ShouldHold", "FaultAssertions", "composed", "Fault.Refused", "Task<Fault<Fault>>"),

        new("a call that returns a Task of a Result", "service.LoadAsync()", null, "maybe?.LoadAsync()", "Task<Result<int, Fault>>", "LoadAsync", "Result", AwaitDrops: true, IsAssertion: false),
    ];

    public static IReadOnlyList<TaskPassThrough> PassThroughs { get; } =
    [
        new("no pass-through", ""),
        new("ConfigureAwait(bool)", ".ConfigureAwait(false)", KeepsTask: false),
        new("ConfigureAwait(ConfigureAwaitOptions)", ".ConfigureAwait(ConfigureAwaitOptions.None)", KeepsTask: false),
        new("WaitAsync(TimeSpan)", ".WaitAsync(TimeSpan.FromSeconds(1))"),
        new("WaitAsync(CancellationToken)", ".WaitAsync(token)"),
        new("WaitAsync(TimeSpan, CancellationToken)", ".WaitAsync(TimeSpan.FromSeconds(1), token)"),
        new("WaitAsync(TimeSpan, TimeProvider)", ".WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System)"),
        new("WaitAsync(TimeSpan, TimeProvider, CancellationToken)", ".WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, token)"),
        new("WaitAsync then ConfigureAwait", ".WaitAsync(token).ConfigureAwait(false)", KeepsTask: false),
    ];

    public static IReadOnlyList<DropForm> Forms { get; } =
    [
        new("the call", "{call}{w}"),
        new("the lowered static form", "{static}{w}"),
        new("a null-conditional call", "{nc}{w}"),
        new("both branches of a conditional", "(choose ? [[{call}]] : [[{call}]]){w}", NeedsPassThrough: true),
        new("the right operand of ??", "(held ?? [[{call}]]){w}", NeedsPassThrough: true),
        new("both arms of a switch expression", "(choose switch { true => [[{call}]], false => [[{call}]] }){w}", NeedsPassThrough: true),

        new("Task.WhenAll of two calls", "Task.WhenAll({call}, {call}){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll of an array", "Task.WhenAll(new[] { {call}, {call} }){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll of a collection expression", "Task.WhenAll([{call}, {call}]){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll of a Select", "Task.WhenAll(Enumerable.Range(0, 2).Select(_ => {call})){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll beside a held task", "Task.WhenAll(held!, {call}){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll of a conditional", "Task.WhenAll(choose ? {call} : {call}){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAny of two calls", "Task.WhenAny({call}, {call}){w}", Combinator: "WhenAny", AwaitDrops: true),
        new("Task.WhenAny of an array", "Task.WhenAny(new[] { {call}, {call} }){w}", Combinator: "WhenAny", AwaitDrops: true),
        new("Task.WhenAny beside a held task", "Task.WhenAny(held!, {call}){w}", Combinator: "WhenAny", AwaitDrops: true),
        new("Task.WhenAll of held tasks", "Task.WhenAll(held!, held!){w}", Combinator: "WhenAll", Silent: true),
        new("Task.WhenAny of held tasks", "Task.WhenAny(held!, held!){w}", Combinator: "WhenAny", Silent: true),
        new("Task.WhenAll of tasks discarded where they are made", "Task.WhenAll(Enumerable.Range(0, 2).Select(i => { _ = {call}; return Task.CompletedTask; })){w}", Combinator: "WhenAll", Silent: true),
        new("Task.WhenAll of tasks made from the calls, not of them", "Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.FromResult(ReferenceEquals({call}, null)))){w}", Combinator: "WhenAll", Silent: true),
        new("Task.WhenAll of a method group", "Task.WhenAll({group}){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll of async lambdas", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => await {call})){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll of async lambdas that catch", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception) { } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.Run of a lambda", "Task.Run(() => {call}){w}", Combinator: "Run", AwaitDrops: false),
        new("Task.Run of an async lambda", "Task.Run(async () => await {call}){w}", Combinator: "Run", AwaitDrops: false),

        // A combinator inside another (#165): WhenAll and Run fault with a task that faults, WhenAny completes with
        // it as its value, at any depth, and each await unwraps one such task.
        new("Task.WhenAll of a Task.WhenAll", "Task.WhenAll(Task.WhenAll({call}, {call})){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of a Task.WhenAny", "Task.WhenAll(Task.WhenAny({call}, {call})){w}", Combinator: "WhenAll", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of a Task.Run", "Task.WhenAll(Task.Run(() => {call})){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAny of a Task.WhenAll", "Task.WhenAny(Task.WhenAll({call}, {call})){w}", Combinator: "WhenAny", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAny of a Task.WhenAny", "Task.WhenAny(Task.WhenAny({call}, {call})){w}", Combinator: "WhenAny", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAny of a Task.Run", "Task.WhenAny(Task.Run(() => {call})){w}", Combinator: "WhenAny", AwaitDrops: true, AssertionsOnly: true),
        new("Task.Run of a Task.WhenAll", "Task.Run(() => Task.WhenAll({call}, {call})){w}", Combinator: "Run", AwaitDrops: false, AssertionsOnly: true),
        new("Task.Run of a Task.WhenAny", "Task.Run(() => Task.WhenAny({call}, {call})){w}", Combinator: "Run", AwaitDrops: true, AssertionsOnly: true),
        new("Task.Run of a Task.Run", "Task.Run(() => Task.Run(() => {call})){w}", Combinator: "Run", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of a Task.Run of a Task.WhenAll of a Task.WhenAll", "Task.WhenAll(Task.Run(() => Task.WhenAll(Task.WhenAll({call}, {call})))){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of a Task.Run of a Task.WhenAll of a Task.WhenAny", "Task.WhenAll(Task.Run(() => Task.WhenAll(Task.WhenAny({call}, {call})))){w}", Combinator: "WhenAll", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of an array of Task.WhenAny", "Task.WhenAll(new[] { Task.WhenAny({call}, {call}), Task.WhenAny({call}, {call}) }){w}", Combinator: "WhenAll", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of a Select of Task.WhenAny", "Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.WhenAny({call}, {call}))){w}", Combinator: "WhenAll", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that return a Task.WhenAny", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => Task.WhenAny({call}, {call}))){w}", Combinator: "WhenAll", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that await a Task.WhenAny once", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => await Task.WhenAny({call}, {call}))){w}", Combinator: "WhenAll", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that await a Task.WhenAny twice", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => await await Task.WhenAny({call}, {call}))){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that await a Task.WhenAny of a Task.WhenAny twice", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => await await Task.WhenAny(Task.WhenAny({call}, {call})))){w}", Combinator: "WhenAll", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that await a Task.WhenAny of a Task.WhenAny three times", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => await await await Task.WhenAny(Task.WhenAny({call}, {call})))){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),

        // A catch around the awaited assertion in an async lambda (#166) keeps the failure only when its type can
        // hold a TypeKitAssertionException, no constant false filter refuses it and its handler can complete. A
        // filter that is no constant may take it, and counts as taking it.
        new("Task.WhenAll of async lambdas that try with only a finally", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } finally { GC.KeepAlive(i); } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch and rethrow", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch { throw; } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that log and rethrow", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception exception) { GC.KeepAlive(exception); throw; } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that throw another exception", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception exception) { throw new InvalidOperationException(\"wrapped\", exception); } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch an unrelated exception", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (OperationCanceledException) { } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch with a false filter", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception) when (false) { } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that rethrow inside a try that rethrows", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { try { await {call}; } catch { throw; } } catch (OperationCanceledException) { } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that await in a catch's handler", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { GC.KeepAlive(i); } catch (Exception) { await {call}; } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that await in a finally", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { GC.KeepAlive(i); } catch (Exception) { } finally { await {call}; } })){w}", Combinator: "WhenAll", AwaitDrops: false, AssertionsOnly: true),
        new("Task.Run of an async lambda that catches and rethrows", "Task.Run(async () => { try { await {call}; } catch { throw; } }){w}", Combinator: "Run", AwaitDrops: false, AssertionsOnly: true),
        new("Task.WhenAny of async lambdas that catch and rethrow", "Task.WhenAny(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch { throw; } })){w}", Combinator: "WhenAny", AwaitDrops: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch everything", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch { } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch the assertion's exception", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (TypeKitAssertionException) { } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch with a true filter", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception) when (true) { } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch with a filter that is no constant", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception exception) when (exception.Data.Count == 0) { } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that rethrow only sometimes", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception) { if (choose) throw; } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that return from the catch", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (Exception) { if (choose) return; throw; } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch in a later clause", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { await {call}; } catch (OperationCanceledException) { throw; } catch (Exception) { } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that rethrow inside a try that catches", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { try { await {call}; } catch { throw; } } catch (Exception) { } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.WhenAll of async lambdas that catch inside a try that rethrows", "Task.WhenAll(Enumerable.Range(0, 2).Select(async i => { try { try { await {call}; } catch (Exception) { } } catch { throw; } })){w}", Combinator: "WhenAll", Silent: true, AssertionsOnly: true),
        new("Task.Run of an async lambda that catches", "Task.Run(async () => { try { await {call}; } catch (Exception) { } }){w}", Combinator: "Run", Silent: true, AssertionsOnly: true),

        new("Task.WaitAny of two calls", "Task.WaitAny([[{call}]], [[{call}]])", Value: FormValue.Index),
        new("Task.WaitAny of held tasks", "Task.WaitAny(held!, held!)", Value: FormValue.Index, Silent: true),
        new("Task.WaitAll of two calls", "Task.WaitAll([[{call}]], [[{call}]])", Value: FormValue.Nothing, AssertionsObserved: true),
        new("Task.WaitAny of an array", "Task.WaitAny(new[] { [[{call}]], [[{call}]] })", Value: FormValue.Index),
        new("Task.WaitAll of an array", "Task.WaitAll(new[] { [[{call}]], [[{call}]] })", Value: FormValue.Nothing, AssertionsObserved: true),
        new("Task.WaitAll of a Task.WhenAll", "Task.WaitAll(Task.WhenAll([[{call}]], [[{call}]]))", Value: FormValue.Nothing, AssertionsObserved: true, AssertionsOnly: true),
        // Task.WaitAll waits for the WhenAny as a plain Task, a task that never faults, reported where it converts.
        new("Task.WaitAll of a Task.WhenAny", "Task.WaitAll([[Task.WhenAny({call}, {call})]])", Combinator: "WhenAny", Value: FormValue.Nothing, AssertionsOnly: true),
        new("Task.WaitAny of a Task.WhenAll", "Task.WaitAny(Task.WhenAll([[{call}]], [[{call}]]))", Value: FormValue.Index, AssertionsOnly: true),

        new("Task.WhenAll of a query", "Task.WhenAll(from i in Enumerable.Range(0, 2) select {call}){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenAll of a list", "Task.WhenAll(Enumerable.Range(0, 2).Select(i => {call}).ToList()){w}", Combinator: "WhenAll", AwaitDrops: false),
        new("Task.WhenEach of two calls", "Task.WhenEach({call}, {call})", Combinator: "WhenEach", Value: FormValue.AsyncSequence),
        new("Task.WhenEach of held tasks", "Task.WhenEach(held!, held!)", Combinator: "WhenEach", Value: FormValue.AsyncSequence, Silent: true),
        new("a Select of the calls", "Enumerable.Range(0, 2).Select(i => {call})", Combinator: "Select", Value: FormValue.Sequence, Lazy: true),
    ];

    public static IReadOnlyList<TaskUse> Uses { get; } =
    [
        new("dropped as a statement", "<<{e}>>;", Drops: true, IsStatement: true),
        new("dropped as a lambda's body", "Action dropped = () => <<{e}>>;", Drops: true, IsStatement: true),
        new("awaited", "<<await {e}>>;", Drops: null, Async: true),
        new("converted to a plain Task", "Func<Task> converted = () => <<{e}>>;", Drops: null, NeedsTask: true),
        new("waited for with Wait()", "<<{e}.Wait()>>;", Drops: null, NeedsTask: true, Chains: true),
        new("the body of a method that returns nothing", "void Check() => <<{e}>>;", Drops: true, IsStatement: true),
        new("drained by a foreach that never reads it", "foreach (var each in <<{e}>>) { }", Drops: true, Loops: FormValue.Sequence),
        new("read by a foreach", "foreach (var each in {e}) { GC.KeepAlive(each); }", Drops: false, Loops: FormValue.Sequence),
        new("drained by an await foreach that never reads it", "await foreach (var each in <<{e}>>) { }", Drops: true, Async: true, Loops: FormValue.AsyncSequence),
        new("read by an await foreach", "await foreach (var each in {e}) { GC.KeepAlive(each); }", Drops: false, Async: true, Loops: FormValue.AsyncSequence),
        new("discarded with _ =", "_ = {e};", Drops: false),
        new("held in a local", "var kept = {e};", Drops: false),
        new("returned from a lambda", "Func<object?> returned = () => {e};", Drops: false),
    ];

    /// <summary>
    /// A file whose two run methods use <paramref name="call"/> in <paramref name="form"/>, through every
    /// pass-through, in every use, and the spans a diagnostic is expected at, by markup location. One file per
    /// form keeps a failure to the form, and the verifier's matching, which recurses once per diagnostic,
    /// within its stack.
    /// </summary>
    public static (string Source, IReadOnlyList<(string Outcome, string Method)> Expected) Use(PendingCall call, DropForm form)
    {
        var expected = new List<(string Outcome, string Method)>();
        var body     = new System.Text.StringBuilder();
        var awaited  = new System.Text.StringBuilder();

        foreach (var passThrough in PassThroughs)
        foreach (var use in Uses)
        {
            // A statement that does not compile is no case.
            if (form.NeedsPassThrough && passThrough.Suffix.Length == 0) continue;

            // await x?.M().ConfigureAwait(false) awaits a nullable awaiter, which has no GetAwaiter, and await x?.M()
            // a task that may be null (CS8602); x?.M().Wait() waits inside the ?..
            if ((use.Async || use.Chains) && form.Expression.Contains("{nc}", StringComparison.Ordinal)) continue;
            if (use.NeedsTask && !passThrough.KeepsTask) continue;

            // An index, nothing or a sequence takes no await, no pass-through and no conversion, nothing is no
            // value, and a loop reads only the sequence it was written for.
            if (use.Loops is { } loops ? loops != form.Value : form.Value != FormValue.Task && (use.Async || use.NeedsTask)) continue;
            if (form.Value != FormValue.Task && passThrough.Suffix.Length > 0) continue;
            if (form.Value == FormValue.Nothing && !use.IsStatement) continue;

            // Task.WaitAny and Task.WaitAll lose or keep the tasks at the call, whatever the statement does next.
            var reported   = !form.Silent
                          && !(form.AssertionsObserved && call.IsAssertion)
                          && !(form.Lazy && use.Loops is null)
                          && (form.Value is FormValue.Index or FormValue.Nothing || (use.Drops ?? (call.AwaitDrops || (form.AwaitDrops ?? false))));
            var expression = form.Expression
                                 .Replace("{call}", call.Call)
                                 .Replace("{static}", call.Static)
                                 .Replace("{nc}", call.NullConditional)
                                 .Replace("{group}", call.Group)
                                 .Replace("{w}", passThrough.Suffix);
            var statement  = use.Statement.Replace("{e}", expression);

            var message    = form.Combinator switch
            {
                null                         => (call.Outcome, call.Method),
                "WhenEach" or "Select"        => ($"sequence of {(call.IsAssertion ? "Task" : call.Outcome)}s", form.Combinator),
                var all when call.IsAssertion => ("Task", all),
                "WhenAll"                    => ($"collection of {call.Outcome}s", "WhenAll"),
                var any                      => (call.Outcome, any),
            };

            statement = Mark(statement, "<<", ">>", reported && !form.IsBranch, message, expected);
            statement = Mark(statement, "[[", "]]", reported, message, expected);

            // A task the caller holds, declared where the form names it.
            var held = expression.Contains("held", StringComparison.Ordinal) ? $"{call.Returns}? held = choose ? null : {call.Call};\n            " : "";

            (use.Async ? awaited : body).Append($"        {{ // {form.Name}, {passThrough.Name}, {use.Name}\n            ")
                                        .Append(held)
                                        .Append(statement)
                                        .Append("\n        }\n");
        }

        var source =
            $$"""
              #nullable enable
              using System;
              using System.Linq;
              using System.Threading;
              using System.Threading.Tasks;
              using CodoMetis.TypeKit;
              using CodoMetis.TypeKit.Testing;

              public static class Use
              {
                  public static void Run({{Parameters}})
                  {
              {{body}}
                  }

                  public static async Task RunAsync({{Parameters}})
                  {
              {{awaited}}
                  }
              }
              """;

        return (source, expected);
    }

    /// <summary>
    /// Whether <paramref name="call"/> can be written in <paramref name="form"/>: a call has no static form, a
    /// <c>ValueTask</c> no <c>?.</c>, an assertion with arguments no method group, and a <c>Result</c> stays out of
    /// a form written for assertions.
    /// </summary>
    public static bool Applies(DropForm form, PendingCall call) =>
        !(form.AssertionsOnly && !call.IsAssertion)
     && !(form.Expression.Contains("{group}", StringComparison.Ordinal) && call.Group is null)
     && !(form.Expression.Contains("{static}", StringComparison.Ordinal) && call.Static is null)
     && !(form.Expression.Contains("{nc}", StringComparison.Ordinal) && call.NullConditional is null);

    /// <summary>Each span between <paramref name="open"/> and <paramref name="close"/> becomes the next markup location, or plain text where nothing is reported.</summary>
    private static string Mark(string text, string open, string close, bool reported, (string Outcome, string Method) message, List<(string Outcome, string Method)> expected)
    {
        var marked = new System.Text.StringBuilder();
        var at     = 0;

        for (var start = text.IndexOf(open, StringComparison.Ordinal); start >= 0; start = text.IndexOf(open, at, StringComparison.Ordinal))
        {
            var end = text.IndexOf(close, start, StringComparison.Ordinal);
            marked.Append(text, at, start - at).Append(reported ? $"{{|#{expected.Count}:" : "").Append(text, start + open.Length, end - start - open.Length).Append(reported ? "|}" : "");
            if (reported) expected.Add(message);
            at = end + close.Length;
        }

        return marked.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>An assertion, on a <c>Task</c> of what it checks and on a <c>ValueTask</c>.</summary>
    private static IEnumerable<PendingCall> Assertion(string method, string @class, string receiver, string arguments, string returns, string? kind = null)
    {
        var name = kind is null ? method : $"{kind} {method}";
        var tail = arguments.Length == 0 ? "" : ", " + arguments;

        string? Group(string held) => arguments.Length == 0 ? $"new[] {{ {held}, {held} }}.Select({@class}.{method})" : null;

        yield return new($"{name} on a Task", $"{receiver}.{method}({arguments})", $"{@class}.{method}({receiver}{tail})", $"{receiver}?.{method}({arguments})", returns, method, "Task", AwaitDrops: false, Group: Group(receiver));
        yield return new($"{name} on a ValueTask", $"{receiver}Value.{method}({arguments})", $"{@class}.{method}({receiver}Value{tail})", null, returns, method, "Task", AwaitDrops: false, Group: Group(receiver + "Value"));
    }
}
