using System.Reflection;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// A <c>default</c> result is <see cref="ResultState.Uninitialized"/>: neither a success nor an
/// error. Every member that would pick a branch throws instead.
/// </summary>
/// <remarks>
/// <para>
/// The analyzer forbids writing <c>default</c> of a result, but array elements, unassigned fields and
/// reflection still produce one. Taking the error branch would hand the caller a
/// <c>default(TError)</c> that was never produced: for an enum fault its first member, a plausible
/// wrong reason, and for a reference type <see langword="null"/> despite <c>[NotNullWhen]</c>. Taking
/// the success branch would be worse.
/// </para>
/// <para>
/// The member lists are written by hand, so the completeness tests hold them to the types' actual
/// public surface: a new member fails them until it is classified as branching (and gets a case
/// here) or not.
/// </para>
/// </remarks>
public sealed class UninitializedResultTests
{
    /// <summary>Members that neither pick a branch nor expose content: construction, state, equality, text.</summary>
    private static readonly string[] NonBranchingMembers =
        ["get_State", "Success", "Error", "ToString", "GetHashCode", "Equals", "op_Equality", "op_Inequality"];

    private static readonly Dictionary<string, Func<Result<string>, Task>> ErrorOnlyMembers = new()
    {
        ["Match(onSuccess, onError)"] = r => Task.FromResult(r.Match(() => 1, () => 2)),
        ["Match(onSuccess, onError -> error)"] = r => Task.FromResult(r.Match(() => 1, _ => 2)),
        ["Map(selector)"]             = r => Task.FromResult(r.Map(() => 1)),
        ["MapError(selector)"]        = r => Task.FromResult(r.MapError(e => e.Length)),
        ["Bind(selector)"]            = r => Task.FromResult(r.Bind(Result<string>.Success)),
        ["Tap(action)"]               = r => Task.FromResult(r.Tap(() => { })),
        ["TapError(action)"]          = r => Task.FromResult(r.TapError(_ => { })),
        ["TapAsync(action)"]          = r => r.TapAsync(() => Task.CompletedTask),
        ["MapAsync(selector)"]        = r => r.MapAsync(() => Task.FromResult(1)),
        ["BindAsync(selector)"]       = r => r.BindAsync(() => Task.FromResult(Result<string>.Success())),
        ["TryGetError(out error)"]    = r => Task.FromResult(r.TryGetError(out _)),
        ["op_Implicit(bool)"]         = r => Task.FromResult((bool)r),
    };

    private static readonly Dictionary<string, Func<Result<int, string>, Task>> ValuedMembers = new()
    {
        ["Match(onSuccess, onError)"]              = r => Task.FromResult(r.Match(x => x, _ => 2)),
        ["Match(onSuccess, onError -> no error)"]  = r => Task.FromResult(r.Match(x => x, () => 2)),
        ["Map(selector)"]                          = r => Task.FromResult(r.Map(x => x)),
        ["MapError(selector)"]                     = r => Task.FromResult(r.MapError(e => e.Length)),
        ["Bind(selector -> Result<T, TError>)"]    = r => Task.FromResult(r.Bind(Result<int, string>.Success)),
        ["Bind(selector -> Result<TError>)"]       = r => Task.FromResult(r.Bind(_ => Result<string>.Success())),
        ["Tap(action)"]                            = r => Task.FromResult(r.Tap(_ => { })),
        ["TapError(action)"]                       = r => Task.FromResult(r.TapError(_ => { })),
        ["Ensure(predicate, error)"]               = r => Task.FromResult(r.Ensure(_ => true, "e")),
        ["TapAsync(action)"]                       = r => r.TapAsync(_ => Task.CompletedTask),
        ["MapAsync(selector)"]                     = r => r.MapAsync(Task.FromResult),
        ["BindAsync(selector -> Result<T, TError>)"] = r => r.BindAsync(x => Task.FromResult(Result<int, string>.Success(x))),
        ["BindAsync(selector -> Result<TError>)"]  = r => r.BindAsync(_ => Task.FromResult(Result<string>.Success())),
        ["TryGetValue(out value, out error)"]      = r => Task.FromResult(r.TryGetValue(out _, out _)),
        ["AsEnumerable() on enumeration"]          = r => Task.FromResult(r.AsEnumerable().ToList()),
    };

    private static readonly Result<int, string> Value = Result<int, string>.Success(1);
    private static readonly Result<int, string> Error = Result<int, string>.Error("e");

    private static Task<Result<int, string>> PendingDefault => Task.FromResult(default(Result<int, string>));
    private static Task<Result<string>>      PendingDefaultCommand => Task.FromResult(default(Result<string>));

    /// <summary>
    /// The extensions that take a result, keyed by signature. A zip's uninitialized argument stands
    /// after an error, since short-circuiting would let it pass there.
    /// </summary>
    private static readonly Dictionary<string, Func<Task>> ExtensionMembers = new()
    {
        ["Select(Result<T, TError>, Func<T, TResult>)"] = () => Task.FromResult(default(Result<int, string>).Select(x => x)),
        ["ToOption(Result<T, TError>)"]                 = () => Task.FromResult(default(Result<int, string>).ToOption()),
        ["SelectMany(Result<T, TError>, Func<T, Result<TNext, TError>>, Func<T, TNext, TResult>)"] =
            () => Task.FromResult(default(Result<int, string>).SelectMany(_ => Value, (x, _) => x)),

        ["Zip(Result<T, TError>, Result<T2, TError>, Func<T, T2, TResult>)"] = () => Task.FromResult(Error.Zip(default(Result<int, string>), (x, _) => x)),
        ["Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Func<T, T2, T3, TResult>)"] =
            () => Task.FromResult(Error.Zip(Value, default(Result<int, string>), (x, _, _) => x)),
        ["Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Result<T4, TError>, Func<T, T2, T3, T4, TResult>)"] =
            () => Task.FromResult(Error.Zip(Value, Value, default(Result<int, string>), (x, _, _, _) => x)),
        ["Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Result<T4, TError>, Result<T5, TError>, Func<T, T2, T3, T4, T5, TResult>)"] =
            () => Task.FromResult(Error.Zip(Value, Value, Value, default(Result<int, string>), (x, _, _, _, _) => x)),
        ["Zip(Result<T, TError>, Result<T2, TError>, Result<T3, TError>, Result<T4, TError>, Result<T5, TError>, Result<T6, TError>, Func<T, T2, T3, T4, T5, T6, TResult>)"] =
            () => Task.FromResult(Error.Zip(Value, Value, Value, Value, default(Result<int, string>), (x, _, _, _, _, _) => x)),

        ["MapAsync(Task<Result<T, TError>>, Func<T, TResult>)"]                        = () => PendingDefault.MapAsync(x => x),
        ["MapAsync(Task<Result<T, TError>>, Func<T, Task<TResult>>)"]                  = () => PendingDefault.MapAsync(Task.FromResult),
        ["BindAsync(Task<Result<T, TError>>, Func<T, Result<TResult, TError>>)"]       = () => PendingDefault.BindAsync(Result<int, string>.Success),
        ["BindAsync(Task<Result<T, TError>>, Func<T, Task<Result<TResult, TError>>>)"] = () => PendingDefault.BindAsync(x => Task.FromResult(Result<int, string>.Success(x))),
        ["BindAsync(Task<Result<T, TError>>, Func<T, Result<TError>>)"]                = () => PendingDefault.BindAsync(_ => Result<string>.Success()),
        ["BindAsync(Task<Result<T, TError>>, Func<T, Task<Result<TError>>>)"]          = () => PendingDefault.BindAsync(_ => Task.FromResult(Result<string>.Success())),
        ["MapErrorAsync(Task<Result<T, TError>>, Func<TError, TNewError>)"]            = () => PendingDefault.MapErrorAsync(e => e.Length),
        ["EnsureAsync(Task<Result<T, TError>>, Func<T, Boolean>, TError)"]            = () => PendingDefault.EnsureAsync(_ => true, "e"),
        ["TapErrorAsync(Task<Result<T, TError>>, Action<TError>)"]                     = () => PendingDefault.TapErrorAsync(_ => { }),
        ["TapAsync(Task<Result<T, TError>>, Action<T>)"]                               = () => PendingDefault.TapAsync(_ => { }),
        ["TapAsync(Task<Result<T, TError>>, Func<T, Task>)"]                           = () => PendingDefault.TapAsync(_ => Task.CompletedTask),

        ["MapAsync(Task<Result<TError>>, Func<TResult>)"]                  = () => PendingDefaultCommand.MapAsync(() => 1),
        ["MapAsync(Task<Result<TError>>, Func<Task<TResult>>)"]            = () => PendingDefaultCommand.MapAsync(() => Task.FromResult(1)),
        ["BindAsync(Task<Result<TError>>, Func<Result<TError>>)"]          = () => PendingDefaultCommand.BindAsync(Result<string>.Success),
        ["BindAsync(Task<Result<TError>>, Func<Task<Result<TError>>>)"]    = () => PendingDefaultCommand.BindAsync(() => Task.FromResult(Result<string>.Success())),
        ["MapErrorAsync(Task<Result<TError>>, Func<TError, TNewError>)"]   = () => PendingDefaultCommand.MapErrorAsync(e => e.Length),
        ["TapErrorAsync(Task<Result<TError>>, Action<TError>)"]            = () => PendingDefaultCommand.TapErrorAsync(_ => { }),
        ["TapAsync(Task<Result<TError>>, Action)"]                         = () => PendingDefaultCommand.TapAsync(() => { }),
        ["TapAsync(Task<Result<TError>>, Func<Task>)"]                     = () => PendingDefaultCommand.TapAsync(() => Task.CompletedTask),

        ["Traverse(IEnumerable<T>, Func<T, Result<TResult, TError>>)"] = () => Task.FromResult(new[] { 1 }.Traverse(_ => default(Result<int, string>))),
        ["Traverse(IEnumerable<T>, Func<T, Result<TError>>)"]          = () => Task.FromResult(new[] { 1 }.Traverse(_ => default(Result<string>))),
        ["Sequence(IEnumerable<Result<T, TError>>)"]                   = () => Task.FromResult(new[] { Value, default }.Sequence()),
        ["Sequence(IEnumerable<Result<TError>>)"]                      = () => Task.FromResult(new[] { Result<string>.Success(), default }.Sequence()),
    };

    public static TheoryData<string> ExtensionMemberNames => [.. ExtensionMembers.Keys];

    public static TheoryData<string> ErrorOnlyMemberNames => [.. ErrorOnlyMembers.Keys];

    public static TheoryData<string> ValuedMemberNames => [.. ValuedMembers.Keys];

    [Fact]
    public void A_default_result_reports_its_state_honestly()
    {
        default(Result<string>).State.ShouldBe(ResultState.Uninitialized);
        default(Result<int, string>).State.ShouldBe(ResultState.Uninitialized);
    }

    [Theory]
    [MemberData(nameof(ErrorOnlyMemberNames))]
    public async Task An_uninitialized_error_only_result_throws_instead_of_picking_a_branch(string member)
    {
        var exception = await Record.ExceptionAsync(() => ErrorOnlyMembers[member](default));

        exception.ShouldBeOfType<InvalidOperationException>($"{member} picked a branch on an uninitialized result")
                 .Message.ShouldContain("never initialized");
    }

    [Theory]
    [MemberData(nameof(ValuedMemberNames))]
    public async Task An_uninitialized_valued_result_throws_instead_of_picking_a_branch(string member)
    {
        var exception = await Record.ExceptionAsync(() => ValuedMembers[member](default));

        exception.ShouldBeOfType<InvalidOperationException>($"{member} picked a branch on an uninitialized result")
                 .Message.ShouldContain("never initialized");
    }

    [Theory]
    [MemberData(nameof(ExtensionMemberNames))]
    public async Task An_extension_throws_on_an_uninitialized_result_instead_of_picking_a_branch(string member)
    {
        var exception = await Record.ExceptionAsync(ExtensionMembers[member]);

        exception.ShouldBeOfType<InvalidOperationException>($"{member} picked a branch on an uninitialized result")
                 .Message.ShouldContain("never initialized");
    }

    /// <summary>
    /// Every extension that takes a result, as its receiver, an argument or what its selector
    /// returns, has a case. The ones that only produce a result (the markers, <c>FirstOrError</c>)
    /// never see an uninitialized one.
    /// </summary>
    [Fact]
    public void Every_extension_that_takes_a_result_has_a_case()
    {
        var taking = (from method in typeof(Result).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                      where method.GetParameters().Any(parameter => MentionsResult(parameter.ParameterType))
                      select $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => Describe(p.ParameterType)))})").ToList();

        taking.Count.ShouldBeGreaterThanOrEqualTo(ExtensionMembers.Count);
        taking.Except(ExtensionMembers.Keys).ShouldBeEmpty("These extensions take a result and have no uninitialized case here.");
        ExtensionMembers.Keys.Except(taking).ShouldBeEmpty("These cases name no extension of Result.");
    }

    [Fact]
    public void Every_public_member_of_the_error_only_result_is_classified() =>
        AssertClassified(typeof(Result<string>), ErrorOnlyMembers.Keys);

    [Fact]
    public void Every_public_member_of_the_valued_result_is_classified() =>
        AssertClassified(typeof(Result<int, string>), ValuedMembers.Keys);

    private static void AssertClassified(Type type, IEnumerable<string> branchingCases)
    {
        // A conversion into the result constructs one, as Success and Error do; only a conversion out
        // of it, such as Result<TError>'s to bool, reads the state.
        var declared = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                           .Where(method => !(method.Name is "op_Implicit" or "op_Explicit" && method.ReturnType == type))
                           .Select(method => method.Name)
                           .ToHashSet();

        var branching = branchingCases.Select(key => key[..key.IndexOf('(')]).ToHashSet();

        declared.Count.ShouldBeGreaterThan(NonBranchingMembers.Length);
        declared.Except(NonBranchingMembers).Except(branching).ShouldBeEmpty(
            $"{type.Name} has public members nobody decided about: add a throw case if they pick a branch, or list them as non-branching.");
    }

    private static bool MentionsResult(Type type) =>
        type.IsGenericType
     && (type.GetGenericTypeDefinition() == typeof(Result<>) || type.GetGenericTypeDefinition() == typeof(Result<,>)
      || type.GetGenericArguments().Any(MentionsResult));

    private static string Describe(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>" : type.Name;
}
