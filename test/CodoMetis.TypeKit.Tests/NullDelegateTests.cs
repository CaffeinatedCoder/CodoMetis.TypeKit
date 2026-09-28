using System.Reflection;
using System.Runtime.CompilerServices;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// A null delegate is an <see cref="ArgumentNullException"/> naming the parameter, whichever branch
/// the receiver is on.
/// </summary>
/// <remarks>
/// <para>
/// Checked only where it was called, a null for the branch not taken passed: <c>Match(null, …)</c>
/// on every error, <c>Zip(…, null)</c> on every <c>None</c>, until the other outcome first
/// arrived, typically in production. Every case therefore runs on both branches.
/// </para>
/// <para>
/// The cases are written by hand, so the completeness test holds them to every public method of the
/// assembly that takes a delegate: a new one fails it until it has a case here.
/// </para>
/// </remarks>
public sealed class NullDelegateTests
{
    private static readonly Option<int> Some = Option.Some(1);
    private static readonly Option<int> None = Option.None<int>();

    private static readonly Result<string> Ok     = Result<string>.Success();
    private static readonly Result<string> Failed = Result<string>.Error("e");

    private static readonly Result<int, string> Value = Result<int, string>.Success(1);
    private static readonly Result<int, string> Error = Result<int, string>.Error("e");

    // Fresh tasks per call: the continuations of a pending result are checked the same way.
    private static Task<Result<string>> PendingOk     => Task.FromResult(Ok);
    private static Task<Result<string>> PendingFailed => Task.FromResult(Failed);

    private static Task<Result<int, string>> PendingValue => Task.FromResult(Value);
    private static Task<Result<int, string>> PendingError => Task.FromResult(Error);

    // A pending result that fails: only a delegate checked before the task is awaited reports itself
    // rather than the task's own exception.
    private static Task<Result<int, string>> PendingFault        => Task.FromException<Result<int, string>>(new TimeoutException());
    private static Task<Result<string>>      PendingCommandFault => Task.FromException<Result<string>>(new TimeoutException());

    private static readonly Dictionary<string, Func<object?>[]> Cases = new()
    {
        ["Option<T>.Match(Func<T, TResult>, Func<TResult>): onSome"]  = [() => Some.Match(Null<Func<int, int>>(), () => 0), () => None.Match(Null<Func<int, int>>(), () => 0)],
        ["Option<T>.Match(Func<T, TResult>, Func<TResult>): onNone"]  = [() => Some.Match(x => x, Null<Func<int>>()), () => None.Match(x => x, Null<Func<int>>())],
        ["Option<T>.Bind(Func<T, Option<TResult>>): selector"]              = [() => Some.Bind(Null<Func<int, Option<int>>>()), () => None.Bind(Null<Func<int, Option<int>>>())],
        ["Option<T>.Map(Func<T, TResult>): selector"]                      = [() => Some.Map(Null<Func<int, int>>()), () => None.Map(Null<Func<int, int>>())],
        ["Option<T>.Tap(Action<T>): action"]                          = [() => Some.Tap(Null<Action<int>>()), () => None.Tap(Null<Action<int>>())],
        ["Option<T>.Filter(Func<T, Boolean>): predicate"]                 = [() => Some.Filter(Null<Func<int, bool>>()), () => None.Filter(Null<Func<int, bool>>())],

        ["Option.Select(Option<T>, Func<T, TResult>): selector"]               = [() => Some.Select(Null<Func<int, int>>()), () => None.Select(Null<Func<int, int>>())],
        ["Option.SelectMany(Option<T>, Func<T, Option<TResult>>): selector"]   = [() => Some.SelectMany(Null<Func<int, Option<int>>>()), () => None.SelectMany(Null<Func<int, Option<int>>>())],
        ["Option.Where(Option<T>, Func<T, Boolean>): predicate"]              = [() => Some.Where(Null<Func<int, bool>>()), () => None.Where(Null<Func<int, bool>>())],
        ["Option.Zip(Option<T>, Option<T2>, Func<T, T2, TResult>): selector"] =
            [() => Some.Zip(Some, Null<Func<int, int, int>>()), () => None.Zip(Some, Null<Func<int, int, int>>())],
        ["Option.Zip(Option<T>, Option<T2>, Option<T3>, Func<T, T2, T3, TResult>): selector"] =
            [() => Some.Zip(Some, Some, Null<Func<int, int, int, int>>()), () => None.Zip(Some, Some, Null<Func<int, int, int, int>>())],
        ["Option.Zip(Option<T>, Option<T2>, Option<T3>, Option<T4>, Func<T, T2, T3, T4, TResult>): selector"] =
            [() => Some.Zip(Some, Some, Some, Null<Func<int, int, int, int, int>>()), () => None.Zip(Some, Some, Some, Null<Func<int, int, int, int, int>>())],
        ["Option.Zip(Option<T>, Option<T2>, Option<T3>, Option<T4>, Option<T5>, Func<T, T2, T3, T4, T5, TResult>): selector"] =
            [() => Some.Zip(Some, Some, Some, Some, Null<Func<int, int, int, int, int, int>>()), () => None.Zip(Some, Some, Some, Some, Null<Func<int, int, int, int, int, int>>())],
        ["Option.Zip(Option<T>, Option<T2>, Option<T3>, Option<T4>, Option<T5>, Option<T6>, Func<T, T2, T3, T4, T5, T6, TResult>): selector"] =
            [() => Some.Zip(Some, Some, Some, Some, Some, Null<Func<int, int, int, int, int, int, int>>()), () => None.Zip(Some, Some, Some, Some, Some, Null<Func<int, int, int, int, int, int, int>>())],
        ["Option.FirstOrNone(IEnumerable<T>, Func<T, Boolean>): predicate"] = [() => new[] { 1 }.FirstOrNone(Null<Func<int, bool>>()), () => Array.Empty<int>().FirstOrNone(Null<Func<int, bool>>())],
        ["Option.LastOrNone(IEnumerable<T>, Func<T, Boolean>): predicate"]  = [() => new[] { 1 }.LastOrNone(Null<Func<int, bool>>()), () => Array.Empty<int>().LastOrNone(Null<Func<int, bool>>())],

        ["Result<TError>.Match(Func<TResult>, Func<TError, TResult>): onSuccess"] = [() => Ok.Match(Null<Func<int>>(), _ => 0), () => Failed.Match(Null<Func<int>>(), _ => 0)],
        ["Result<TError>.Match(Func<TResult>, Func<TError, TResult>): onError"]   = [() => Ok.Match(() => 0, Null<Func<string, int>>()), () => Failed.Match(() => 0, Null<Func<string, int>>())],
        ["Result<TError>.Match(Func<TResult>, Func<TResult>): onSuccess"]         = [() => Ok.Match(Null<Func<int>>(), () => 0), () => Failed.Match(Null<Func<int>>(), () => 0)],
        ["Result<TError>.Match(Func<TResult>, Func<TResult>): onError"]           = [() => Ok.Match(() => 0, Null<Func<int>>()), () => Failed.Match(() => 0, Null<Func<int>>())],
        ["Result<TError>.Map(Func<TResult>): selector"]                                 = [() => Ok.Map(Null<Func<int>>()), () => Failed.Map(Null<Func<int>>())],
        ["Result<TError>.Bind(Func<Result<TError>>): selector"]                         = [() => Ok.Bind(Null<Func<Result<string>>>()), () => Failed.Bind(Null<Func<Result<string>>>())],
        ["Result<TError>.MapError(Func<TError, TNewError>): selector"]         = [() => Ok.MapError(Null<Func<string, int>>()), () => Failed.MapError(Null<Func<string, int>>())],
        ["Result<TError>.Tap(Action): action"]                                    = [() => Ok.Tap(Null<Action>()), () => Failed.Tap(Null<Action>())],
        ["Result<TError>.TapAsync(Func<Task>): action"]                           = [() => Ok.TapAsync(Null<Func<Task>>()), () => Failed.TapAsync(Null<Func<Task>>())],

        ["Result<T, TError>.Match(Func<T, TResult>, Func<TError, TResult>): onSuccess"]          = [() => Value.Match(Null<Func<int, int>>(), _ => 0), () => Error.Match(Null<Func<int, int>>(), _ => 0)],
        ["Result<T, TError>.Match(Func<T, TResult>, Func<TError, TResult>): onError"]            = [() => Value.Match(x => x, Null<Func<string, int>>()), () => Error.Match(x => x, Null<Func<string, int>>())],
        ["Result<T, TError>.Match(Func<T, TResult>, Func<TResult>): onSuccess"]                         = [() => Value.Match(Null<Func<int, int>>(), () => 0), () => Error.Match(Null<Func<int, int>>(), () => 0)],
        ["Result<T, TError>.Match(Func<T, TResult>, Func<TResult>): onError"]            = [() => Value.Match(x => x, Null<Func<int>>()), () => Error.Match(x => x, Null<Func<int>>())],
        ["Result<T, TError>.Map(Func<T, TResult>): selector"]                                          = [() => Value.Map(Null<Func<int, int>>()), () => Error.Map(Null<Func<int, int>>())],
        ["Result<T, TError>.Bind(Func<T, Result<TResult, TError>>): selector"]                         = [() => Value.Bind(Null<Func<int, Result<int, string>>>()), () => Error.Bind(Null<Func<int, Result<int, string>>>())],
        ["Result<T, TError>.Bind(Func<T, Result<TError>>): selector"]                          = [() => Value.Bind(Null<Func<int, Result<string>>>()), () => Error.Bind(Null<Func<int, Result<string>>>())],
        ["Result<T, TError>.MapError(Func<TError, TNewError>): selector"]                      = [() => Value.MapError(Null<Func<string, int>>()), () => Error.MapError(Null<Func<string, int>>())],
        ["Result<T, TError>.Tap(Action<T>): action"]                                             = [() => Value.Tap(Null<Action<int>>()), () => Error.Tap(Null<Action<int>>())],
        ["Result<T, TError>.TapAsync(Func<T, Task>): action"]                                    = [() => Value.TapAsync(Null<Func<int, Task>>()), () => Error.TapAsync(Null<Func<int, Task>>())],

        ["Result<TError>.MapAsync(Func<Task<TResult>>): selector"]                = [() => Ok.MapAsync(Null<Func<Task<int>>>()), () => Failed.MapAsync(Null<Func<Task<int>>>())],
        ["Result<TError>.BindAsync(Func<Task<Result<TError>>>): selector"]        = [() => Ok.BindAsync(Null<Func<Task<Result<string>>>>()), () => Failed.BindAsync(Null<Func<Task<Result<string>>>>())],

        ["Result<T, TError>.MapAsync(Func<T, Task<TResult>>): selector"]                 = [() => Value.MapAsync(Null<Func<int, Task<int>>>()), () => Error.MapAsync(Null<Func<int, Task<int>>>())],
        ["Result<T, TError>.BindAsync(Func<T, Task<Result<TResult, TError>>>): selector"] =
            [() => Value.BindAsync(Null<Func<int, Task<Result<int, string>>>>()), () => Error.BindAsync(Null<Func<int, Task<Result<int, string>>>>())],
        ["Result<T, TError>.BindAsync(Func<T, Task<Result<TError>>>): selector"]         =
            [() => Value.BindAsync(Null<Func<int, Task<Result<string>>>>()), () => Error.BindAsync(Null<Func<int, Task<Result<string>>>>())],

        ["Result.MapAsync(Task<Result<T, TError>>, Func<T, TResult>): selector"]       = [() => PendingValue.MapAsync(Null<Func<int, int>>()), () => PendingError.MapAsync(Null<Func<int, int>>()), () => PendingFault.MapAsync(Null<Func<int, int>>())],
        ["Result.MapAsync(Task<Result<T, TError>>, Func<T, Task<TResult>>): selector"] = [() => PendingValue.MapAsync(Null<Func<int, Task<int>>>()), () => PendingError.MapAsync(Null<Func<int, Task<int>>>()), () => PendingFault.MapAsync(Null<Func<int, Task<int>>>())],
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Result<TResult, TError>>): selector"] =
            [() => PendingValue.BindAsync(Null<Func<int, Result<int, string>>>()), () => PendingError.BindAsync(Null<Func<int, Result<int, string>>>()), () => PendingFault.BindAsync(Null<Func<int, Result<int, string>>>())],
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Task<Result<TResult, TError>>>): selector"] =
            [() => PendingValue.BindAsync(Null<Func<int, Task<Result<int, string>>>>()), () => PendingError.BindAsync(Null<Func<int, Task<Result<int, string>>>>()), () => PendingFault.BindAsync(Null<Func<int, Task<Result<int, string>>>>())],
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Result<TError>>): selector"] =
            [() => PendingValue.BindAsync(Null<Func<int, Result<string>>>()), () => PendingError.BindAsync(Null<Func<int, Result<string>>>()), () => PendingFault.BindAsync(Null<Func<int, Result<string>>>())],
        ["Result.BindAsync(Task<Result<T, TError>>, Func<T, Task<Result<TError>>>): selector"] =
            [() => PendingValue.BindAsync(Null<Func<int, Task<Result<string>>>>()), () => PendingError.BindAsync(Null<Func<int, Task<Result<string>>>>()), () => PendingFault.BindAsync(Null<Func<int, Task<Result<string>>>>())],
        ["Result.MapErrorAsync(Task<Result<T, TError>>, Func<TError, TNewError>): selector"] =
            [() => PendingValue.MapErrorAsync(Null<Func<string, int>>()), () => PendingError.MapErrorAsync(Null<Func<string, int>>()), () => PendingFault.MapErrorAsync(Null<Func<string, int>>())],
        ["Result.TapAsync(Task<Result<T, TError>>, Action<T>): action"]     = [() => PendingValue.TapAsync(Null<Action<int>>()), () => PendingError.TapAsync(Null<Action<int>>()), () => PendingFault.TapAsync(Null<Action<int>>())],
        ["Result.TapAsync(Task<Result<T, TError>>, Func<T, Task>): action"] = [() => PendingValue.TapAsync(Null<Func<int, Task>>()), () => PendingError.TapAsync(Null<Func<int, Task>>()), () => PendingFault.TapAsync(Null<Func<int, Task>>())],

        ["Result.MapAsync(Task<Result<TError>>, Func<TResult>): selector"]       = [() => PendingOk.MapAsync(Null<Func<int>>()), () => PendingFailed.MapAsync(Null<Func<int>>()), () => PendingCommandFault.MapAsync(Null<Func<int>>())],
        ["Result.MapAsync(Task<Result<TError>>, Func<Task<TResult>>): selector"] = [() => PendingOk.MapAsync(Null<Func<Task<int>>>()), () => PendingFailed.MapAsync(Null<Func<Task<int>>>()), () => PendingCommandFault.MapAsync(Null<Func<Task<int>>>())],
        ["Result.BindAsync(Task<Result<TError>>, Func<Result<TError>>): selector"] =
            [() => PendingOk.BindAsync(Null<Func<Result<string>>>()), () => PendingFailed.BindAsync(Null<Func<Result<string>>>()), () => PendingCommandFault.BindAsync(Null<Func<Result<string>>>())],
        ["Result.BindAsync(Task<Result<TError>>, Func<Task<Result<TError>>>): selector"] =
            [() => PendingOk.BindAsync(Null<Func<Task<Result<string>>>>()), () => PendingFailed.BindAsync(Null<Func<Task<Result<string>>>>()), () => PendingCommandFault.BindAsync(Null<Func<Task<Result<string>>>>())],
        ["Result.MapErrorAsync(Task<Result<TError>>, Func<TError, TNewError>): selector"] =
            [() => PendingOk.MapErrorAsync(Null<Func<string, int>>()), () => PendingFailed.MapErrorAsync(Null<Func<string, int>>()), () => PendingCommandFault.MapErrorAsync(Null<Func<string, int>>())],
        ["Result.TapAsync(Task<Result<TError>>, Action): action"]     = [() => PendingOk.TapAsync(Null<Action>()), () => PendingFailed.TapAsync(Null<Action>()), () => PendingCommandFault.TapAsync(Null<Action>())],
        ["Result.TapAsync(Task<Result<TError>>, Func<Task>): action"] = [() => PendingOk.TapAsync(Null<Func<Task>>()), () => PendingFailed.TapAsync(Null<Func<Task>>()), () => PendingCommandFault.TapAsync(Null<Func<Task>>())],

        ["Result.Zip(Result<T, TError>, Result<T2, TError>, Func<T, T2, TResult>): selector"] =
            [() => Value.Zip(Value, Null<Func<int, int, int>>()), () => Error.Zip(Value, Null<Func<int, int, int>>())],
        ["Result.Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Func<T, T2, T3, TResult>): selector"] =
            [() => Value.Zip(Value, Value, Null<Func<int, int, int, int>>()), () => Error.Zip(Value, Value, Null<Func<int, int, int, int>>())],
        ["Result.Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Result<T4, TError>, Func<T, T2, T3, T4, TResult>): selector"] =
            [() => Value.Zip(Value, Value, Value, Null<Func<int, int, int, int, int>>()), () => Error.Zip(Value, Value, Value, Null<Func<int, int, int, int, int>>())],
        ["Result.Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Result<T4, TError>, Result<T5, TError>, Func<T, T2, T3, T4, T5, TResult>): selector"] =
            [() => Value.Zip(Value, Value, Value, Value, Null<Func<int, int, int, int, int, int>>()), () => Error.Zip(Value, Value, Value, Value, Null<Func<int, int, int, int, int, int>>())],
        ["Result.Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Result<T4, TError>, Result<T5, TError>, Result<T6, TError>, Func<T, T2, T3, T4, T5, T6, TResult>): selector"] =
            [() => Value.Zip(Value, Value, Value, Value, Value, Null<Func<int, int, int, int, int, int, int>>()), () => Error.Zip(Value, Value, Value, Value, Value, Null<Func<int, int, int, int, int, int, int>>())],

        ["Result.Traverse(IEnumerable<T>, Func<T, Result<TResult, TError>>): selector"] =
            [() => new[] { 1 }.Traverse(Null<Func<int, Result<int, string>>>()), () => Array.Empty<int>().Traverse(Null<Func<int, Result<int, string>>>())],
        ["Result.Traverse(IEnumerable<T>, Func<T, Result<TError>>): selector"] =
            [() => new[] { 1 }.Traverse(Null<Func<int, Result<string>>>()), () => Array.Empty<int>().Traverse(Null<Func<int, Result<string>>>())],

        ["Result.Select(Result<T, TError>, Func<T, TResult>): selector"]                = [() => Value.Select(Null<Func<int, int>>()), () => Error.Select(Null<Func<int, int>>())],
        ["Result.FirstOrError(IEnumerable<T>, Func<T, Boolean>, TError): predicate"] =
            [() => new[] { 1 }.FirstOrError(Null<Func<int, bool>>(), "e"), () => Array.Empty<int>().FirstOrError(Null<Func<int, bool>>(), "e")],
        ["Result.LastOrError(IEnumerable<T>, Func<T, Boolean>, TError): predicate"] =
            [() => new[] { 1 }.LastOrError(Null<Func<int, bool>>(), "e"), () => Array.Empty<int>().LastOrError(Null<Func<int, bool>>(), "e")],
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task A_null_delegate_is_refused_on_either_branch(string member)
    {
        var parameter = member[(member.LastIndexOf(": ", StringComparison.Ordinal) + 2)..];

        foreach (var (call, branch) in Cases[member].Select((call, branch) => (call, branch)))
        {
            var exception = await Record.ExceptionAsync(async () =>
            {
                if (call() is Task task) await task;
            });

            exception.ShouldBeOfType<ArgumentNullException>($"{member}, branch {branch}, accepted a null delegate")
                     .ParamName.ShouldBe(parameter, $"{member}, branch {branch}");
        }
    }

    [Fact]
    public void Every_delegate_parameter_of_the_assembly_has_a_case()
    {
        var parameters = DelegateParameters().ToList();

        parameters.Count.ShouldBeGreaterThanOrEqualTo(Cases.Count);
        parameters.Except(Cases.Keys).ShouldBeEmpty("These public methods take a delegate and have no null case here.");
        Cases.Keys.Except(parameters).ShouldBeEmpty("These cases name no public method of the assembly.");
    }

    private static IEnumerable<string> DelegateParameters() =>
        from type in typeof(Option<>).Assembly.GetExportedTypes()
        // The compiler's grouping types for extension blocks carry skeleton members; the callable
        // methods are the static ones on the enclosing class.
        where !type.Name.Contains('<') && type.GetCustomAttribute<CompilerGeneratedAttribute>() is null
        from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        from parameter in method.GetParameters()
        where typeof(Delegate).IsAssignableFrom(parameter.ParameterType)
        select $"{Describe(type)}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => Describe(p.ParameterType)))}): {parameter.Name}";

    private static string Describe(Type type) =>
        type.IsByRef ? Describe(type.GetElementType()!)
      : type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>"
      : type.Name;

    private static T Null<T>() where T : class => null!;
}
