# 0100. Every probe is shown to fail against a seeded defect

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, an outside review of the prober. Every fix and every new test here is proven by
reverting it or by seeding the defect it claims to catch ([verify-the-guard](../../.claude/skills/verify-the-guard/SKILL.md),
AGENTS.md "Workflow" step 3). The probe corpus had no such step: no probe had ever been shown to fail
against a defect in the product. A probe whose checks all end as skips, refusals or a comparison of the
product with itself passes on every commit and earns its claim's pairs, and nothing would notice. The
review's first items made a row's outcome exact ([decision 99](0099-a-probe-row-says-how-it-ended.md): a
closed catalogue of notes, a row that earns nothing ends as a skip); this record is the step after it: each
probe proves that it can fail.

## Decision

**A seed per registered probe.** A probe's entry in `tools/probe/corpus/registry.json` names one seeded
product defect:

```json
"seed": { "patch": "seeds/L01-json-bytes.patch", "defect": "A dictionary key is written with ToString(), not the wrapped type's key converter and DictionaryKeyPolicy." }
```

`patch` is a unified diff, relative to the corpus directory, against the product files only (the replay's
`include`, `src/` here). `env` is an environment toggle set for the seeded build and every run; the product
reads none today, and none is added to shipping code for this. `defect` says, in one sentence, what the
seed breaks. A seed is the defect the claim is about, one a consumer would meet: a facet dropped, a refusal
that quotes its input, a rule skipped. It is not part of the probe's identity, so writing or changing one
stales no result. The schema is `tools/Prober/Schemas/registry.schema.json`.

**`prober seeds [--probe P]`.** For each probe that is not retired, it applies the seed in a scratch
worktree of its own (`seeds`, beside the replay's `wt`, under `PROBE_CACHE_DIR`), builds, runs the probe
in every one of its process variants, reads how each row ended, and undoes the seed. The seed is **caught**
only by a row that ends as a `violation` and does not in the unseeded probe. The unseeded rows come from
the ledger where the probe's evidence is current, else from an unseeded build and run first. A harness
error, an inconclusive row or a skip is not a catch: it says the harness broke, not that the claim's check
fired. Every other end fails the command (exit 1): a seed **missed**, a probe with **no seed**, a seed that
**does not apply** (gone, outside the product files, or its lines moved), and a seeded **build failed**,
which proves nothing about the probe. Nothing a seeded run produces is recorded as evidence or as a
finding; the command writes `seeds-last.json` (`Schemas/seeds-run.schema.json`) and takes no lock. It runs
in neither the checkout nor the replay's worktree.

**Local, not CI.** It starts no model, so [decision 92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)
would allow it in CI, but it builds once per seed. It runs after a probe is written or its harness changes,
and before a release. CI holds the part that needs no build: `SeedsTests` in the prober tests fails while a
registered probe has no seed, or a seed's patch touches anything outside `src/` or no longer applies.

**Pairwise coverage is a floor, not a guarantee.** The gate's measure, every pair of a claim's axes covered
by a row that passed, says that each combination of two settings ran once. A defect that needs three
settings at once (a value object over a decimal, as a dictionary key, under a naming policy) is in the
covering array only by chance, and a pass says nothing about the rows the array never generated. A seed
shows that a probe can fail; it does not show that the probe covers its claim. Where a defect is known to
live in a class of input, the class is tested over its variant catalogue ([decision 95](0095-a-fix-is-tested-across-its-input-class.md)),
every entry rather than every pair, and that is where combinations of three or more settings are
covered.

## Consequences

- Registering a probe takes a seed: `prober register ... --seed seeds/<claim>.patch --defect "..."`, and
  `.claude/probe-axes.md` tells the writer how to make one.
- A product change that moves a patched line fails `SeedsTests` until the seed is made again against the
  new code. That is the cost of a seed that keeps proving something.
- A seed is itself a claim about the product, and is measured like one: the first seed written for
  E01 (a nullable value object left unmapped when the plugin does not unwrap `Nullable`) was missed,
  because EF unwraps `Nullable` before it asks a type mapping plugin. The defect was not real, and the
  command said so.

## Evidence

Measured 2026-10-15 on macOS (arm64) with the pinned SDK, at 168ac2a (`fix/probe-soft-outcomes` at 8ee899d
with this change), from an empty state: one unseeded build (21 s) and run of all 11 probes, no violation in
any of them, then one seeded build and run per probe, 553 s in all.

| Probe | Claim | Seed (file under `src/`) | Rows violating / run |
|---|---|---|---|
| `BaseProbe` | C07 | the value object's schema drops the wrapped type's `format` (`ValueObjectSchemaTransformer.cs`) | 52 / 346 |
| `C03ComposedProbe` | C03 | a composed fault's statuses listed descending (`FaultResponsesOperationTransformer.cs`) | 94 / 345 |
| `C05RuleProbe` | C05 | an enum rule named without its nesting chain (`Fault.cs`) | 63 / 347 |
| `E01ColumnParityProbe` | E01 | an enum value object mapped as a text column (`ValueObjectTypeMappingSourcePlugin.cs`) | 6 / 90 |
| `E02ValueTranslatesProbe` | E02 | `.Value` on a column translated as a cast (`ValueObjectTranslators.cs`) | 81 / 108 |
| `E04IntegerKeyProbe` | E04 | an integer key declared never generated (`ValueObjectKeyValueGenerationConvention.cs`) | 5 / 28 |
| `E05OwnCompareToProbe` | E05 | a hand-written ordering not refused in a query (`HandWrittenOrderingInterceptor.cs`) | 66 / 90 |
| `L01JsonBytesProbe` | L01 | a dictionary key written with `ToString()` (`GeneratedJsonPlan.cs`) | 6 / 289 |
| `L02NoQuoteProbe` | L02 | parsing through the wrapped type's `Parse`, which quotes the input (`GeneratedParsing.cs`) | 92 / 296 |
| `L03OneRuleProbe` | L03 | the text entries build a validated value object without `Create` (`ValueObjectAspect.Templates.cs`) | 56 / 296 |
| `L04RoundTripProbe` | L04 | formatting in the current culture, not the provider (`GeneratedFormatting.cs`) | 15 / 296 |

The first run, on `release/2.2.0` at 6e630d6, caught 9 and failed on two misses, which is how both seeds
were found wrong: E01's (above) and L02's, which changed only the string `TryParse` while the generated
`Parse` of most wrapped types goes through the span overload. Seeding that overload too caught 92 rows.

The command's tests (`SeedsTests`) were shown to catch each of these, seeded one at a time in
`Seeds.cs` and `Ledger.cs`: the baseline ignored (a violation the probe already had counted as the
seed's), the seed left in the worktree, a patch outside `src/` accepted, a missed seed exiting 0, the seed
made part of the identity, a probe without a seed counted caught, and a seeded build failure ignored. The
repository check fails when one registered probe's seed is removed.
