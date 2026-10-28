using System.Reflection;

namespace CodoMetis.TypeKit.Testing.Tests;

/// <summary>
/// Every assertion has a form on a <c>Task</c> and on a <c>ValueTask</c> of what it asserts on, which
/// awaits it and answers as the synchronous form does: the same content back, the same message on a
/// failure. A task that faults is the test's own failure, and its exception propagates as it is.
/// </summary>
public sealed class AsyncAssertionTests
{
    public enum Fault { NotFound, AlreadyShipped }

    public enum Other { Conflict }

    public enum Third { Gone }

    private const string Secret = "s3cr3t-value";

    private static readonly Result<string, Fault> Success = Secret;
    private static readonly Result<string, Fault> Error = Result.Error(Fault.NotFound);
    private static readonly Result<Fault> Done = Result.Success();
    private static readonly Result<Fault> Refused = Result.Error(Fault.AlreadyShipped);
    private static readonly Fault<Fault, Other> Pair = Fault<Fault, Other>.Of(Fault.NotFound);
    private static readonly Fault<Fault, Other, Third> Triple = Fault<Fault, Other, Third>.Of(Third.Gone);

    private static async Task<T> Later<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    private static async ValueTask<T> LaterValue<T>(T value)
    {
        await Task.Yield();
        return value;
    }

    /// <summary>
    /// Each synchronous assertion, keyed by its signature, with inputs it passes and fails on: the
    /// synchronous call, the <c>Task</c> form's and the <c>ValueTask</c> form's over the same input.
    /// </summary>
    private static readonly Dictionary<string, (Func<object?> Sync, Func<Task<object?>> OnTask, Func<Task<object?>> OnValueTask)[]> Cases = new()
    {
        ["ShouldBeSome(Option<T>)"] =
        [
            (() => Option.Some(Secret).ShouldBeSome(), async () => await Later(Option.Some(Secret)).ShouldBeSome(), async () => await LaterValue(Option.Some(Secret)).ShouldBeSome()),
            (() => Option.None<string>().ShouldBeSome(), async () => await Later(Option.None<string>()).ShouldBeSome(), async () => await LaterValue(Option.None<string>()).ShouldBeSome()),
        ],
        ["ShouldBeNone(Option<T>)"] =
        [
            (Void(() => Option.None<string>().ShouldBeNone()), Void(() => Later(Option.None<string>()).ShouldBeNone()), Void(() => LaterValue(Option.None<string>()).ShouldBeNone())),
            (Void(() => Option.Some(Secret).ShouldBeNone()), Void(() => Later(Option.Some(Secret)).ShouldBeNone()), Void(() => LaterValue(Option.Some(Secret)).ShouldBeNone())),
        ],
        ["ShouldBeSuccess(Result<T, TError>)"] =
        [
            (() => Success.ShouldBeSuccess(), async () => await Later(Success).ShouldBeSuccess(), async () => await LaterValue(Success).ShouldBeSuccess()),
            (() => Error.ShouldBeSuccess(), async () => await Later(Error).ShouldBeSuccess(), async () => await LaterValue(Error).ShouldBeSuccess()),
            (() => default(Result<string, Fault>).ShouldBeSuccess(), async () => await Later(default(Result<string, Fault>)).ShouldBeSuccess(), async () => await LaterValue(default(Result<string, Fault>)).ShouldBeSuccess()),
        ],
        ["ShouldBeError(Result<T, TError>)"] =
        [
            (() => Error.ShouldBeError(), async () => await Later(Error).ShouldBeError(), async () => await LaterValue(Error).ShouldBeError()),
            (() => Success.ShouldBeError(), async () => await Later(Success).ShouldBeError(), async () => await LaterValue(Success).ShouldBeError()),
        ],
        ["ShouldBeError(Result<T, TError>, TError)"] =
        [
            (Void(() => Error.ShouldBeError(Fault.NotFound)), Void(() => Later(Error).ShouldBeError(Fault.NotFound)), Void(() => LaterValue(Error).ShouldBeError(Fault.NotFound))),
            (Void(() => Error.ShouldBeError(Fault.AlreadyShipped)), Void(() => Later(Error).ShouldBeError(Fault.AlreadyShipped)), Void(() => LaterValue(Error).ShouldBeError(Fault.AlreadyShipped))),
        ],
        ["ShouldBeSuccess(Result<TError>)"] =
        [
            (Void(() => Done.ShouldBeSuccess()), Void(() => Later(Done).ShouldBeSuccess()), Void(() => LaterValue(Done).ShouldBeSuccess())),
            (Void(() => Refused.ShouldBeSuccess()), Void(() => Later(Refused).ShouldBeSuccess()), Void(() => LaterValue(Refused).ShouldBeSuccess())),
        ],
        ["ShouldBeError(Result<TError>)"] =
        [
            (() => Refused.ShouldBeError(), async () => await Later(Refused).ShouldBeError(), async () => await LaterValue(Refused).ShouldBeError()),
            (() => Done.ShouldBeError(), async () => await Later(Done).ShouldBeError(), async () => await LaterValue(Done).ShouldBeError()),
        ],
        ["ShouldBeError(Result<TError>, TError)"] =
        [
            (Void(() => Refused.ShouldBeError(Fault.AlreadyShipped)), Void(() => Later(Refused).ShouldBeError(Fault.AlreadyShipped)), Void(() => LaterValue(Refused).ShouldBeError(Fault.AlreadyShipped))),
            (Void(() => Refused.ShouldBeError(Fault.NotFound)), Void(() => Later(Refused).ShouldBeError(Fault.NotFound)), Void(() => LaterValue(Refused).ShouldBeError(Fault.NotFound))),
        ],
        ["ShouldBeValue(Patch<T>)"] =
        [
            (() => Patch.Value(Secret).ShouldBeValue(), async () => await Later(Patch.Value(Secret)).ShouldBeValue(), async () => await LaterValue(Patch.Value(Secret)).ShouldBeValue()),
            (() => Patch.Null<string>().ShouldBeValue(), async () => await Later(Patch.Null<string>()).ShouldBeValue(), async () => await LaterValue(Patch.Null<string>()).ShouldBeValue()),
        ],
        ["ShouldBeNull(Patch<T>)"] =
        [
            (Void(() => Patch.Null<string>().ShouldBeNull()), Void(() => Later(Patch.Null<string>()).ShouldBeNull()), Void(() => LaterValue(Patch.Null<string>()).ShouldBeNull())),
            (Void(() => Patch.Value(Secret).ShouldBeNull()), Void(() => Later(Patch.Value(Secret)).ShouldBeNull()), Void(() => LaterValue(Patch.Value(Secret)).ShouldBeNull())),
        ],
        ["ShouldBeAbsent(Patch<T>)"] =
        [
            (Void(() => Patch.Absent<string>().ShouldBeAbsent()), Void(() => Later(Patch.Absent<string>()).ShouldBeAbsent()), Void(() => LaterValue(Patch.Absent<string>()).ShouldBeAbsent())),
            (Void(() => Patch.Null<string>().ShouldBeAbsent()), Void(() => Later(Patch.Null<string>()).ShouldBeAbsent()), Void(() => LaterValue(Patch.Null<string>()).ShouldBeAbsent())),
        ],
        ["ShouldHold(Fault<T1, T2>, T1)"] =
        [
            (() => Pair.ShouldHold(Fault.NotFound), async () => await Later(Pair).ShouldHold(Fault.NotFound), async () => await LaterValue(Pair).ShouldHold(Fault.NotFound)),
            (() => Pair.ShouldHold(Fault.AlreadyShipped), async () => await Later(Pair).ShouldHold(Fault.AlreadyShipped), async () => await LaterValue(Pair).ShouldHold(Fault.AlreadyShipped)),
        ],
        ["ShouldHold(Fault<T1, T2>, T2)"] =
        [
            (() => Pair.ShouldHold(Other.Conflict), async () => await Later(Pair).ShouldHold(Other.Conflict), async () => await LaterValue(Pair).ShouldHold(Other.Conflict)),
        ],
        ["ShouldHold(Fault<T1, T2, T3>, T1)"] =
        [
            (() => Triple.ShouldHold(Fault.NotFound), async () => await Later(Triple).ShouldHold(Fault.NotFound), async () => await LaterValue(Triple).ShouldHold(Fault.NotFound)),
        ],
        ["ShouldHold(Fault<T1, T2, T3>, T2)"] =
        [
            (() => Triple.ShouldHold(Other.Conflict), async () => await Later(Triple).ShouldHold(Other.Conflict), async () => await LaterValue(Triple).ShouldHold(Other.Conflict)),
        ],
        ["ShouldHold(Fault<T1, T2, T3>, T3)"] =
        [
            (() => Triple.ShouldHold(Third.Gone), async () => await Later(Triple).ShouldHold(Third.Gone), async () => await LaterValue(Triple).ShouldHold(Third.Gone)),
        ],
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Each_form_answers_as_the_synchronous_assertion_does(string assertion)
    {
        foreach (var (sync, onTask, onValueTask) in Cases[assertion])
        {
            var expected = Outcome(sync);

            (await OutcomeAsync(onTask)).ShouldBe(expected, $"{assertion} on a Task");
            (await OutcomeAsync(onValueTask)).ShouldBe(expected, $"{assertion} on a ValueTask");
        }
    }

    /// <summary>A task that faults failed the code under test, not the assertion: its exception comes out as it was thrown.</summary>
    [Fact]
    public async Task A_faulted_task_s_exception_propagates_as_it_is()
    {
        var thrown = new InvalidOperationException("the store is down");

        async Task<Result<string, Fault>> Faults() { await Task.Yield(); throw thrown; }
        async ValueTask<Option<string>> FaultsValue() { await Task.Yield(); throw thrown; }

        (await Should.ThrowAsync<InvalidOperationException>(() => Faults().ShouldBeSuccess())).ShouldBeSameAs(thrown);
        (await Should.ThrowAsync<InvalidOperationException>(() => Faults().ShouldBeError(Fault.NotFound))).ShouldBeSameAs(thrown);
        (await Should.ThrowAsync<InvalidOperationException>(async () => await FaultsValue().ShouldBeSome())).ShouldBeSameAs(thrown);

        var cancelled = Task.FromCanceled<Option<string>>(new CancellationToken(canceled: true));
        await Should.ThrowAsync<TaskCanceledException>(() => cancelled.ShouldBeNone());
    }

    [Fact]
    public async Task A_null_task_is_refused_by_name()
    {
        (await Should.ThrowAsync<ArgumentNullException>(() => ((Task<Option<string>>)null!).ShouldBeSome())).ParamName.ShouldBe("option");
        (await Should.ThrowAsync<ArgumentNullException>(() => ((Task<Result<string, Fault>>)null!).ShouldBeSuccess())).ParamName.ShouldBe("result");
        (await Should.ThrowAsync<ArgumentNullException>(() => ((Task<Patch<string>>)null!).ShouldBeAbsent())).ParamName.ShouldBe("patch");
        (await Should.ThrowAsync<ArgumentNullException>(() => ((Task<Fault<Fault, Other>>)null!).ShouldHold(Other.Conflict))).ParamName.ShouldBe("fault");
    }

    /// <summary>
    /// Every public assertion on a value has a case here, a form on a <c>Task</c> and one on a
    /// <c>ValueTask</c> of the value, with its other parameters, and nothing else is public.
    /// </summary>
    [Fact]
    public void Every_assertion_has_a_Task_and_a_ValueTask_form()
    {
        var methods = (from type in typeof(TypeKitAssertionException).Assembly.GetExportedTypes()
                       from method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                       select method).ToList();

        var synchronous = methods.Where(method => Pending(method.GetParameters()[0].ParameterType) is null).ToList();
        var keys = synchronous.Select(Key).ToList();

        keys.Count.ShouldBe(16);
        keys.Except(Cases.Keys).ShouldBeEmpty("These assertions have no case here.");
        Cases.Keys.Except(keys).ShouldBeEmpty("These cases name no assertion.");

        foreach (var wrapper in new[] { typeof(Task<>), typeof(ValueTask<>) })
        {
            var forms = methods.Where(method => Pending(method.GetParameters()[0].ParameterType) == wrapper).ToList();
            forms.Select(form => Key(form)).Order(StringComparer.Ordinal).ShouldBe(keys.Order(StringComparer.Ordinal), $"each assertion has one form on a {wrapper.Name}");

            foreach (var form in forms)
            {
                var twin = synchronous.Single(method => Key(method) == Key(form));
                form.DeclaringType.ShouldBe(twin.DeclaringType, $"{Key(form)} sits beside its synchronous form");
                Describe(form.ReturnType).ShouldBe(twin.ReturnType == typeof(void) ? "Task" : $"Task<{Describe(twin.ReturnType)}>", Key(form));
            }
        }
    }

    private static Type? Pending(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? type.GetGenericTypeDefinition()
            : null;

    private static string Key(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => Describe(Pending(p.ParameterType) is null || p.Position > 0 ? p.ParameterType : p.ParameterType.GetGenericArguments()[0])))})";

    private static string Describe(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>" : type.Name;

    private static string Outcome(Func<object?> call)
    {
        try
        {
            return $"passes: {Show(call())}";
        }
        catch (TypeKitAssertionException failure)
        {
            return $"fails: {failure.Message}";
        }
    }

    private static async Task<string> OutcomeAsync(Func<Task<object?>> call)
    {
        try
        {
            return $"passes: {Show(await call())}";
        }
        catch (TypeKitAssertionException failure)
        {
            return $"fails: {failure.Message}";
        }
    }

    private static string Show(object? value) => value switch
    {
        null => "nothing",
        Fault<Fault> held => $"{held.Reason} {held.Detail}",
        Fault<Other> held => $"{held.Reason} {held.Detail}",
        Fault<Third> held => $"{held.Reason} {held.Detail}",
        _ => value.ToString()!,
    };

    private static Func<object?> Void(Action call) => () => { call(); return null; };

    private static Func<Task<object?>> Void(Func<Task> call) => async () => { await call(); return null; };
}
