# Design

The design of CodoMetis.TypeKit, split by how fast each part changes. Before changing anything, read
this page, the [roadmap](roadmap.md), the [decisions' index](adr/README.md) and the note for the area
you touch; the rest is reference.

| Page | Holds | Changes |
|---|---|---|
| [Roadmap](roadmap.md) | What is open: 2.2.0, gaps for any 2.x, 2.x with a spike first, blocked upstream, 3.0.0 | as work is taken or done |
| [Decisions](adr/README.md) | One record per decision, dated, with the evidence that settled it; a decision is changed by a new one that names it | append only |
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

The spikes that decided something are in [spikes/](../spikes/), each with a README stating the
question, the answer and the evidence. A spike's README names the decision it led to.

Until 2026-10-13 all of this was one file, `design/plan.md`, with a phase log that git holds
(`git show 4237abb:design/plan.md`) and the changelog summarises; links into its sections land here.
