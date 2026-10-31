# 0126. Parallel work runs in worktrunk worktrees beside the repository, based on the release branch

Date: 2026-10-31
Status: accepted

Context: 2026-10-31, issue #265, at the start of 2.4.0. 2.3.0 closed with eight PRs in flight at once and 2.3.1
with ten, each in a worktree of its own. Those worktrees came from three places, each with a flaw:

- **The desktop app's worktree sessions** go to `.claude/worktrees/<name>` on `claude/<name>`, inside the
  repository, kept out of `git add -A` only by a line in `.git/info/exclude`, which no clone carries. On this day
  the main checkout had 19 linked worktrees, 17 of them there, several detached and several for merged work.
- **Agents with `isolation: "worktree"`** started at `origin/main`, not at the release branch: on 2026-10-13 three
  of four were 49 commits behind `release/2.0.0`.
- **Hand-made worktrees** (`git worktree add`) under the scratchpad and `/tmp`, each named and removed by hand.

The maintainer adopted worktrunk (`wt`, 0.81.0, and its Claude Code plugin at commit `2c04fb370427`) in another
repository the same day, with a handoff of what was measured there. This record fits it to this repository,
whose work lands by a pull request into `release/<version>` with a merge commit
([decision 94](0094-topic-branches-into-a-release-branch-and-fix-trailers.md)), not by a local merge into `main`.

## Decision

**Worktrees go beside the repository**, at `../CodoMetis.TypeKit-worktrees/<branch, sanitized>`, made by
`wt switch -c <type>/<slug>`. The path is set in each machine's worktrunk user config, since the project file
cannot set it. Outside the repository there is no ignore rule to keep right. `.claude/worktrees/` joins the
tracked `.gitignore` for the desktop app's sessions, which ignore the plugin.

**worktrunk's default branch is the open release branch**, `wt config state default-branch set release/<version>`,
set per clone and moved by CONTRIBUTING's "Releasing" when the next release branch is cut. One setting makes
`wt switch -c` branch from the release, the plugin's agent worktrees start there (its hook runs
`wt switch --create`), and `wt remove` and `wt step prune` judge "merged" against the branch topics merge into.

**Nothing lands locally.** The Bash guard refuses `wt merge` and its building blocks `wt step squash`, `rebase`
and `push`, past worktrunk's global options and except with `--help`: by default they squash, rebase and
fast-forward onto the default branch, and none writes a merge commit. The user config turns the four `merge.*`
keys off as a net. The guard reads the words the shell would pass, like its other rules; `wt` called through a
wrapper or a variable passes it.

**Nothing is copied into a new worktree.** No `.worktreeinclude` and no `.config/wt.toml` hook, since a fresh
worktree needs no gitignored file to build, and so no hook waits on `wt config approvals add`.

[design/worktrees.md](../worktrees.md) is the runbook, linked from AGENTS.md and CONTRIBUTING.

## Evidence

Measured 2026-10-31 on macOS (arm64), SDK 10.0.4xx, in a worktree `wt switch -c build/worktrunk-worktrees` made
with no `--base` from `release/2.4.0` at `3be21dc`:

| Step | Result |
|---|---|
| `wt switch -c`, default branch set to `release/2.4.0` | branch and worktree from `release/2.4.0`, at `../CodoMetis.TypeKit-worktrees/build-worktrunk-worktrees` |
| the guard and the tools, cold, as SessionStart builds them | 2 s, no error |
| the whole solution, cold, through `build-verdict build` | 27 s, 33 projects, no error, nothing copied in |
| `CodoMetis.TypeKit.BashGuard.Tests` | 112 passed |

The other repository measured a copy of its build caches at 1.9 GiB in 6.3 s, saving about 2 s, and found the
copied `obj/` holding absolute paths into the primary checkout; the compiler servers and the NuGet cache are
shared per machine, which is why a cold worktree builds fast here too.

The guard's rule was shown to fail against two seeded defects: the detection removed (17 of the new cases fail:
every refused `wt merge` and `wt step` form, and the message test), and the value of `-C`, `--config` and
`--config-set` read as a subcommand (4 fail: the forms after `-C`, after `--config-set`, a step after options, and
the message test).

Not measured: an agent's `isolation: "worktree"` under the plugin starting at the release branch. The plugin
loads only in a session started after its install, and worktrunk's documentation is the source for its hook
running `wt switch --create`.

## Consequences

- A session works in the worktree it was started in. `build-verdict` tests the checkout it was built in, so a
  session in the main checkout that edits a worktree judges the wrong tree, and the code graph does not cover a
  worktree outside the repository.
- Every clone sets two things the repository cannot: the worktree path in worktrunk's user config and the default
  branch in its local git config. A clone that sets neither gets worktrunk's sibling layout (`../repo.branch`)
  and branches from `main`.
- An agent worktree made by the plugin is named `agent-<id>`, which PR hygiene refuses as a head branch, and
  outlives its agent; the runbook pre-creates worktrees for subagents instead.
- The plugin follows worktrunk's `main`. Its behaviour is re-checked after `claude plugin update`.
