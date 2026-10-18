# 0109. Work is tracked as labelled issues under a release epic, and CI checks each pull request's labels, milestone, branch and issue link

Date: 2026-10-18
Status: accepted

Context: 2026-10-18, after the release of 2.2.0. The repository had 66 pull requests and not one issue: none
of the 66 descriptions closed an issue, and the repository had no milestone and no label beyond GitHub's
defaults and Dependabot's two. Work was planned in [the roadmap](../roadmap.md) and recorded in the commits,
the decision records and [the fix history](../fix-history.md), so what was open, what a pull request was for
and which release it belonged to could be read only from files and branch names. CodoMetis.ValueRanges, the
sibling library, tracks its work as labelled issues and has a CI check that holds each pull request to its
labels and its issue; this repository adopts the same scheme, fitted to its own branches (decision 94) and
its own tooling (decision 92).

## Decision

**Work is tracked as issues.** A bug, an enhancement, a follow-up, a finding from a review or a probe: each is
an issue, opened before the work starts, through one of the forms in `.github/ISSUE_TEMPLATE/` (Bug,
Enhancement, Epic; no blank issues, and a security report goes to the private advisory form).

**A release is an epic.** Each version has an `epic` issue and a milestone of the same name (`2.3.0`); its work
is attached as native sub-issues and carries the milestone. A theme that spans releases is an `epic` without a
milestone. [The roadmap](../roadmap.md) stays the plan and the decision records the reasons: an epic links its
section of the roadmap rather than copying it, and there is no roadmap issue.

**Labels, on issues and pull requests alike**, as ValueRanges has them:

| Label | Means |
|---|---|
| `bug`, `enhancement`, `documentation`, `tests`, `chore` | The type, exactly one |
| `severity/critical`, `severity/high`, `severity/medium`, `severity/low` | How bad a bug is, exactly one on a bug |
| `silent` | A bug that compiles or answers and does less than it appears to |
| `breaking` | Changes a signature, a generated member, a wire form or a diagnostic's severity: needs a major |
| `found-by/prober` | Found by the prober; the fix's commit carries `Found-By: PR` |
| `stacked` | Based on another open pull request |
| `epic` | A release or a theme; its sub-issues are the work |
| `package/core`, `package/analyzers`, `package/generators`, `package/efcore`, `package/aspnetcore`, `package/serilog`, `package/testing` | The package affected, one per package |
| `area/option-result`, `area/value-objects`, `area/json`, `area/aot`, `area/parsing`, `area/release`, `area/tooling` | The cross-cutting area |

ValueRanges' `found-by/oracle` is `found-by/prober` here, its oracles' counterpart; its `roadmap` label has no
counterpart, there being no roadmap issue. The legend a contributor reads is CONTRIBUTING's "Issues and labels".

**A pull request carries its issue's labels and milestone** and opens with a closing keyword (`Closes #12`),
from the template in `.github/pull_request_template.md`: Problem, Change, Tests. Since `Closes` closes an issue
only on a merge into `main`, and a topic merges into its release branch, the issue is closed by hand when its
topic merges, with a comment linking the pull request; the release's pull request into `main` closes its epic.

**Held by CI.** `.github/workflows/pr-hygiene.yml` runs on every pull request into `main` or a release branch,
and again when its labels, milestone or description change. It runs `tools/PrHygiene`, a tool project under
decision 92 (BCL only, tested in `test/CodoMetis.TypeKit.PrHygiene.Tests`), on the event payload alone, and
fails a pull request that has

- not exactly one type label, or a bug without exactly one severity, or `silent` on anything but a bug;
- no closing reference in its description;
- a head branch not named `<type>/<slug>` (decision 94), or whose type disagrees with its type label: `feat` is
  `enhancement`, `fix` is `bug`, `docs` is `documentation`, `test` is `tests`, and `build`, `refactor`, `spike`
  and `release` are `chore`;
- a base `release/<version>`, or a head `release/<version>` into `main`, without the milestone `<version>`;
- the label `breaking` and a release branch that is not a major (`x.0.0`).

ValueRanges' script checks the first two; the branch and milestone rules hold decision 94's naming and the
release a pull request belongs to, which here are visible in the payload. Its rule for a `silent` fix in a minor
has no counterpart, since this repository ships no "changed answers" section; its rule that a breaking change
waits for a major is the one above. Dependabot's pull requests are not checked.

Its job, "Labels and issue link", is a required status check: on `main` in its branch protection, beside
the test, pack and smoke-test jobs, and on `release/**` through the ruleset "Release branches: PR hygiene",
which does not apply to creating a branch, so a release branch is still cut by a push.

## Consequences

- What is open, and in which release, is a GitHub query rather than a reading of the roadmap.
- The labels count what the commit trailers count from the other side: `found-by/prober` beside `Found-By: PR`,
  and the severity a trailer does not record.
- A pull request that misses a label fails a check that turns green when the label is added, without a commit.
- A pull request from a fork builds the check from its own checkout, so it can weaken the check on its own run
  only, where the reviewer reads the diff; the workflow uses `pull_request`, never `pull_request_target`, and has
  a read-only token.

## Evidence

- Counted on 2026-10-18 with `gh`: 66 pull requests, 0 issues, 0 milestones; 0 of the 66 descriptions match
  GitHub's closing keywords.
- CodoMetis.ValueRanges at 1da4fa8: `.github/ISSUE_TEMPLATE/{bug,enhancement,epic,config}.yml`,
  `.github/pull_request_template.md`, `.github/workflows/pr-hygiene.yml` and `.github/scripts/pr-hygiene.sh`,
  and its 35 labels.
- The check's rules were each proved by deleting the rule from `tools/PrHygiene` and watching its cases fail
  (12 seeds, each caught).
