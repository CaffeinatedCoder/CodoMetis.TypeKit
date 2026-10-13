# 0052. A `Cause` on `Fault<TFault>` joins 2.1

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, after the FluentResults comparison was re-read for 2.0.0.

A `Fault<TFault>` is one reason from a fixed set and a sentence about the occurrence. What a caught
exception was is lost on the way to it: `Result.Catch` makes the exception the whole error, and the
`MapError` that turns it into the fault an endpoint answers drops it, so a log of the refusal cannot
say what failed underneath. A cause nested under an error is the one FluentResults capability a
TypeKit user plausibly misses; success messages and a list of reasons stay outside what a `Result`
here is ([decision 5](0005-option-result.md), [§9](../option-result.md)).

In 2.1, additive: an optional `Exception? Cause` on `Fault<TFault>`, set through
`Fault.Of(reason, detail, cause)`, kept beside the reason and the detail for the log and the fault
feature on the request, and never written to the problem, whose `detail` stays the application's
sentence and whose body names no exception. Equality stays the reason's and the detail's, so a
fault with a cause still equals the same fault without one where a test compares them; the cause
is for the log. A `MapError` from a `Result<T, Exception>` that keeps the exception as the cause is
the pattern the docs show. Not a chain: one cause, which may itself be an aggregate exception.
