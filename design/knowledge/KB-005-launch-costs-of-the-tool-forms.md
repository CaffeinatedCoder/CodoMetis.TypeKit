---
verified: 2026-10-15
pins:
  - sdk 10.0.4xx
source: warm runs of a trivial console tool on macOS (arm64) with SDK 10.0.402, timed per launch form, and the Bash guard as Claude Code runs it, measured 2026-10-15
turned-on: decision 92 (a tool run often is a tool project run as its built dll, never dotnet run)
test: none, a timing that a shared CI runner measures too noisily to assert
---

# KB-005. On SDK 10.0.4xx, `dotnet <built dll>` starts in about 0.02 s and `dotnet run --project` in 0.7 to 0.9 s

Warm runs of a trivial console tool, per call:

| How it is launched | Time per call |
|---|---|
| `dotnet <built dll>` | 0.02 s |
| A standalone file-based app (`dotnet run x.cs`) | 0.14 s |
| A file-based launcher with `#:project` pointing at a tool project | about 0.6 s |
| `dotnet run --project` | 0.7 to 0.9 s |

A file-based app's first run builds it, about 1.6 s. Six calls started together against an unbuilt file-based
app failed two or three times in each of three rounds with exit 1 ("being used by another process" on the
build's cache file); built first, 40 concurrent calls all answered. The measurements and their conditions are
[decision 92](../adr/0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)'s evidence.
