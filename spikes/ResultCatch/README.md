# Spike: can an exception enter a `Result` through one factory, without a `Try` type?

**Status: run 2026-10-13, answered.** Proposed 2026-10-13, from the "`Result.Catch`" part of phase 13
(2.0.0) in the former plan's phase log (git, `design/plan.md` before 2026-10-13), [decision 43](../../design/adr/0043-exceptions-type-argument-constraints-constructor-guarantee.md). [Option and Result](../../design/option-result.md) keeps `Try` and `Unit` out:
a `Try<T>` is `Result<T, Exception>` under a second name, and `Result<TError>` is the value-less
shape a `Unit` would stand in for.

**Question.** A call that throws has no way into a `Result` today, so every caller writes the
`try`/`catch` by hand. Can four factories on the `Result` static class take that job, each
returning a concrete result rather than a marker, with the combinators unchanged?

1. **`Catch<T>(Func<T>)`** to `Result<T, Exception>`, and **`Catch(Action)`** to
   `Result<Exception>`.
2. **`Catch<T, TException>(Func<T>)`** to `Result<T, TException>`, catching that type and letting
   every other exception through.
3. **`CatchAsync`** for each, over `Task`, `Task<T>` and `ValueTask<T>`.
4. **A cancellation is not an outcome**: `OperationCanceledException` passes through every overload.

**Answer: yes, with two shapes settled by the compiler rather than by the plan.** Measured 2026-10-13
on .NET SDK 10.0.401 against the real base package, under JIT and published with Native AOT (no
trim or AOT warning; the AOT binary passes every claim). The async trap is an `[Obsolete(error: true)]`
overload over `Func<Task<T>>` and `Func<Task>`: more specific than `Func<T>`, so the compiler picks it
for a task-returning delegate and reports CS0619 naming `CatchAsync`. Its cost is a lambda that only
throws, which has no return type: with a type argument it is ambiguous (CS0121), and without one the
compiler prefers the non-void delegate for an expression body and lands on the trap; `int () => throw …`
or `void () => throw …` resolves both, and real call sites return something. No `ValueTask` overloads:
beside `Func<Task<T>>` they are ambiguous (CS0121) for every `async` lambda, so a `ValueTask` takes
`.AsTask()` or an `async` lambda, as the README already says of `MapAsync`. `Catch<T, TException>`
spells both type arguments; a one-argument `Catch<TException>(Action)` is the command form, and a
value-returning expression lambda given to it is discarded, which the README says. Shipped in 2.0.0
as `Result.Catch`/`Result.CatchAsync`, with `ResultCatchTests` holding each row.

## The candidate

A console project on the base package: `Candidate.cs` holds the factories as the base package
ships them, `Program.cs` runs each claim and prints PASS or FAIL, and `Trap.cs`, behind `-p:Trap=true`,
holds the calls that must not compile, so the build's errors are the measurement. `dotnet run` is the
JIT run, `dotnet build --no-incremental` shows CMTK0003 on the dropped call, and
`dotnet publish -c Release -p:Aot=true -o out/aot` repeats the run under Native AOT.

## Findings

Each row is a claim the spike must settle. A seed that breaks the guard proves each one, as in
`.claude/skills/verify-the-guard`.

| # | Claim | Result |
|---|---|---|
| 1 | `Result.Catch(() => int.Parse(s))` infers `T`, and the result is `Result<int, Exception>`, not a marker | measured: `Result<int, Exception>`, `Success(42)`; `int.Parse("x")` is `Error(FormatException)` |
| 2 | `Catch(Action)` returns `Result<Exception>`, so no `Unit` is needed for a call without a value | measured: `Catch(Action)` is `Result<Exception>`, a throwing action is its error |
| 3 | `Catch<T, TException>` catches that type only; another exception propagates with its stack intact | measured: `FormatException` caught; an `InvalidOperationException` propagates with `Throws` in its stack |
| 4 | `Catch<T, TException>` needs both type arguments spelled (C# infers all or none), or a shape exists that infers `T` from the delegate and takes `TException` alone | measured: both are spelled; no shape infers `T` alone, since C# infers all or none. The command form `Catch<TException>(Action)` takes one |
| 5 | The async trap: `Result.Catch(() => client.GetAsync(url))` is refused, by an `[Obsolete]` error overload over `Func<Task>`/`Func<Task<T>>`, by a CMTK rule, or both; `CatchAsync` is the way | measured: the `[Obsolete(error: true)]` overloads over `Func<Task<T>>` and `Func<Task>` are CS0619 for a lambda and for a method group, naming `CatchAsync`; no analyzer rule needed. Cost: a lambda that only throws needs `int () =>`/`void () =>` |
| 6 | `CatchAsync` over `Task`, `Task<T>` and `ValueTask<T>` catches an exception the awaited task throws, not only one thrown before the first await | measured for `Task` and `Task<T>`: an exception after `Task.Yield()` is the error. `ValueTask<T>` dropped: its overload is CS0121 for every `async` lambda |
| 7 | `OperationCanceledException` passes through every overload, sync and async, and a cancelled token surfaces as cancellation, never as an error | measured: a thrown `OperationCanceledException` and a cancelled `Task.Delay` both surface as cancellation, sync, narrowed and async |
| 8 | `Map`, `Bind` and the query-syntax `Select` are unchanged: a selector that throws propagates | measured: `Catch(() => 1).Map(_ => Throws())` throws |
| 9 | `ToHttpResult()` over `Result<T, Exception>` is a compile error | measured: `Catch(() => 1).ToHttpResult()` is CS1061, the extension does not bind on `Result<int, Exception>` |
| 10 | A caught exception is handed out by `TryGetError` and `Match` as the instance thrown; nothing wraps it | measured: `ReferenceEquals(error, thrown)` |
| 11 | CMTK0003 reports a dropped `Result.Catch(...)` as it reports any dropped result | measured: CMTK0003 on the dropped `Candidate.Catch(() => 1)` in a clean build |
| 12 | Native AOT: no new trim or AOT warning, in the packages or in the consumer | measured: `dotnet publish -p:Aot=true` with no IL warning; the binary prints ALL PASS |
