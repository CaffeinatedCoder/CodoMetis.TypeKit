namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// A delegate that returns no task is refused by every asynchronous combinator, as <c>CatchAsync</c>
/// refuses it: an <see cref="ArgumentNullException"/> naming the combinator and the delegate, and
/// linking the README. Awaited, the null was a <see cref="NullReferenceException"/> naming nothing.
/// </summary>
public sealed class NullTaskTests
{
    private static readonly Option<int> Some = Option.Some(1);
    private static readonly Result<int, string> Success = Result<int, string>.Success(1);
    private static readonly Result<int, string> Failure = Result<int, string>.Error("e");
    private static readonly Result<string> CommandSuccess = Result<string>.Success();
    private static readonly Result<string> CommandFailure = Result<string>.Error("e");
    private static readonly int[] Elements = [1, 2];

    public static TheoryData<string> Calls => [.. Call.Keys];

    private static readonly Dictionary<string, (string Method, string Parameter, Func<Task> Run)> Call = new()
    {
        ["TraverseAsync(selector)"]                   = ("TraverseAsync", "selector", () => Elements.TraverseAsync(_ => (Task<Result<int, string>>)null!)),
        ["TraverseAsync(selector, token)"]            = ("TraverseAsync", "selector", () => Elements.TraverseAsync((_, _) => (Task<Result<int, string>>)null!, CancellationToken.None)),
        ["command TraverseAsync(selector)"]           = ("TraverseAsync", "selector", () => Elements.TraverseAsync(_ => (Task<Result<string>>)null!)),
        ["command TraverseAsync(selector, token)"]    = ("TraverseAsync", "selector", () => Elements.TraverseAsync((_, _) => (Task<Result<string>>)null!, CancellationToken.None)),
        ["Option.TraverseAsync(selector)"]            = ("TraverseAsync", "selector", () => Some.TraverseAsync(_ => (Task<Result<int, string>>)null!)),
        ["Option.TraverseAsync(selector, token)"]     = ("TraverseAsync", "selector", () => Some.TraverseAsync((_, _) => (Task<Result<int, string>>)null!, CancellationToken.None)),
        ["Patch.TraverseAsync(selector)"]             = ("TraverseAsync", "selector", () => Patch.Value(1).TraverseAsync(_ => (Task<Result<int, string>>)null!)),
        ["Patch.TraverseAsync(selector, token)"]      = ("TraverseAsync", "selector", () => Patch.Value(1).TraverseAsync((_, _) => (Task<Result<int, string>>)null!, CancellationToken.None)),
        ["Option.MapAsync"]                           = ("MapAsync", "selector", () => Some.MapAsync(_ => (Task<int>)null!)),
        ["Option.BindAsync"]                          = ("BindAsync", "selector", () => Some.BindAsync(_ => (Task<Option<int>>)null!)),
        ["Option.TapAsync"]                           = ("TapAsync", "action", () => Some.TapAsync(_ => null!)),
        ["Result<T, TError>.MapAsync"]                = ("MapAsync", "selector", () => Success.MapAsync(_ => (Task<int>)null!)),
        ["Result<T, TError>.BindAsync"]               = ("BindAsync", "selector", () => Success.BindAsync(_ => (Task<Result<int, string>>)null!)),
        ["Result<T, TError>.BindAsync to a command"]  = ("BindAsync", "selector", () => Success.BindAsync(_ => (Task<Result<string>>)null!)),
        ["Result<T, TError>.TapAsync"]                = ("TapAsync", "action", () => Success.TapAsync(_ => null!)),
        ["Result<T, TError>.TapErrorAsync"]           = ("TapErrorAsync", "action", () => Failure.TapErrorAsync(_ => null!)),
        ["Result<T, TError>.RecoverAsync"]            = ("RecoverAsync", "selector", () => Failure.RecoverAsync(_ => null!)),
        ["Result<TError>.MapAsync"]                   = ("MapAsync", "selector", () => CommandSuccess.MapAsync(() => (Task<int>)null!)),
        ["Result<TError>.BindAsync"]                  = ("BindAsync", "selector", () => CommandSuccess.BindAsync(() => null!)),
        ["Result<TError>.TapAsync"]                   = ("TapAsync", "action", () => CommandSuccess.TapAsync(() => null!)),
        ["Result<TError>.TapErrorAsync"]              = ("TapErrorAsync", "action", () => CommandFailure.TapErrorAsync(_ => null!)),
        ["Result<TError>.RecoverAsync"]               = ("RecoverAsync", "selector", () => CommandFailure.RecoverAsync(_ => null!)),
        ["a pending Option's MapAsync"]               = ("MapAsync", "selector", () => Task.FromResult(Some).MapAsync(_ => (Task<int>)null!)),
        ["a pending Result's BindAsync"]              = ("BindAsync", "selector", () => Task.FromResult(Success).BindAsync(_ => (Task<Result<int, string>>)null!)),
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task A_delegate_that_returns_no_task_is_refused_by_name(string call)
    {
        var (method, parameter, run) = Call[call];

        var refusal = await Should.ThrowAsync<ArgumentNullException>(run);

        refusal.ParamName.ShouldBe(parameter);
        refusal.Message.ShouldStartWith($"The delegate given to {method} returned no task");
        refusal.Message.ShouldContain("#a-null-in-an-option-or-a-result");
    }
}
