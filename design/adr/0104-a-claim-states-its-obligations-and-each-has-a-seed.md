# 0104. A claim states its obligations, and each has a seed

Date: 2026-10-16
Status: accepted

Context: 2026-10-16, the first workstream of the concept for finding issues with agents, which read the probe
corpus claim by claim against what each probe checks. C07 (`C07-fault-responses-blanket`, the AspNetCore
README's "Every fault set at once") had one registered probe when
[decision 100](0100-every-probe-is-shown-to-fail-against-a-seeded-defect.md) gave every probe a seed, and that
probe, `BaseProbe`, never checked the claim's central promise: that one call maps every fault set. Its seed
dropped the wrapped type's `format` from a value object's OpenAPI schema, which `BaseProbe` caught, so the probe
passed, it could be shown to fail, and the corpus's count of passing probes had an unchecked claim inside it.
A seed that breaks any assertion of a registered probe shows that the probe can fail. It does not show that the
claim is checked. Decision 100 asked for a seed per probe; this record asks for one per obligation, a thing the
claim promises that a probe can check on its own, and for the claim to say in the corpus what those are and
what it does not promise.

## Decision

**A claim lists its obligations.** An entry of `tools/probe/corpus/claims.json` takes `obligations`, each an
`id` (`^[a-z0-9][a-z0-9-]*$`), a `sentence` quoted verbatim from the claim's source document, and, where the
obligation is not the whole claim, the `axes` values it concerns. A claim without the field has one obligation,
the claim itself, under the id `claim` (`Claim.ImplicitObligation`).

**A claim lists what it does not promise.** `not-promised` takes one entry per intended difference: an `id`, the
`source` (`file#heading`) of the document or record that makes it intended, its `sentence` quoted verbatim, and
the `axes` values it concerns. A note of outcome expected-refusal, a narrowing or an applicability condition
cites an entry by its id.

**Every quote is checked as a narrowing's is.** `Axes.Build`, which `universe`, `gaps` and every command that
builds the universe call, collapses whitespace and requires each obligation's sentence in the claim's source and
each not-promised sentence in the entry's own source. A sentence the document no longer holds is a refusal that
names the claim and the id (`claim C1: obligation keys quotes a sentence not found in README.md`), never a stale
promise, and so is an id listed twice. The schema (`Schemas/claims.schema.json`) holds the form, as
[decision 102](0102-an-exclusion-carries-its-reason.md) made it hold an exclusion's.

**A seed per obligation.** A registry entry takes `seeds`, one per obligation its probe checks, each a seed as
decision 100 defines it (`patch`, `env` or both, and `defect`) with `obligation` naming the id it breaks. `seed`
may name an obligation too. A seed that names none proves the probe, and counts for the implicit obligation
`claim` only, so for a claim without `obligations`. `prober seeds` runs each seed of a probe on its own, judges
it as decision 100 and [decision 103](0103-a-harness-error-is-never-a-catch-and-note-keys-are-constants.md) say,
and reports it as `P/<obligation>` in its output and in `seeds-last.json`. Its check without a build
(`Seeds.Problems`, which `SeedsTests` runs over the repository) refuses a probe without a seed, a seed that does
not apply, a seed naming an obligation its claim does not list, and a listed obligation of a claim with probes
that no seed of them names.

**`status` shows the obligations.** `prober status --json` gives each claim its `probes` and its `obligations`,
each with `seeded_by`, the probes whose seeds name it. The text form ends each claim's line with
`obligations N, each seeded` or `obligations N (K without a seed: ids)`.

**`gaps` reports what the corpus leaves out.** `prober gaps [--base REF]` prints, each under its heading with its
count and its recorded reason: the generated settings no surface reaches and nothing ignores, the settings
without values, the declarations the generators refuse (`shape.refused-kind`), the values the frameworks refuse
on their own (`refused`), every not-promised entry and narrowing by what it concerns, and the surfaces the axes
file's new `out-of-reach` object declares, each with why no probe reaches it. A heading with nothing says
`none`. It reads the tracked corpus and the generated axes only, runs no model, and decides nothing: it is the
list a release checklist and a feature-research round read, and no command fails on it.

## Consequences

- The work that follows is the walk over every claim, family by family: list its obligations from its source,
  move each intended difference into `not-promised`, and give each obligation a seed in the probe that checks it,
  or a probe where none does. Until a claim is walked it keeps the implicit obligation, which every probe with a
  seed already meets, so this record fails no current probe.
- A claim's obligations are the gate's next unit after pairs. Today the gate counts pairwise coverage per claim;
  an obligation without a seed is visible in `status` and refused by the check without a build only for a claim
  that lists it.
- `out-of-reach` starts with PostgreSQL (the EF probes run SQLite in memory) and Native AOT (measured by the
  consumer smoke test only).

## Evidence

Measured 2026-10-16 on macOS (arm64) with the pinned SDK, on `build/obligations-per-claim`:

- **The tests.** `ObligationsTests` and `GapsTests` in `test/CodoMetis.TypeKit.Prober.Tests`, 15 cases, run the
  commands in-process on a scratch repository, `prober seeds` with a fake adapter whose row N violates while the
  seed's `SEED_ROW` is N, and compare output with line endings normalised, so Windows runs them too.
- **Each test fails against its seeded defect,** one line of the prober's source changed at a time:

  | Seed | Fails | How |
  |---|---|---|
  | `Axes.Build` drops the refusal of an obligation whose sentence its source does not hold | `An_obligation_quoting_a_sentence_its_source_does_not_hold_is_refused` | `universe` exits 0 |
  | the same for a not-promised entry against its own source | `A_not_promised_entry_is_checked_against_its_own_source` | `universe` exits 0 |
  | `Axes.Build` drops the refusal of an obligation id listed twice | `A_duplicate_obligation_id_is_refused` | `universe` exits 0 |
  | `Claim.ObligationIds` returns the listed ids alone, without the implicit `claim` | `A_claim_without_obligations_has_one_the_claim_seeded_by_a_seed_naming_none` | `status --json` gives C1 no obligation |
  | `status` drops the obligations from a claim's line | `Status_shows_the_obligations_no_seed_breaks`, both line ends | the line ends at `C1: 0/4` |
  | `Seeds.Problems` adds nothing for a listed obligation no seed names | `The_check_without_a_build_names_an_obligation_no_seed_breaks` | no problem where `nulls` was expected |
  | `Seeds.Problems` drops the check that a named obligation is listed | `The_check_without_a_build_names_a_seed_naming_an_obligation_the_claim_does_not_list`, both cases | no problem for `kyes` |
  | `seeds` runs only a probe's first seed (`seeds.Take(1)`) | `Seeds_runs_each_seed_under_its_obligation` | exit 0: `P/nulls`, the missed seed, never ran |
  | `gaps` drops its "out of reach" section | both `GapsTests`, all four cases | the heading and its entry are missing |
  | `gaps` prints a count of 0 where it said `none` | `A_corpus_without_gaps_says_none_under_each_heading`, both line ends | `out of reach: 0` and the like |

- **`prober gaps` on this branch** prints, at `776c9f6`, for the tracked corpus (the axes enumerated at
  `JsonSerializerOptions` 10.0.0.0, `OpenApiOptions` 10.0.12.0 and `MvcOptions` 10.0.0.0): no setting that no surface reaches, no setting without values, 4 declarations the
  generators refuse, 3 values the frameworks refuse, 64 narrowings over 22 claims and no not-promised entry yet,
  and 2 surfaces out of reach. `prober status --json` gives each of the 42 claims, all with probes, its one
  implicit obligation `claim`, and every one is seeded, so `Seeds.Problems` over the repository stays empty.
- **A slip found on the way.** `seeds-last.json` gained `obligation`, which `Schemas/seeds-run.schema.json` did
  not list under its `additionalProperties: false`; the schema now has it, a string or null.
