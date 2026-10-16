# Finder benchmark, 2026-10-16: the escapes as seeds

The 24 confirmed release escapes of the [fix history](fix-history.md#escapes-by-class) (escape `Y`),
each put back into today's code as a seed, and run against the two finders that need no model: the
test suite and the probe corpus. One row per escape, then totals in Magma's split (seeds that applied,
seeds a finder reached, seeds it detected), then the accounting every probe round and analyst run
reports from now on. This is the first half of the concept's WS5, detection of a reintroduced defect; the
second half, discovery of a defect no finder has seen, has not run.

Measured at `265309f` (`release/2.2.0`), whose `src/` is the same as `e6cdb5e`'s, the commit the probe
footprints below were recorded at. The suite ran on the maintainer's Mac and on a Linux host (see
"Where it ran"), the probes on the Linux host; nothing was changed in the repository to run either.

## What this shows and what it does not

**Today's suite holds the regression test each of these fixes added.** A seed that reverses a fix is
therefore expected to fail that fix's own test, and does: the suite column measures that the guards are
still there and still fail for the right reason (the [verify-the-guard](../.claude/skills/verify-the-guard/SKILL.md)
loop, run again a week to a month later), not that the suite would have found the defect before it shipped.
Each suite cell says whose tests failed: **own** (added or extended by the fix itself), **later** (a test
added after the fix, which still knew the class of defect: a variant catalogue, a docs test), or **older**
(a test that existed before the fix). An older test that fails only on a fixture or an expected text the
fix itself added or changed counts as own.

The interesting rows are the ones only a probe detects, or that nothing detects. The probe corpus was also
written after most of these fixes, and four of its claims are quoted from the 2.1.0 changelog's Fixed
list (C09, C11, C14, C15), so a probe's catch is not independent of the fix either; but no probe was written
from a seed, and none had been run against one before this.

What it does not answer: how fast a finder finds a defect nobody has described to it (that needs held-out
seeds, a separate run), what a finder costs per defect (the suite and the corpus need no model; the rounds
that wrote the corpus are not costed here), or how the review sessions, outside reviews and the
code-analyst would fare (they found 21 of these 24, but they are not re-runnable without a model and a
budget, and they would be reading code that now carries the fix's comments).

## Method

**Seeds.** For each escape, the fix's changes to `src/**/*.cs` reverse-applied at `265309f`, the tests and
docs left as they are. Ten applied as `git diff -R <fix>^ <fix> -- 'src/**/*.cs'` (the brief's
`git revert --no-commit`, restricted to the product source, since a revert of the whole commit removes the
fix's tests as well). Fourteen conflicted with later changes and were reconstructed by hand: the guard the
fix added is neutralised where it now sits, keeping every member the tests call (a condition that can no
longer hold, a call removed, a list cut back to the fix's parent), so the test projects compile and a test
fails for the defect, not for a missing symbol. A bundle fix (`e4b751c`, `92d01dd`) is seeded whole; of
`415ce73`, whose escape was one of five items, only that item. No seed was impossible to reconstruct: for
every fix a line of the guarded code remains. The patches are not kept in the repository.

**Suite.** The whole solution through the verdict tool (`build-verdict.dll test`), once unseeded on each
host and once per seed. A test detects a seed when it fails seeded and passes unseeded on the same host.
`SeedsTests.Every_probe_in_the_repository_has_a_seed_that_applies` (the prober's tests) fails wherever a
seed touches a line a probe's own seed patches, which is a collision of two patches, not a detection; it
is left out of every count.

**Probes.** `prober try` per probe, on a Linux host, against a commit holding the seed (`try` runs the
checkout's `HEAD`). The unseeded baseline ran every one of the 43 registered probes; all pass, no row
violates (L01's row #242 ends inconclusive, seeded and unseeded alike). A probe detects a seed as
`prober seeds` counts a catch ([decision 103](adr/0103-a-harness-error-is-never-a-catch-and-note-keys-are-constants.md)):
a row that ends as a violation, raised no harness error, and does not violate unseeded. A seed was run
against the probes of the surfaces its fix touched (the surfaces in `claims.json`, matched to the files
the fix changed by the `watched` paths of [.claude/probe-axes.md](../.claude/probe-axes.md); a
Generators fix took the generation, text and JSON probes): HTTP 12 probes, EF 16, generators 13,
Option/Result 8. To save builds, the seeds of one surface ran together first and each probe that failed
was then run on each seed alone; every catch attributed this way is a single seed's, and a probe that
passed with all seeds of its surface applied is counted as detecting none of them. Where the probes'
own project no longer compiles under a seed, that is recorded as **build**: a probe's generated
declarations failed to build, which is the C11 claim's check ("these shapes build").

**Reached.** For a probe, a seed is reached when a probe it ran against executed a file the seed changes,
read from the replay's footprints (`footprints.jsonl`, the product files each probe ran, at `e6cdb5e`):
file-level, so an upper bound. A seed that changes only the Generators package is woven at build time
into every probe, and a footprint cannot say whether the woven code it changes ran; those rows say
**compile**. For the suite, reach was not measured: a suite run records no coverage, so the suite's
reached column is its detected column, a lower bound.

**Time to detect** is the probe's own `prober try` time on the Linux host with the probes already built
(`--no-build`), which is what a replay of that probe costs after a commit; a cold build of the probes took
54 to 98 s more there. For a build catch, the build's time.

## The table

Class and finder are the fix history's (these commits predate the `Defect-Class` and `Found-By` trailers
of [decision 94](adr/0094-topic-branches-into-a-release-branch-and-fix-trailers.md), so the appendix of
the fix history is the source). Seed: **rev** reverse-applied, **hand** reconstructed.

Suite cells name the failing tests; `xN` counts failing rows. **Probe** names the first probe and row that
caught the seed (`+n` more rows of it), and any other probe that caught it; **Reach** is the probes, of
those run against the seed, whose footprint holds a file the seed changes. A probe marked "(changelog)" checks a
claim quoted from the 2.1.0 changelog's Fixed list, which names that very fix.

| # | Fix | Class | Found by | Seed | Suite | Probe | Probe s | Reach |
|---|---|---|---|---|---|---|---|---|
| 1 | `f66bb79` | FCI | US | hand | own: `ParameterTests.A_value_object_parameter_publishes_the_schema_of_a_wrapped_type_parameter` x4, the rows the fix added | BaseProbe #37 (+4): an MVC body parameter's schema differs from the wrapped type's | 35.7 | 11 of 12 |
| 2 | `b1bf428` | EFT | FR | hand | own: `QueryOptionTests` x3; later: `A_Func_or_a_method_group_reads_every_row…` (`9422c21`) | E09 #0 (+5): the condition did not reach the database | 4.6 | 8 of 8 |
| 3 | `1ef30be` | GTS | RS | hand | own: `BuildOutcomeTests` CMTK1022 x6, and the two older inventories of build errors, which miss the fix's CMTK1022 | none | | compile |
| 4 | `e779dd3` | CRP | RS | rev | own: `NullTaskTests.A_delegate_that_returns_no_task_is_refused_by_name` x20 | none | | 8 of 8 |
| 5 | `63471e7` | EFT | RS | rev | own: `ForeignConverterTests` x4; later: `QueryTranslationVariantTests` x627 (`a849b57`) | E06 #1 (+4): `.Value` on a column the application converts answered 0 rows instead of failing | 5.2 | 15 of 16 |
| 6 | `92dfe16` | EFT | RS | hand | own: `KeyGenerationTests` x4; later: `KeyGenerationVariantTests` x32 (`a849b57`) | E14 #2 (+3): a TPH root's default keys differ from the plain hierarchy's | 5.0 | 16 of 16 |
| 7 | `e644a8a` | EFT | RS | hand | own: `KeyGenerationTests` x13; later: `KeyGenerationVariantTests` x74, the ulong key at `Add` x2 (`84e9776`) | E04 #8 (+1): the value-object key threw where the plain key is generated | 4.9 | 16 of 16 |
| 8 | `717de54` | EFT | RS | hand | own: `HandWrittenOrderingTests` x17; later: `QueryTranslationVariantTests` x132 | E05 #0 (+65): a comparison by the own `CompareTo` answered instead of throwing | 6.7 | 15 of 16 |
| 9 | `719ee82` | EFT | RS | hand | own: `HandWrittenOrderingTests` query filter x3; later: `QueryTranslationVariantTests` x12 | E05 #2 (+11): a query filter by the own `CompareTo` answered instead of throwing | 6.4 | 16 of 16 |
| 10 | `8f6af8b` | EFT | SF | rev | own: `HandWrittenOrderingTests` "ThenBy a key cast to object" x2; later: `QueryTranslationVariantTests` x12 | none | | 15 of 16 |
| 11 | `53188ed` | GTS | SF | hand | own: `BuildOutcomeTests` x2 (CMTK1005 on a value object over `object`) | none | | compile |
| 12 | `e4b751c` | GTS | RS | hand | own: `BuildOutcomeTests`, `EqualityOperatorBuildTests`, 8 methods x18 | none | | compile |
| 13 | `a4e2fd7` | GTS | RS | rev | own: `ThrowingCreateTests.An_exception_from_Create_leaves_TryParse_as_it_leaves_Parse` x3 | none | | compile |
| 14 | `bef5770` | GTS | RS | rev | own: `DefaultInstanceFormattingTests` x5 | none | | compile |
| 15 | `9534751` | FCI | SF | rev | own: `InlinedValueObjectTests…inlined_enum…` x2 | none | | 11 of 12 |
| 16 | `92d01dd` | GTS | OR | hand | build: `Generators.Tests` does not compile (`ParsingTests.cs:67`, LAMA0611 CS0029) | build: C11 (changelog) (LAMA0611 CS0029 in its declarations) | 58 | 8 of 13, and compile |
| 17 | `415ce73` | FCI | OR | hand, 1 of 5 items | build: `Generators.Tests` does not compile (`ParsingTests.cs:36`, LAMA0611 CS0144) | build: C11 (changelog) (CS0144) | 60–70 | compile |
| 18 | `1ee02fc` | POS | PR | rev | own: `ParsingTests.A_type_whose_converter_was_looked_up_first_still_parses_through_it` x1 | BaseProbe #59 (+5): a form body's parse differs; also L02, L03, L04 | 26.6 | 5 of 13 |
| 19 | `4489c05` | GTS | RS | hand | own: `AssertionTests`, 8 methods: two the fix added, six whose expected text it changed; `ComposedFaultAssertionTests` x3 (`afdd1cd`) fail on that text too, not on the defect, and are not counted as later | none | | 1 of 8 |
| 20 | `b8f9432` | FCI | OR | rev | own: `PatchSchemaTests.A_patch_the_serializer_requires_stays_required` x2 | C13 #3 (+50): a required constructor parameter is not in `required` | 12.5 | 11 of 12 |
| 21 | `27c59d0` | FCI | SF | hand | own: `DescribedSchemaTests` x1; later: `A_factory_in_the_options_is_asked…` (`1b6c805`) | none | | 11 of 12 |
| 22 | `d565948` | FCI | CA | rev | own: `FaultExclusionTests` x2, `FaultStatusScopeTests` x1 | C04 #1 (+33): a status set on `MapControllers()` is missing; also C06, C07, C14 (changelog), C15 (changelog) | 22.6 | 11 of 12 |
| 23 | `92f9bc4` | FCI | CA | hand | own: `FaultResponseTests` x2; later: the declared-problem matrix x252 (`e32a94c`) | C14 (changelog) #1 (+76): a declared 404 names no problem type | 35.7 | 11 of 12 |
| 24 | `461c920` | FCI | PR | rev | own: `ParameterTests.A_parameter_MVC_names_no_type_for…` x3 | BaseProbe #11 (+7): `AddTypeKit()` fails the document; also C03 to C07, C14, C15 | 32.3 | 12 of 12 |

The suite ran on the Mac for rows 1 to 5 and on the Linux host for the rest. The `SeedsTests` collision
(see Method) came up for 9 seeds and is left out above.

## Totals

| Finder | Seeds that applied | Reached | Detected | Detected by a test or row the fix did not add |
|---|---|---|---|---|
| Test suite | 24 | not measured; at least the 24 it detected | 24: 22 by a failing test, 2 by the test projects' build | 9, each a test added after the fix (rows 2, 5 to 10, 21, 23); none older than the fix |
| Probe corpus | 24 | 18 by footprint; the other 6 change the Generators only (compile) | 14: 12 by a row, 2 by the build of C11's declarations | 11 (the 14 less the 3 caught first by a claim quoted from the fix's changelog line: rows 16, 17, 23) |
| Either | 24 | | 24 | 9 by a later test, 11 by a probe, 14 by one or the other |

## What the table shows

- **Nothing is missed.** All 24 escapes, reintroduced, fail the suite today, every one through the
  regression test its own fix added, two by no longer compiling. That is the verify-the-guard loop
  holding a week to a month on, not evidence that the suite would have found them first.
- **Eight escapes rest on their own regression test alone** (rows 3, 4, 11 to 15, 19): no later test and
  no probe catches them. Five of the eight change the Generators alone (rows 3, 11 to 14): refusals the
  generators make at build time (CMTK1005, CMTK1022, CMTK1011, CMTK1021) and generated members' edge
  behaviour (`TryParse` after a throwing `Create`, a default instance's formatting). The axis of refused
  declarations, `shape.refused-kind`, is in the axes file, but every claim that meets it excludes its
  pairs (C09, C11, C18) or narrows it away (L05), so no probe row declares a type the generators must
  refuse; the build-outcome consumers of `Generators.Tests` are the only finder there.
- **The variant catalogues are the strongest second finder.** `QueryTranslationVariantTests` and
  `KeyGenerationVariantTests` ([decision 95](adr/0095-a-fix-is-tested-across-its-input-class.md), written
  2026-10-15) catch six of the seven EF escapes (rows 5 to 10), with 12 to 627 rows each, `8f6af8b`
  among them, which no probe catches; the seventh (row 2) fails a later docs test. Every EF escape is
  caught by something besides its own test.
- **The probes catch 14 of 24**, every row catch within 36 s once the probes are built (median 9.6 s), and every catch
  but three comes from a claim quoted from a README or a docs page, not from the fix. They catch six of
  the eight framework-interplay (FCI) escapes; the two they miss, `9534751` (a nullable value object over
  an inlined enum) and `27c59d0` (a converter of the options' own), are reached by 11 HTTP probes, none of
  whose rows failed.
- **Reached but not detected**, Magma's interesting split: five seeds whose changed file ran in a probe
  without a row failing (rows 4, 10, 15, 19, 21). By a reading of the claims that reach them, not a
  measurement, each wants a row or an obligation the corpus lacks, not a new probe: a delegate that returns
  no task, an ordering key cast to `object`, a nullable value object over an inlined enum, a type nested in
  a generic one named in an assertion's message (C20 checks the verdict, not the text), and a converter in
  the options that replaces the generated one.
- The suite is also slower to tell: a whole run took 53 to 83 s on the Linux host (warm) and 3.7 to 8.7
  minutes on the loaded Mac, against a probe's 5 to 36 s; a replay re-runs only the probes whose
  footprint a commit touched.

## Where it ran

- **Mac** (the maintainer's, macOS, Apple M4 Max): the suite for rows 1 to 5, unseeded 12369 passed, 342
  skipped, 0 failed. The machine was shared with other sessions' replays (load average 50 to 200 on
  16 cores), so its times are long.
- **Linux** (Ubuntu 26.04.1, AMD Ryzen AI Max+ 395, SDK 10.0.402, the system's libicu 78, never in
  globalization-invariant mode): every probe, and the suite for rows 6 to 24. Unseeded, the suite failed
  the 8 `PostgreSqlRoundTripTests` only (no Docker there), which never count; a seed only PostgreSQL could
  catch would not show there. Unseeded, every probe passed with no violating row.
- **Probe groups**: the seeds of one surface applied together, each surface's probes run once, then per
  seed. For every probe the seeds' rows add up to the group's, except one process-wide failure of 1ee02fc's
  (a static `TypeConverters<T>` initializer throws once per process, so a different row reports it); no
  catch needed two seeds together.

## Seeds that could not be reconstructed

None. Every fix left a line of the guarded code in place to neutralise.

## Finder accounting, from now on

The fix history counts findings per finder, never a rate, and nothing here was ever held out. From this
page on, every probe round (`prober round`, `/probe explore` or `full`) and every analyst run
(`code-analyst`, a review session's reviewers) ends with this block in its report, and the run that
writes a dated page under `design/` copies it there. The precedents: Anthropic's documentation-grounded
agent reports valid-bug rates, 984 reports over 100 packages, of a reviewed 50 56% valid and 32% valid and
worth reporting ([Anthropic, 2026-01-14](https://www.anthropic.com/research/property-based-testing));
Magma reinserts real bugs with oracles for reached, triggered and detected
([arXiv 2009.01120](https://arxiv.org/abs/2009.01120)); DARPA's AIxCC scored systems on injected and
real vulnerabilities with cost per task, 54 of 63 injected found
([DARPA, 2025-08-08](https://www.darpa.mil/news/2025/aixcc-results)). The three are cited from the
research digest of 2026-10-16 (sections a and g), which is local to the maintainer's checkout.

```text
Run: <round explore|full, or code-analyst on <target>>, <date>, <commit>, <host>
Budget: <minutes cap, claims cap, USD cap>
Valid findings:     <n>  (a product defect, confirmed; list the ids and, once fixed, the commits)
False findings:     <n>  (reported as a defect, refuted on reproduction or by the code-analyst)
Dismissed findings: <n>  (real behaviour, not TypeKit's defect or intended: `prober dismiss`, with the reason)
Harness errors:     <n rows>  (rows that ended as harness-error; their probes earn nothing)
Triage minutes:     <n>  (wall time from the first failing row to its probe-triage verdict, summed)
Model minutes:      <n>  (and USD where known)
Escapes detected:   <k of 24>  (this page's seeds the run's new probes or tests catch; "not run" is allowed)
```

Where each figure comes from today, and where nothing records it:

| Figure | Recorded by | Not recorded |
|---|---|---|
| Valid | the `Found-By` trailer of the fix commit that repairs it (decision 94) | a finding not yet fixed: the round's report lists it, nothing counts it |
| False | nothing | a finding the code-analyst refutes is said in its report only; count it from the report |
| Dismissed | `tools/probe/corpus/dismissed.jsonl` (`prober dismiss`: id, reason, date) | the analyst's dismissals, which have no file |
| Harness errors | each row's `category` in the replay's evidence (`harness-error`), and `prober try`'s row lines | no total per run: count the rows |
| Triage minutes | nothing | the probe-triage skill records the verdict, not the time it took; note the times in the report |
| Model minutes | `round-last.json` under the prober's state for the branch: `duration_ms`, `num_turns`, `total_cost_usd` of a `prober round` | a round stopped at its cap (the CLI's JSON never arrives, only `seconds`), a round started from the `/probe` skill inside a session, and every `code-analyst` run: take them from the session's usage |
| Escapes detected | this page's method: the seeds, `prober try` per probe, the verdict tool for the suite | the seeds are not in the repository; a run that wants the figure rebuilds them as above |
