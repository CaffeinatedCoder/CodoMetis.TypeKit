# 0030. Asynchronous callbacks return `Task`

Date: 2026-09-28
Status: accepted

Context: 2026-09-28, §9.

`TapErrorAsync(Func<TError, Task>)` sits
beside `Action<TError>`, as `TapAsync` already did. It ships in 1.0 because adding it later
rebinds existing calls. No `ValueTask` overloads: beside the `Task` ones every `async` lambda
becomes ambiguous (CS0121, measured).
