---
verified: 2026-09-28
pins:
  - external LanguageExt.Core 4.4.9
  - external LanguageExt.Core 5.0.0-beta-77
source: the language-ext repository at tags 4.4.9 and 5.0.0-beta-77 and their nuspecs on api.nuget.org, read by a research agent
turned-on: the LanguageExt column of the Option and Result table in docs/comparison.md, and decision 5 (pure Option and Result, not a LanguageExt-style framework)
test: none, a competitor's feature, read from its source and package
---

# KB-015. LanguageExt has no public `.Value` on `Option`, and its `default` is `None`, in 4.4.9 and in the 5.0 beta

- `Option` has no public `.Value`; the content is reached through `Match`, `IfNone` and the like. `default(Option<T>)`
  is `None`. Both hold in 4.4.9 and 5.0.0-beta-77, so neither is a difference from TypeKit.
- No System.Text.Json converter of its own, and no analyzer.
- 4.4.9 targets `netstandard2.0`; 5.0.0-beta-77 targets `net10.0` only.
