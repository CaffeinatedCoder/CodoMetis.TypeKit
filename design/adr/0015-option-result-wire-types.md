# 0015. `Option`/`Result` are not wire types

Date: 2026-09-27
Status: accepted

Context: 2026-09-27, [Option and Result](../option-result.md).

: a `[JsonConverter]` on every exported
struct of the base package throws `NotSupportedException` in both directions. Not a lossless
converter, and not an analyzer.
