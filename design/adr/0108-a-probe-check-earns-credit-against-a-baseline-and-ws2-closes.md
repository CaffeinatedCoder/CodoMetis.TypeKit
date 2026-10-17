# 0108. A probe check earns credit against a baseline, applicability is declared and the harness compares three sides; WS2 closes, and WS3 to WS6 are dropped

Date: 2026-10-17
Status: accepted

Context: 2026-10-16 and 2026-10-17, the second workstream (WS2, the harness) of the concept for finding issues with
agents, which set out to make the probe corpus (`tools/probe/corpus`) and the prober (`tools/Prober`) find defects
mechanically. The gate round of 2026-10-16 that preceded it had written 18 probes and found two product defects and about
ten harness traps, five traps for each defect, and left five failing rows of C07 untriaged (`.claude/skills/probe-triage`).
What the traps had in common was that the harness, not the product, decided a row:

- **A row was credited all or nothing.** A row ended as one category, so one excuse anywhere in it voided the comparisons
  that passed beside it, and one passing comparison credited every setting the row held. At 265309f, the commit before
  the first per-check change, BaseProbe passed 135 of its 411 rows and C07's two probes 545 of 822 (66.3%); L01 credited
  nothing for 160 rows whose write passed, because their read was refused for the wrapped type too.
- **An excuse was a string.** A check noted an outcome key of `NoteKeys` (63 keys at the start of WS2: 25 pass, 20
  inapplicable, 17 expected refusal, 1 inconclusive, [decision 103](0103-a-harness-error-is-never-a-catch-and-note-keys-are-constants.md)),
  and the harness decided rows by an exception's words wherever a substring could tell (`stj-maxdepth`,
  `mvc-400-unwritable-under-source-gen`, `json:read-refused-by-stj-alone`, the OpenAPI 2.0 and enum paths).
- **The baseline was the product beside itself.** The wrapped twin ran with TypeKit, not without it, and at a nullability
  of its own (`Hx.PlainNull`), and a source-generated context fell back to reflection where it did not list a shape
  (`+reflection-fallback`), so about 270 pairs could never be earned as the row named them.
- **Nothing tied a probe to what it read outside its footprint**, the generated harness files had lost their
  generators, and an obligation could not say where it does not apply.

The maintainer ended the programme on 2026-10-17 at WS2 and the 2.2.0 release.

## Decision

**A row runs checks, and the ledger credits checks.** `Row.Check(obligation, baseline, body)` (#41) is one check per
obligation the row's axes select, named by the claim's obligation id ([decision 104](0104-a-claim-states-its-obligations-and-each-has-a-seed.md)).
A returned failure is the check's violation, an exception its harness error, and a pass without a `Baseline` (`Twin`,
`Framework`, `RoundTrip`, `Model`, `Refusal`, `Cost`, `Fact("KB-NNN")`, `Docs("file#anchor")`) a harness error. A row
passes when no check failed and one passed. The ledger credits each passing check with the settings it read and the
row's, never a pair put together from two checks, and `prober seeds` counts a seed naming an obligation caught only by a
clean violation of a check naming it. Evidence is schema version 2.

**An excuse is a closed citation.** A check that did not apply, refused or could not tell says so through its scope:
`c.Inapplicable(Cite…)`, `c.Refused(Cite…)`, `c.Inconclusive(reason)`. A `Cite` names a not-promised entry of the claim,
a docs section, a verified fact or a record, with a string literal; `register`, `publish` and `ProbeCiteTests` refuse
anything else. A fact that prunes or excuses a row is executable, and its measured answer is a fixture beside its test
([decision 107](0107-a-cited-fact-is-executable-and-seeds-run-by-footprint.md)). `NoteKeys` keeps the pass keys, which
say how a check compared, and one outcome key, `framework-refuses`; the 41 outcome keys no source called any more are
gone (27 keys remain).

**Applicability is declared.** An obligation's `inapplicable` entries in `claims.json` (axes and a cite, #50) take a pair
out of what the obligation wants; the universe, `rows`, the gate and `Row.Check` read the same declaration, and a pair no
obligation wants is counted as the gate counts an exclusion.

**The harness compares three sides.** For the http, json and text families (#58, `harness/Sides.cs`): (1) the wrapped
twin without TypeKit, (2) the twin with it, (3) the value object. (1) and (3) refusing alike, by operation, stage,
exception type and status and never by message, is the framework's refusal (`framework-refuses`, an expected refusal
against side (1)'s baseline); one refusing where the other answers is a violation unless the check cites the sentence
that makes it intended; (2) differing from (1) is a violation. Side (1) comes from a fact's fixture where one covers the
row, else it is measured, on every row of an obligation that promises non-interference and lazily elsewhere.
`ProbeMessageTextTests` refuses a message read that excuses a row. **Decision 99's document rule joins it**: a document
ASP.NET alone refuses is compared with the one `AddTypeKit()` refused by type and stage (`Hx.DocAlike`), and leaves a
check that compared an answer on the wire first to be judged by that answer (`doc:refused-by-aspnet-alone`); a check
that compared nothing else is the framework's refusal and earns nothing. No message comparison is kept.

**The twins and the context are generated with the shapes** (#59): the twin is the wrapped type at the value object's
nullability, `Sides.Twins` holds both sides to the same shape, and one `ProbeContext` lists every shape, so
`json.TypeInfoResolver=source-generated-context` is strict and the reflection fallback is the existing
`json.TypeInfoResolverChain=context-then-default`.

**Rows are per probe, and `watched` stales a footprint** (#39): `rows/<probe>.json.gz`, deduplicated, and a registry
entry's `watched` paths stale the probe like its footprint. **The harness generators are C# file-based apps**
(`tools/probe/generators`, #35), held to their output by `GeneratedHarnessTests`.

**The prober closes the loop it left open:** `prober fix F --commit SHA` records a finding fixed; `rows` never places
an excluded pair or one no obligation wants, and leaves out of a row an axis none of whose values can join it;
`prober gaps` lists, from the last replay's evidence, the wanted pairs no obligation wanting them has a passing check
that read both their axes of.

**WS3 to WS6 are dropped**, by the maintainer's decision of 2026-10-17: WS3 (beyond pairs: three-way and deeper
combinations), WS4 (the mutation nightly), the second half of WS5 (the finder benchmark at equal budgets) and WS6
(PostgreSQL in CI). Their work is not scheduled; the roadmap says so.

## Consequences

- **A probe writer** writes one `Row.Check` per obligation the row reaches, with its baseline; cites every excuse or
  ends the check inconclusive; declares in `claims.json` an inapplicability the axes alone decide; builds two-sided
  comparisons with `Sides.Twins` and refuses alike by `Sides.Alike`; never reads a message to excuse a row; names
  `watched` paths the footprint cannot see; and gives each obligation a seed that a check of that obligation catches.
- **The gate counts** pairs earned by passing checks of probes current at their identity and footprint, less exclusions
  and pairs no obligation wants. It now counts nothing for a document both hosts refuse in a check that compared nothing
  else: 2,001 pairs that 401 such rows alone had credited are uncovered (C12 821, C13 48, C16 455, C21 677), nearly all
  under `json.ReferenceHandler=Preserve` or `json.MaxDepth=3`, where System.Text.Json refuses the document for plain
  ASP.NET too (decision 99). Each needs a declaration of its document obligations' inapplicability there, citing record
  0099 or a fact, and rows that hold the rest of those pairs without those settings.
- **Open, not done here:**
  - The EF twins at the value object's nullability (#59): `EfEntry.Plain` is the type `From` takes, and an EF twin is the
    entity's own wrapped-type property; giving EF twins the value object's nullability is its own step, with an EF
    replay.
  - The untriaged candidate rows #59 saw in regenerated arrays it did not commit, open leads for whoever regenerates
    those files next: C21 under OpenAPI 2.0 with a class value object over a struct at the root, where ASP.NET writes the
    twin's nullable body (`double?`, `int?`) without `pattern` and the value object keeps it (rows 6 and 143 of that
    file); a validated struct over `string` as a nullable property, the twin `{"type":"string"}` against the value
    object's `{}` (row 115); a validated class over `Uri`, the twin's `x-nullable` (row 181); a sealed record class over
    `char` at the root, `format: char` alone against `type: string, minLength/maxLength 1` (row 282); a record class
    bound from a form, the twin's `A`/`B` properties (rows 53, 208); `Instant` as a dictionary value (row 134); and C10
    row 266, an array of value objects over `byte` under `default-with-renaming-modifier` with
    `input.json=metadata-property` that does not read back its own document, under `ignore-cycles-unaffected`.
  - The narrowing of the pairs an obligation wants by its own `axes` (#54's option 2): an obligation that never reads an
    axis still wants every pair over it, as C11's parse-shape obligations wanted `shape.wrapped`'s; `prober gaps` now
    lists such pairs, and none is open today.
  - `polymorphic-base-property` as a value of `shape.position` (#51): the axes file is in every probe's identity, so
    adding it stales the corpus and widens every claim over `shape.position`.
- `gaps` reads the last replay's evidence beside the tracked corpus and the generated axes (decision 104 said it read
  only those), and still decides nothing.
- A row that `rows` writes may leave an axis out where it cannot apply; a harness that reads it there ends the row as a
  harness error, never a pass.

## Evidence

Measured 2026-10-17 on Linux x64 (32 threads, otherwise idle) with the pinned SDK. **Before**: release/2.2.0 at
265309f (#39 merged, before #41), replayed from an empty state in a detached worktree with its own built tools.
**Now**: `build/ws2-close-out` at 5c5affe, the same replays. Each replay ran `replay --all --family F` for http (one
probe process at a time, as a replay recording footprints runs), json, text, build and ef, with footprints.

**Replays.** Before: 43 probes, 43 passed; 677 s wall (http 398 s, json 42 s, text 75 s, build 53 s, ef 102 s, each the
replay's own "in all"). Now: 45 probes, 45 passed; 818 s wall (http 502 s, json 50 s, text 83 s, build 68 s, ef 106 s).
The two new probes (C21, C22), side (1)'s hosts and the larger `ProbeContext` (#59: the Samples' rebuild after a harness
change went from 38 s to 74 s) are the difference.

**Row for row, per probe.** A row that runs no `Row.Check` counts as one check. "Rows passing" is `Rec.Run`'s rule: no
violation or harness error in the row or a check, and one check that passed. Regained and lost rows are counted only
where the row is the same at the same index before and now; where rows were regenerated (#49, #59) or appended (#54) the
table says so, and those probes' pairs are in the next table.

| Probe | rows before | checks before | rows now | checks now | rows passing, before → now | regained (category before) | lost (category now) |
|---|---|---|---|---|---|---|---|
| BaseProbe | expected-refusal 39, inapplicable 237, pass 135 | expected-refusal 39, inapplicable 237, pass 135 | expected-refusal 123, inapplicable 109, inconclusive 43, pass 136 | expected-refusal 180, inapplicable 388, inconclusive 63, pass 986 | 135 → 332 | 197 (expected-refusal 30, inapplicable 167) | 0 |
| C01DecimalSpellingProbe | expected-refusal 5, inapplicable 4, pass 108 | expected-refusal 5, inapplicable 4, pass 108 | expected-refusal 5, inconclusive 15, pass 97 | expected-refusal 25, inconclusive 32, pass 528 | 108 → 111 | 3 (inapplicable 3) | 0 |
| C02OptionalTextProbe | pass 46 | pass 46 | pass 46 | pass 52 | 46 → 46 | 0 | 0 |
| C03ComposedProbe | pass 648 | pass 648 | expected-refusal 397, pass 251 | expected-refusal 794, pass 2125 | 648 → 648 | 0 | 0 |
| C04WithoutFaultsProbe | expected-refusal 2, pass 394 | expected-refusal 2, pass 394 | expected-refusal 135, pass 162 | expected-refusal 135, pass 493 | 394 → 297 | 0; 284 of 297 regenerated | 0 |
| C05RuleProbe | pass 645 | pass 645 | pass 645 | pass 1666 | 645 → 645 | 0 | 0 |
| C06FaultKindsProbe | expected-refusal 4, pass 404 | expected-refusal 4, pass 404 | pass 306 | pass 1224 | 404 → 306 | 0; 295 of 306 regenerated | 0 |
| C07BlanketMappingProbe | expected-refusal 1, pass 410 | expected-refusal 1, pass 410 | expected-refusal 44, pass 367 | expected-refusal 44, pass 431 | 410 → 411 | 1 (expected-refusal 1) | 0 |
| C08SameNameWarningProbe | pass 318 | pass 318 | pass 302 | pass 540 | 318 → 302 | 0; 292 of 302 regenerated | 0 |
| C09BaseRecordEqualityProbe | pass 101 | pass 101 | pass 101 | pass 202 | 101 → 101 | 0 | 0 |
| C10PreserveGuardProbe | expected-refusal 14, pass 292 | expected-refusal 14, pass 292 | expected-refusal 100, inapplicable 1, inconclusive 2, pass 203 | expected-refusal 112, inapplicable 1, inconclusive 2, pass 497 | 292 → 294 | 2 (expected-refusal 2) | 0 |
| C11ParseShapesBuildProbe | pass 241 | pass 241 | inapplicable 4, pass 243 | inapplicable 4, pass 243 | 241 → 243 | 0; 6 rows appended | 4 (inapplicable 4) |
| C12DescribedConverterProbe | pass 332 | pass 332 | expected-refusal 156, pass 145 | expected-refusal 338, pass 363 | 332 → 145 | 0; 289 of 301 regenerated | 5 (expected-refusal 5) |
| C13PatchRequiredProbe | expected-refusal 55, pass 301 | expected-refusal 55, pass 301 | expected-refusal 82, inconclusive 51, pass 163 | expected-refusal 105, inconclusive 51, pass 436 | 301 → 273 | 0; 282 of 296 regenerated | 0 |
| C14DeclaredProblemProbe | expected-refusal 3, pass 418 | expected-refusal 3, pass 418 | expected-refusal 273, pass 148 | expected-refusal 633, pass 764 | 418 → 420 | 2 (expected-refusal 2) | 0 |
| C15MvcFaultStatusDocProbe | expected-refusal 7, pass 398 | expected-refusal 7, pass 398 | expected-refusal 104, pass 301 | expected-refusal 104, pass 447 | 398 → 404 | 6 (expected-refusal 6) | 0 |
| C16OptionsAgreementProbe | pass 310 | pass 310 | expected-refusal 93, pass 203 | expected-refusal 127, pass 287 | 310 → 203 | 0; 287 of 296 regenerated | 3 (expected-refusal 3) |
| C17Cmtk0014Probe | pass 310 | pass 310 | pass 283 | pass 512 | 310 → 283 | rows regenerated | rows regenerated |
| C18Cmtk1023Probe | pass 280 | pass 280 | pass 280 | pass 840 | 280 → 280 | 0 | 0 |
| C19Cmtk0010KindsProbe | pass 302 | pass 302 | pass 282 | pass 508 | 302 → 282 | rows regenerated | rows regenerated |
| C20ShouldHoldProbe | pass 261 | pass 261 | pass 261 | pass 727 | 261 → 261 | 0 | 0 |
| C21OpenapiSchemaParityProbe | - | - | expected-refusal 129, inapplicable 4, inconclusive 6, pass 155 | expected-refusal 230, inapplicable 7, inconclusive 12, pass 248 | 0 → 155 | new probe | new probe |
| C22GeneratorRefusalsProbe | - | - | pass 144 | pass 432 | 0 → 144 | new probe | new probe |
| E01ColumnParityProbe | pass 175 | pass 175 | pass 175 | pass 307 | 175 → 175 | 0 | 0 |
| E02ValueTranslatesProbe | expected-refusal 12, pass 203 | expected-refusal 12, pass 203 | inapplicable 12, pass 203 | inapplicable 24, pass 406 | 203 → 203 | 0 | 0 |
| E03ColumnRoundTripProbe | pass 177 | pass 177 | pass 177 | pass 177 | 177 → 177 | 0 | 0 |
| E04IntegerKeyProbe | pass 28 | pass 28 | pass 28 | pass 28 | 28 → 28 | 0 | 0 |
| E05OwnCompareToProbe | pass 90 | pass 90 | pass 90 | pass 90 | 90 → 90 | 0 | 0 |
| E06OwnConverterNoValueProbe | pass 12 | pass 12 | pass 12 | pass 12 | 12 → 12 | 0 | 0 |
| E07ClosedSetNameProbe | pass 9 | pass 9 | pass 9 | pass 11 | 9 → 9 | 0 | 0 |
| E08OptionResultNotColumnsProbe | pass 20 | pass 20 | pass 20 | pass 40 | 20 → 20 | 0 | 0 |
| E09OrNoneInDbProbe | pass 18 | pass 18 | pass 18 | pass 18 | 18 → 18 | 0 | 0 |
| E10ReadBackSkipsValidationProbe | pass 30 | pass 30 | pass 30 | pass 60 | 30 → 30 | 0 | 0 |
| E11UseTypeKitOrderProbe | pass 14 | pass 14 | pass 14 | pass 14 | 14 → 14 | 0 | 0 |
| E12ConventionByTypeProbe | pass 12 | pass 12 | pass 12 | pass 12 | 12 → 12 | 0 | 0 |
| E13PrimitiveCollectionProbe | pass 10 | pass 10 | pass 10 | pass 10 | 10 → 10 | 0 | 0 |
| E14InheritanceKeyProbe | pass 9 | pass 9 | pass 9 | pass 9 | 9 → 9 | 0 | 0 |
| E15CompositeKeyProbe | pass 8 | pass 8 | pass 8 | pass 8 | 8 → 8 | 0 | 0 |
| E16OwnConfigurationWinsProbe | pass 25 | pass 25 | pass 25 | pass 25 | 25 → 25 | 0 | 0 |
| L01JsonBytesProbe | expected-refusal 180, inapplicable 7, inconclusive 1, pass 393 | expected-refusal 180, inapplicable 7, inconclusive 1, pass 393 | expected-refusal 186, inapplicable 112, inconclusive 21, pass 262 | expected-refusal 202, inapplicable 128, inconclusive 22, pass 810 | 393 → 548 | 160 (expected-refusal 160) | 5 (inapplicable 1, inconclusive 4) |
| L02NoQuoteProbe | expected-refusal 29, inapplicable 11, pass 546 | expected-refusal 29, inapplicable 11, pass 546 | inapplicable 218, inconclusive 132, pass 236 | inapplicable 302, inconclusive 159, pass 843 | 546 → 434 | 16 (expected-refusal 15, inapplicable 1) | 128 (inapplicable 96, inconclusive 32) |
| L03OneRuleProbe | expected-refusal 5, inapplicable 13, pass 568 | expected-refusal 5, inapplicable 13, pass 568 | expected-refusal 56, inapplicable 79, inconclusive 21, pass 430 | expected-refusal 75, inapplicable 95, inconclusive 22, pass 1330 | 568 → 561 | 13 (expected-refusal 2, inapplicable 11) | 20 (expected-refusal 14, inconclusive 6) |
| L04RoundTripProbe | expected-refusal 78, inapplicable 127, pass 381 | expected-refusal 78, inapplicable 127, pass 381 | expected-refusal 18, inapplicable 74, inconclusive 59, pass 139 | expected-refusal 21, inapplicable 111, inconclusive 64, pass 994 | 381 → 290 | rows regenerated | rows regenerated |
| L05EqualityProbe | pass 309 | pass 309 | pass 309 | pass 793 | 309 → 309 | 0 | 0 |
| L06OffTheWireProbe | expected-refusal 9, inapplicable 12, pass 304 | expected-refusal 9, inapplicable 12, pass 304 | expected-refusal 21, inapplicable 3, inconclusive 29, pass 272 | expected-refusal 22, inapplicable 15, inconclusive 38, pass 575 | 304 → 303 | 0 | 1 (inconclusive 1) |
| **all** | 10850 rows | 10850 checks (9995 pass) | 10629 rows | 25810 checks (21123 pass) | 9995 → 9871 | 400 | 166 |

The 400 rows regained all earned nothing before (expected refusal or inapplicable). In 391 one excuse had voided a row
whose other comparisons passed (BaseProbe 197, L01 160, L02 16, L03 13, C01 3, C10 2; #53, #57). C07's 1, C14's 2 and
C15's 6 are rows under `json.MaxDepth=3` that the `stj-maxdepth` message read had ended; both hosts now refuse their
documents alike by type, and their checks are judged by the wire they compared (this record). Every one of the 166 rows lost passed before on no comparison, each for its stated reason:

| Probe | Rows | Reason, and where it was decided |
|---|---|---|
| C11ParseShapesBuildProbe | 4 | #174, #180, #183, #189: span parsing over `Uri`, an enum, a flags enum and `Instant`, declared inapplicable by `no-span-parsing-without-the-underlying-types` after #52 narrowed the Generators README (#54) |
| C12DescribedConverterProbe | 5 | this record: a document both hosts refuse alike, in checks that compared nothing else |
| C16OptionsAgreementProbe | 3 | the same |
| L01JsonBytesProbe | 5 | #57: four rows under an application's converter that cannot write the value, where the old branch passed with nothing read back, now inconclusive; #363 declared inapplicable (a closed-set key under a key policy, "Your own converter wins") |
| L02NoQuoteProbe | 128 | #57: 99 rows whose only check was `json-never-repeats` under an application's converter, now declared inapplicable (`not-promised:the-applications-own-converter`), and 29 rows that refuse nothing to quote (a string or `Uri` reads every input, or the options leave the member out), now inconclusive |
| L03OneRuleProbe | 20 | #58: `json-half:wrapped-alone-refuses` took any two refusals for the framework's; 14 refuse alike and end `framework-refuses`, 6 refuse differently and end inconclusive |
| L06OffTheWireProbe | 1 | #58: #240's pass compared two refusals' messages; it is `framework-refuses` by type and path |

**The document rule (this record).** The http family replayed from empty states without footprints at 26e51e6, before
the change, and with it; 13 passed both times:

| Probe | rows before | checks before | rows after | checks after | rows passing | regained | lost |
|---|---|---|---|---|---|---|---|
| BaseProbe | expected-refusal 123, inapplicable 109, inconclusive 43, pass 136 | expected-refusal 180, inapplicable 388, inconclusive 63, pass 986 | expected-refusal 123, inapplicable 109, inconclusive 43, pass 136 | expected-refusal 180, inapplicable 388, inconclusive 63, pass 986 | 332 → 332 | 0 | 0 |
| C03ComposedProbe | pass 648 | pass 2919 | expected-refusal 397, pass 251 | expected-refusal 794, pass 2125 | 648 → 648 | 0 | 0 |
| C04WithoutFaultsProbe | expected-refusal 4, pass 293 | expected-refusal 5, pass 623 | expected-refusal 135, pass 162 | expected-refusal 135, pass 493 | 297 → 297 | 0 | 0 |
| C05RuleProbe | pass 645 | pass 1666 | pass 645 | pass 1666 | 645 → 645 | 0 | 0 |
| C06FaultKindsProbe | expected-refusal 6, pass 300 | expected-refusal 6, pass 1218 | pass 306 | pass 1224 | 306 → 306 | 0 | 0 |
| C07BlanketMappingProbe | expected-refusal 1, pass 410 | expected-refusal 1, pass 474 | expected-refusal 44, pass 367 | expected-refusal 44, pass 431 | 410 → 411 | 1 (expected-refusal 1) | 0 |
| C08SameNameWarningProbe | pass 302 | pass 540 | pass 302 | pass 540 | 302 → 302 | 0 | 0 |
| C12DescribedConverterProbe | pass 301 | pass 701 | expected-refusal 156, pass 145 | expected-refusal 338, pass 363 | 301 → 145 | 0 | 156 (expected-refusal 156) |
| C13PatchRequiredProbe | inconclusive 51, pass 245 | inconclusive 51, pass 541 | expected-refusal 82, inconclusive 51, pass 163 | expected-refusal 105, inconclusive 51, pass 436 | 296 → 273 | 0 | 23 (inconclusive 23) |
| C14DeclaredProblemProbe | expected-refusal 4, pass 417 | expected-refusal 15, pass 1382 | expected-refusal 273, pass 148 | expected-refusal 633, pass 764 | 417 → 420 | 3 (expected-refusal 3) | 0 |
| C15MvcFaultStatusDocProbe | expected-refusal 9, pass 396 | expected-refusal 11, pass 540 | expected-refusal 104, pass 301 | expected-refusal 104, pass 447 | 396 → 404 | 8 (expected-refusal 8) | 0 |
| C16OptionsAgreementProbe | pass 296 | pass 414 | expected-refusal 93, pass 203 | expected-refusal 127, pass 287 | 296 → 203 | 0 | 93 (expected-refusal 93) |
| C21OpenapiSchemaParityProbe | inapplicable 4, inconclusive 6, pass 284 | inapplicable 7, inconclusive 12, pass 478 | expected-refusal 129, inapplicable 4, inconclusive 6, pass 155 | expected-refusal 230, inapplicable 7, inconclusive 12, pass 248 | 284 → 155 | 0 | 129 (expected-refusal 129) |
| **all** | 5033 rows | 13221 checks (12482 pass) | 5033 rows | 13221 checks (10010 pass) | 4930 → 4541 | 12 | 401 |

In each of the 401 rows lost every check that had passed noted a document both hosts refused and compared nothing else
(checked over the evidence: 0 exceptions); the 12 regained are the `json.MaxDepth=3` rows above. Seeded: the
value-object schema transformer throwing an `InvalidOperationException` fails, in 8 sampled rows of C21, the 4 whose
document ASP.NET alone writes ("ASP.NET alone writes it") both before and after; of the 4 whose document ASP.NET alone
refuses, before the change 3 passed (the refusal came first, with the same words) and #156 failed on a message that
differed, and after it all 4 end `framework-refuses`. #156 is what comparing no message gives up.

**`rows` on the whole corpus**, `rows --claim C --full` for each of the 44 claims with the tool before (9e23403, the same
`Covering` as ac4c561) and now: 7,746 rows of which 740 held an excluded pair, against 7,842 rows of which none holds an
excluded pair or one no obligation wants, with no pair left uncovered by either. A first version that gave up a pair no
full row could hold left 3,288 pairs uncovered (C09, C11 and C18 got no row at all) and was replaced by leaving the axis
out of the row.

**Pairs per claim.** `prober status --json`; "wanted" is the claim's pairs less those no obligation wants:

| Claim | 265309f: current / pairs | now: current / wanted | declared inapplicable | excluded or declared since |
|---|---|---|---|---|
| C01-decimal-spelling | 4344 / 4344 | 4344 / 4344 | - |  |
| C02-optional-text | 119 / 126 | 119 / 126 | - |  |
| C03-composed-faults | 15583 / 15607 | 15605 / 15607 | - |  |
| C04-without-faults | 15409 / 15426 | 15427 / 15427 | - | +1 pairs in the universe |
| C05-with-rule | 15750 / 15783 | 15781 / 15783 | - |  |
| C06-fault-kinds | 15231 / 15248 | 15249 / 15249 | - | +1 pairs in the universe |
| C07-fault-responses-blanket | 15945 / 15956 | 15957 / 15957 | - | +1 pairs in the universe |
| C08-same-name-warning | 15609 / 15609 | 15609 / 15609 | - |  |
| C09-base-record-equality | 487 / 487 | 487 / 487 | - |  |
| C10-preserve-guard | 6797 / 6797 | 6797 / 6797 | - |  |
| C11-parse-shapes-build | 1048 / 1048 | 1044 / 1044 | - | -4 pairs in the universe |
| C12-described-converter | 14904 / 14904 | 14083 / 14904 | - |  |
| C13-patch-required | 14731 / 14731 | 14683 / 14731 | - |  |
| C14-declared-problem | 14886 / 14903 | 14904 / 14904 | - | +1 pairs in the universe |
| C15-mvc-fault-status-doc | 15238 / 15255 | 15256 / 15256 | - | +1 pairs in the universe |
| C16-options-agreement | 14904 / 14904 | 14449 / 14904 | - |  |
| C17-cmtk0014 | 1652 / 1652 | 1791 / 1832 | - | +180 pairs in the universe |
| C18-cmtk1023 | 1067 / 1067 | 1067 / 1067 | - |  |
| C19-cmtk0010-kinds | 2091 / 2091 | 2242 / 2292 | - | +201 pairs in the universe |
| C20-testing-shouldhold | 507 / 507 | 507 / 507 | - |  |
| C21-openapi-schema-parity | (no claim) | 13185 / 13883 | 336 |  |
| C22-generator-refusals | (no claim) | 0 / 621 | - |  |
| E01-column-parity | 372 / 414 | 414 / 414 | - |  |
| E02-value-translates-to-column | 400 / 412 | 400 / 406 | 6 |  |
| E03-column-round-trip | 290 / 290 | 290 / 290 | - |  |
| E04-integer-key-generated | 61 / 61 | 61 / 61 | - |  |
| E05-own-compareto-rejected | 90 / 90 | 90 / 90 | - |  |
| E06-own-converter-no-value | 26 / 26 | 26 / 26 | - |  |
| E07-closed-set-stored-as-name | 9 / 9 | 9 / 9 | - |  |
| E08-option-result-not-columns | 47 / 47 | 47 / 47 | - |  |
| E09-or-none-in-database | 69 / 69 | 69 / 69 | - |  |
| E10-read-back-skips-validation | 52 / 52 | 52 / 52 | - |  |
| E11-usetypekit-order | 14 / 14 | 14 / 14 | - |  |
| E12-convention-by-vo-type | 26 / 26 | 26 / 26 | - |  |
| E13-primitive-collections | 10 / 10 | 10 / 10 | - |  |
| E14-inheritance-key-generation | 9 / 9 | 9 / 9 | - |  |
| E15-composite-key | 8 / 8 | 8 / 8 | - |  |
| E16-own-configuration-wins | 25 / 25 | 25 / 25 | - |  |
| L01-json-bytes | 6367 / 6478 | 6473 / 6478 | - |  |
| L02-no-quote | 8280 / 8314 | 8240 / 8314 | - |  |
| L03-one-rule | 7055 / 7084 | 7079 / 7084 | - |  |
| L04-round-trip | 8025 / 8314 | 8312 / 8314 | - |  |
| L05-equality | 1076 / 1076 | 1076 / 1076 | - |  |
| L06-off-the-wire | 8022 / 8022 | 8021 / 8022 | - |  |
| **all** | 226635 / 227295 | 239337 / 242175 | 342 | |

**C07's pass share by check.** Before, each row was one check `claim`: BaseProbe 135 of 411 (32.8%),
C07BlanketMappingProbe 410 of 411; 545 of 822 (66.3%). Now: BaseProbe's checks 986 of 1,617 (61.0%; 332 of its 411
rows pass); C07BlanketMappingProbe by obligation `by-kind` 57 of 76, `enum-needs-own-mapping` 66 of 66,
`mapping-refusal-stops-start` 138 of 138, `member-without-kind-stops-start` 68 of 68, `own-mapping` 38 of 63,
`own-set-keeps-mapping` 64 of 64; 1,417 of 2,092 checks in all (67.7%).

**`NoteKeys`.** 63 keys at the start of WS2; 68 at ac4c561;
27 now: 26 pass keys and `framework-refuses`. The corpus makes 119 `Note` calls (`ProbeNoteKeyTests`' floor stays 100).

**Product defects the conversions and the analyst found**, each fixed in its own pull request:

| Found by | Defect | Fixed in |
|---|---|---|
| C21's first run (#40) | F3: a nullable value object over an enum written by name left out the `null` ASP.NET lists among a parameter's values | #45 (8ffad55, `Found-By: PR`), with a sibling found by the catalogue (25118b8, `VA`) |
| C11's conversion (#54) | a value object over `string` did not implement `ISpanParsable`, as `string` does | #52 (9247406, `GTS`, `PR`) |
| the code-analyst run of 2026-10-16 | A1: a non-abstract polymorphic base's own branch undescribed; A2: a query or header array of a described type lost its `items`; A3: a value object over a described type was not the parameter's schema outside the body | #51 (4917a03, ccfcb5d, 66b4d62, `CA`), with two siblings (5a8f048, 0293dcd, `VA`) |
| the code-analyst | `.Value` through a type parameter; Greatest, Least and row values over an own `CompareTo`; a Guid or string key generated on add | #44 (0764462), #43 (7c6c48f), #46 (d9ccfe0), `EFT`, `CA` |

**Seeds.** `prober seeds --probe P` for every registered probe, from a copy of the final replay's state, in two lanes of
separate worktrees (10,941 s and 11,027 s wall; 21,855 s of seed runs). **170 seeds, 170 caught, each by a check of its own obligation**; BaseProbe's one seed names none, and its `claim` check catches it. Each line names the row and the obligation of the check that caught it:

| Probe | Seed's obligation | Outcome | Rows violating; e.g. the row and the check that caught it |
|---|---|---|---|
| `BaseProbe` | none (`claim`) | caught | 55 of 411 rows; #10 `claim` |
| `C01DecimalSpellingProbe` | `smallest-scale` | caught | 57 of 117 rows; #0 `smallest-scale` |
| `C01DecimalSpellingProbe` | `number-is-a-number` | caught | 43 of 117 rows; #0 `number-is-a-number` |
| `C01DecimalSpellingProbe` | `string-is-a-string` | caught | 54 of 117 rows; #1 `string-is-a-string` |
| `C01DecimalSpellingProbe` | `reads-a-number-or-a-string` | caught | 101 of 117 rows; #0 `reads-a-number-or-a-string` |
| `C01DecimalSpellingProbe` | `keeps-the-scale-it-reads` | caught | 64 of 117 rows; #0 `keeps-the-scale-it-reads` |
| `C01DecimalSpellingProbe` | `covers-every-decimal` | caught | 4 of 117 rows; #32 `covers-every-decimal` |
| `C02OptionalTextProbe` | `hidden-is-an-error-in-both` | caught | 7 of 46 rows; #3 `hidden-is-an-error-in-both` |
| `C02OptionalTextProbe` | `required-blank-is-an-error` | caught | 11 of 46 rows; #2 `required-blank-is-an-error` |
| `C02OptionalTextProbe` | `optional-blank-is-no-value` | caught | 14 of 46 rows; #0 `optional-blank-is-no-value` |
| `C02OptionalTextProbe` | `null-is-blank` | caught | 6 of 46 rows; #6 `null-is-blank` |
| `C03ComposedProbe` | `each-error-own-set` | caught | 487 of 648 rows; #0 `each-error-own-set` |
| `C03ComposedProbe` | `detail-and-rule-intact` | caught | 163 of 648 rows; #2 `detail-and-rule-intact` |
| `C03ComposedProbe` | `statuses-ascending` | caught | 182 of 648 rows; #1 `statuses-ascending` |
| `C03ComposedProbe` | `shared-status-types` | caught | 182 of 648 rows; #1 `shared-status-types` |
| `C03ComposedProbe` | `unmapped-set-stops-start` | caught | 648 of 648 rows; #0 `unmapped-set-stops-start` |
| `C04WithoutFaultsProbe` | `doc-omits-left-out` | caught | 78 of 297 rows; #1 `doc-omits-left-out` |
| `C04WithoutFaultsProbe` | `reporting-left-out-throws` | caught | 238 of 297 rows; #0 `reporting-left-out-throws` |
| `C04WithoutFaultsProbe` | `narrows-wider-status` | caught | 93 of 297 rows; #0 `narrows-wider-status` |
| `C04WithoutFaultsProbe` | `left-out-everywhere-stops-start` | caught | 59 of 297 rows; #11 `left-out-everywhere-stops-start` |
| `C05RuleProbe` | `rule-qualified` | caught | 129 of 645 rows; #4 `rule-qualified` |
| `C05RuleProbe` | `nested-chain` | caught | 125 of 645 rows; #3 `nested-chain` |
| `C05RuleProbe` | `stays-the-errors` | caught | 376 of 645 rows; #2 `stays-the-errors` |
| `C05RuleProbe` | `unnamed-value-throws` | caught | 269 of 645 rows; #0 `unnamed-value-throws` |
| `C05RuleProbe` | `no-namespace` | caught | 247 of 645 rows; #2 `no-namespace` |
| `C06FaultKindsProbe` | `kind-gives-status` | caught | 131 of 306 rows; #3 `kind-gives-status` |
| `C06FaultKindsProbe` | `description-as-detail` | caught | 260 of 306 rows; #1 `description-as-detail` |
| `C06FaultKindsProbe` | `no-description-no-detail` | caught | 306 of 306 rows; #0 `no-description-no-detail` |
| `C06FaultKindsProbe` | `member-must-have-kind` | caught | 306 of 306 rows; #0 `member-must-have-kind` |
| `C07BlanketMappingProbe` | `by-kind` | caught | 40 of 411 rows; #22 `by-kind` |
| `C07BlanketMappingProbe` | `own-mapping` | caught | 27 of 411 rows; #2 `own-mapping` |
| `C07BlanketMappingProbe` | `own-set-keeps-mapping` | caught | 64 of 411 rows; #1 `own-set-keeps-mapping` |
| `C07BlanketMappingProbe` | `enum-needs-own-mapping` | caught | 66 of 411 rows; #0 `enum-needs-own-mapping` |
| `C07BlanketMappingProbe` | `member-without-kind-stops-start` | caught | 33 of 411 rows; #7 `member-without-kind-stops-start` |
| `C07BlanketMappingProbe` | `mapping-refusal-stops-start` | caught | 70 of 411 rows; #4 `mapping-refusal-stops-start` |
| `C08SameNameWarningProbe` | `bare-names-warn` | caught | 90 of 302 rows; #0 `bare-names-warn` |
| `C08SameNameWarningProbe` | `qualified-chain-warns` | caught | 74 of 302 rows; #8 `qualified-chain-warns` |
| `C08SameNameWarningProbe` | `warning-names-both` | caught | 164 of 302 rows; #0 `warning-names-both` |
| `C08SameNameWarningProbe` | `qualified-names-fix` | caught | 74 of 302 rows; #8 `qualified-names-fix` |
| `C08SameNameWarningProbe` | `leave-one-out-fixes` | caught | 138 of 302 rows; #1 `leave-one-out-fixes` |
| `C09BaseRecordEqualityProbe` | `in-a-collection-of-the-base` | caught | 12 of 101 rows; #12 `in-a-collection-of-the-base` |
| `C09BaseRecordEqualityProbe` | `equal-by-the-wrapped-value` | caught | 21 of 101 rows; #1 `equal-by-the-wrapped-value` |
| `C10PreserveGuardProbe` | `refused-where-references-could-be-written` | caught | 4 of 306 rows; #221 `refused-where-references-could-be-written` |
| `C10PreserveGuardProbe` | `value-converters-unaffected` | caught | 4 of 306 rows; #59 `value-converters-unaffected` |
| `C10PreserveGuardProbe` | `own-converters-unaffected` | caught | 15 of 306 rows; #1 `own-converters-unaffected` |
| `C10PreserveGuardProbe` | `ignore-cycles-unaffected` | caught | 4 of 306 rows; #72 `ignore-cycles-unaffected` |
| `C11ParseShapesBuildProbe` | `implicit-result` | caught | 28 of 247 rows; #2 `implicit-result` |
| `C11ParseShapesBuildProbe` | `ref-text` | caught | 22 of 247 rows; #4 `ref-text` |
| `C11ParseShapesBuildProbe` | `abstract-string-constructor` | caught | 22 of 247 rows; #3 `abstract-string-constructor` |
| `C11ParseShapesBuildProbe` | `parsing-where-it-can-be-parsed` | caught | 16 of 247 rows; #37 `parsing-where-it-can-be-parsed` |
| `C12DescribedConverterProbe` | `own-converter-describes-itself` | caught | 115 of 301 rows; #1 `own-converter-describes-itself` |
| `C12DescribedConverterProbe` | `options-describe` | caught | 30 of 301 rows; #26 `options-describe` |
| `C12DescribedConverterProbe` | `every-position` | caught | 145 of 301 rows; #1 `every-position` |
| `C12DescribedConverterProbe` | `parameter-keeps-aspnet` | caught | 73 of 301 rows; #2 `parameter-keeps-aspnet` |
| `C13PatchRequiredProbe` | `doc-required-where-rejected` | caught | 35 of 296 rows; #4 `doc-required-where-rejected` |
| `C13PatchRequiredProbe` | `doc-optional-where-absent` | caught | 120 of 296 rows; #0 `doc-optional-where-absent` |
| `C13PatchRequiredProbe` | `absent-is-default` | caught | 200 of 296 rows; #0 `absent-is-default` |
| `C14DeclaredProblemProbe` | `declared-describes-fault` | caught | 30 of 421 rows; #15 `declared-describes-fault` |
| `C14DeclaredProblemProbe` | `declared-names-types` | caught | 77 of 421 rows; #1 `declared-names-types` |
| `C14DeclaredProblemProbe` | `ascending-with-declared` | caught | 105 of 421 rows; #1 `ascending-with-declared` |
| `C14DeclaredProblemProbe` | `application-json-declared` | caught | 38 of 421 rows; #1 `application-json-declared` |
| `C15MvcFaultStatusDocProbe` | `builder-reaches-doc` | caught | 24 of 405 rows; #30 `builder-reaches-doc` |
| `C15MvcFaultStatusDocProbe` | `without-faults-leaves-doc` | caught | 45 of 405 rows; #20 `without-faults-leaves-doc` |
| `C15MvcFaultStatusDocProbe` | `status-per-scope` | caught | 76 of 405 rows; #0 `status-per-scope` |
| `C16OptionsAgreementProbe` | `differing-refused` | caught | 42 of 296 rows; #6 `differing-refused` |
| `C16OptionsAgreementProbe` | `error-names-type-and-descriptions` | caught | 42 of 296 rows; #6 `error-names-type-and-descriptions` |
| `C16OptionsAgreementProbe` | `patch-held-refused` | caught | 42 of 296 rows; #6 `patch-held-refused` |
| `C16OptionsAgreementProbe` | `same-converters-build` | caught | 86 of 296 rows; #1 `same-converters-build` |
| `C16OptionsAgreementProbe` | `minimal-host-not-compared` | caught | 25 of 296 rows; #0 `minimal-host-not-compared` |
| `C17Cmtk0014Probe` | `a-cast-naming-no-member` | caught | 75 of 283 rows; #4 `a-cast-naming-no-member` |
| `C17Cmtk0014Probe` | `default-and-zero` | caught | 122 of 283 rows; #0 `default-and-zero` |
| `C17Cmtk0014Probe` | `constructor-accessibility-too` | caught | 32 of 283 rows; #4 `constructor-accessibility-too` |
| `C17Cmtk0014Probe` | `reported-where-written` | caught | 139 of 283 rows; #0 `reported-where-written` |
| `C17Cmtk0014Probe` | `a-member-is-silent` | caught | 43 of 283 rows; #3 `a-member-is-silent` |
| `C18Cmtk1023Probe` | `names-each-base-member` | caught | 130 of 280 rows; #0 `names-each-base-member` |
| `C18Cmtk1023Probe` | `a-warning-and-generated` | caught | 280 of 280 rows; #0 `a-warning-and-generated` |
| `C18Cmtk1023Probe` | `generated-equality-never-calls-the-base` | caught | 83 of 280 rows; #4 `generated-equality-never-calls-the-base` |
| `C19Cmtk0010KindsProbe` | `methods-the-compiler-calls` | caught | 59 of 282 rows; #6 `methods-the-compiler-calls` |
| `C19Cmtk0010KindsProbe` | `interface-and-unsealed` | caught | 32 of 282 rows; #1 `interface-and-unsealed` |
| `C19Cmtk0010KindsProbe` | `value-object-and-closed-set` | caught | 28 of 282 rows; #0 `value-object-and-closed-set` |
| `C19Cmtk0010KindsProbe` | `flags-enum-and-read-only-struct` | caught | 33 of 282 rows; #2 `flags-enum-and-read-only-struct` |
| `C19Cmtk0010KindsProbe` | `a-pair-once-at-the-later-argument` | caught | 80 of 282 rows; #6 `a-pair-once-at-the-later-argument` |
| `C20ShouldHoldProbe` | `fails-unless-it-holds-that-reason` | caught | 111 of 261 rows; #0 `fails-unless-it-holds-that-reason` |
| `C20ShouldHoldProbe` | `whichever-of-its-types` | caught | 120 of 261 rows; #1 `whichever-of-its-types` |
| `C20ShouldHoldProbe` | `hands-back-detail-and-rule` | caught | 111 of 261 rows; #0 `hands-back-detail-and-rule` |
| `C20ShouldHoldProbe` | `uninitialized-fails` | caught | 28 of 261 rows; #6 `uninitialized-fails` |
| `C20ShouldHoldProbe` | `a-failure-says-the-outcome` | caught | 122 of 261 rows; #2 `a-failure-says-the-outcome` |
| `C21OpenapiSchemaParityProbe` | `the-underlying-types-schema` | caught | 10 of 294 rows; #15 `the-underlying-types-schema` |
| `C21OpenapiSchemaParityProbe` | `container-elements` | caught | 16 of 294 rows; #0 `container-elements` |
| `C21OpenapiSchemaParityProbe` | `dictionary-key` | caught | 1 of 294 rows; #137 `dictionary-key` |
| `C21OpenapiSchemaParityProbe` | `every-document-version` | caught | 52 of 294 rows; #5 `every-document-version` |
| `C21OpenapiSchemaParityProbe` | `nullable-admits-null-as-the-wrapped-does` | caught | 7 of 294 rows; #63 `nullable-admits-null-as-the-wrapped-does` |
| `C22GeneratorRefusalsProbe` | `every-error-links-the-table` | caught | 79 of 144 rows; #1 `every-error-links-the-table` |
| `C22GeneratorRefusalsProbe` | `unsealed-record-class` | caught | 21 of 144 rows; #5 `unsealed-record-class` |
| `C22GeneratorRefusalsProbe` | `over-value-object` | caught | 20 of 144 rows; #1 `over-value-object` |
| `C22GeneratorRefusalsProbe` | `generic-wrapped` | caught | 24 of 144 rows; #3 `generic-wrapped` |
| `C22GeneratorRefusalsProbe` | `abstract` | caught | 20 of 144 rows; #4 `abstract` |
| `C22GeneratorRefusalsProbe` | `over-system-enum` | caught | 7 of 144 rows; #8 `over-system-enum` |
| `C22GeneratorRefusalsProbe` | `named-as-generated-member` | caught | 20 of 144 rows; #2 `named-as-generated-member` |
| `C22GeneratorRefusalsProbe` | `closed-set-named-as-generated-member` | caught | 4 of 144 rows; #11 `closed-set-named-as-generated-member` |
| `C22GeneratorRefusalsProbe` | `equality-operator` | caught | 20 of 144 rows; #0 `equality-operator` |
| `C22GeneratorRefusalsProbe` | `no-type-half-generated` | caught | 79 of 144 rows; #1 `no-type-half-generated` |
| `C22GeneratorRefusalsProbe` | `over-object` | caught | 6 of 144 rows; #15 `over-object` |
| `E01ColumnParityProbe` | `column-type` | caught | 12 of 175 rows; #0 `column-type` |
| `E01ColumnParityProbe` | `nullable` | caught | 36 of 175 rows; #1 `nullable` |
| `E01ColumnParityProbe` | `facet-kept` | caught | 108 of 175 rows; #1 `facet-kept` |
| `E02ValueTranslatesProbe` | `value` | caught | 58 of 215 rows; #0 `value` |
| `E02ValueTranslatesProbe` | `get-value` | caught | 58 of 215 rows; #1 `get-value` |
| `E02ValueTranslatesProbe` | `value-or-null` | caught | 51 of 215 rows; #2 `value-or-null` |
| `E02ValueTranslatesProbe` | `column-not-cast` | caught | 203 of 215 rows; #0 `column-not-cast` |
| `E03ColumnRoundTripProbe` | `stored-as-underlying` | caught | 5 of 177 rows; #13 `stored-as-underlying` |
| `E03ColumnRoundTripProbe` | `keys` | caught | 35 of 177 rows; #0 `keys` |
| `E03ColumnRoundTripProbe` | `nullable-properties` | caught | 35 of 177 rows; #1 `nullable-properties` |
| `E04IntegerKeyProbe` | `generated-on-add` | caught | 5 of 28 rows; #8 `generated-on-add` |
| `E04IntegerKeyProbe` | `byte-not-generated` | caught | 1 of 28 rows; #9 `byte-not-generated` |
| `E04IntegerKeyProbe` | `char-key-yours` | caught | 1 of 28 rows; #16 `char-key-yours` |
| `E04IntegerKeyProbe` | `guid-key-yours` | caught | 1 of 28 rows; #12 `guid-key-yours` |
| `E04IntegerKeyProbe` | `not-on-efs-list` | caught | 1 of 28 rows; #7 `not-on-efs-list` |
| `E04IntegerKeyProbe` | `own-configuration-wins` | caught | 2 of 28 rows; #18 `own-configuration-wins` |
| `E05OwnCompareToProbe` | `comparison` | caught | 30 of 90 rows; #0 `comparison` |
| `E05OwnCompareToProbe` | `ordering` | caught | 24 of 90 rows; #7 `ordering` |
| `E05OwnCompareToProbe` | `min-max` | caught | 12 of 90 rows; #4 `min-max` |
| `E05OwnCompareToProbe` | `equality-allowed` | caught | 6 of 90 rows; #1 `equality-allowed` |
| `E05OwnCompareToProbe` | `value-order-allowed` | caught | 6 of 90 rows; #11 `value-order-allowed` |
| `E05OwnCompareToProbe` | `query-filter` | caught | 12 of 90 rows; #2 `query-filter` |
| `E06OwnConverterNoValueProbe` | `value-not-translated` | caught | 5 of 12 rows; #1 `value-not-translated` |
| `E06OwnConverterNoValueProbe` | `value-object-comparison` | caught | 7 of 12 rows; #0 `value-object-comparison` |
| `E07ClosedSetNameProbe` | `stored-as-name` | caught | 3 of 9 rows; #2 `stored-as-name` |
| `E07ClosedSetNameProbe` | `in-collections-and-json` | caught | 2 of 9 rows; #2 `in-collections-and-json` |
| `E07ClosedSetNameProbe` | `unknown-name-throws` | caught | 3 of 9 rows; #0 `unknown-name-throws` |
| `E07ClosedSetNameProbe` | `non-member-throws` | caught | 3 of 9 rows; #3 `non-member-throws` |
| `E08OptionResultNotColumnsProbe` | `model-fails` | caught | 10 of 20 rows; #4 `model-fails` |
| `E08OptionResultNotColumnsProbe` | `error-says-so` | caught | 20 of 20 rows; #0 `error-says-so` |
| `E09OrNoneInDbProbe` | `first-reads-one-row` | caught | 3 of 18 rows; #2 `first-reads-one-row` |
| `E09OrNoneInDbProbe` | `single-asks-for-two` | caught | 1 of 18 rows; #9 `single-asks-for-two` |
| `E09OrNoneInDbProbe` | `element-at-skips` | caught | 3 of 18 rows; #4 `element-at-skips` |
| `E10ReadBackSkipsValidationProbe` | `reads-without-create` | caught | 30 of 30 rows; #0 `reads-without-create` |
| `E10ReadBackSkipsValidationProbe` | `revalidate-finds` | caught | 30 of 30 rows; #0 `revalidate-finds` |
| `E11UseTypeKitOrderProbe` | `order-does-not-matter` | caught | 14 of 14 rows; #0 `order-does-not-matter` |
| `E12ConventionByTypeProbe` | `value-object-type-reaches` | caught | 4 of 12 rows; #1 `value-object-type-reaches` |
| `E12ConventionByTypeProbe` | `underlying-type-does-not-reach` | caught | 7 of 12 rows; #0 `underlying-type-does-not-reach` |
| `E13PrimitiveCollectionProbe` | `element-value` | caught | 5 of 10 rows; #0 `element-value` |
| `E13PrimitiveCollectionProbe` | `contains` | caught | 5 of 10 rows; #1 `contains` |
| `E14InheritanceKeyProbe` | `tph-tpt-root` | caught | 4 of 9 rows; #0 `tph-tpt-root` |
| `E14InheritanceKeyProbe` | `tpc-assign-on-sqlite` | caught | 2 of 9 rows; #4 `tpc-assign-on-sqlite` |
| `E15CompositeKeyProbe` | `as-for-primitives` | caught | 6 of 8 rows; #0 `as-for-primitives` |
| `E16OwnConfigurationWinsProbe` | `never` | caught | 2 of 25 rows; #17 `never` |
| `E16OwnConfigurationWinsProbe` | `on-add` | caught | 2 of 25 rows; #9 `on-add` |
| `E16OwnConfigurationWinsProbe` | `column-type` | caught | 5 of 25 rows; #0 `column-type` |
| `E16OwnConfigurationWinsProbe` | `conversion` | caught | 5 of 25 rows; #1 `conversion` |
| `E16OwnConfigurationWinsProbe` | `optional` | caught | 5 of 25 rows; #2 `optional` |
| `L01JsonBytesProbe` | `as-a-dictionary-key-too` | caught | 11 of 581 rows; #56 `as-a-dictionary-key-too` |
| `L01JsonBytesProbe` | `the-underlying-types-json` | caught | 12 of 581 rows; #11 `the-underlying-types-json` |
| `L01JsonBytesProbe` | `read-as-the-underlying-type-reads` | caught | 26 of 581 rows; #20 `read-as-the-underlying-type-reads` |
| `L02NoQuoteProbe` | `parsing-never-repeats` | caught | 183 of 586 rows; #0 `parsing-never-repeats` |
| `L02NoQuoteProbe` | `json-never-repeats` | caught | 199 of 586 rows; #0 `json-never-repeats` |
| `L02NoQuoteProbe` | `no-inner-that-quotes` | caught | 22 of 586 rows; #5 `no-inner-that-quotes` |
| `L03OneRuleProbe` | `parsing-runs-create` | caught | 109 of 586 rows; #3 `parsing-runs-create` |
| `L03OneRuleProbe` | `tryfrom-runs-create` | caught | 30 of 586 rows; #7 `tryfrom-runs-create` |
| `L03OneRuleProbe` | `json-runs-create` | caught | 157 of 586 rows; #0 `json-runs-create` |
| `L03OneRuleProbe` | `refusals-name-the-fault` | caught | 196 of 586 rows; #0 `refusals-name-the-fault` |
| `L04RoundTripProbe` | `invariant-whatever-the-culture` | caught | 21 of 290 rows; #15 `invariant-whatever-the-culture` |
| `L04RoundTripProbe` | `the-type-converter-writes-the-same` | caught | 5 of 290 rows; #14 `the-type-converter-writes-the-same` |
| `L04RoundTripProbe` | `text-reads-back` | caught | 1 of 290 rows; #64 `text-reads-back` |
| `L04RoundTripProbe` | `json-refusal-is-a-json-exception` | caught | 45 of 290 rows; #10 `json-refusal-is-a-json-exception` |
| `L05EqualityProbe` | `through-every-base` | caught | 41 of 309 rows; #0 `through-every-base` |
| `L05EqualityProbe` | `equality-is-the-underlying-values` | caught | 22 of 309 rows; #2 `equality-is-the-underlying-values` |
| `L05EqualityProbe` | `ordering-follows-the-underlying-type` | caught | 238 of 309 rows; #0 `ordering-follows-the-underlying-type` |
| `L06OffTheWireProbe` | `never-written` | caught | 268 of 325 rows; #0 `never-written` |
| `L06OffTheWireProbe` | `never-read` | caught | 267 of 325 rows; #0 `never-read` |

**Tests**, through `build-verdict test --project P` at 5c5affe: Prober.Tests 268 passed (11 new: `FixTests`,
`CoveringTests`, one `GapsTests` case); Conventions.Tests 1,491 passed; Facts 87 passed, 9 skipped (the PostgreSQL facts,
without `TYPEKIT_PG`). `docs-query check`: 0 problems. Each new test fails against a seeded defect, one change at a
time: `fix` writing the commit as given, accepting any status, taking an unknown id, dropping the claim or the probe,
accepting any commit; `Covering` picking a blocked value, the seeded fill taking the first value or ignoring
exclusions; `gaps` counting a check that did not pass, an obligation's reads for a pair it does not want, a claim
without evidence, or no section. `ToolchainTests.A_dotnet_without_the_pinned_sdk_falls_back_to_the_installers` failed
with `PROBE_STATE_DIR` set and passes with it set now. A removed note key called again fails the probes' build with
CS0117.

**The gate**, `prober gate --since v2.1.0` on the final replay's state:

```
probe gate: refused for build/ws2-close-out since v2.1.0 (44 claim(s) touched, 2838 pair(s) uncovered):
  C22GeneratorRefusalsProbe: not replayed at its current identity and footprint (run `prober replay --all`)
  C02-optional-text: 7 of 126 pairs uncovered (touched since v2.1.0)
  C03-composed-faults: 2 of 15607 pairs uncovered (touched since v2.1.0)
  C05-with-rule: 2 of 15783 pairs uncovered (touched since v2.1.0)
  C12-described-converter: 821 of 14904 pairs uncovered (touched since v2.1.0)
  C13-patch-required: 48 of 14731 pairs uncovered (touched since v2.1.0)
  C16-options-agreement: 455 of 14904 pairs uncovered (touched since v2.1.0)
  C17-cmtk0014: 41 of 1832 pairs uncovered (touched since v2.1.0)
  C19-cmtk0010-kinds: 50 of 2292 pairs uncovered (touched since v2.1.0)
  C21-openapi-schema-parity: 698 of 13883 pairs uncovered (touched since v2.1.0)
  C22-generator-refusals: 621 of 621 pairs uncovered (touched since v2.1.0)
  E02-value-translates-to-column: 6 of 406 pairs uncovered (touched since v2.1.0)
  L01-json-bytes: 5 of 6478 pairs uncovered (touched since v2.1.0)
  L02-no-quote: 74 of 8314 pairs uncovered (touched since v2.1.0)
  L03-one-rule: 5 of 7084 pairs uncovered (touched since v2.1.0)
  L04-round-trip: 2 of 8314 pairs uncovered (touched since v2.1.0)
  L06-off-the-wire: 1 of 8022 pairs uncovered (touched since v2.1.0)
```

| Refusal | Since | What it needs |
|---|---|---|
| C22GeneratorRefusalsProbe not current at its footprint | #47 | The probe builds its consumer in a child process, so the in-process coverage shows no file under `src/` and `footprint` refuses an empty one (#54): `watched` paths for the generators, as C18 has, so its footprint records them. Its 621 pairs follow. |
| C12 821, C13 48, C16 455, C21 677 | this record | Checks that compared nothing but a document both hosts refuse no longer pass. A declaration of the document obligations' inapplicability under `json.ReferenceHandler=Preserve` and `json.MaxDepth=3`, citing record 0099 or a fact, and rows (`rows --probe`) for the pairs that remain. C21's other 21 were open at ac4c561. |
| C17 41, C19 50 | #49, #54 | The stored rows substitute excluded refused kinds and credit other values; rows written again with `rows --probe`, which now leaves such an axis out instead. |
| L02 74 | #57 | The own-converter pairs of its TryFrom and formatting rows, whose only check is declared inapplicable there: a declaration on the claim's other obligations or rows that reach them. |
| E02 6 | before WS2 (#56) | `where-value-compare` with a wrapped type that has no `>=` (the probe substitutes `where-equals`): an exclusion with that reason; and one `group-by` row for `nested-two-levels` × `DateTimeOffset`. |
| C02 7, C03 2, C05 2, L01 5, L03 5, L04 2, L06 1 | before or during WS2 | Scattered pairs no stored row holds: rows from `rows --claim`, or exclusions with their reasons. |

None is a finding: 0 are open. Every refusal is answered by a probe's rows, a declaration, an exclusion, or a waiver naming the maintainer as reviewer (decision 97); this record answers none of them.
