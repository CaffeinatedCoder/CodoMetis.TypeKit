# 0112. The prober gives way to CodoMetis.ValueRanges' oracle kit

Date: 2026-10-28
Status: accepted

Context: 2026-10-28, issue #97, while 2.3.0's features landed. The prober (`tools/Prober`, about 5,500 lines) held
44 claims with 169 obligations, 38 probes over pairwise covering arrays, a ledger, a replay by footprint in CI and
a release gate ([decisions 96](0096-ci-replays-every-probe-and-the-release-runs-the-gate.md),
[99](0099-a-probe-row-says-how-it-ended.md) to [104](0104-a-claim-states-its-obligations-and-each-has-a-seed.md),
[107](0107-a-cited-fact-is-executable-and-seeds-run-by-footprint.md) and
[108](0108-a-probe-check-earns-credit-against-a-baseline-and-ws2-closes.md)). Its seeds cost a build of the whole
probe harness each: 170 seeds at about four minutes, so a release's `prober seeds --all` took roughly eleven hours.
One change to the fault responses, #85, staled 23 seeds of five probes, about ninety minutes, and a checkout
whose ledger had never recorded a catch wanted all 170. CodoMetis.ValueRanges had taken the same approach and kept
the parts that pay for themselves (its `docs/oracles.md`): no covering arrays, no ledger, no replay footprints, no
agent, with the sweeps as ordinary tests.

## Decision

**The prober is retired.** The tool, its tests, the corpus under `tools/probe/`, its 170 seeds, the `probes` and
`probe-status` jobs of `dotnet.yml`, the replay and gate steps of `release.yml`, `.claude/probe-axes.md`, the
`probe-triage` skill and the probe conventions tests are removed. DocsQuery's verified facts stay
([decision 93](0093-verified-facts-are-pinned-dated-and-executable-where-they-can-be.md)), fixtures included; its
check of the facts the corpus cited goes with the corpus.

**ValueRanges' oracle kit takes its place**, under `test/oracles/` (its README is the manual):

- `claims.json`: one entry per sentence a consumer-facing document promises, its source heading, the sentence
  verbatim, the oracle kind, the public members it is about and the test classes that hold it. The first
  catalogue is the prober's obligations, one claim per distinct sentence, 150 in all, 112 of them held by a test
  named for it; an empty `heldBy` is the worklist.
- `surface-waivers.json`: every public member of the five run-time libraries is named by a claim or waived with a
  reason. Members whose documents the prober never read are waived as `unread:`, naming #98, which reads them.
- `oracles.json` and `seeds/*.patch`: the oracles are ordinary test classes (five at first: `JsonParityTests`,
  `NullDelegateTests`, `AsynchronousContextTests`, `NotWireTypeTests`, `KeyGenerationVariantTests`), each with a
  seed in `src/`. CI checks, without building, that every oracle has a seed and every seed applies.
- `candidates.json`: known disagreements, each tied to an issue, so a sweep stays green without hiding them.
- `axes.md`: written by hand, what a consumer varies and the pairs no test varies yet.

`test/CodoMetis.TypeKit.Oracles.Tests` holds the catalogue, in a project of its own because the surface is read by
reflection over the libraries, which the conventions project does not reference. `tools/Oracles` is the local tool,
a tool project like the others ([decision 92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)): `seeds`
copies the working tree, applies one seed, builds the oracle's project alone and runs its class alone, in minutes,
and `status` prints the worklist. The seed verdict is one file compiled into both.

**The release runs the suite, which holds the catalogue, and no gate.** Decision 97's gate, which bound from
2.2.0, is withdrawn with the replay it read; 2.2.0 shipped under it. Before a tag, `oracles seeds` shows every seed
caught, as `prober seeds --all` did, at a fraction of the cost.

Superseded: decisions 96, 99, 100, 101, 102, 103, 104 and 108; decision 107 but for its fixture rule, which
DocsQuery keeps; and the gate of decision 97.

Rejected: keeping the prober and dropping only its seeds, since the replay, the footprints and the gate carried the
same weight; and porting the 38 probes as they were, since their harness compiled consumer projects per row. A probe
that found a bug is ported as an ordinary oracle class when its area is next touched, with its seed.
