# 0092. Repository tooling is .NET, in two forms; git and the pinned SDK are the only prerequisites, and CI needs no AI

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, making the repository contributable on Windows as well as macOS and Linux.

The tooling a contributor or CI ran was written for macOS and Linux. `test/consumer-smoke-test.sh` is
bash and calls `sed` 12 times, `awk` 7 times and `grep` 10 times. The verdict tool and the Bash guard
of the agent tooling were bash and Python, and the prober's Python core uses `fcntl`, `killpg` and
`os.kill(pid, 0)`, which on Windows terminates the process it was meant to probe. CI ran only on
`ubuntu-latest`, so nothing caught a Windows breakage. The .NET SDK is already required and pinned in
`global.json`, which makes it the natural single prerequisite.

## Decision

**Tooling is C#, in one of two forms, chosen by how it is used.** Text processing is C# (`Regex`,
`System.Text.Json`), never `sed`, `awk` or `grep`.

1. **A standalone file-based app**, `dotnet run tools/x.cs`, for a small tool run rarely: one file, no
   tests of its own. `tools/probe/enumerate.cs` is the example.
2. **A tool project** for a tool that is tested, longer than a few hundred lines, or run many times a
   round or on every tool call: a hook on every Bash call, the verdict tool on every build and test,
   the probe ledger. Such a tool
   - is a project under `tools/<Name>/` that references the BCL only, no package, with its tests in
     `tools/<Name>.Tests/`;
   - is in the solution, so `dotnet build` builds it and CI runs its tests;
   - is run as its built dll, `dotnet tools/<Name>/bin/Release/net10.0/<Name>.dll`, never
     `dotnet run --project`;
   - never runs stale, and never silently: `tools/Directory.Build.targets` embeds the path, relative to
     the repository root, and the SHA-256 of each of its sources, with a schema version and the
     tool's `ToolVersion`. Content, never mtimes, which a checkout rewrites. At startup the tool hashes
     the checkout's sources (`tools/Shared/SourceFingerprint.cs`); on a difference, a changed, removed
     or new source, it refuses and names `dotnet build tools/<Name> -c Release`. It never rebuilds
     itself in place: Windows refuses to overwrite a dll that is running. No shell wrapper checks
     staleness, since that is what breaks Windows;
   - writes every file to a temporary file beside it and renames it into place;
   - compares strings ordinally, and does not set `InvariantGlobalization` unless its test host does
     too, or its tests pass against behaviour the tool does not have.

   A tool project run from `bin/Release` does not collide with the builds it starts: `dotnet build`
   without `-c` writes `bin/Debug`, and a Release build of an unchanged tool copies nothing.

**Scripts only in `spikes/`.** `ShellScriptTests` in the conventions tests refuses a tracked `.sh`,
`.bash`, `.py` or `.ps1`, or a file whose `#!` line names no `dotnet`, anywhere else. Its allow-list
holds the two ports still to come, `test/consumer-smoke-test.sh` and the Python of `tools/probe/`, and
shrinks as each lands.

**Hooks are one-line shims.** A git hook or a Claude Code hook execs `dotnet` and holds no logic; Git
for Windows runs a git hook through its bundled `sh`, so a one-line shim works on every platform.
Claude Code's hooks build the tool projects they run in a SessionStart hook.

**Prerequisites.** A contributor needs git and the SDK that `global.json` pins. A task that needs more
names it as its own prerequisite, and no other task needs it: the platform linker for the smoke
test's Native AOT consumer (clang on Linux and macOS, MSVC's C++ build tools on Windows), and Docker
for the PostgreSQL round trips.

**The test job runs on `ubuntu-latest`, `windows-latest` and `macos-latest`.** Packing, the smoke test
and the SBOMs stay on Linux. The workflow had said a second runner "would re-test the .NET runtime
rather than this library"; the second and third test the repository's own assumptions (paths, line
endings, child processes, the tooling), which no runtime test covers. The Windows and macOS legs leave
out `PostgreSqlRoundTripTests`, since neither runner can start a Linux container.

**CI never depends on an AI being available.**

- No workflow invokes `claude`, an agent or a model's API, or needs an AI key or secret.
  `CiWithoutAiTests` in the conventions tests refuses a workflow that names one.
- No CI check may fail because a model cannot be reached.
- AI only produces committed artifacts: code, tests, probes, documents. CI checks them with the .NET
  SDK alone.
- A hook that exists for Claude Code, such as the Bash guard, is a local convenience and never a CI
  step. CI runs its tests as it runs any other code's, with the SDK alone.
- No test reads agent state (`.claude/agent-memory-local`, `~/.claude`): CI reads only tracked files.

## Evidence

Measured on 2026-10-15 on macOS (arm64) with the pinned SDK 10.0.402, warm runs of a trivial console
tool:

| How it is launched | Time per call |
|---|---|
| `dotnet <built dll>` | 0.02 s |
| A standalone file-based app (`dotnet run x.cs`) | 0.14 s |
| A file-based launcher with `#:project` pointing at a tool project | about 0.6 s |
| `dotnet run --project` | 0.7 to 0.9 s |

The file-based launcher with `#:project` did pick up an edit to the referenced project. A Python
script starts in about 0.04 s. A file-based app's first run builds it: about 1.6 s.

The Bash guard, measured as Claude Code runs it on every Bash call: 0.03 s as its built dll, 0.07 s as a
cached file-based app, 0.01 s as its Python original. As a file-based app it was also unsafe to race:
six calls started together against an unbuilt guard failed two or three times in each of three rounds
with exit 1 ("being used by another process" on the build's cache file), which Claude Code reads as
allow. Built first, by the SessionStart hook, 40 concurrent calls all answered.
