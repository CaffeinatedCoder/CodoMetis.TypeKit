# Spike: can an exception enter a `Result` through one factory, without a `Try` type?

**Status: outline, not yet run.** Proposed 2026-10-13, from the "`Result.Catch`" part of phase 13
(2.0.0) in [docs/plan.md](../../docs/plan.md) §2, decision 43. §9 keeps `Try` and `Unit` out:
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

**Answer: pending.**

## The candidate (planned)

A console project on the packed base package: `Candidate.cs` holds the factories as the base
package would ship them, `Program.cs` runs each claim and prints the outcome, and `Trap.cs`, behind
`#if TRAP`, holds the calls that must not compile, built once with `-p:DefineConstants=TRAP` to
read the errors. An `-p:Aot=true` publish repeats the run under Native AOT.

## Findings

Each row is a claim the spike must settle. A seed that breaks the guard proves each one, as in
`.claude/skills/verify-the-guard`.

| # | Claim | Result |
|---|---|---|
| 1 | `Result.Catch(() => int.Parse(s))` infers `T`, and the result is `Result<int, Exception>`, not a marker | |
| 2 | `Catch(Action)` returns `Result<Exception>`, so no `Unit` is needed for a call without a value | |
| 3 | `Catch<T, TException>` catches that type only; another exception propagates with its stack intact | |
| 4 | `Catch<T, TException>` needs both type arguments spelled (C# infers all or none), or a shape exists that infers `T` from the delegate and takes `TException` alone | |
| 5 | The async trap: `Result.Catch(() => client.GetAsync(url))` is refused, by an `[Obsolete]` error overload over `Func<Task>`/`Func<Task<T>>`, by a CMTK rule, or both; `CatchAsync` is the way | |
| 6 | `CatchAsync` over `Task`, `Task<T>` and `ValueTask<T>` catches an exception the awaited task throws, not only one thrown before the first await | |
| 7 | `OperationCanceledException` passes through every overload, sync and async, and a cancelled token surfaces as cancellation, never as an error | |
| 8 | `Map`, `Bind` and the query-syntax `Select` are unchanged: a selector that throws propagates | |
| 9 | `ToHttpResult()` over `Result<T, Exception>` is a compile error | |
| 10 | A caught exception is handed out by `TryGetError` and `Match` as the instance thrown; nothing wraps it | |
| 11 | CMTK0003 reports a dropped `Result.Catch(...)` as it reports any dropped result | |
| 12 | Native AOT: no new trim or AOT warning, in the packages or in the consumer | |
