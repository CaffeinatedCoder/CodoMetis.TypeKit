using System.Reflection;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// Asynchronous steps in a result pipeline: <c>MapAsync</c>/<c>BindAsync</c> on a result, and the
/// continuations of a <c>Task&lt;Result&lt;…&gt;&gt;</c>, so a chain is awaited once, at its end.
/// </summary>
/// <remarks>
/// An error skips every later step, whether it is known at the start or arrives from an awaited
/// one. Null delegates and uninitialized results are covered in <see cref="NullDelegateTests"/> and
/// <see cref="UninitializedResultTests"/>, whose completeness tests hold these members too.
/// </remarks>
public sealed class ResultContinuationTests
{
    private enum Fault { NotFound, Declined }

    private sealed record Order(int Id, decimal Total);

    private static Task<Result<Order, Fault>> Find(int id) =>
        Task.FromResult<Result<Order, Fault>>(id > 0 ? new Order(id, id * 10m) : Result.Error(Fault.NotFound));

    private static Task<Result<Fault>> Charge(Order order) =>
        Task.FromResult<Result<Fault>>(order.Total < 100m ? Result.Success() : Fault.Declined);

    private static Result<int, Fault> Parse(string text) =>
        int.TryParse(text, out var id) ? id : Result.Error(Fault.NotFound);

    [Fact]
    public async Task A_pipeline_mixes_synchronous_and_asynchronous_steps_and_is_awaited_once()
    {
        var logged = new List<decimal>();

        var charged = await Parse("3")
            .BindAsync(Find)                               // Result → Task<Result>
            .MapAsync(order => order with { Total = order.Total + 1m })   // a synchronous step on a pending result
            .TapAsync(order => logged.Add(order.Total))
            .BindAsync(Charge);                            // a command: Result<Fault> remains

        ((bool)charged).ShouldBeTrue();
        logged.ShouldBe([31m]);
    }

    [Fact]
    public async Task An_error_known_at_the_start_skips_every_step()
    {
        var calls = 0;

        var result = await Parse("x")
            .BindAsync(id => { calls++; return Find(id); })
            .MapAsync(order => { calls++; return order.Total; })
            .TapAsync(_ => calls++);

        result.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe(Fault.NotFound);
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_error_from_an_awaited_step_skips_the_rest()
    {
        var calls = 0;

        var result = await Parse("-1")
            .BindAsync(Find)
            .MapAsync(async order => { calls++; await Task.Yield(); return order.Total; })
            .TapAsync(async _ => { calls++; await Task.Yield(); });

        result.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe(Fault.NotFound);
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_command_declines_and_its_error_is_what_remains()
    {
        var result = await Find(20).BindAsync(Charge);

        result.TryGetError(out var error).ShouldBeTrue();
        error.ShouldBe(Fault.Declined);
    }

    [Fact]
    public async Task MapErrorAsync_crosses_layers_on_a_pending_result()
    {
        var mapped = await Find(-1).MapErrorAsync(fault => $"order: {fault}");

        mapped.TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("order: NotFound");

        var command = await Charge(new Order(1, 500m)).MapErrorAsync(fault => (int)fault);
        command.TryGetError(out var code).ShouldBeTrue();
        code.ShouldBe((int)Fault.Declined);
    }

    [Fact]
    public async Task The_asynchronous_forms_on_a_result_run_only_on_success()
    {
        (await Result<int, string>.Success(2).MapAsync(x => Task.FromResult(x * 21))).Match(x => x, _ => -1).ShouldBe(42);
        (await Result<int, string>.Success(2).BindAsync(x => Task.FromResult(Result<int, string>.Success(x + 1)))).Match(x => x, _ => -1).ShouldBe(3);
        ((bool)await Result<int, string>.Success(2).BindAsync(_ => Task.FromResult(Result<string>.Success()))).ShouldBeTrue();

        var calls = 0;
        (await Result<int, string>.Error("e").MapAsync(x => { calls++; return Task.FromResult(x); })).Match(_ => "", e => e).ShouldBe("e");
        (await Result<int, string>.Error("e").BindAsync(x => { calls++; return Task.FromResult(Result<int, string>.Success(x)); })).Match(_ => "", e => e).ShouldBe("e");
        (await Result<string>.Error("e").MapAsync(() => { calls++; return Task.FromResult(1); })).Match(_ => "", e => e).ShouldBe("e");
        (await Result<string>.Error("e").BindAsync(() => { calls++; return Task.FromResult(Result<string>.Success()); })).TryGetError(out _).ShouldBeTrue();
        calls.ShouldBe(0);

        (await Result<string>.Success().MapAsync(() => Task.FromResult(7))).Match(x => x, _ => -1).ShouldBe(7);
    }

    [Fact]
    public async Task The_continuations_of_a_pending_command_run_only_on_success()
    {
        var calls = 0;
        var ok = Task.FromResult(Result<string>.Success());

        (await ok.MapAsync(() => 1)).Match(x => x, _ => -1).ShouldBe(1);
        (await ok.MapAsync(() => Task.FromResult(2))).Match(x => x, _ => -1).ShouldBe(2);
        ((bool)await ok.BindAsync(Result<string>.Success)).ShouldBeTrue();
        ((bool)await ok.BindAsync(() => Task.FromResult(Result<string>.Success()))).ShouldBeTrue();
        ((bool)await ok.TapAsync(() => calls++)).ShouldBeTrue();
        ((bool)await ok.TapAsync(() => { calls++; return Task.CompletedTask; })).ShouldBeTrue();
        calls.ShouldBe(2);

        var failed = Task.FromResult(Result<string>.Error("e"));
        (await failed.MapAsync(() => { calls++; return 1; })).Match(_ => "", e => e).ShouldBe("e");
        (await failed.BindAsync(() => { calls++; return Result<string>.Success(); })).TryGetError(out _).ShouldBeTrue();
        (await failed.TapAsync(() => calls++)).TryGetError(out _).ShouldBeTrue();
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task EnsureAsync_and_TapErrorAsync_continue_a_pending_result()
    {
        var logged = new List<Fault>();

        Result<Order, Fault> small = await Find(3).EnsureAsync(order => order.Total < 100m, Fault.Declined).TapErrorAsync(logged.Add);
        Result<Order, Fault> large = await Find(30).EnsureAsync(order => order.Total < 100m, Fault.Declined).TapErrorAsync(logged.Add);
        Result<Order, Fault> none  = await Find(0).EnsureAsync(_ => throw new InvalidOperationException("not called on an error"), Fault.Declined);

        small.Match(order => order.Id, _ => -1).ShouldBe(3);
        large.ShouldBe(Result<Order, Fault>.Error(Fault.Declined));
        none.ShouldBe(Result<Order, Fault>.Error(Fault.NotFound));
        logged.ShouldBe([Fault.Declined]);

        var commandErrors = new List<Fault>();
        (await Find(30).BindAsync(Charge).TapErrorAsync(commandErrors.Add)).TryGetError(out _).ShouldBeTrue();
        ((bool)await Find(3).BindAsync(Charge).TapErrorAsync(commandErrors.Add)).ShouldBeTrue();
        commandErrors.ShouldBe([Fault.Declined]);

        // The error is checked before the result is known, as the delegates are.
        (await Should.ThrowAsync<ArgumentNullException>(() => Task.FromResult(Result<int, string>.Error("e")).EnsureAsync(_ => true, null!))).ParamName.ShouldBe("error");
        (await Should.ThrowAsync<ArgumentNullException>(() => Task.FromException<Result<int, string>>(new TimeoutException()).EnsureAsync(_ => true, null!))).ParamName.ShouldBe("error");
    }

    /// <summary>
    /// A selector that returns a task picks the asynchronous overload, a method group included,
    /// rather than wrapping the task as the value.
    /// </summary>
    [Fact]
    public async Task A_task_returning_selector_is_awaited_rather_than_wrapped()
    {
        Result<decimal, Fault> total = await Task.FromResult(Result<int, Fault>.Success(4))
            .MapAsync(id => Task.FromResult(id * 2.5m));

        total.Match(x => x, _ => -1m).ShouldBe(10m);

        Result<Order, Fault> order = await Task.FromResult(Result<int, Fault>.Success(4)).BindAsync(Find);
        order.Match(o => o.Id, _ => -1).ShouldBe(4);
    }

    /// <summary>
    /// Everything that can be done to a result can be done to one still being produced: every
    /// combinator of both shapes that returns a result has a continuation on
    /// <c>Task&lt;Result&lt;…&gt;&gt;</c> with the same delegate. <c>Match</c> ends a chain, after
    /// the one <c>await</c>.
    /// </summary>
    [Theory]
    [InlineData(typeof(Result<,>))]
    [InlineData(typeof(Result<>))]
    public void Every_combinator_continues_a_pending_result(Type shape)
    {
        var combinators = (from method in shape.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                           where method.Name != nameof(Result<,>.Match)
                           let parameters = method.GetParameters()
                           where parameters.Length == 1 && typeof(Delegate).IsAssignableFrom(parameters[0].ParameterType)
                           select $"{(method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name : method.Name + "Async")}({Describe(parameters[0].ParameterType)})").ToList();

        var continuations = (from method in typeof(Result).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                             let parameters = method.GetParameters()
                             where parameters.Length == 2
                                && parameters[0].ParameterType.IsGenericType
                                && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(Task<>)
                                && parameters[0].ParameterType.GetGenericArguments()[0].GetGenericTypeDefinition() == shape
                             select $"{method.Name}({Describe(parameters[1].ParameterType)})").ToList();

        combinators.Count.ShouldBeGreaterThanOrEqualTo(6);
        combinators.Except(continuations).ShouldBeEmpty($"These combinators of {shape.Name} have no continuation on a pending result.");
    }

    private static string Describe(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>" : type.Name;
}
