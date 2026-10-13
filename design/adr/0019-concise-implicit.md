# 0019. Concise, not implicit

Date: 2026-09-27
Status: accepted

Context: 2026-09-27, §9.

Markers where a target type exists, explicit type
arguments where none does.
- One word for success: `Result.Success(...)` beside `Result<…>.Success(...)`, the `Success`
  markers, `ResultState.Success` and `onSuccess`, as `Option.Some` sits beside `Option<T>`.
  `Result.Ok` was the one place that said it differently.
- `Option.None()` returns a marker that converts to any `Option<T>`; `Option.None<T>()` stays for
  `var`. The analyzer forbids `default(Option<T>)`, so every None needed its type argument.
- `MapError` on both shapes, for crossing layers, which needed
  `Match<Result<T, TNew>>(x => x, e => Result.Error(…))`; and `Bind(Func<T, Result<TError>>)`, a
  command after a query, which replaces the collapsing `Match(_ => Result.Ok(), e => e)`, a
  conversion wearing `Match`'s name.
- `Option.ToResult(error)` keeps the value (`Result<T, TError>`); it returned `Result<TError>`,
  dropping it. `Coalesce` is `Or`, beside `OrDefault()` and `OrNull()`.
- Not added: an implicit `T` → `Option<T>`, since a null would throw exactly where a reader
  expects `None`; a bare error → `Result<T, TError>` (the ambiguity above); a shipped
  `using static`. A lambda that returns a bare value and `Result.Error(...)` still needs its type
  argument, because C# infers a lambda's return type from its body alone; the README says so.
