---
verified: 2026-09-28
pins:
  - external CSharpFunctionalExtensions 3.7.0
source: the CSharpFunctionalExtensions repository at tag 3.7.0 and its nuspec on api.nuget.org, read by a research agent
turned-on: the CSharpFunctionalExtensions column of the Option and Result table and the "A Result on the wire" row in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-016. CSharpFunctionalExtensions 3.7.0 throws from `.Value` on a failure, and its `default(Result<T>)` is a success

- `Maybe<T>.Value` and `Result<T>.Value` throw when there is no value.
- `default(Result<T>)` reads as a success holding `default(T)`; `default(Maybe<T>)` is none.
- Opt-in System.Text.Json converters for its types. No analyzer of its own (a third-party one exists).
- Targets `netstandard2.0`, `net6.0` and `net8.0`. License MIT.
