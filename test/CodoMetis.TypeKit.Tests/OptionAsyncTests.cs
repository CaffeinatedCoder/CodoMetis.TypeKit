namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// An option's asynchronous steps, and the continuations of a <c>Task&lt;Option&lt;T&gt;&gt;</c>: a
/// repository's lookup continues as a pending result does, where <c>MapAsync</c> on one failed to
/// infer its type arguments against the <c>Result</c> overloads (CS0411) and nothing else existed.
/// </summary>
public sealed class OptionAsyncTests
{
    private static Task<Option<int>> PendingSome => Task.FromResult(Option.Some(2));
    private static Task<Option<int>> PendingNone => Task.FromResult(Option.None<int>());

    private static async Task<Option<int>> LaterAsync(Option<int> option)
    {
        await Task.Yield();
        return option;
    }

    [Fact]
    public async Task A_pending_option_maps_binds_and_filters_once_known()
    {
        (await PendingSome.MapAsync(x => x * 10)).ShouldBe(Option.Some(20));
        (await PendingSome.MapAsync(async x => { await Task.Yield(); return x + 1; })).ShouldBe(Option.Some(3));
        (await PendingSome.BindAsync(x => x > 1 ? Option.Some(x.ToString()) : Option.None<string>())).ShouldBe(Option.Some("2"));
        (await PendingSome.BindAsync(x => LaterAsync(Option.Some(x + 5)))).ShouldBe(Option.Some(7));
        (await PendingSome.FilterAsync(x => x > 5)).ShouldBe(Option.None<int>());
        (await LaterAsync(Option.Some(9)).FilterAsync(x => x > 5)).ShouldBe(Option.Some(9));
    }

    [Fact]
    public async Task None_passes_through_without_calling_anything()
    {
        static int Never(int _) => throw new InvalidOperationException("called on None");

        (await PendingNone.MapAsync(Never)).ShouldBe(Option.None<int>());
        (await PendingNone.MapAsync(x => Task.FromResult(Never(x)))).ShouldBe(Option.None<int>());
        (await PendingNone.BindAsync(x => Option.Some(Never(x)))).ShouldBe(Option.None<int>());
        (await PendingNone.BindAsync(x => Task.FromResult(Option.Some(Never(x))))).ShouldBe(Option.None<int>());
        (await PendingNone.FilterAsync(x => Never(x) > 0)).ShouldBe(Option.None<int>());
        (await PendingNone.TapAsync(x => { Never(x); })).ShouldBe(Option.None<int>());
        (await PendingNone.TapAsync(x => Task.FromResult(Never(x)))).ShouldBe(Option.None<int>());
        (await Option.None<int>().MapAsync(x => Task.FromResult(Never(x)))).ShouldBe(Option.None<int>());
    }

    /// <summary>A side effect that returns a task is waited for: it has run when the chain's await returns.</summary>
    [Fact]
    public async Task An_asynchronous_side_effect_is_waited_for()
    {
        var seen = 0;

        var option = await PendingSome.TapAsync(async x => { await Task.Delay(10); seen = x; });

        seen.ShouldBe(2);
        option.ShouldBe(Option.Some(2));
        (await Option.Some(4).TapAsync(async x => { await Task.Yield(); seen = x; })).ShouldBe(Option.Some(4));
        seen.ShouldBe(4);
    }

    [Fact]
    public async Task ToResultAsync_turns_absence_into_the_error()
    {
        (await PendingSome.ToResultAsync("missing")).ShouldBe(Result<int, string>.Success(2));
        (await PendingNone.ToResultAsync("missing")).ShouldBe(Result<int, string>.Error("missing"));
    }

    /// <summary>The error is refused whether or not the option has a value, and before the task is awaited, as <c>ToResult</c> refuses it.</summary>
    [Fact(Timeout = 10_000)]
    public async Task ToResultAsync_refuses_a_null_error_before_the_option_is_known()
    {
        // Never completes: only a check made before the await can answer. The test's token ends it if not.
        var never = new TaskCompletionSource<Option<int>>().Task.WaitAsync(TestContext.Current.CancellationToken);

        (await Should.ThrowAsync<ArgumentNullException>(() => never.ToResultAsync((string)null!))).ParamName.ShouldBe("error");
        await Should.ThrowAsync<ArgumentNullException>(() => PendingSome.ToResultAsync((string)null!));
    }

    [Fact]
    public async Task A_selector_whose_task_produces_null_is_refused()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => Option.Some(1).MapAsync(_ => Task.FromResult((string)null!)));
        await Should.ThrowAsync<ArgumentNullException>(() => PendingSome.MapAsync(_ => (string)null!));
    }

    [Fact]
    public async Task A_faulted_lookup_surfaces_its_own_exception()
    {
        var faulted = Task.FromException<Option<int>>(new TimeoutException());

        await Should.ThrowAsync<TimeoutException>(() => faulted.MapAsync(x => x));
        await Should.ThrowAsync<TimeoutException>(() => faulted.ToResultAsync("missing"));
    }
}
