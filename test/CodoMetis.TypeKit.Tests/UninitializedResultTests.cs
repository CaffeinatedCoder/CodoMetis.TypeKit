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
        ["Map(fn)"]                   = r => Task.FromResult(r.Map(() => 1)),
        ["Bind(fn)"]                  = r => Task.FromResult(r.Bind(Result<string>.Success)),
        ["Tap(action)"]               = r => Task.FromResult(r.Tap(() => { })),
        ["TapAsync(action)"]          = r => r.TapAsync(() => Task.CompletedTask),
        ["TryGetError(out error)"]    = r => Task.FromResult(r.TryGetError(out _)),
        ["op_Implicit(bool)"]         = r => Task.FromResult((bool)r),
    };

    private static readonly Dictionary<string, Func<Result<int, string>, Task>> ValuedMembers = new()
    {
        ["Match(onSuccess, onError)"]              = r => Task.FromResult(r.Match(x => x, _ => 2)),
        ["Match(onSuccess -> Ok(x), onError)"]     = r => Task.FromResult(r.Match(x => Result.Ok(x), _ => 2)),
        ["Match(fn, defaultProvider)"]             = r => Task.FromResult(r.Match(x => x, () => 2)),
        ["Match(onSuccess -> Ok(), onError) collapse"] = r => Task.FromResult(r.Match(_ => Result.Ok(), e => e)),
        ["Map(fn)"]                                = r => Task.FromResult(r.Map(x => x)),
        ["Bind(fn -> Result)"]                     = r => Task.FromResult(r.Bind(Result<int, string>.Success)),
        ["Bind(fn -> Ok(x))"]                      = r => Task.FromResult(r.Bind(x => Result.Ok(x))),
        ["Tap(action)"]                            = r => Task.FromResult(r.Tap(_ => { })),
        ["TapAsync(action)"]                       = r => r.TapAsync(_ => Task.CompletedTask),
        ["TryGetValue(out value, out error)"]      = r => Task.FromResult(r.TryGetValue(out _, out _)),
        ["AsEnumerable() on enumeration"]          = r => Task.FromResult(r.AsEnumerable().ToList()),
        ["op_Implicit(bool)"]                      = r => Task.FromResult((bool)r),
        ["Select(fn) extension"]                   = r => Task.FromResult(r.Select(x => x)),
        ["ToOption() extension"]                   = r => Task.FromResult(r.ToOption()),
    };

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

    [Fact]
    public void Every_public_member_of_the_error_only_result_is_classified() =>
        AssertClassified(typeof(Result<string>), ErrorOnlyMembers.Keys);

    [Fact]
    public void Every_public_member_of_the_valued_result_is_classified() =>
        AssertClassified(typeof(Result<int, string>), ValuedMembers.Keys);

    private static void AssertClassified(Type type, IEnumerable<string> branchingCases)
    {
        var declared = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                           .Select(method => method.Name)
                           .ToHashSet();

        var branching = branchingCases.Select(key => key[..key.IndexOf('(')]).ToHashSet();

        declared.Count.ShouldBeGreaterThan(NonBranchingMembers.Length);
        declared.Except(NonBranchingMembers).Except(branching).ShouldBeEmpty(
            $"{type.Name} has public members nobody decided about: add a throw case if they pick a branch, or list them as non-branching.");
    }
}
