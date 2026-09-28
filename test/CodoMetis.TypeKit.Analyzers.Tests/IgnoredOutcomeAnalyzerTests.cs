using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0003: a <c>Result</c> or <c>Option</c>, or a collection of them, dropped by an expression
/// statement or by a conversion of its task to a plain <c>Task</c>.
/// </summary>
public sealed class IgnoredOutcomeAnalyzerTests
{
    private const string Subjects =
        """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;
        using System.Threading.Tasks;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        public enum Fault { Refused }

        public readonly struct Validated : IValidatedValue<Validated, int, Fault>
        {
            public static Result<Validated, Fault> Create(int value) => Result<Validated, Fault>.Error(Fault.Refused);
        }

        public sealed class Service
        {
            public Result<int, Fault> Load() => 1;

            public Result<Fault> Save(int value) => Result.Success();

            public Option<int> Find() => Option.Some(1);

            public Task<Result<int, Fault>> LoadAsync() => Task.FromResult(Load());

            public ValueTask<Result<Fault>> SaveAsync() => new(Save(1));

            public Task<Option<int>> FindAsync() => Task.FromResult(Find());

            public Task<Result<Fault>> CancelAsync(int id) => Task.FromResult(Save(id));

            public List<Result<Fault>> SaveAll(int[] values) => [];

            public Task<IReadOnlyList<Result<Fault>>> SaveAllAsync(int[] values) => Task.FromResult<IReadOnlyList<Result<Fault>>>([]);

            public Task RunAsync() => Task.CompletedTask;

            public int Count() => 1;

            public List<int> Counts() => [];
        }

        public static class Bus
        {
            public static void Subscribe<T>(Func<T, Task> handler) { }
        }

        """;

    private static AnalyzerTest<IgnoredOutcomeAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<IgnoredOutcomeAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0003(int location, string outcome, string method) =>
        new DiagnosticResult("CMTK0003", DiagnosticSeverity.Warning).WithLocation(location).WithArguments(outcome, method);

    /// <summary>A second file, whose own using directives come first.</summary>
    private static AnalyzerTest<IgnoredOutcomeAnalyzer> TestWithFile(string file, params DiagnosticResult[] expected)
    {
        var test = Test("", expected);
        test.TestState.Sources.Add(("Caller.cs", file));
        return test;
    }

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new IgnoredOutcomeAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0003");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Warning);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task An_ignored_result_reports() =>
        Test(
            """
            public static class Caller
            {
                public static void Run(Service service) { {|#0:service.Save(1)|}; }
            }
            """,
            new DiagnosticResult("CMTK0003", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("The Result that 'Save' returns is ignored, so its outcome is never seen. Handle it, or discard it with '_ =' if nothing depends on it."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Every_form_of_dropping_one_reports() =>
        Test(
            """
            public static class Caller
            {
                public static async Task Run(Service service, Service? maybe)
                {
                    {|#0:service.Load()|};
                    {|#1:service.Find()|};
                    {|#2:await service.LoadAsync()|};
                    {|#3:await service.SaveAsync()|};
                    {|#4:service.LoadAsync()|};
                    {|#5:service.Load().Map(x => x + 1)|};
                    {|#6:maybe?.Load()|};
                    {|#7:Validated.Create(1)|};
                    {|#8:await service.LoadAsync().ConfigureAwait(false)|};
                }
            }
            """,
            Cmtk0003(0, "Result", "Load"),
            Cmtk0003(1, "Option", "Find"),
            Cmtk0003(2, "Result", "LoadAsync"),
            Cmtk0003(3, "Result", "SaveAsync"),
            Cmtk0003(4, "Result", "LoadAsync"),
            Cmtk0003(5, "Result", "Map"),
            Cmtk0003(6, "Result", "Load"),
            Cmtk0003(7, "Result", "Create"),
            Cmtk0003(8, "Result", "LoadAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// <c>Tap</c>, <c>TapAsync</c>, <c>TapError</c> and <c>TapErrorAsync</c> return their receiver:
    /// dropping it loses nothing only when the receiver is still held.
    /// </summary>
    [Fact]
    public Task Tap_on_a_stored_result_is_silent_and_on_a_call_reports() =>
        Test(
            """
            public static class Caller
            {
                private static Result<int, Fault> _field = 1;

                public static async Task Run(Service service, Result<int, Fault> parameter)
                {
                    var local = service.Load();
                    local.Tap(_ => { });
                    parameter.Tap(_ => { });
                    _field.Tap(_ => { });
                    await local.TapAsync(_ => Task.CompletedTask);
                    local.TapError(_ => { });
                    parameter.TapError(_ => { });
                    await local.TapErrorAsync(_ => Task.CompletedTask);
                    await parameter.TapErrorAsync(_ => Task.CompletedTask);

                    {|#0:service.Load().Tap(_ => { })|};
                    {|#1:await service.LoadAsync().TapAsync(_ => { })|};
                    {|#2:service.Load().TapError(_ => { })|};
                    {|#3:await service.LoadAsync().TapErrorAsync(_ => { })|};
                    {|#4:local.Ensure(_ => true, Fault.Refused)|};
                    {|#5:await service.Load().TapErrorAsync(_ => Task.CompletedTask)|};
                }
            }
            """,
            Cmtk0003(0, "Result", "Tap"),
            Cmtk0003(1, "Result", "TapAsync"),
            Cmtk0003(2, "Result", "TapError"),
            Cmtk0003(3, "Result", "TapErrorAsync"),
            Cmtk0003(4, "Result", "Ensure"),
            Cmtk0003(5, "Result", "TapErrorAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The <c>Task</c> continuations <c>TapAsync</c> and <c>TapErrorAsync</c> return the task's result
    /// unchanged, so awaiting them on a task the caller still holds drops nothing, as the instance
    /// <c>Tap</c> does not.
    /// </summary>
    [Fact]
    public Task A_Task_continuation_that_taps_a_stored_task_is_silent_and_on_a_call_reports() =>
        Test(
            """
            public static class Caller
            {
                private static readonly Task<Result<int, Fault>> Field = Task.FromResult<Result<int, Fault>>(1);

                public static async Task Run(Service service, Task<Result<int, Fault>> pending, Task<Result<Fault>> command)
                {
                    var local = service.LoadAsync();
                    await pending.TapAsync(_ => { });
                    await pending.TapAsync(_ => Task.CompletedTask).ConfigureAwait(false);
                    await pending.TapErrorAsync(_ => { });
                    await local.TapAsync(_ => { });
                    await Field.TapErrorAsync(_ => { });
                    await command.TapAsync(() => { });
                    await command.TapErrorAsync(_ => { });

                    {|#0:await service.LoadAsync().TapErrorAsync(_ => { })|};
                    {|#1:await pending.MapAsync(x => x + 1)|};
                }
            }
            """,
            Cmtk0003(0, "Result", "TapErrorAsync"),
            Cmtk0003(1, "Result", "MapAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A <c>Task</c> of a result converts implicitly to <c>Task</c>, which drops the result as silently
    /// as a statement does: an expression-bodied member, a <c>return</c>, a lambda, an argument.
    /// </summary>
    [Fact]
    public Task A_task_of_a_result_converted_to_a_plain_task_reports() =>
        Test(
            """
            public static class Caller
            {
                public static Task Cancel(Service service) => {|#0:service.LoadAsync()|};

                public static Task Save(Service service)
                {
                    return {|#1:service.LoadAsync()|};
                }

                public static Func<Task> Deferred(Service service) => () => {|#2:service.FindAsync()|};

                public static Task Handed(Service service) => Task.WhenAny({|#3:service.LoadAsync()|}, service.RunAsync());
            }
            """,
            Cmtk0003(0, "Result", "LoadAsync"),
            Cmtk0003(1, "Result", "LoadAsync"),
            Cmtk0003(2, "Option", "FindAsync"),
            Cmtk0003(3, "Result", "LoadAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>A task that keeps its result type, an explicit cast, and a task the caller holds are not dropped by the conversion.</summary>
    [Fact]
    public Task A_task_of_a_result_that_keeps_its_type_is_cast_or_is_held_stays_silent() =>
        Test(
            """
            public static class Caller
            {
                public static Task<Result<int, Fault>> Kept(Service service) => service.LoadAsync();

                public static Func<Task<Result<int, Fault>>> Deferred(Service service) => () => service.LoadAsync();

                public static Task Cast(Service service) => (Task)service.LoadAsync();

                public static Task Held(Task<Result<int, Fault>> pending) => pending;

                public static Task Plain(Service service) => service.RunAsync();

                public static async Task Awaited(Service service) { var kept = await service.LoadAsync(); }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A method group converts to a delegate that returns <c>Task</c> as the lambda around it does, and
    /// every call through the delegate drops the result: a local, an argument for a <c>Func&lt;T, Task&gt;</c>
    /// parameter, a delegate constructor, a local function.
    /// </summary>
    [Fact]
    public Task A_method_group_of_a_task_of_a_result_converted_to_a_delegate_returning_a_plain_task_reports() =>
        Test(
            """
            public static class Caller
            {
                public static void Run(Service service)
                {
                    Func<int, Task> cancel = {|#0:service.CancelAsync|};
                    Bus.Subscribe<int>({|#1:service.CancelAsync|});
                    var constructed = new Func<int, Task>({|#2:service.CancelAsync|});
                    Func<Task> find = {|#3:service.FindAsync|};

                    Task<Result<int, Fault>> Local() => service.LoadAsync();
                    Func<Task> local = {|#4:Local|};
                }
            }
            """,
            Cmtk0003(0, "Result", "CancelAsync"),
            Cmtk0003(1, "Result", "CancelAsync"),
            Cmtk0003(2, "Result", "CancelAsync"),
            Cmtk0003(3, "Option", "FindAsync"),
            Cmtk0003(4, "Result", "Local"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A delegate that keeps the result type, a cast to the delegate type, a method group of a plain
    /// <c>Task</c> or of a result, and a lambda that discards the result are not dropped by a conversion.
    /// </summary>
    [Fact]
    public Task A_method_group_that_keeps_its_result_is_cast_or_returns_no_result_stays_silent() =>
        Test(
            """
            public static class Caller
            {
                public static void Run(Service service)
                {
                    Func<int, Task<Result<Fault>>> kept = service.CancelAsync;
                    Bus.Subscribe<int>((Func<int, Task>)service.CancelAsync);
                    Func<Task> plain = service.RunAsync;
                    Func<ValueTask<Result<Fault>>> valueTask = service.SaveAsync;
                    Func<int, Result<Fault>> synchronous = service.Save;
                    Func<object> boxed = service.LoadAsync;
                    Bus.Subscribe<int>(async id => _ = await service.CancelAsync(id));
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A collection of results the statement made, dropped with it: awaited from <c>Task.WhenAll</c>,
    /// over a query, a method group or a list of calls, or materialised by <c>ToList</c> or
    /// <c>ToArray</c>, or returned by a call that was given no result and made them itself.
    /// </summary>
    [Fact]
    public Task A_collection_of_results_the_statement_made_and_dropped_reports() =>
        Test(
            """
            public static class Caller
            {
                private static ImmutableList<Result<Fault>> _saved = [];

                public static async Task Run(Service service, int[] ids, Task<Result<Fault>> held, List<Result<int, Fault>> loaded)
                {
                    {|#0:await Task.WhenAll(ids.Select(i => service.CancelAsync(i)))|};
                    {|#1:await Task.WhenAll(ids.Select(service.CancelAsync))|};
                    {|#2:await Task.WhenAll(service.CancelAsync(1), service.CancelAsync(2))|};
                    {|#3:await Task.WhenAll(service.FindAsync(), service.FindAsync()).ConfigureAwait(false)|};
                    {|#4:await Task.WhenAll(held, service.CancelAsync(2))|};
                    {|#5:Task.WhenAll(service.CancelAsync(1), service.CancelAsync(2))|};
                    {|#6:ids.Select(i => service.Save(i)).ToList()|};
                    {|#7:ids.Select(service.Save).ToArray()|};
                    {|#8:ids.Select(i => service.CancelAsync(i)).ToList()|};
                    {|#9:loaded.Select(r => r.Map(x => x + 1)).ToList()|};
                    {|#10:service.SaveAll(ids)|};
                    {|#11:await service.SaveAllAsync(ids)|};
                    {|#12:_saved.Add(service.Save(1))|};
                }
            }
            """,
            Cmtk0003(0, "collection of Results", "WhenAll"),
            Cmtk0003(1, "collection of Results", "WhenAll"),
            Cmtk0003(2, "collection of Results", "WhenAll"),
            Cmtk0003(3, "collection of Options", "WhenAll"),
            Cmtk0003(4, "collection of Results", "WhenAll"),
            Cmtk0003(5, "collection of Results", "WhenAll"),
            Cmtk0003(6, "collection of Results", "ToList"),
            Cmtk0003(7, "collection of Results", "ToArray"),
            Cmtk0003(8, "collection of Results", "ToList"),
            Cmtk0003(9, "collection of Results", "ToList"),
            Cmtk0003(10, "collection of Results", "SaveAll"),
            Cmtk0003(11, "collection of Results", "SaveAllAsync"),
            Cmtk0003(12, "collection of Results", "Add"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_task_of_a_collection_of_results_the_call_made_converted_to_a_plain_task_reports() =>
        Test(
            """
            public static class Caller
            {
                public static Task CancelAll(Service service, int[] ids) => {|#0:Task.WhenAll(ids.Select(service.CancelAsync))|};

                public static Task SaveAll(Service service, int[] ids) => {|#1:service.SaveAllAsync(ids)|};
            }
            """,
            Cmtk0003(0, "collection of Results", "WhenAll"),
            Cmtk0003(1, "collection of Results", "SaveAllAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Results the statement was given are still held where they came from: tasks in variables, a
    /// parameter or a field, and a copy of a held collection. A lazy query has made nothing, an explicit
    /// discard says the drop is intended, and a collection of anything else is not a result.
    /// </summary>
    [Fact]
    public Task A_collection_of_results_the_statement_was_given_or_that_is_used_stays_silent() =>
        Test(
            """
            public sealed class Batch : List<Result<Fault>>;

            public static class Caller
            {
                private static readonly List<Task<Result<Fault>>> Pending = [];

                public static async Task Run(Service service, int[] ids, List<Task<Result<Fault>>> pending, List<Result<int, Fault>> loaded, Batch batch)
                {
                    var first = service.CancelAsync(1);
                    var second = service.CancelAsync(2);
                    await Task.WhenAll(first, second);
                    await Task.WhenAll(first, second).ConfigureAwait(false);
                    await Task.WhenAll(pending);
                    await Task.WhenAll(Pending);
                    await Task.WhenAll(pending.ToArray());
                    await Task.WhenAll(pending.Where(task => !task.IsCompleted));
                    batch.GetRange(0, 1);
                    loaded.ToList();
                    loaded.Where(result => result.TryGetValue(out _, out _)).ToArray();
                    loaded.Select(result => result.Tap(_ => { })).ToList();
                    ids.Select(i => service.Save(i));
                    service.Counts();
                    await Task.WhenAll(ids.Select(async i => { _ = await service.CancelAsync(i); }));

                    _ = await Task.WhenAll(ids.Select(service.CancelAsync));
                    _ = ids.Select(service.Save).ToList();
                    var all = await Task.WhenAll(service.CancelAsync(1), service.CancelAsync(2));
                }

                public static Task Held(Task<Result<Fault>> first, Task<Result<Fault>> second) => Task.WhenAll(first, second);

                public static Task Cast(Service service, int[] ids) => (Task)Task.WhenAll(ids.Select(service.CancelAsync));
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A generated <c>TryFrom</c> on a value object of the same project does not bind where Metalama
    /// runs analyzers; CS0117 here reproduces that compilation.
    /// </summary>
    [Fact]
    public Task An_ignored_TryFrom_that_does_not_bind_reports() =>
        Test(
            """
            public static class Caller
            {
                public static void Run() { {|#0:Validated.{|CS0117:TryFrom|}(1)|}; }
            }
            """,
            Cmtk0003(0, "Option", "TryFrom"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Unbound, <c>TryFrom</c> and the generated instance <c>Revalidate</c> are recognised by name as a
    /// statement, and as the body of a method, local function, accessor or lambda that returns nothing.
    /// </summary>
    [Fact]
    public Task Unbound_generated_calls_dropped_by_a_body_that_returns_nothing_report() =>
        Test(
            """
            public static class Caller
            {
                public static void Statement(Validated code) { {|#0:code.{|CS1061:Revalidate|}()|}; }

                public static void Arrow(int input) => {|#1:Validated.{|CS0117:TryFrom|}(input)|};

                public static void Local(int input)
                {
                    static void Inner(int value) => {|#2:Validated.{|CS0117:TryFrom|}(value)|};
                    Inner(input);
                }

                public static Action Lambda(int input) => () => {|#3:Validated.{|CS0117:TryFrom|}(input)|};

                public static Action<Validated> Revalidating() => code => {|#4:code.{|CS1061:Revalidate|}()|};

                public static int Setter { set => {|#5:Validated.{|CS0117:TryFrom|}(value)|}; }
            }
            """,
            Cmtk0003(0, "Result", "Revalidate"),
            Cmtk0003(1, "Option", "TryFrom"),
            Cmtk0003(2, "Option", "TryFrom"),
            Cmtk0003(3, "Option", "TryFrom"),
            Cmtk0003(4, "Result", "Revalidate"),
            Cmtk0003(5, "Option", "TryFrom"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>An unbound generated call whose value is returned, or that is not a validated value object's, is not dropped.</summary>
    [Fact]
    public Task Unbound_generated_calls_whose_value_is_used_stay_silent() =>
        Test(
            """
            public static class Caller
            {
                public static Option<Validated> Returned(int input) => Validated.{|CS0117:TryFrom|}(input);

                public static Func<Option<Validated>> Deferred(int input) => () => Validated.{|CS0117:TryFrom|}(input);

                public static Option<Validated> Property => Validated.{|CS0117:TryFrom|}(1);

                public static void NotValidated(Service service) { service.{|CS1061:Revalidate|}(); }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task An_unbound_TryFrom_under_using_static_reports() =>
        TestWithFile(
            """
            using static Validated;

            public static class Caller
            {
                public static void Run() { {|#0:{|CS0103:TryFrom|}(1)|}; }
            }
            """,
            Cmtk0003(0, "Option", "TryFrom"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_used_or_explicitly_discarded_result_stays_silent() =>
        Test(
            """
            public static class Caller
            {
                public static Result<int, Fault> Run(Service service)
                {
                    _ = service.Load();
                    var kept = service.Find();
                    service.Save(service.Load().Match(x => x, _ => 0)).TryGetError(out _);
                    service.Count();
                    Result<int, Fault> assigned;
                    assigned = service.Load();
                    return service.Load();
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<IgnoredOutcomeAnalyzer>("public static class S { public static int F() => 1; public static void G() { F(); } }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
