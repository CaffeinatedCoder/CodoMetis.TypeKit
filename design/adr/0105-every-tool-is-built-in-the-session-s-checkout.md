# 0105. Every tool is built in the session's checkout, with a dotnet that has the pinned SDK

Date: 2026-10-17
Status: accepted

Context: 2026-10-17, an analysis of the agent sessions of 2026-10-15 and 2026-10-16, the first two days of the
tooling [decision 92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md) ported to C#.

The port worked: raw `dotnet build` and `dotnet test` calls fell from about 1,000 a day to none within an hour
of the guard, and the verdict replaced the hand-written greps of German MSBuild output that 83% of build and test
calls had carried. Two things did not:

- **Tools missing in worktree sessions.** 32 calls in 30 sessions failed because `build-verdict.dll`,
  `prober.dll` or `docs-query.dll` was not there, every one in a session the desktop app had started in a
  worktree, and agents then built the tools by hand about 113 times. The SessionStart hook built only the guard
  and the verdict tool, and built them in `$CLAUDE_PROJECT_DIR`, which in a desktop worktree session is the main
  checkout, not the worktree the session works in (Claude Code's worktree documentation: "Hook paths don't
  follow the worktree").
- **Session-start builds that failed unseen.** 70 of about 100 session starts reported "A compatible .NET SDK
  was not found" for both builds. On the maintainer's Mac a hook's PATH, unlike a login shell's, puts Homebrew's
  `dotnet` (SDK 10.0.401) before the installer's (`/usr/local/share/dotnet`, 10.0.402), and global.json pins
  10.0.402 with `latestPatch`. Homebrew's formula stays because PowerShell depends on it. A SessionStart hook
  that exits non-zero shows the user a notice and the agent nothing, so a session went on without its tools,
  and in a worktree without its guard: a PreToolUse hook whose dll is missing fails, and a failing hook allows
  the call. The verdict tool already fell back to the installer's `dotnet`; the hook, the prober and the
  smoke test did not, so agents prefixed `export PATH=/usr/local/share/dotnet:$PATH` to every call.

The guard also refused `dotnet msbuild <project> -getProperty:X -getItem:Y`, a question about a project that
the verdict tool has no form for; the agent that met it dropped the approach.

## Decision

**SessionStart builds every tool, each where it is run from.** Two commands:

- the guard, in `$CLAUDE_PROJECT_DIR`, because the PreToolUse hook runs it from there;
- every other tool, through `tools/AgentTools.slnf`, by a path relative to the session's directory, so a
  worktree session builds its own. The filter lists every tool project but the guard; `SessionStartHookTests`
  discovers the projects under `tools/` and refuses one the filter leaves out.

**A dotnet without the pinned SDK falls back to the installer's, everywhere a tool starts one.** Each hook
command builds with the `dotnet` on PATH, then once more with `/usr/local/share/dotnet` first on PATH, the SDK
installer's default location and the place the verdict tool already falls back to. `tools/Shared/Dotnet.cs` holds
the same rule for the tools: `dotnet --version` in the directory decides whether a dotnet has the pinned SDK,
the installer's (`%ProgramFiles%\dotnet` on Windows, or `TOOLS_DOTNET_FALLBACK_DIR`) is used when only it has,
and its directory goes first on the child's PATH and into `DOTNET_ROOT`. The fallback is started by its full path:
.NET looks for a bare `dotnet` beside the host that runs the tool before it reads PATH, so a tool Homebrew's
`dotnet` runs would start Homebrew's again. The prober starts every `dotnet` that way, the smoke test decides once
at its start, and the verdict tool takes its fallback location from it. When
neither dotnet has the SDK, the one on PATH stays, so its own error reaches whoever reads the output, and the
prober's toolchain refusal still says so.

**A session start that builds nothing says so to the agent.** Each hook command ends in an `echo` naming what did
not build and the command that shows why, and exits 0: Claude Code adds a SessionStart hook's stdout to the
agent's context only on exit 0. This is the one place a hook holds more than a call to `dotnet`: three steps joined
by `||`, which `sh` on macOS and Linux and Git Bash on Windows, where Claude Code runs hooks, all read.

**The guard lets a project query through.** `dotnet msbuild` with `-getProperty` or `-getItem` (as `-`, `--` or
`/`, any case) and no `-t`/`-target`, `-getTargetResult` or `-r`/`-restore` passes. Without a target MSBuild stops
after evaluation and writes nothing; with one it builds, and the guard refuses it as before.

## Evidence

Measured 2026-10-17 on macOS (arm64), Homebrew's `dotnet` first on PATH as a hook sees it:

| Hook, in a new worktree | Result |
|---|---|
| guard, nothing built yet | Homebrew's fails (exit 155), the fallback builds it in 1.9 s, exit 0 |
| tools, nothing built yet | the same, all four built in 1.7 s, exit 0 |
| guard and tools, built | 1.7 s and 1.6 s, exit 0 |
| global.json pinning an SDK neither has | both commands print their message on stdout, exit 0 |

`dotnet msbuild src/CodoMetis.TypeKit/CodoMetis.TypeKit.csproj -getProperty:TargetFramework
-getProperty:IsRoslynComponent` and `-getItem:ProjectReference` answered in 0.8 s each and wrote no file under
`obj/` or `bin/`. MSBuild's `XMake.cs` stops evaluation after the properties pass, or the items pass, when
`-getProperty` or `-getItem` comes without a target.

Each guard was shown to fail against a seeded defect: the prober starting `dotnet` without the resolver
(`ToolchainTests.A_dotnet_without_the_pinned_sdk_falls_back_to_the_installers`, which runs the built prober
under a pin its own dotnet lacks, with a stub in the installer's place), the resolver never falling back, a child
left without `DOTNET_ROOT`, the guard refusing a query, the guard reading a query that runs a target as one,
the guard missing a `/getProperty` switch, the filter missing a tool, the tools built in `$CLAUDE_PROJECT_DIR`,
and a hook without the fallback.

## Consequences

- An agent no longer needs to prefix `PATH` for the tools, the prober's replay and the smoke test. A raw `dotnet`
  in its own Bash command still resolves Homebrew's; that is the machine's PATH, which the repository does not
  set.
- The installer's location is fixed in the hook text. On a machine whose installer put `dotnet` elsewhere the
  fallback finds nothing, and the message says so.
- A worktree a session enters after its start (EnterWorktree) gets no session-start build; the tool's missing dll
  names itself, and `dotnet build tools/AgentTools.slnf -c Release` builds them all.
