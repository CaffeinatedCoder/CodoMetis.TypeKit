# Design

The design of CodoMetis.TypeKit, split by how fast each part changes. Before changing anything, read
this page, the [roadmap](roadmap.md), the [decisions' index](adr/README.md) and the note for the area
you touch; the rest is reference.

| Page | Holds | Changes |
|---|---|---|
| [Roadmap](roadmap.md) | What is open: before the tag, 2.1, 2.x with a spike first, 3.0.0 | as work is taken or done |
| [Decisions](adr/README.md) | One record per decision, dated, with the evidence that settled it; a decision is changed by a new one that names it | append only |
| [Packages](packages.md) | The package map, what each depends on, and the namespaces | rarely |
| [Discovery is by interface](discovery.md) | Why nothing is found by name, and how the analyzer and the satellites find a value object | with the area |
| [EF Core](ef-core.md) | The mapping plugin, the convention, the translators, keys and collections | with the area |
| [Aspects](aspects.md) | The aspects, the fabric, the seams, what the generators refuse | with the area |
| [Test strategy](test-strategy.md) | What is tested where, and the guards that hold the repository to its rules | with the area |
| [OpenAPI](openapi.md) | The schema transformers and what was measured in the document | with the area |
| [Option and Result](option-result.md) | Their shape, the non-goals, the combinators and the async forms | with the area |
| [Analyzer package and rule set](analyzers.md) | Packaging, the rule table with severities, and the limits of each rule | with the area |
| [Native AOT](native-aot.md) | What is measured, how, and what each package asks of a consumer | with the area |
| [History](history.md) | The phase log from 1.0.0 to 2.0.0 as it was kept until 2026-10-13, with the measurements it recorded | closed |

The spikes that decided something are in [spikes/](../spikes/), each with a README stating the
question, the answer and the evidence. A spike's README names the decision it led to.

Until 2026-10-13 all of this was one file, `design/plan.md`, and links into its sections land here.
