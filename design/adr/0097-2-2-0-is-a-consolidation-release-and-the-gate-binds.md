# 0097. 2.2.0 is a consolidation release, and the probe gate binds from it

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, an independent review of the repository at `1863f32`, the head of `release/2.2.0`;
amends where [decision 88](0088-2-1-0-ships-without-its-open-roadmap-items.md) placed the open roadmap
items, the "Releasing" section of `AGENTS.md`, which left the gate advisory until a release was named, and
[decision 96](0096-ci-replays-every-probe-and-the-release-runs-the-gate.md), whose release step reported a
refusal and went on.

The review's numbers, measured again here: 399 commits since 2026-09-27, 135 of them `fix:`; five tags in
18 days, a major two weeks after 1.0.0. By the fix history's count, 69 fixes came from review sessions and 5
from users. The machinery meant to replace that exposure has not run yet: the corpus holds 42 claims and 11
registered probes, `prober status` on `release/2.2.0` found no universe, the gate is advisory, and no `fix:`
commit since v2.1.0 carries the trailers of [decision 94](0094-topic-branches-into-a-release-branch-and-fix-trailers.md).
Also, `AGENTS.md` said CI runs the gate, but no workflow did, until decision 96 added the step as advisory.

**Decision.**

- **2.2.0 adds no public API.** It adds no type, member, overload or generated member, and no new
  analyzer rule. It takes fixes to behaviour that is already documented, each with its trailers. It also
  takes performance work measured before and after, guards, docs and repository tooling. A Warning may
  still learn what it misses, as [decision 34](0034-errors-fixed-within-major-version-warnings.md) allows.
- **The additive items move to 2.3.0 unchanged:** the `Cause` on a fault
  ([decision 52](0052-cause-on-fault-joins-2-1.md)), the opt-in that withholds the `fault` name
  ([decision 64](0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md)) and the default `ulong`
  key ([decision 63](0063-char-keys-application-assigned-ulong-documented.md)) stay decided, with 2.3.0 as
  their release. So do the gaps that add surface: async test assertions, `CatchAsync` over a `ValueTask`,
  and `TraverseAsync` on `Option` and `Patch`.
- **2.2.0's own work** is the probe corpus, grown until the gate passes on probes and reviewed waivers;
  the gaps that are defects; and the generated-surface snapshot.
- **The gate binds from 2.2.0.** No tag of 2.2.0 or later goes over a refusal without the user's word. A
  refusal is answered by a probe in the corpus, a fix, or a waiver in `tools/probe/corpus/waivers.json`
  that names its reviewer; the reviewer is the user's word. `release.yml`'s gate step ends with the gate's
  exit code, so a refusal fails the release before it packs, and `ReleaseWiringTests` fails for a step that
  catches the code without ending on it or that carries `continue-on-error`.

Not decided here: a cadence for later releases, which the 2.2.0 cycle should inform, not a rule written
before it.
