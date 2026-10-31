# Parallel work in worktrees

How several fixes and features run at once, each on its own branch in its own worktree, made and cleaned up
by worktrunk (`wt`). [Decision 126](adr/0126-parallel-work-runs-in-worktrunk-worktrees-beside-the-repository.md)
holds the reasons and the measurements; this page holds the commands.

```
~/RiderProjects/
├── CodoMetis.TypeKit/                   ← the main checkout
└── CodoMetis.TypeKit-worktrees/         ← every worktrunk and agent worktree
    ├── fix-ef-boxed-ordering/           ← branch fix/ef-boxed-ordering (`/` becomes `-`)
    └── feat-value-object-arithmetic/    ← branch feat/value-object-arithmetic
```

## Once per machine

```bash
brew install worktrunk                           # or: cargo install worktrunk
wt config shell install                          # then start a new shell
claude plugin marketplace add max-sixty/worktrunk
claude plugin install worktrunk@worktrunk        # loads in new sessions only
```

Then, in `~/.config/worktrunk/config.toml`, under the identifier `wt config show` prints for this checkout
(`github.com/CaffeinatedCoder/CodoMetis.TypeKit` for a clone of the main repository):

```toml
[projects."github.com/CaffeinatedCoder/CodoMetis.TypeKit"]
worktree-path = "{{ repo_path }}/../{{ repo }}-worktrees/{{ branch | sanitize }}"
# A net under the guard's refusal of `wt merge`: nothing lands locally here.
merge.squash = false
merge.rebase = false
merge.ff = false
merge.commit = false
```

The project file `.config/wt.toml` cannot set the path, so each machine sets it. A clone without a remote is
keyed by its path, and adding a remote later changes the key: run `wt config show` again then.

## Once per release branch

```bash
wt config state default-branch set release/2.4.0
```

worktrunk's default branch is what `wt switch -c` branches from, what the plugin's agent worktrees start at,
and what `wt remove` and `wt step prune` call "merged". Left at `main`, a new branch misses the open release's
work and a merged topic never counts as merged. The setting lives in the clone's local git config, so every
clone sets it, and CONTRIBUTING's "Releasing" moves it with the next release branch.

## Every task

```bash
git fetch origin release/2.4.0:release/2.4.0     # the base, current
wt switch -c fix/ef-boxed-ordering --no-cd       # branch from release/2.4.0, worktree beside the repo
```

Start the session *in* `../CodoMetis.TypeKit-worktrees/fix-ef-boxed-ordering`. Its SessionStart hook builds the
guard and the tools there ([decision 105](adr/0105-every-tool-is-built-in-the-session-s-checkout.md)), and
`build-verdict` tests the checkout it was built in, so a session started in the main checkout judges the wrong
tree. Name the branch from the issue's type label (`feat/`, `fix/`, `docs/`, `test/`, `build/`), as PR hygiene
reads it.

To hand subagents a worktree each from one session, make the worktrees with `wt switch -c ... --no-cd` and name
the path in each agent's prompt, without `isolation: "worktree"`.

```bash
wt list                                          # every worktree, ahead/behind release, agent markers
git push -u origin fix/ef-boxed-ordering         # then open the PR into release/2.4.0
```

Work lands by the PR and its merge commit
([decision 94](adr/0094-topic-branches-into-a-release-branch-and-fix-trailers.md)). The Bash guard refuses
`wt merge` and `wt step squash`, `rebase` and `push`, which squash, rebase or fast-forward locally by default.

After the merge:

```bash
git fetch origin release/2.4.0:release/2.4.0     # wt judges "merged" against the local branch
wt remove fix/ef-boxed-ordering                  # deletes the branch once merged; -D forces
wt step prune --dry-run                          # sweeps merged worktrees older than a day
```

## What to know

- **Nothing is copied into a new worktree.** A fresh one builds the solution cold in 27 s and its tools in 2 s
  (measured 2026-10-31): no gitignored file is needed, so there is no `.worktreeinclude` and no `post-start`
  hook. Add both if that changes, and measure first: copied `obj/` folders keep absolute paths into the main
  checkout.
- **Agents with `isolation: "worktree"` follow the plugin**: its hook makes their worktrees with
  `wt switch --create`, so they land beside the repository and start at the default branch (worktrunk's
  documentation; not yet observed here, where agents used to start at `origin/main`). They are named `agent-<id>`
  on a branch of the same name, which PR hygiene refuses, and outlive the agent until `wt remove` or
  `wt step prune`. Pre-creating the worktree avoids both.
- **The desktop app's own worktree sessions ignore the plugin.** They go to `.claude/worktrees/<name>` on
  `claude/<name>`, which `.gitignore` keeps out of `git add -A`.
- **Ids minted in parallel collide**, textually clean: decision records, `design/knowledge/` entries and claim
  ids in `test/oracles/claims.json`. Hand each parallel task its numbers, and check them against the release
  branch right before the merge.
- **The code graph does not cover a worktree outside the repository.** Index it under a name of its own, as
  AGENTS.md's "Finding code" says, or search the source.
- **The plugin installs from worktrunk's `main`, not a release.** Its worktree hooks need `wt` on Claude Code's
  `PATH` (`/opt/homebrew/bin` on macOS). Check `claude plugin list` after `claude plugin update`.
- **The guard reads words, not every shell.** `wt` called through a wrapper (`cargo run --bin wt`, a variable)
  passes it.
