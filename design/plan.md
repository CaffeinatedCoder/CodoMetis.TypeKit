# Design

The design of CodoMetis.TypeKit, split by how fast each part changes. Before changing anything, read
this page, the [roadmap](roadmap.md), the [decisions' index](adr/README.md) and the note for the area
you touch; the rest is reference.

| Page | Holds | Changes |
|---|---|---|
| [Roadmap](roadmap.md) | What is open: 2.2.0, gaps for any 2.x, 2.x with a spike first, blocked upstream, 3.0.0 | as work is taken or done |
| [Decisions](adr/README.md) | One record per decision, dated, with the evidence that settled it; a decision is changed by a new one that names it | append only |
| [Verified facts](knowledge/README.md) | What the frameworks TypeKit builds on do, one fact per file, pinned, dated and sourced, most with a characterization test; a stale one settles nothing | as research settles a question |
| [Packages](packages.md) | The package map, what each depends on, and the namespaces | rarely |
| [Discovery is by interface](discovery.md) | Why nothing is found by name, and how the analyzer and the satellites find a value object | with the area |
| [EF Core](ef-core.md) | The mapping plugin, the convention, the translators, keys and collections | with the area |
| [Aspects](aspects.md) | The aspects, the fabric, the seams, what the generators refuse | with the area |
| [Closed sets](closed-sets.md) | The closed-set aspect, what must stay true of a set, and where each satellite meets one | with the area |
| [Patch](patch.md) | `Patch<T>`'s three states, its converter under Native AOT, and where each satellite meets one | with the area |
| [Test strategy](test-strategy.md) | What is tested where, and the guards that hold the repository to its rules | with the area |
| [OpenAPI](openapi.md) | The schema transformers and what was measured in the document | with the area |
| [Option and Result](option-result.md) | Their shape, the non-goals, the combinators and the async forms | with the area |
| [Analyzer package and rule set](analyzers.md) | Packaging, the rule table with severities, and the limits of each rule | with the area |
| [Native AOT](native-aot.md) | What is measured, how, and what each package asks of a consumer | with the area |
| [Fix history](fix-history.md) | Every `fix:` to 2026-10-15 by defect class, finder, sibling chain and release escape: the baseline for reviewing | when the classification is run again |
| [Mutation survivors, 2026-10-16](mutation-2026-10-16.md) | The first Stryker.NET run, on the Serilog satellite: counts per package, each survivor read as a missing assertion or an equivalent mutant, and the valid mutants the compile lost | one dated page per run |
| [Finder benchmark, 2026-10-16](finder-benchmark-2026-10-16.md) | The 24 release escapes put back as seeds: which test, probe row or nothing detects each, totals per finder as applied, reached and detected, and the accounting every probe round and analyst run reports | one dated page per run |
| [Oracles](../test/oracles/README.md) | What the documents promise as claims held by named tests, every public member claimed or waived, the oracle classes and their seeds, the candidates and the axes a consumer varies ([decision 112](adr/0112-the-prober-gives-way-to-valueranges-oracle-kit.md)) | with each feature and each promise a document gains |
| [Parallel work in worktrees](worktrees.md) | The runbook for worktrunk: worktrees beside the repository, based on the release branch, landed by PR, and what to know ([decision 126](adr/0126-parallel-work-runs-in-worktrunk-worktrees-beside-the-repository.md)) | when worktrunk or the plugin changes |
| [The code graph](code-graph.md) | What the codebase-memory graph was measured to find and miss, and how to keep it current; AGENTS.md's "Finding code" rests on it | when the graph is measured again |
| [Probing without the tooling](probing-without-tooling.md) | How BLite applied the probing concept by hand, with no prober, covering array or ledger: what each tool function became, the process as it ran, the issues and PRs it produced, and an adoption guide for trying the same here without the tooling | copied 2026-10-18; as the method is tried here |

The spikes that decided something are in [spikes/](../spikes/), each with a README stating the
question, the answer and the evidence. A spike's README names the decision it led to.

Until 2026-10-13 all of this was one file, `design/plan.md`, with a phase log that git holds
(`git show 4237abb:design/plan.md`) and the changelog summarises; links into its sections land here.
