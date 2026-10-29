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
public sealed record PendingCall(string Name, string Call, string? Static, string? NullConditional, string Returns, string Method, string Outcome, bool AwaitDrops)
{
    public override string ToString() => Name;
}

/// <summary>A call on a task that hands on the task it is called on, or none.</summary>
/// <param name="Name">What the call is, as a test case names it.</param>
/// <param name="Suffix">The call as written after the task.</param>
public sealed record TaskPassThrough(string Name, string Suffix)
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
public sealed record DropForm(string Name, string Expression, bool NeedsPassThrough = false)
{
    public bool IsBranch => Expression.Contains("[[", StringComparison.Ordinal);

    public override string ToString() => Name;
}

/// <summary>What the statement does with the expression.</summary>
/// <param name="Name">What the use is, as a test case names it.</param>
/// <param name="Statement"><c>{e}</c> is the expression, and <c>&lt;&lt;</c> <c>&gt;&gt;</c> enclose what a diagnostic reports for the statement's own call.</param>
/// <param name="Drops">Whether the use drops the task: <see langword="null"/> where it drops what the task completes with, which only <see cref="PendingCall.AwaitDrops"/> holds anything of.</param>
/// <param name="Async">Whether the use needs an <c>async</c> method.</param>
public sealed record TaskUse(string Name, string Statement, bool? Drops, bool Async = false)
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
/// static form, through <c>?.</c>, and as a branch of a <c>?:</c>, a <c>??</c> and a <c>switch</c> expression. The
/// use: dropped as a statement or as a lambda's body, against awaited, discarded, held and returned.
/// </para>
/// <para>
/// Both calls go through the one path, so they agree on every form: #152 found the assertions alone silent
/// through the pass-throughs of a plain <c>Task</c> and through the branches.
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

        new("a call that returns a Task of a Result", "service.LoadAsync()", null, "maybe?.LoadAsync()", "Task<Result<int, Fault>>", "LoadAsync", "Result", AwaitDrops: true),
    ];

    public static IReadOnlyList<TaskPassThrough> PassThroughs { get; } =
    [
        new("no pass-through", ""),
        new("ConfigureAwait(bool)", ".ConfigureAwait(false)"),
        new("ConfigureAwait(ConfigureAwaitOptions)", ".ConfigureAwait(ConfigureAwaitOptions.None)"),
        new("WaitAsync(TimeSpan)", ".WaitAsync(TimeSpan.FromSeconds(1))"),
        new("WaitAsync(CancellationToken)", ".WaitAsync(token)"),
        new("WaitAsync(TimeSpan, CancellationToken)", ".WaitAsync(TimeSpan.FromSeconds(1), token)"),
        new("WaitAsync(TimeSpan, TimeProvider)", ".WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System)"),
        new("WaitAsync(TimeSpan, TimeProvider, CancellationToken)", ".WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, token)"),
        new("WaitAsync then ConfigureAwait", ".WaitAsync(token).ConfigureAwait(false)"),
    ];

    public static IReadOnlyList<DropForm> Forms { get; } =
    [
        new("the call", "{call}{w}"),
        new("the lowered static form", "{static}{w}"),
        new("a null-conditional call", "{nc}{w}"),
        new("both branches of a conditional", "(choose ? [[{call}]] : [[{call}]]){w}", NeedsPassThrough: true),
        new("the right operand of ??", "(held ?? [[{call}]]){w}", NeedsPassThrough: true),
        new("both arms of a switch expression", "(choose switch { true => [[{call}]], false => [[{call}]] }){w}", NeedsPassThrough: true),
    ];

    public static IReadOnlyList<TaskUse> Uses { get; } =
    [
        new("dropped as a statement", "<<{e}>>;", Drops: true),
        new("dropped as a lambda's body", "Action dropped = () => <<{e}>>;", Drops: true),
        new("awaited", "<<await {e}>>;", Drops: null, Async: true),
        new("discarded with _ =", "_ = {e};", Drops: false),
        new("held in a local", "var kept = {e};", Drops: false),
        new("returned from a lambda", "Func<object?> returned = () => {e};", Drops: false),
    ];

    /// <summary>
    /// A file whose two run methods use <paramref name="call"/> in every form, through every pass-through, in
    /// every use, and the spans a diagnostic is expected at, by markup location.
    /// </summary>
    public static (string Source, int Expected) Use(PendingCall call)
    {
        var expected = 0;
        var body     = new System.Text.StringBuilder();
        var awaited  = new System.Text.StringBuilder();

        foreach (var form in Forms)
        foreach (var passThrough in PassThroughs)
        foreach (var use in Uses)
        {
            // A statement that does not compile is no case.
            if (form.NeedsPassThrough && passThrough.Suffix.Length == 0) continue;
            if (!Applies(form, call)) continue;

            // await x?.M().ConfigureAwait(false) awaits a nullable awaiter, which has no GetAwaiter, and await x?.M()
            // a task that may be null (CS8602).
            if (use.Async && form.Expression.Contains("{nc}", StringComparison.Ordinal)) continue;

            var reported   = use.Drops ?? call.AwaitDrops;
            var expression = form.Expression
                                 .Replace("{call}", call.Call)
                                 .Replace("{static}", call.Static)
                                 .Replace("{nc}", call.NullConditional)
                                 .Replace("{w}", passThrough.Suffix);
            var statement  = use.Statement.Replace("{e}", expression);

            statement = Mark(statement, "<<", ">>", reported && !form.IsBranch, ref expected);
            statement = Mark(statement, "[[", "]]", reported, ref expected);

            // ??'s left operand is a task the caller holds, declared where the form names it.
            var held = expression.Contains("held ??", StringComparison.Ordinal) ? $"{call.Returns}? held = choose ? null : {call.Call};\n            " : "";

            (use.Async ? awaited : body).Append($"        {{ // {form.Name}, {passThrough.Name}, {use.Name}\n            ")
                                        .Append(held)
                                        .Append(statement)
                                        .Append("\n        }\n");
        }

        var source =
            $$"""
              #nullable enable
              using System;
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

    /// <summary>Whether <paramref name="call"/> can be written in <paramref name="form"/>: a call has no static form, a <c>ValueTask</c> no <c>?.</c>.</summary>
    public static bool Applies(DropForm form, PendingCall call) =>
        !(form.Expression.Contains("{static}", StringComparison.Ordinal) && call.Static is null)
     && !(form.Expression.Contains("{nc}", StringComparison.Ordinal) && call.NullConditional is null);

    /// <summary>Each span between <paramref name="open"/> and <paramref name="close"/> becomes the next markup location, or plain text where nothing is reported.</summary>
    private static string Mark(string text, string open, string close, bool reported, ref int next)
    {
        var marked = new System.Text.StringBuilder();
        var at     = 0;

        for (var start = text.IndexOf(open, StringComparison.Ordinal); start >= 0; start = text.IndexOf(open, at, StringComparison.Ordinal))
        {
            var end = text.IndexOf(close, start, StringComparison.Ordinal);
            marked.Append(text, at, start - at).Append(reported ? $"{{|#{next++}:" : "").Append(text, start + open.Length, end - start - open.Length).Append(reported ? "|}" : "");
            at = end + close.Length;
        }

        return marked.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>An assertion, on a <c>Task</c> of what it checks and on a <c>ValueTask</c>.</summary>
    private static IEnumerable<PendingCall> Assertion(string method, string @class, string receiver, string arguments, string returns, string? kind = null)
    {
        var name = kind is null ? method : $"{kind} {method}";
        var tail = arguments.Length == 0 ? "" : ", " + arguments;

        yield return new($"{name} on a Task", $"{receiver}.{method}({arguments})", $"{@class}.{method}({receiver}{tail})", $"{receiver}?.{method}({arguments})", returns, method, "Task", AwaitDrops: false);
        yield return new($"{name} on a ValueTask", $"{receiver}Value.{method}({arguments})", $"{@class}.{method}({receiver}Value{tail})", null, returns, method, "Task", AwaitDrops: false);
    }
}
