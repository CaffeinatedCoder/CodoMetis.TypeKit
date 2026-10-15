---
verified: 2026-09-28
pins:
  - external OneOf 3.0.271
source: the OneOf repository at tag 3.0.271, read by a research agent
turned-on: the OneOf row of "Choose something else when" in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-019. OneOf 3.0.271 is a readonly struct whose `default` is its first case

- `OneOf<T0, T1, ...>` is a general-purpose readonly struct.
- `default` is the first case: its index field is `0`.
