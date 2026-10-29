using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0003: a <c>Result</c> or <c>Option</c>, or a collection of them, dropped by an expression
/// statement, by a conversion of its task to a plain <c>Task</c>, by <c>Wait</c>, or by a loop that
/// never reads them.
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

            public IEnumerable<Result<Fault>> SaveEach(int[] values) => values.Select(Save);

            public async IAsyncEnumerable<Result<Fault>> SaveEachAsync(int[] values)
            {
                foreach (var value in values) yield return await CancelAsync(value);
            }

            public (Result<int, Fault>, int) Pair() => (Load(), 1);

            public Task<(Result<int, Fault> Loaded, int Count)> PairAsync() => Task.FromResult<(Result<int, Fault>, int)>(Pair());

            public Dictionary<int, Result<Fault>> Map(int[] values) => [];

            public Task<IReadOnlyDictionary<int, Option<int>>> MapAsync() => Task.FromResult<IReadOnlyDictionary<int, Option<int>>>(new Dictionary<int, Option<int>>());

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
        new DiagnosticResult("CMTK0003", DiagnosticSeverity.Error).WithLocation(location).WithArguments(outcome, method);

    /// <summary>A second file, whose own using directives come first.</summary>
    private static AnalyzerTest<IgnoredOutcomeAnalyzer> TestWithFile(string file, params DiagnosticResult[] expected)
    {
        var test = Test("", expected);
        test.TestState.Sources.Add(("Caller.cs", file));
        return test;
    }

    /// <summary>The subjects, with the real test assertions referenced and imported.</summary>
    private static AnalyzerTest<IgnoredOutcomeAnalyzer> TestWithAssertions(string code, params DiagnosticResult[] expected)
    {
        var test = Test("", expected);
        test.TestState.AdditionalReferences.Add(RealTesting.Reference);
        test.TestState.Sources.Add(("Caller.cs", "using System;\nusing System.Threading.Tasks;\nusing CodoMetis.TypeKit;\nusing CodoMetis.TypeKit.Testing;\n\n" + code));
        return test;
    }

    /// <summary>
    /// An assertion on a pending value fails the test only through the task it returns: dropped
    /// unawaited, in a test method that is not <c>async</c> (where C# warns of nothing) or in one that is,
    /// it passes whatever the value is.
    /// </summary>
    [Fact]
    public Task A_dropped_assertion_on_a_pending_value_reports() =>
        TestWithAssertions(
            """
            public static class Caller
            {
                public static void Run(Service service)
                {
                    {|#0:service.LoadAsync().ShouldBeSuccess()|};
                    {|#1:service.FindAsync().ShouldBeNone()|};
                    {|#2:service.SaveAsync().ShouldBeError(Fault.Refused)|};
                    {|#3:service.LoadAsync().ShouldBeSuccess().ConfigureAwait(false)|};
                    Action check = () => {|#4:service.FindAsync().ShouldBeSome()|};
                }

                public static async Task RunAsync(Service service)
                {
                    #pragma warning disable CS4014
                    {|#5:service.LoadAsync().ShouldBeError()|};
                    #pragma warning restore CS4014
                    await Task.Yield();
                }
            }
            """,
            new DiagnosticResult("CMTK0003", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("The Task that 'ShouldBeSuccess' returns is ignored, so its outcome is never seen. Handle it, or discard it with '_ =' if nothing depends on it."),
            Cmtk0003(1, "Task", "ShouldBeNone"),
            Cmtk0003(2, "Task", "ShouldBeError"),
            Cmtk0003(3, "Task", "ShouldBeSuccess"),
            Cmtk0003(4, "Task", "ShouldBeSome"),
            Cmtk0003(5, "Task", "ShouldBeError"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task An_awaited_held_or_discarded_assertion_is_silent() =>
        TestWithAssertions(
            """
            public static class Caller
            {
                public static async Task RunAsync(Service service)
                {
                    await service.LoadAsync().ShouldBeSuccess();
                    await service.FindAsync().ShouldBeNone().ConfigureAwait(false);
                    var pending = service.LoadAsync().ShouldBeSuccess();
                    await pending;
                    _ = service.SaveAsync().ShouldBeSuccess();
                    service.Load().ShouldBeSuccess();
                }

                public static Task Returned(Service service) => service.FindAsync().ShouldBeNone();

                public static Func<Task> Deferred(Service service) => () => service.FindAsync().ShouldBeNone();
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Only the package's own assertions are reported: the same shape from any other assembly, a
    /// consumer's own extension on a pending result among them, built before 2.3 and builds still.
    /// </summary>
    [Fact]
    public Task A_task_of_another_assembly_s_extension_on_a_pending_value_is_silent() =>
        TestWithAssertions(
            """
            public static class Logging
            {
                public static Task LogAsync<T>(this Task<Result<T, Fault>> pending) where T : notnull => pending;

                public static Task ShouldBeSuccess<T>(this Task<Option<T>> pending) where T : notnull => pending;
            }

            public static class Caller
            {
                public static void Run(Service service)
                {
                    service.LoadAsync().LogAsync();
                    Logging.ShouldBeSuccess(service.FindAsync());
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new IgnoredOutcomeAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0003");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
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
            new DiagnosticResult("CMTK0003", DiagnosticSeverity.Error)
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
    /// A patch is no outcome: its <c>Tap</c> and <c>IfPresent</c> return it, as <c>Option</c>'s <c>Tap</c>
    /// returns the option, and a statement that drops it drops nothing anyone has to decide on.
    /// </summary>
    [Fact]
    public Task A_patch_s_Tap_and_IfPresent_as_statements_are_silent() =>
        Test(
            """
            public static class Caller
            {
                public static void Run(Patch<int> quantity, Patch<string> nickname, List<int> seen)
                {
                    quantity.Tap(seen.Add);
                    quantity.IfPresent(value => seen.Add(value ?? 0));
                    nickname.IfPresent(value => seen.Add(value?.Length ?? 0));
                    Patch.Value(3).Tap(seen.Add);
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// <c>ConfigureAwait</c> and <c>WaitAsync</c> hand on the task they are called on: a task the caller
    /// still holds keeps its result through them, as it does through <c>await held;</c>. They were
    /// reported as the producers of the result, at Error.
    /// </summary>
    [Fact]
    public Task A_held_task_passed_through_ConfigureAwait_or_WaitAsync_stays_silent() =>
        Test(
            """
            public static class Caller
            {
                private static readonly Task<Result<int, Fault>> Field = Task.FromResult<Result<int, Fault>>(1);

                public static async Task Run(Task<Result<int, Fault>> held, Task<Result<int, Fault>> other, ValueTask<Result<Fault>> valueHeld, bool dry, System.Threading.CancellationToken t)
                {
                    await held;
                    await held.ConfigureAwait(false);
                    await held.ConfigureAwait(ConfigureAwaitOptions.None);
                    await Field.ConfigureAwait(false);
                    await (dry ? held : other).ConfigureAwait(false);
                    await valueHeld.ConfigureAwait(false);
                    await held.WaitAsync(t);
                    await held.WaitAsync(TimeSpan.FromSeconds(1));
                    await held.WaitAsync(TimeSpan.FromSeconds(1), t);
                    await held.WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System);
                    await held.WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, t);
                    await held.WaitAsync(t).ConfigureAwait(false);
                    held.WaitAsync(t);
                    held.ConfigureAwait(false);
                    held.WaitAsync(t).Wait();
                    await Task.WhenAll(held.WaitAsync(t), other);
                }

                public static Task Handed(Task<Result<int, Fault>> held, System.Threading.CancellationToken t) => held.WaitAsync(t);
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A task a call made, passed through <c>ConfigureAwait</c> or <c>WaitAsync</c>, is still that
    /// call's result, dropped and named after the call: awaited, not awaited, waited for, converted to
    /// a plain task, or picked by a branch.
    /// </summary>
    [Fact]
    public Task A_call_s_task_passed_through_ConfigureAwait_or_WaitAsync_reports_naming_the_call() =>
        Test(
            """
            public static class Caller
            {
                public static async Task Run(Service service, Task<Result<int, Fault>> held, bool dry, System.Threading.CancellationToken t)
                {
                    {|#0:await service.LoadAsync().ConfigureAwait(ConfigureAwaitOptions.None)|};
                    {|#1:await service.SaveAsync().ConfigureAwait(false)|};
                    {|#2:await service.LoadAsync().WaitAsync(t)|};
                    {|#3:await service.LoadAsync().WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, t).ConfigureAwait(false)|};
                    {|#4:service.LoadAsync().WaitAsync(t)|};
                    {|#5:service.LoadAsync().ConfigureAwait(false)|};
                    await (dry ? held : {|#6:service.LoadAsync()|}).ConfigureAwait(false);
                    {|#7:service.LoadAsync().WaitAsync(t).Wait()|};
                }

                public static Task Handed(Service service, System.Threading.CancellationToken t) => {|#8:service.LoadAsync().WaitAsync(t)|};
            }
            """,
            Cmtk0003(0, "Result", "LoadAsync"),
            Cmtk0003(1, "Result", "SaveAsync"),
            Cmtk0003(2, "Result", "LoadAsync"),
            Cmtk0003(3, "Result", "LoadAsync"),
            Cmtk0003(4, "Result", "LoadAsync"),
            Cmtk0003(5, "Result", "LoadAsync"),
            Cmtk0003(6, "Result", "LoadAsync"),
            Cmtk0003(7, "Result", "LoadAsync"),
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
    /// A chain of taps on a stored result or task hands back what the caller still holds, link by
    /// link: each <c>Tap</c> returns its receiver, and that receiver is the previous <c>Tap</c>, which
    /// returned a stored one. So is a tap on what a held task completes with, awaited in the chain. A
    /// chain that starts at a call, or passes through a combinator that makes a new result, drops it.
    /// </summary>
    [Fact]
    public Task A_chain_of_taps_on_a_stored_result_or_task_is_silent_and_on_a_call_reports() =>
        Test(
            """
            public static class Caller
            {
                private static Result<int, Fault> _field = 1;

                public static async Task Run(Service service, Result<int, Fault> parameter, Result<Fault> command, Option<int> option, Task<Result<int, Fault>> pending)
                {
                    var local = service.Load();
                    parameter.Tap(_ => { }).TapError(_ => { });
                    local.Tap(_ => { }).Tap(_ => { }).TapError(_ => { });
                    _field.TapError(_ => { }).Tap(_ => { });
                    command.Tap(() => { }).TapError(_ => { });
                    option.Tap(_ => { }).Tap(_ => { });
                    await local.TapAsync(_ => Task.CompletedTask).TapErrorAsync(_ => { });
                    await pending.TapAsync(_ => { }).TapErrorAsync(_ => { });
                    await pending.TapAsync(_ => { }).TapErrorAsync(_ => Task.CompletedTask).ConfigureAwait(false);
                    await parameter.Tap(_ => { }).TapErrorAsync(_ => Task.CompletedTask);

                    var held = await pending;
                    held.Tap(_ => { }).TapError(_ => { });
                    (await pending).Tap(_ => { }).TapError(_ => { });
                    (await pending.TapAsync(_ => { })).TapError(_ => { });
                    (await pending.TapAsync(_ => { }).ConfigureAwait(false)).TapError(_ => { });
                    await pending.WaitAsync(TimeSpan.FromSeconds(1)).TapAsync(_ => { }).TapErrorAsync(_ => { });

                    {|#0:service.Load().Tap(_ => { }).TapError(_ => { })|};
                    {|#6:await service.LoadAsync().WaitAsync(TimeSpan.FromSeconds(1)).TapAsync(_ => { })|};
                    {|#1:await service.LoadAsync().TapAsync(_ => { }).TapErrorAsync(_ => { })|};
                    {|#2:(await service.LoadAsync().TapAsync(_ => { })).TapError(_ => { })|};
                    {|#3:(await service.LoadAsync()).Tap(_ => { })|};
                    {|#4:local.Map(x => x + 1).Tap(_ => { })|};
                    {|#5:await pending.MapAsync(x => x + 1).TapAsync(_ => { })|};
                }
            }
            """,
            Cmtk0003(0, "Result", "TapError"),
            Cmtk0003(1, "Result", "TapErrorAsync"),
            Cmtk0003(2, "Result", "TapError"),
            Cmtk0003(3, "Result", "Tap"),
            Cmtk0003(4, "Result", "Tap"),
            Cmtk0003(5, "Result", "TapAsync"),
            Cmtk0003(6, "Result", "TapAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A tap on a stored result or task, converted to a plain <c>Task</c> or waited for, leaves the
    /// outcome with the caller, as the same tap as a statement does: both were reported, on a held
    /// <c>Result</c> and on a held <c>Task</c> of one. A tap on what a call made still drops it.
    /// </summary>
    [Fact]
    public Task A_tap_on_a_stored_result_or_task_converted_to_a_plain_Task_or_waited_for_is_silent() =>
        Test(
            """
            public static class Caller
            {
                private static Result<int, Fault> _field = 1;

                public static Task FromResult(Result<int, Fault> held) => held.TapAsync(_ => Task.CompletedTask);
                public static Task FromTask(Task<Result<int, Fault>> pending) => pending.TapAsync(_ => { });
                public static Task Chained(Task<Result<int, Fault>> pending) => pending.TapAsync(_ => { }).TapErrorAsync(_ => { });
                public static Task Field() => _field.TapErrorAsync(_ => Task.CompletedTask);
                public static Task Bounded(Task<Result<int, Fault>> pending) => pending.WaitAsync(TimeSpan.FromSeconds(1)).TapAsync(_ => { });
                public static Func<Task> Later(Task<Result<int, Fault>> pending) => () => pending.TapAsync(_ => { });
                public static Task Either(bool flag, Task<Result<int, Fault>> pending, Service service) =>
                    flag ? pending.TapAsync(_ => { }) : {|#2:service.LoadAsync().TapAsync(_ => { })|};

                public static Task Made(Service service) => {|#0:service.LoadAsync().TapAsync(_ => { })|};
                public static Task MadeFromResult(Service service) => {|#1:service.Load().TapAsync(_ => Task.CompletedTask)|};

                public static void Blocked(Task<Result<int, Fault>> pending, Service service)
                {
                    pending.TapAsync(_ => { }).Wait();
                    {|#3:service.LoadAsync().TapAsync(_ => { }).Wait()|};
                }
            }
            """,
            Cmtk0003(0, "Result", "TapAsync"),
            Cmtk0003(1, "Result", "TapAsync"),
            Cmtk0003(2, "Result", "TapAsync"),
            Cmtk0003(3, "Result", "TapAsync"))
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
    /// A result task reached through a branch was silent: the ternary or the <c>switch</c> expression
    /// that picked it, awaited or converted whole to a plain task; and a method group that answers a
    /// collection of results, converted to a delegate that returns a plain task.
    /// </summary>
    [Fact]
    public Task A_result_dropped_through_a_branch_or_a_collection_s_method_group_reports() =>
        Test(
            """
            public static class Branches
            {
                public static async Task Run(Service service, bool dry, int id)
                {
                    await (dry ? {|#0:service.CancelAsync(id)|} : {|#1:service.CancelAsync(0)|});
                    await (id switch { 1 => {|#2:service.CancelAsync(1)|}, _ => {|#3:service.CancelAsync(2)|} });
                    await (dry ? service.RunAsync() : Task.CompletedTask);
                }

                public static Task Submit(Service service, bool draft, int id) => draft ? {|#4:service.CancelAsync(id)|} : {|#5:service.CancelAsync(0)|};

                public static void Hold(Service service)
                {
                    Func<int[], Task> all = {|#6:service.SaveAllAsync|};
                    Func<Task> run = service.RunAsync;
                }
            }
            """,
            Cmtk0003(0, "Result", "CancelAsync"),
            Cmtk0003(1, "Result", "CancelAsync"),
            Cmtk0003(2, "Result", "CancelAsync"),
            Cmtk0003(3, "Result", "CancelAsync"),
            Cmtk0003(4, "Result", "CancelAsync"),
            Cmtk0003(5, "Result", "CancelAsync"),
            Cmtk0003(6, "collection of Results", "SaveAllAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Neighbours of the branch, collection and conversion forms were silent: a call picked by
    /// <c>??</c>, awaited or converted to a plain task; a tuple that holds a result; a dictionary of
    /// results, or a sequence of key-value pairs of them materialised.
    /// </summary>
    [Fact]
    public Task A_result_dropped_through_a_coalesce_a_tuple_or_a_dictionary_reports() =>
        Test(
            """
            public static class Neighbours
            {
                public static async Task Run(Service service, Task<Result<int, Fault>> held, int[] ids)
                {
                    await (held ?? {|#0:service.LoadAsync()|});
                    {|#1:service.Pair()|};
                    {|#2:await service.PairAsync()|};
                    {|#3:service.PairAsync()|};
                    {|#4:service.Map(ids)|};
                    {|#5:ids.ToDictionary(i => i, i => service.Save(i))|};
                    {|#6:await service.MapAsync()|};
                    {|#7:ids.Select(i => new KeyValuePair<int, Result<Fault>>(i, service.Save(i))).ToList()|};
                }

                public static Func<Task> Deferred(Service service, Task<Result<int, Fault>> held) => () => held ?? {|#8:service.LoadAsync()|};

                public static Task Converted(Service service) => {|#9:service.PairAsync()|};

                public static Task Mapped(Service service) => {|#10:service.MapAsync()|};

                public static void Groups(Service service)
                {
                    Func<Task> pairs = {|#11:service.PairAsync|};
                    Func<Task> maps = {|#12:service.MapAsync|};
                }
            }
            """,
            Cmtk0003(0, "Result", "LoadAsync"),
            Cmtk0003(1, "tuple holding a Result", "Pair"),
            Cmtk0003(2, "tuple holding a Result", "PairAsync"),
            Cmtk0003(3, "tuple holding a Result", "PairAsync"),
            Cmtk0003(4, "collection of Results", "Map"),
            Cmtk0003(5, "collection of Results", "ToDictionary"),
            Cmtk0003(6, "collection of Options", "MapAsync"),
            Cmtk0003(7, "collection of Results", "ToList"),
            Cmtk0003(8, "Result", "LoadAsync"),
            Cmtk0003(9, "tuple holding a Result", "PairAsync"),
            Cmtk0003(10, "collection of Options", "MapAsync"),
            Cmtk0003(11, "tuple holding a Result", "PairAsync"),
            Cmtk0003(12, "collection of Options", "MapAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A coalesce of held tasks, a tuple or a dictionary that is used or discarded, and a copy of a
    /// held dictionary or of a held tuple's result drop nothing.
    /// </summary>
    [Fact]
    public Task A_coalesce_a_tuple_or_a_dictionary_that_is_held_or_used_stays_silent() =>
        Test(
            """
            public static class Neighbours
            {
                public static async Task Run(Service service, Task<Result<int, Fault>> held, Task<Result<int, Fault>> other, Dictionary<int, Result<Fault>> saved, (Result<int, Fault>, int) pair)
                {
                    await (held ?? other);
                    _ = service.Pair();
                    var (loaded, count) = service.Pair();
                    _ = await service.PairAsync();
                    _ = service.Map([1]);
                    var kept = await service.MapAsync();
                    saved.ToDictionary(entry => entry.Key, entry => entry.Value);
                    saved.Where(entry => entry.Key > 0).ToList();
                    ValueTuple.Create(pair.Item1, 1);
                    service.Counts().ToDictionary(c => c, c => c);
                }

                public static Task Held(Task<Result<int, Fault>> held, Task<Result<int, Fault>> other) => held ?? other;
            }
            """)
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

    /// <summary>
    /// <c>SequenceAsync</c> and <c>TraverseAsync</c> over an async stream answer a <c>Task</c> of a result,
    /// so dropping one reports as dropping <c>TraverseAsync</c> over a sequence does: awaited or not, and
    /// converted to a plain <c>Task</c>. Kept or discarded with <c>_ =</c>, it is silent.
    /// </summary>
    [Fact]
    public Task A_dropped_SequenceAsync_or_TraverseAsync_over_a_stream_reports_as_over_a_sequence() =>
        Test(
            """
            public static class Caller
            {
                public static async Task Run(Service service, int[] ids, IAsyncEnumerable<int> stream, IAsyncEnumerable<Result<int, Fault>> values)
                {
                    {|#0:await ids.TraverseAsync(service.CancelAsync)|};
                    {|#1:await stream.TraverseAsync(service.CancelAsync)|};
                    {|#2:await stream.TraverseAsync((id, token) => service.LoadAsync(), default)|};
                    {|#3:await service.SaveEachAsync(ids).SequenceAsync()|};
                    {|#4:await values.SequenceAsync(default)|};
                    #pragma warning disable CS4014
                    {|#5:service.SaveEachAsync(ids).SequenceAsync()|};
                    {|#6:stream.TraverseAsync(service.CancelAsync)|};
                    #pragma warning restore CS4014

                    _ = await values.SequenceAsync();
                    var all = await stream.TraverseAsync(id => service.LoadAsync());
                    if (await service.SaveEachAsync(ids).SequenceAsync()) { }
                }

                public static Task Drop(Service service, int[] ids) => {|#7:service.SaveEachAsync(ids).SequenceAsync()|};

                public static Task DropEach(Service service, IAsyncEnumerable<int> stream) => {|#8:stream.TraverseAsync(service.CancelAsync)|};
            }
            """,
            Cmtk0003(0, "Result", "TraverseAsync"),
            Cmtk0003(1, "Result", "TraverseAsync"),
            Cmtk0003(2, "Result", "TraverseAsync"),
            Cmtk0003(3, "Result", "SequenceAsync"),
            Cmtk0003(4, "Result", "SequenceAsync"),
            Cmtk0003(5, "Result", "SequenceAsync"),
            Cmtk0003(6, "Result", "TraverseAsync"),
            Cmtk0003(7, "Result", "SequenceAsync"),
            Cmtk0003(8, "Result", "TraverseAsync"))
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
    /// <c>Wait</c> blocks until the result exists and hands it to nobody, whatever reads what it returns;
    /// <c>Task.WaitAll</c> over tasks the statement made converts them to plain ones; a lazy sequence a
    /// call made without being given a result makes its results as it is materialised.
    /// </summary>
    [Fact]
    public Task Waiting_for_or_materialising_results_the_statement_made_reports() =>
        Test(
            """
            public static class Caller
            {
                public static async Task Run(Service service, int[] ids)
                {
                    {|#0:service.CancelAsync(1).Wait()|};
                    if ({|#1:service.FindAsync().Wait(100)|}) { }
                    {|#2:Task.WhenAll(service.CancelAsync(1), service.CancelAsync(2)).Wait()|};
                    Task.WaitAll({|#3:ids.Select(service.CancelAsync)|});
                    Task.WaitAll({|#4:ids.Select(i => service.CancelAsync(i)).ToArray()|});
                    {|#5:service.SaveEach(ids).ToList()|};
                    {|#6:await service.SaveEachAsync(ids).ToListAsync()|};
                    Task.WaitAny({|#7:ids.Select(service.CancelAsync).ToArray()|});
                    Drain({|#8:ids.Select(service.CancelAsync).ToArray()|});
                }

                private static void Drain(ReadOnlySpan<Task> tasks) { }
            }
            """,
            Cmtk0003(0, "Result", "CancelAsync"),
            Cmtk0003(1, "Option", "FindAsync"),
            Cmtk0003(2, "collection of Results", "WhenAll"),
            Cmtk0003(3, "collection of Results", "Select"),
            Cmtk0003(4, "collection of Results", "ToArray"),
            Cmtk0003(5, "collection of Results", "ToList"),
            Cmtk0003(6, "collection of Results", "ToListAsync"),
            Cmtk0003(7, "collection of Results", "ToArray"),
            Cmtk0003(8, "collection of Results", "ToArray"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A task still held keeps its result after <c>Wait</c>, and so do held tasks after
    /// <c>Task.WaitAll</c>; a plain task has none; a held sequence, or one filtered from what the
    /// statement was given, made nothing; an explicit cast to plain tasks says the drop is intended.
    /// </summary>
    [Fact]
    public Task Waiting_for_or_materialising_results_that_are_held_stays_silent() =>
        Test(
            """
            public static class Caller
            {
                public static void Run(Service service, int[] ids, Task<Result<Fault>> held, List<Task<Result<Fault>>> pending, IEnumerable<Result<Fault>> each)
                {
                    var task = service.CancelAsync(1);
                    task.Wait();
                    held.Wait();
                    service.RunAsync().Wait();
                    Task.WaitAll(pending);
                    Task.WaitAll(pending.ToArray());
                    Task.WaitAll(task, held);
                    Task.WaitAll(ids.Select(_ => service.RunAsync()));
                    each.ToList();
                    each.Where(result => result.TryGetError(out _)).ToList();
                    _ = service.SaveEach(ids).ToList();
                    var all = service.SaveEach(ids).ToList();
                    Task.WaitAll((ReadOnlySpan<Task>)ids.Select(service.CancelAsync).ToArray());
                    Task.WaitAll((IEnumerable<Task>)ids.Select(service.CancelAsync));
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A loop over results its header made, whose body never reads the iteration variable, drains
    /// them: a call, a method group passed to <c>Select</c>, an asynchronous stream with its
    /// cancellation, a sequence of tasks.
    /// </summary>
    [Fact]
    public Task A_loop_that_never_reads_the_results_its_header_made_reports() =>
        Test(
            """
            public static class Caller
            {
                public static async Task Run(Service service, int[] ids, System.Threading.CancellationToken token)
                {
                    var count = 0;
                    foreach (var outcome in {|#0:service.SaveAll(ids)|}) { }
                    foreach (var outcome in {|#1:ids.Select(service.Save)|}) count++;
                    await foreach (var outcome in {|#2:service.SaveEachAsync(ids)|}) { }
                    await foreach (var outcome in {|#3:service.SaveEachAsync(ids).WithCancellation(token)|}) count++;
                    foreach (var pending in {|#4:ids.Select(service.CancelAsync)|}) { }
                }
            }
            """,
            Cmtk0003(0, "sequence of Results", "SaveAll"),
            Cmtk0003(1, "sequence of Results", "Select"),
            Cmtk0003(2, "sequence of Results", "SaveEachAsync"),
            Cmtk0003(3, "sequence of Results", "WithCancellation"),
            Cmtk0003(4, "sequence of Results", "Select"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A loop that reads each result, even only inside a lambda, handles them; a variable named
    /// <c>_</c> says the drop is intended; a held collection keeps its results; a loop over anything
    /// else is not about results.
    /// </summary>
    [Fact]
    public Task A_loop_that_reads_its_results_discards_them_or_walks_a_held_collection_stays_silent() =>
        Test(
            """
            public static class Caller
            {
                public static async Task Run(Service service, int[] ids, List<Result<int, Fault>> loaded, IAsyncEnumerable<Result<Fault>> stream)
                {
                    var errors = 0;
                    foreach (var outcome in service.SaveAll(ids)) if (outcome.TryGetError(out _)) errors++;
                    await foreach (var outcome in service.SaveEachAsync(ids)) _ = outcome;
                    foreach (var outcome in service.SaveEach(ids)) Run(() => outcome.TryGetError(out _));
                    foreach (var _ in service.SaveAll(ids)) { }
                    await foreach (var _ in service.SaveEachAsync(ids)) { }
                    foreach (var outcome in loaded) { }
                    await foreach (var outcome in stream) { }
                    foreach (var outcome in loaded.Where(result => result.TryGetValue(out _, out _))) { }
                    foreach (var count in service.Counts()) { }
                }

                private static void Run(Func<bool> check) { }
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
