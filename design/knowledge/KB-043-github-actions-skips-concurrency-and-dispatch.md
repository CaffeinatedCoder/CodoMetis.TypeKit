---
verified: 2026-10-29
pins:
  - external GitHub-Actions 2026-10-29
source: docs.github.com, "Using conditions to control job execution", "Control the concurrency of workflows and jobs", "Reuse workflows" and "Triggering a workflow" (events triggered by the GITHUB_TOKEN), and "Disabling and enabling a workflow", read through Context7 on 2026-10-29; the skipped-required-check rule as issue 70 states it, not reproduced on a live pull request
turned-on: decision 113 (the gate job, the concurrency group of dotnet.yml under release.yml's call, and the nightly dispatch)
test: none, a hosted service's behaviour, read from its documentation
---

# KB-043. On GitHub Actions, a skipped job passes a required check, concurrency groups ignore case and a called workflow takes its caller's name, and the GITHUB_TOKEN can dispatch a workflow

- **A skipped job reports Success.** A job skipped by its `if`, or because a job it needs failed, does not
  block a merge even if it is a required check. A required check cannot tell a deliberate skip from an
  accidental one. A job that judges the others under `always()` can.
- **A concurrency group's name ignores case.** `prod` and `Prod` are the same group.
- **A called workflow uses its caller's name in `${{ github.workflow }}`.** A reusable workflow whose
  workflow-level group is `${{ github.workflow }}-${{ github.ref }}`, called from a workflow whose group is
  `release-${{ github.ref }}`, shares that group with its caller (the names differ only in case) and waits
  for it.
- **The GITHUB_TOKEN can start a run by dispatch.** Events the GITHUB_TOKEN triggers create no new workflow
  run, except `workflow_dispatch` and `repository_dispatch`, which always do. So a scheduled job with
  `actions: write` can dispatch the build workflow on other branches.
- **A schedule stops.** In a public repository, GitHub turns scheduled workflows off after 60 days without
  repository activity, and a schedule runs only from the default branch.
