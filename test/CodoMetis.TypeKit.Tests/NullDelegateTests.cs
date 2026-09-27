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
/// on every error, <c>ToResult(null, …)</c> on every <c>None</c>, until the other outcome first
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

    private static readonly Dictionary<string, Func<object?>[]> Cases = new()
    {
        ["Option<T>.Match(Func<T, TResult>, Func<TResult>): fnSome"]  = [() => Some.Match(Null<Func<int, int>>(), () => 0), () => None.Match(Null<Func<int, int>>(), () => 0)],
        ["Option<T>.Match(Func<T, TResult>, Func<TResult>): fnNone"]  = [() => Some.Match(x => x, Null<Func<int>>()), () => None.Match(x => x, Null<Func<int>>())],
        ["Option<T>.Bind(Func<T, Option<TResult>>): fn"]              = [() => Some.Bind(Null<Func<int, Option<int>>>()), () => None.Bind(Null<Func<int, Option<int>>>())],
        ["Option<T>.Map(Func<T, TResult>): map"]                      = [() => Some.Map(Null<Func<int, int>>()), () => None.Map(Null<Func<int, int>>())],
        ["Option<T>.Tap(Action<T>): action"]                          = [() => Some.Tap(Null<Action<int>>()), () => None.Tap(Null<Action<int>>())],
        ["Option<T>.Filter(Func<T, Boolean>): check"]                 = [() => Some.Filter(Null<Func<int, bool>>()), () => None.Filter(Null<Func<int, bool>>())],

        ["Option.ToResult(Option<T>, Func<T, TSuccess>, TError): onSuccess"] =
            [() => Some.ToResult(Null<Func<int, int>>(), "e"), () => None.ToResult(Null<Func<int, int>>(), "e")],
        ["Option.Select(Option<T>, Func<T, TResult>): map"]               = [() => Some.Select(Null<Func<int, int>>()), () => None.Select(Null<Func<int, int>>())],
        ["Option.SelectMany(Option<T>, Func<T, Option<TResult>>): map"]   = [() => Some.SelectMany(Null<Func<int, Option<int>>>()), () => None.SelectMany(Null<Func<int, Option<int>>>())],
        ["Option.Where(Option<T>, Func<T, Boolean>): check"]              = [() => Some.Where(Null<Func<int, bool>>()), () => None.Where(Null<Func<int, bool>>())],
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
        ["Result<TError>.Map(Func<TResult>): fn"]                                 = [() => Ok.Map(Null<Func<int>>()), () => Failed.Map(Null<Func<int>>())],
        ["Result<TError>.Bind(Func<Result<TError>>): fn"]                         = [() => Ok.Bind(Null<Func<Result<string>>>()), () => Failed.Bind(Null<Func<Result<string>>>())],
        ["Result<TError>.Tap(Action): action"]                                    = [() => Ok.Tap(Null<Action>()), () => Failed.Tap(Null<Action>())],
        ["Result<TError>.TapAsync(Func<Task>): action"]                           = [() => Ok.TapAsync(Null<Func<Task>>()), () => Failed.TapAsync(Null<Func<Task>>())],

        ["Result<T, TError>.Match(Func<T, TResult>, Func<TError, TResult>): onSuccess"]          = [() => Value.Match(Null<Func<int, int>>(), _ => 0), () => Error.Match(Null<Func<int, int>>(), _ => 0)],
        ["Result<T, TError>.Match(Func<T, TResult>, Func<TError, TResult>): onError"]            = [() => Value.Match(x => x, Null<Func<string, int>>()), () => Error.Match(x => x, Null<Func<string, int>>())],
        ["Result<T, TError>.Match(Func<T, Success<TResult>>, Func<TError, TResult>): onSuccess"] = [() => Value.Match(Null<Func<int, Success<int>>>(), _ => 0), () => Error.Match(Null<Func<int, Success<int>>>(), _ => 0)],
        ["Result<T, TError>.Match(Func<T, Success<TResult>>, Func<TError, TResult>): onError"]   = [() => Value.Match(x => Result.Ok(x), Null<Func<string, int>>()), () => Error.Match(x => Result.Ok(x), Null<Func<string, int>>())],
        ["Result<T, TError>.Match(Func<T, TResult>, Func<TResult>): fn"]                         = [() => Value.Match(Null<Func<int, int>>(), () => 0), () => Error.Match(Null<Func<int, int>>(), () => 0)],
        ["Result<T, TError>.Match(Func<T, TResult>, Func<TResult>): defaultProvider"]            = [() => Value.Match(x => x, Null<Func<int>>()), () => Error.Match(x => x, Null<Func<int>>())],
        ["Result<T, TError>.Match(Func<T, Success>, Func<TError, TError>): onSuccess"]           = [() => Value.Match(Null<Func<int, Success>>(), e => e), () => Error.Match(Null<Func<int, Success>>(), e => e)],
        ["Result<T, TError>.Match(Func<T, Success>, Func<TError, TError>): onError"]             = [() => Value.Match(_ => Result.Ok(), Null<Func<string, string>>()), () => Error.Match(_ => Result.Ok(), Null<Func<string, string>>())],
        ["Result<T, TError>.Map(Func<T, TResult>): fn"]                                          = [() => Value.Map(Null<Func<int, int>>()), () => Error.Map(Null<Func<int, int>>())],
        ["Result<T, TError>.Bind(Func<T, Result<TResult, TError>>): fn"]                         = [() => Value.Bind(Null<Func<int, Result<int, string>>>()), () => Error.Bind(Null<Func<int, Result<int, string>>>())],
        ["Result<T, TError>.Bind(Func<T, Success<TResult>>): fn"]                                = [() => Value.Bind(Null<Func<int, Success<int>>>()), () => Error.Bind(Null<Func<int, Success<int>>>())],
        ["Result<T, TError>.Tap(Action<T>): action"]                                             = [() => Value.Tap(Null<Action<int>>()), () => Error.Tap(Null<Action<int>>())],
        ["Result<T, TError>.TapAsync(Func<T, Task>): action"]                                    = [() => Value.TapAsync(Null<Func<int, Task>>()), () => Error.TapAsync(Null<Func<int, Task>>())],

        ["Result.Select(Result<T, TError>, Func<T, TResult>): fn"]                = [() => Value.Select(Null<Func<int, int>>()), () => Error.Select(Null<Func<int, int>>())],
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
