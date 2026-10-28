# 0110. `CatchAsync` takes a `ValueTask` at a lower overload priority, and `TraverseAsync` reaches `Option` and `Patch`

Date: 2026-10-28
Status: accepted

Context: 2026-10-28, issue #88, the 2.3.0 roadmap item "`CatchAsync` over a `ValueTask`, and
`TraverseAsync` on `Option` and `Patch`". [Decision 30](0030-asynchronous-callbacks-return-task.md)
left `ValueTask` overloads out because beside the `Task` ones every `async` lambda became ambiguous
(CS0121). Since then a `ValueTask` reached `CatchAsync` only through `.AsTask()` or an `async`
lambda, and `stream.ReadAsync(buffer)`, the commonest `ValueTask` call, was the README's example
of the detour. C# 13's `[OverloadResolutionPriority]` removes the ambiguity: among the applicable
candidates of one type, the ones of lower priority drop out.

## Decision

**`CatchAsync` has four `ValueTask` forms**, over `Func<ValueTask<T>>` and `Func<ValueTask>`, valued and
command, wide and narrowed to one exception type, each `[OverloadResolutionPriority(-1)]`. They catch as
the `Task` forms do: an exception thrown before the task or after its first `await`, a timeout as the
error, a cancellation and an `AggregateException` of cancellations passed through, a null value and a task
of a task refused. They return `Task<Result<…>>`, as every asynchronous member does.

Measured in `CatchAsyncValueTaskTests`, compiled as a consumer: an `async` lambda, valued or command,
with or without type arguments, a lambda that only throws with its type argument spelled, and a delegate
or method group over a `Task` all bind to the `Task` form, as before the `ValueTask` forms existed; a lambda
or method group over a `ValueTask` binds to the new form. Without the attribute the first group is CS0121.

**`Catch` keeps refusing a `ValueTask` at run time.** A compile-time refusal beside `Catch<T>(Func<T>)`
is now possible the same way, but it would turn a call that builds today into a build error, which is a
major version's change, as a new rule at Error severity is.

**Decision 30 stands for callbacks.** `TapAsync`, `MapAsync` and the rest take `Task`; a lambda given
to a callback is usually `async`, and it already binds to the `Task` form.

**`TraverseAsync` on `Option<T>` and `Patch<T>`**, over `Func<T, Task<Result<TResult, TError>>>` and its
`CancellationToken` form, is the synchronous `Traverse` with the operation awaited: `None`, a clear and
an absent field call nothing and check no token, as the sequence's `TraverseAsync` checks nothing over
an empty sequence; with a value the token is checked before the operation and handed to it.
