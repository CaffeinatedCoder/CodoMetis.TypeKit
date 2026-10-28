namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>TraverseAsync</c> on an <c>Option</c> and a <c>Patch</c>: the synchronous <c>Traverse</c> with the
/// operation awaited. <c>None</c>, a clear and an absent field never call the selector, and the token is
/// checked before the one operation, as the sequence's <c>TraverseAsync</c> checks it before each.
/// </summary>
public sealed class OptionPatchTraverseAsyncTests
{
    private static async Task<Result<int, string>> ParseLater(string text)
    {
        await Task.Yield();
        return int.TryParse(text, out var n) ? Result<int, string>.Success(n) : Result<int, string>.Error($"not a number: {text}");
    }

    private static Task<Result<int, string>> Unexpected(string _) => throw new InvalidOperationException("the selector was called");

    private static Task<Result<int, string>> Unexpected(string _, CancellationToken __) => throw new InvalidOperationException("the selector was called");

    [Fact]
    public async Task An_option_with_a_value_is_awaited_into_some_or_the_error()
    {
        var parsed = await Option.Some("42").TraverseAsync(ParseLater);
        parsed.TryGetValue(out var option, out _).ShouldBeTrue();
        option.TryGetValue(out var value).ShouldBeTrue();
        value.ShouldBe(42);

        var refused = await Option.Some("x").TraverseAsync(ParseLater);
        refused.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("not a number: x");
    }

    [Fact]
    public async Task None_is_a_success_without_a_value_and_the_selector_is_not_called()
    {
        var none = await Option.None<string>().TraverseAsync(Unexpected);
        none.TryGetValue(out var option, out _).ShouldBeTrue();
        option.IsNone().ShouldBeTrue();

        var noneWithToken = await Option.None<string>().TraverseAsync(Unexpected, new CancellationToken(canceled: true));
        noneWithToken.TryGetValue(out var withToken, out _).ShouldBeTrue("None checks no token: there is no operation to start");
        withToken.IsNone().ShouldBeTrue();
    }

    [Fact]
    public async Task A_patch_with_a_value_is_awaited_into_a_value_or_the_error()
    {
        var parsed = await Patch.Value("42").TraverseAsync(ParseLater);
        parsed.TryGetValue(out var patch, out _).ShouldBeTrue();
        patch.State.ShouldBe(PatchState.Value);
        patch.TryGetValue(out var value).ShouldBeTrue();
        value.ShouldBe(42);

        var refused = await Patch.Value("x").TraverseAsync(ParseLater);
        refused.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("not a number: x");
    }

    [Theory]
    [InlineData(PatchState.Null)]
    [InlineData(PatchState.Absent)]
    public async Task A_clear_or_an_absent_field_passes_through_and_the_selector_is_not_called(PatchState state)
    {
        var patch = state == PatchState.Null ? Patch.Null<string>() : Patch.Absent<string>();

        var passed = await patch.TraverseAsync(Unexpected);
        passed.TryGetValue(out var produced, out _).ShouldBeTrue();
        produced.State.ShouldBe(state);

        var passedWithToken = await patch.TraverseAsync(Unexpected, new CancellationToken(canceled: true));
        passedWithToken.TryGetValue(out var withToken, out _).ShouldBeTrue("a field without a value checks no token");
        withToken.State.ShouldBe(state);
    }

    [Fact]
    public async Task The_token_is_handed_to_the_operation()
    {
        using var cancellation = new CancellationTokenSource();
        var seen = new List<CancellationToken>();

        Task<Result<int, string>> Load(string text, CancellationToken token)
        {
            seen.Add(token);
            return ParseLater(text);
        }

        (await Option.Some("1").TraverseAsync(Load, cancellation.Token)).TryGetValue(out var option, out _).ShouldBeTrue();
        option.TryGetValue(out var fromOption).ShouldBeTrue();
        fromOption.ShouldBe(1);

        (await Patch.Value("2").TraverseAsync(Load, cancellation.Token)).TryGetValue(out var patch, out _).ShouldBeTrue();
        patch.TryGetValue(out var fromPatch).ShouldBeTrue();
        fromPatch.ShouldBe(2);

        seen.ShouldBe([cancellation.Token, cancellation.Token]);
    }

    [Fact]
    public async Task A_cancelled_token_starts_nothing_and_propagates()
    {
        var cancelled = new CancellationToken(canceled: true);

        await Should.ThrowAsync<OperationCanceledException>(() => Option.Some("1").TraverseAsync(Unexpected, cancelled));
        await Should.ThrowAsync<OperationCanceledException>(() => Patch.Value("1").TraverseAsync(Unexpected, cancelled));
    }
}
