# 0062. An asynchronous callback runs where the task it waited for completed

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the pre-tag review of 2.0.0; [Option and Result](../option-result.md),
[decision 30](0030-asynchronous-callbacks-return-task.md).

Every `await` in the base package is `ConfigureAwait(false)`, about fifty of them: the continuations
of a pending `Option` or `Result`, `TraverseAsync`, `CatchAsync`, and the asynchronous forms on a known
result, which await the task their callback returns. Measured with a `SynchronizationContext`
installed by the caller: `await SlowResult().MapAsync(x => …)` ran the selector without the caller's
context while the result was still pending, and on it, inline, when the task had already completed;
the caller's own `await` returned to its context either way. In a Blazor Server component or a WPF
window, a callback that touches state bound to the renderer or the dispatcher therefore works or fails
by timing.

`ConfigureAwait(false)` stays, and the behaviour is the documented contract: a callback runs where the
task it waits for completed, at once on the caller's thread and context when that task had already
completed, and otherwise wherever it completed, without the caller's context. In a UI or a Blazor
component, await the task and call the synchronous combinator on what it answers, or marshal inside
the callback.

Dropping it would resume every continuation on the caller's context, and a caller that blocks on a
chain with `.Result`, `.Wait()` or `GetAwaiter().GetResult()` on a single-threaded context, which
works today, would hang: the continuation is posted to the thread that is waiting for it. Measured
with a caller's context that never runs what is posted to it, as a blocked UI thread does not: every
member that returns a task completes with `ConfigureAwait(false)`, and with it removed from one
continuation that continuation is posted and the chain never completes. A context-capturing overload
beside each member would double the surface for what the caller's own `await` before a synchronous
combinator already does.

`AsynchronousContextTests` holds every public member of the base package that returns a task to both
halves, completeness-checked by reflection: a callback after a pending task does not run on the
caller's context, one after a completed task runs on it, and nothing posts to it.
