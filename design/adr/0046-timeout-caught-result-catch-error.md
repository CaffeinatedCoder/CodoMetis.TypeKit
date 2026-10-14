# 0046. A timeout caught by `Result.Catch` is the error

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13.

Every overload let
an `OperationCanceledException` through, and `HttpClient` throws one over a `TimeoutException`
when its `Timeout` elapses, so `CatchAsync(() => client.GetAsync(…))` threw on the most common
failure of the call the README shows it with, and a sample test that
raced a 100 ms timeout against a refused port failed whenever the timeout won (measured). A
timeout is the call failing, not the caller cancelling: an `OperationCanceledException` whose
`InnerException` is a `TimeoutException` is caught, every other one still passes through. No
`CancellationToken` overload: the shape tells the two apart without one, and a token cancelled by
the caller still surfaces as cancellation.
