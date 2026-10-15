---
verified: 2026-09-28
pins:
  - external ErrorOr 2.1.1
source: the ErrorOr repository at tag 2.1.1 and its nuspec on api.nuget.org, read by a research agent
turned-on: the ErrorOr column of the Option and Result table and the "Several errors per failure" row in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-017. ErrorOr 2.1.1 returns `default` from `.Value` on an error, and its `default` instance is a success

- `.Value` is unguarded: on an error it returns `default`.
- A `default` instance reads as a success holding `default(T)`.
- An `ErrorOr<T>` carries a list of errors.
- Its System.Text.Json support is a debug recorder, not a converter for the type. No analyzer.
- Targets `netstandard2.0`, `net8.0` and `net10.0`.
