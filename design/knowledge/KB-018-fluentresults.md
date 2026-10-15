---
verified: 2026-09-28
pins:
  - external FluentResults 4.0.0
source: the FluentResults repository at tag 4.0.0, read by a research agent
turned-on: the FluentResults row of "Choose something else when" in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-018. FluentResults 4.0.0 results are classes that carry reasons, errors and successes, and an error can name its cause

- `Result<T>` is a class.
- A result carries reasons, errors and successes; an error names what caused it with `CausedBy`.
