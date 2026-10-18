# Fix history, classified (2026-10-15)

Read-only analysis of the 133 `fix:` commits on `feat/2.1.0` (`git log --grep='^fix' -E feat/2.1.0`, head `dc459b0`). Nothing in the repository was changed. This note is the baseline for the review approach (variant analysis, shape catalogues, redesign reviews, lens reviews, defect tracking). Consumers are called "downstream".

From the 2.2.0 cycle on, each `fix:` commit records its class and finder itself, in the trailers `Defect-Class:` and `Found-By:` with the codes below ([decision 94](adr/0094-topic-branches-into-a-release-branch-and-fix-trailers.md)), so the next classification reads them from `git log` instead of reconstructing them.

## How to read this

- **Class** is the primary cause of the defect. A commit that fixes several defects (16 of 133, flagged **B**) is counted once, under the class of its main defect. **S** marks a defect whose symptom was silent (compiled or ran and did less or something else); it is a flag, not a class, so a defect can be class `GTS` and still silent. 58 of 133 carry S. Class `SDL` is kept for the three commits where nothing more specific than "silent" could be said.
- Three classes were added to the starting list because the data asked for them: **AOT** (reflection or trimming, 4 commits), **API** (the public surface let a misuse compile, 9) and **TOOL** (test or tooling infrastructure, 2).
- **Found by** comes from the commit body, the pasted text a session started with, the reports a session wrote, and the agent calls in the local Claude Code session transcripts (44 sessions and their subagent transcripts; commit hashes rarely appear there, so 130 of 133 commits were matched by the text of their subject in a tool call; for the other 3, `ae9b444`, `92d01dd` and `461c920`, the session was found from the commit body and the sessions' reports).
- **Escape** `Y` = the defective code and behaviour were in a tagged release (earliest tag named); `P` = a pre-existing reach gap or Warning-severity false positive of an analyzer that shipped, then widened by a release decision; `N` = never released (fixed before the first tag, or the defect came from code of the same unreleased cycle).

## Headline numbers

- 133 fixes: 37 before v1.0.0 (2026-09-27/28), 1 in the 1.5.1 cycle, 64 in the 2.0.0 cycle, 31 since v2.0.0 (the 2.1.0 cycle, no tag yet; the changelog section is dated 2026-10-14). By local author date: 13 on 09-27, 24 on 09-28, 1 on 10-11, 60 on 10-13, 31 on 10-14, 4 on 10-15.
- Escaped into a release: **24 confirmed (`Y`) and 6 analyzer-reach (`P`)** = 30 of the 96 fixes made after v1.0.0 (31%). Of the 24 confirmed, 18 were in v1.0.0 already, 1 first in v1.5.0 and 5 first in v2.0.0.
- The other 66 post-1.0.0 fixes repaired code that was never in a tag: new 2.0.0 features fixed before 2.0.0 shipped, or 2.1.0-cycle code fixed before the 2.1.0 tag.
- 19 of the 24 confirmed escapes were silent (compiled or ran, and did less or something else).
- Fixes made while fixing: 12 fixes repair a regression an earlier fix in this list caused, or finish it (see "Fixes that repair fixes").

## Class by found-by

Rows are the class, columns who found it. RS review session, CA code-analyst, PR prober, ST smoke test / Native AOT publish / clean-container run, OR outside review (pasted by the user), US user or downstream use, MS measurement (benchmark, stress run), FR feature research, SF found by the session that was fixing something else, UN unknown.

| Class | RS | CA | PR | ST | OR | US | MS | FR | SF | UN | Total | of which silent | of which bundle |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Framework configuration interplay (`FCI`) | 6 | 5 | 2 |  | 9 | 5 |  |  | 5 |  | 32 | 19 | 4 |
| Generator type shape (`GTS`) | 22 | 1 |  |  | 3 |  |  |  | 1 | 1 | 28 | 19 | 7 |
| Analyzer input shape (`AIS`) | 10 | 4 |  |  | 4 |  |  |  | 7 |  | 25 | 1 | 2 |
| EF Core translation and mapping (`EFT`) | 7 |  |  |  |  |  |  | 1 | 2 |  | 10 | 8 | 0 |
| API surface lets misuse compile (new) (`API`) | 6 |  |  |  | 3 |  |  |  |  |  | 9 | 2 | 1 |
| Process order and state (`POS`) | 4 |  | 1 |  |  |  | 1 |  |  |  | 6 | 4 | 0 |
| Contract or docs drift (`DOC`) | 6 |  |  |  |  |  |  |  |  |  | 6 | 0 | 0 |
| Text and globalization (`TXT`) | 3 |  |  |  | 1 |  |  |  | 1 |  | 5 | 1 | 0 |
| Native AOT / trimming (new) (`AOT`) |  |  |  | 4 |  |  |  |  |  |  | 4 | 1 | 0 |
| Silent does-less (no more specific cause) (`SDL`) | 3 |  |  |  |  |  |  |  |  |  | 3 | 3 | 2 |
| Crash or performance (`CRP`) | 2 |  |  |  |  |  | 1 |  |  |  | 3 | 0 | 0 |
| Tooling / test infrastructure (new) (`TOOL`) |  |  |  | 1 |  |  |  |  | 1 |  | 2 | 0 | 0 |
| **Total** | **69** | **10** | **3** | **5** | **20** | **5** | **2** | **1** | **17** | **1** | **133** | **58** | **16** |

What sits behind the columns:

- **RS (69)** is mostly parallel review subagents inside a session. The big ones: the pre-1.0.0 review (f6d91586, 18 commits), the v2.0.0 codebase review with six reviewers (35e0b081, 18), the 2.0.0 release review with six reviewers (41c9ec0e, 15), the Opus review agent of the 2.0.0 prep (181e7fa9, 6), the 1.0.0 polish review (1060dfae, 4) and the implementation reviews of 09-27 (d97881be and 91e15090, 5). Item-level attribution inside a session is firm where the session's report lists the item and a guess where the fix agent had several reports (see confidence column in the appendix).
- **OR (20)** is text the user pasted from another reviewer: a review of 09-27 (a214e7d), a six-finding list of 10-13 (5 commits: 3457c2f, a99de55, a77bdda, 2fa0693, 4008323), a two-defect report (df4e806), a downstream consumer's review (b8f9432), an outside model's ten findings on 2.1.0 (e6d1704, 36b74b1, 564a51e), and outside reports pasted on 10-14 (1b6c805, 92d01dd, 415ce73, 2f8a854, d6b654a, 143af09, daafd89, 6368c1c, 821e1b2).
- **CA (10)** is the user-level `code-analyst` agent, first run 2026-10-14 (80c139b3, 8ac7df60, 241ebe10). All 10 are in the 2.1.0 cycle; 2 of them repair defects that were in v2.0.0 (d565948, 92f9bc4).
- **PR (3)** is the prober: 1ee02fc (v1.0.0, a NodaTime converter lookup order), 461c920 (v2.0.0, MVC route parameter with no type) and 8d1ebd8 (operation status order; 2.1.0-cycle). A fourth prober finding, G1, was dismissed as not a TypeKit bug; `35b64c3` is a bug in the prober itself.
- **ST (5)** is the 09-27 Native AOT work (4 commits, each measured by publishing a probe consumer with Native AOT) and one clean-container Release run.
- Not counted as a finder: the user steering. 5 commits trace to the user's own observation or a downstream package (US).

## Escapes by class

| Class | Total | Y (confirmed) | P (analyzer reach) | N | Y or P share |
|---|---|---|---|---|---|
| Framework configuration interplay (`FCI`) | 32 | 8 | 0 | 24 | 25% |
| Generator type shape (`GTS`) | 28 | 7 | 0 | 21 | 25% |
| Analyzer input shape (`AIS`) | 25 | 0 | 6 | 19 | 24% |
| EF Core translation and mapping (`EFT`) | 10 | 7 | 0 | 3 | 70% |
| API surface lets misuse compile (new) (`API`) | 9 | 0 | 0 | 9 | 0% |
| Process order and state (`POS`) | 6 | 1 | 0 | 5 | 17% |
| Contract or docs drift (`DOC`) | 6 | 0 | 0 | 6 | 0% |
| Text and globalization (`TXT`) | 5 | 0 | 0 | 5 | 0% |
| Native AOT / trimming (new) (`AOT`) | 4 | 0 | 0 | 4 | 0% |
| Silent does-less (no more specific cause) (`SDL`) | 3 | 0 | 0 | 3 | 0% |
| Crash or performance (`CRP`) | 3 | 1 | 0 | 2 | 33% |
| Tooling / test infrastructure (new) (`TOOL`) | 2 | 0 | 0 | 2 | 0% |
| **Total** | **133** | **24** | **6** | **103** | **23%** |

Counted over the 96 fixes made after v1.0.0 only (the 37 earlier fixes cannot have escaped), the share is 31% (30 of 96). By fix cycle:

| Cycle | Fixes | Y | P | N |
|---|---|---|---|---|
| pre-1.0.0 | 37 |  |  | 37 |
| 1.5.1 cycle | 1 | 1 |  |  |
| 2.0.0 cycle | 64 | 15 | 6 | 43 |
| 2.1.0 cycle (no tag yet) | 31 | 8 |  | 23 |

The 24 confirmed escapes, by earliest tag with the defect: 
- **v1.0.0** (18): `f66bb79`, `b1bf428`, `1ef30be`, `e779dd3`, `63471e7`, `92dfe16`, `e644a8a`, `717de54`, `719ee82`, `8f6af8b`, `53188ed`, `e4b751c`, `a4e2fd7`, `bef5770`, `9534751`, `92d01dd`, `415ce73`, `1ee02fc`
- **v1.5.0** (1): `4489c05`
- **v2.0.0** (5): `b8f9432`, `27c59d0`, `d565948`, `92f9bc4`, `461c920`

The 6 `P`: `37317de`, `780353b`, `e269ec8`, `71a4565`, `a7003bf`, `2e68a70`. CMTK0003 was a Warning in 1.x with the same false positives (`await held.ConfigureAwait(false)` read as a dropped result, a tap on a held result) and the same missed forms; 2.0.0 made it an Error and widened it as decision 50. They are counted separately because a Warning false positive is not the same harm as a wrong EF query.

Finder and escape (post-1.0.0 fixes only). Which of today's finders detect these escapes when each is put back as a seed is measured in the [finder benchmark of 2026-10-16](finder-benchmark-2026-10-16.md).

| Found by | Fixes after v1.0.0 | Y | P |
|---|---|---|---|
| Review session | 42 | 11 | 4 |
| code-analyst | 10 | 2 |  |
| prober | 3 | 2 |  |
| Outside review | 19 | 3 |  |
| User or downstream use | 4 | 1 |  |
| Feature research | 1 | 1 |  |
| Found by the fixing session | 17 | 4 | 2 |

## Sibling chains

A chain is a run of fixes that repair the same pattern in another place or another shape of the same input, ordered by commit. I drew them from the commit bodies and the code the commits touch; only a handful are stated in the commits themselves (marked **explicit** below). **Tight** chains share a file and a rule or function; **thematic** chains share a pattern across files. A commit can sit in several chains. The length counts the first fix (the origin) too.

| Chain | Kind | Pattern | Length | Span | Members in commit order |
|---|---|---|---|---|---|
| A | tight | CMTK0010 type-argument constraints (TypeArgumentConstraintAnalyzer.cs) | 14 | 2026-10-13 to 2026-10-14 | `a77bdda` → `37317de` → `e4361e9` → `a0ddf61` → `370414a` → `7c30840` → `0d6ba1a` → `cdab810` → `2f8a854` → `7792801` → `d27cf2d` → `1a9d7f2` → `75fb3aa` → `6368c1c` |
| B | tight | CMTK0003 dropped outcome (IgnoredOutcomeAnalyzer.cs) | 7 | 2026-09-28 to 2026-10-14 | `3aa5c83` → `37317de` → `780353b` → `e269ec8` → `71a4565` → `a7003bf` → `2e68a70` |
| C | tight | ReferenceHandler.Preserve guard (GeneratedJsonPlan.cs) | 6 | 2026-10-14 to 2026-10-14 | `92d01dd` → `415ce73` → `a5d575c` → `ae1729d` → `143af09` → `4e016a2` |
| D | tight | Which converter writes this type (JsonSchemaDescription.cs); overlaps C | 10 | 2026-10-14 to 2026-10-14 | `564a51e` → `27c59d0` → `1b6c805` → `415ce73` → `2f8a854` → `a5d575c` → `d6b654a` → `143af09` → `4e016a2` → `2141706` |
| E1 | tight | A hand-written member beside a generated one (CMTK1008/1009/1011) | 9 | 2026-09-27 to 2026-10-13 | `7da0da1` → `a214e7d` → `9961392` → `1261346` → `c326c1f` → `64b9dd5` → `0a30a64` → `b514d10` → `e4b751c` |
| E2 | tight | State and members inherited from a base record | 6 | 2026-09-28 to 2026-10-14 | `25a468e` → `bdf5e4f` → `28ec7fa` → `92d01dd` → `415ce73` → `21f4ad9` |
| H | tight | EF value-object key generation convention | 4 | 2026-10-13 to 2026-10-13 | `92dfe16` → `e644a8a` → `3bddba7` → `84e9776` |
| I | tight | EF translation of .Value and of ordering | 5 | 2026-09-28 to 2026-10-13 | `7732936` → `63471e7` → `717de54` → `719ee82` → `8f6af8b` |
| J | tight | Text guard AsText() | 4 | 2026-10-13 to 2026-10-13 | `4008323` → `da60907` → `0e450c9` → `ddcb549` |
| O | tight | Which Parse or constructor counts as a parser | 5 | 2026-09-28 to 2026-10-14 | `9961392` → `e4b751c` → `92d01dd` → `415ce73` → `daafd89` |
| P | tight | OpenAPI fault operation transformer and its startup check | 6 | 2026-10-13 to 2026-10-15 | `8af8136` → `66a5cec` → `d565948` → `92f9bc4` → `821e1b2` → `8d1ebd8` |
| Q | tight | Closed-set generated shape | 8 | 2026-10-13 to 2026-10-13 | `b514d10` → `169a00a` → `2fa0693` → `78e6bb0` → `851e2f9` → `787ad30` → `6805889` → `94beab0` |
| R | tight | CMTK0011 and its code fix | 3 | 2026-10-13 to 2026-10-14 | `44a31b1` → `aaae5b3` → `df4e806` |
| F | thematic | Nested or same-name types: companion classes, OpenAPI components, qualified names | 9 | 2026-09-27 to 2026-10-13 | `53a36db` → `1e58ffc` → `ae9b444` → `38a3a4f` → `78e6bb0` → `1e01a7e` → `3a84bc3` → `2bfb1c7` → `4489c05` |
| G | thematic | A nullable value object or closed set admits null in the OpenAPI document | 5 | 2026-09-28 to 2026-10-13 | `c5b2bf7` → `d78674d` → `a99de55` → `390b0fc` → `9534751` |
| K | thematic | Result.Catch: cancellation, timeout, task shapes | 4 | 2026-10-13 to 2026-10-13 | `5d2f583` → `3457c2f` → `d487fb4` → `84aaec2` |
| L | thematic | Null content, null delegate, null task | 4 | 2026-09-27 to 2026-10-13 | `199cc81` → `53a36db` → `a214e7d` → `e779dd3` |
| M | thematic | Native AOT: reflection removed | 5 | 2026-09-27 to 2026-10-13 | `321fc73` → `c6f0cc2` → `a6e8ab3` → `46bc412` → `169a00a` |
| N | thematic | Wrapped types the generators cannot wrap (CMTK1005) | 6 | 2026-09-27 to 2026-10-13 | `32c840b` → `7b11076` → `9961392` → `787ad30` → `53188ed` → `e4b751c` |
| S | thematic | Patch<T> | 6 | 2026-10-13 to 2026-10-14 | `b514d10` → `a99de55` → `50cd09b` → `39633ff` → `ee9214b` → `b8f9432` |
| T | thematic | Analyzers reporting from generated code or Razor | 3 | 2026-09-28 to 2026-10-13 | `3aa5c83` → `dd24278` → `7c30840` |
| U | thematic | API surface that lets misuse compile | 8 | 2026-09-28 to 2026-10-14 | `d1c48e9` → `aab357f` → `3457c2f` → `2fa0693` → `d487fb4` → `1d48edc` → `ee9214b` → `189ab9f` |
| V | thematic | Fault equality against a bare reason | 3 | 2026-10-13 to 2026-10-14 | `d487fb4` → `1d48edc` → `36b74b1` |
| W | thematic | Order and initialization state (aspect order, static init, registration order) | 6 | 2026-09-27 to 2026-10-15 | `1e58ffc` → `ae9b444` → `851e2f9` → `787ad30` → `6805889` → `1ee02fc` |

64 of 133 fixes are a non-first member of a tight chain; 95 are a non-first member of any chain (the thematic chains overlap the tight ones and are looser, so treat the second number as an upper bound). Chain origins that are themselves pre-1.0.0 fixes: B, E1, E2, F, G, I, L, M, N, O, T, W.

Chains the brief named:

- **CMTK0010 (chain A)**: 14 fixes in 2026-10-13..14, 13 of them in `TypeArgumentConstraintAnalyzer.cs`: method groups (a77bdda), doc-comment crefs and arrays and implementations (37317de), extension blocks and generic attributes (e4361e9), null-conditional and `nameof` (a0ddf61), base class through a type parameter (370414a), generated code (7c30840), enum/delegate/proofs (0d6ba1a), stack overflow on a default interface method (cdab810), bounds built from a type parameter (7792801), pair arguments built from one (d27cf2d), an `Enum` bound (2f8a854), the library's own `ShouldHold`/`ToHttpResult` (1a9d7f2), interface markers (75fb3aa), `dynamic`/`[ReadOnlyStruct]` (6368c1c). 12 of 13 file commits are class `AIS`. None of the CMTK0010 changes repair a released defect (the rule is new in 2.0.0; 37317de is `P` only for its CMTK0003 half); Error severity is what made each one release-blocking (decision 34).
- **ReferenceHandler.Preserve (chain C)**: origin `92d01dd` (2026-10-14; the guard itself, from an outside review, fixing a duplicate `$id` that existed since 1.0.0), then five fixes in a row: `415ce73` (nullable structs, ignored properties), `a5d575c` (a nullable struct judged by its contract's converter), `ae1729d` (a struct collection judged by its elements), `143af09` (a property judged by the converter in charge of it), `4e016a2` (a converter named on a nullable property; **explicit**: the commit says `143af09` introduced it). Chain D (which converter writes this type) overlaps C at `415ce73`, `a5d575c`, `143af09`, `4e016a2`; C and D together are 12 fixes, all on 2026-10-14 (92d01dd, 564a51e, 27c59d0, 1b6c805, 415ce73, 2f8a854, a5d575c, d6b654a, ae1729d, 143af09, 4e016a2, 2141706). All are class `FCI` except the origin.
- **CMTK0003 (chain B)**: 7 fixes over three weeks; six in the 2.0.0/2.1.0 cycle all shipped as Warning-severity behaviour in 1.x (`P`).
- **Hand-written or inherited member beside a generated one (E1 and E2)**: 9 + 6 fixes, 2026-09-27 to 2026-10-14. E1 is the CMTK1008/1009/1011 family refused one member at a time (CompareTo seam, constructor, ToString seam, equality, PrintMembers, explicit interfaces, explicit Create, closed-set members, operators). `c326c1f` states it is "the sibling of the ToString() leak decision 28 fixed" (**explicit**). E2 is the same input coming through a base record (state, ToString, equality), and it was reopened three times after 1.0.0 (`92d01dd`, `415ce73`, `21f4ad9`).
- **EF key generation (H)** and **EF `.Value`/ordering translation (I)**: 4 and 5 fixes, each pair-wise one convention or translator revisited for another shape (hierarchy root, unsigned types, abstract TPC root, char; foreign converter, ordering, filters, boxed key). 6 of these 9 are confirmed escapes from v1.0.0 (92dfe16, e644a8a, 63471e7, 717de54, 719ee82, 8f6af8b).

## Fixes that repair fixes

Twelve fixes repair a regression an earlier fix introduced or finish an earlier fix. Evidence in brackets; the first four are stated in the commit or in the session's own report, the rest are my reading.

- `bdf5e4f` repairs `25a468e` (CMTK1012 refused every value object over a base record from another assembly; the body names the earlier commit).
- `ae9b444` finishes `1e58ffc` (the CMTK1007 check "was still missed … after the check moved into the fabric").
- `3bddba7` repairs `92dfe16` (an abstract TPC root lost key generation; the review report says it comes from the newest key-generation commit).
- `4e016a2` repairs `143af09` (explicit in its body).
- `84e9776` repairs `e644a8a` (a char key declared generated on the strength of EF's list; "as 1.5.1 left it").
- `ae1729d` repairs `92d01dd` (a struct collection refused under Preserve, "where 2.0 wrote the wrapped type's bytes").
- `415ce73` (nullable and ignored members refused under Preserve) and `daafd89` (the parser check lost IParsable for a consumer) repair `92d01dd`.
- `21f4ad9` finishes `415ce73` (a deeper base record's explicit IEquatable; "decision 77's known limit").
- `a7003bf` finishes `780353b`/`71a4565` (a tap through `WaitAsync` still reported).
- `8d1ebd8` finishes `66a5cec` (the transformer sorted only the statuses it added).
- `27c59d0` follows `564a51e` (found while fixing it).

Two more, measured after this note was written by running the variant catalogue at each commit of chain A
([decision 95](adr/0095-a-fix-is-tested-across-its-input-class.md)); the counts above are this note's reading
and leave them out:

- `7c30840` repairs `e4361e9`, which moved CMTK0010's reporting to `context.ReportDiagnostic`, so a breach in
  generated code was reported from `e4361e9` on (the catalogue's four generated-code sites fail at `e4361e9`,
  `a0ddf61` and `370414a`, and pass at `a77bdda` and `37317de`).
- `cdab810` repairs `e4361e9`: a default interface method called through a derived interface overflows the stack
  from `e4361e9` on (the test host aborts at `e4361e9`, `a0ddf61`, `370414a` and `7c30840`; `37317de` completes).

## Source files with the most fixes

Counts are `.cs` files under `src/` touched by a fix; READMEs are left out (they are touched by 24, 24, 18, 15 and 10 fixes for Generators, Analyzers, base, AspNetCore and EF Core). Classes are those of the commits that touch the file, so a file touched by a cross-cutting commit also shows that commit's class.

| Fixes | File | Classes of the commits | Escaped (Y or P) | Silent |
|---|---|---|---|---|
| 13 | `CodoMetis.TypeKit.Generators/ValueObjectDeclaration.cs` | GTS 11, SDL 1, EFT 1 | 3 | 10 |
| 13 | `CodoMetis.TypeKit.Analyzers/TypeArgumentConstraintAnalyzer.cs` | AIS 12, FCI 1 | 1 | 2 |
| 12 | `CodoMetis.TypeKit.Generators/ValueObjectAspect.cs` | GTS 10, POS 2 | 3 | 10 |
| 10 | `CodoMetis.TypeKit/ResultExtensions.cs` | API 3, GTS 2, FCI 2, DOC 1, EFT 1, CRP 1 | 2 | 2 |
| 10 | `CodoMetis.TypeKit.Generators/AspectDiagnostics.cs` | GTS 8, SDL 2 | 3 | 8 |
| 9 | `CodoMetis.TypeKit/OptionExtensions.cs` | GTS 3, API 3, DOC 2, EFT 1 | 1 | 2 |
| 8 | `CodoMetis.TypeKit/JsonSchemaDescription.cs` | FCI 8 | 1 | 6 |
| 7 | `CodoMetis.TypeKit.Generators/ValueObjectParsableAspect.cs` | GTS 5, AOT 1, FCI 1 | 3 | 4 |
| 7 | `CodoMetis.TypeKit.Generators/ValueObjectJsonAspect.Templates.cs` | GTS 3, AOT 2, CRP 1, DOC 1 | 0 | 2 |
| 7 | `CodoMetis.TypeKit/CompilerServices/GeneratedJsonPlan.cs` | FCI 5, GTS 2 | 2 | 5 |
| 7 | `CodoMetis.TypeKit.Analyzers/IgnoredOutcomeAnalyzer.cs` | AIS 7 | 6 | 0 |
| 7 | `CodoMetis.TypeKit.AspNetCore/ValueObjectSchemaTransformer.cs` | FCI 7 | 3 | 7 |
| 6 | `CodoMetis.TypeKit/Result.cs` | GTS 3, AIS 1, DOC 1, CRP 1 | 1 | 1 |
| 6 | `CodoMetis.TypeKit/ResultT.cs` | GTS 2, AIS 1, API 1, DOC 1, CRP 1 | 1 | 1 |

Fixes touching each package (a fix can touch several): base 55, Generators 39, Analyzers 29, AspNetCore 25, EntityFrameworkCore 14, Testing 3, Serilog 1.

## Where a classification is a guess

- **Class** is a judgement from the commit body. Boundaries that moved the counts: `FCI` vs `GTS` for OpenAPI/JSON fixes about nested or inlined value objects (I put "which type is described" under `GTS` when the trigger was the type's declaration shape, under `FCI` when it was a host option or ASP.NET behaviour); `EFT` includes `b1bf428` (FirstOrNone on a query reads every row); `API` vs `DOC` for `50cd09b` (a naming inconsistency, put under `DOC`); `d487fb4` is `API` though it also fixes a null-task refusal. Bundles (B) hide second and third defects of other classes: `9961392`, `53a36db`, `a214e7d`, `b514d10`, `169a00a`, `564a51e`, `1b6c805`, `415ce73`, `2f8a854`, `92d01dd` carry 4 to 9 defects each.
- **Found by, low confidence (L):** `1e58ffc` (a flaky test during a review), `7b11076` (made in a side worktree session whose transcript I could not find), `fbb7091`, `94beab0`, `3121d03`, `53188ed`, `ae1729d`. **Medium (M)** is any commit made by a fix agent after a multi-reviewer report where I matched the item to the report by content (most of 41c9ec0e, 35e0b081, f6d91586, 1060dfae, 55e23683). `dcaec45` came out of the user asking whether a claim in the EF spike was measured; I class it as the user.
- **Two sessions share credit:** `d27cf2d` was found by both an outside review and the code-analyst (counted as CA); `d6b654a` was an outside review's finding that the code-analyst confirmed on its second run (counted as OR). 
- **Escape**: `Y` is verified by the pre-fix code existing in the tag (removed lines present in the tag's file, or the feature existing there), except `f66bb79`, `9534751`, `1ef30be`, `53188ed`, `e4b751c` and `4489c05`, whose fixes add code, so I reasoned from the feature's age. `415ce73` is `Y` for one of its five items (an abstract class's constructor read as a parser) and `N` for the other four. `8af8136` (the problem schema describes `fault`) is `N` although 1.5.x had no `fault` on that schema, because decision 49 made that a 2.0.0 design change, not a bug against a 1.5.x claim; counted the other way it would be one more escape from v1.5.0. The transcript of a session claims the NodaTime bug (`1ee02fc`) shipped "since 2.0.0"; the registration code is the same in v1.0.0, v1.5.1 and v2.0.0, so I record v1.0.0.
- Commit order and "cycle" use `git log --reverse` on `feat/2.1.0` and the first tag that contains each commit. 31 fixes are in no tag.
- I did not run any code or revert any fix; "escaped" rests on reading the tag trees and the commit bodies, not on reproducing the defect against the tag.

## What the numbers show

- Three classes hold 85 of 133 fixes: framework configuration interplay (32), generator type shape (28) and analyzer input shape (25). Everything else is 10 or fewer.
- Review sessions and outside reviews found 89 of 133 (67%); the code-analyst 10, the prober 3, the smoke test and AOT runs 5, the fixing session itself 17.
- EF translation defects escaped the most: 7 of 10, all from v1.0.0 and all silent; generator type shape 7 of 28 (Y), framework interplay 8 of 32, analyzer input shape 0 of 25 confirmed (6 `P`). The largest group of the 37 fixes before v1.0.0 is generator type shape (16), then framework interplay (4) and Native AOT (4).
- 58 of 133 fixes (44%) repair a defect that was silent; 19 of the 24 confirmed escapes were.
- Single chains run from three to nine fixes, except CMTK0010 (14) and the converter chain D (10). Two groups are longer when chains are merged: the converter and Preserve family (C and D, 12 fixes, all on 2026-10-14) and the hand-written or inherited member family (E1 and E2, 15 fixes over three weeks). CMTK0003 has 7 fixes over the same three weeks. Only about six commit bodies say that the fix repairs an earlier fix or is a sibling of one (`c326c1f`, `4e016a2`, `169a00a`, `370414a`, `bdf5e4f`, `ae9b444`); the rest of each chain is my reading of the code and the bodies.
- 60 of 133 fixes land on 2026-10-13 and 31 on 10-14, the two days around v2.0.0; 66 of the 96 post-1.0.0 fixes repaired code of the same unreleased cycle.

## Appendix: every fix

Columns: **Class**; **S** silent symptom, **B** bundle of several defects; **Found by** with the session name (see the session list below) and the confidence of the attribution (H explicit, M matched by content, L guess); **Sibling of** = the previous member of the first chain the commit belongs to (chain in brackets); **Escape**; **In tag** = first tag that contains the fix (`-` = none yet).

| Hash | Date | In tag | Class | Fl | Found by | Session | Conf | Sibling of | Escape | Subject |
|---|---|---|---|---|---|---|---|---|---|---|
| `dcaec45` | 09-27 | v1.0.0 | FCI |  | US | OSS-extraction | M | – | N | fix: map value objects through an additive EF plugin, not a replaced selector |
| `32c840b` | 09-27 | v1.0.0 | GTS | B | RS | Implementation review | H | – | N | fix: enum parsing, ordinal string order, invariant null provider, CMTK1005 for Nullable<T> |
| `199cc81` | 09-27 | v1.0.0 | GTS | B | RS | Implementation review | H | – | N | fix: null-first record-class comparison, Some(null) throws, Result.Error marker on Result<TError>, fallback JS |
| `53a36db` | 09-27 | v1.0.0 | GTS | B | RS | Review iteration | H | 199cc81 (L) | N | fix: companion-class collisions, JSON exceptions, formatting culture, UTC DateTime, null refusals, sealed reco |
| `7da0da1` | 09-27 | v1.0.0 | GTS | S | RS | Implementation review | H | – | N | fix: CMTK1008 for a comparison member beside the CompareTo seam |
| `1e58ffc` | 09-27 | v1.0.0 | POS |  | RS | Implementation review | L | 53a36db (F) | N | fix: answer the companion-name question in the fabric, where no aspect is introducing yet |
| `e73b3ce` | 09-27 | v1.0.0 | TOOL |  | ST | clean-container Release run | H | – | N | fix: build the build-outcome consumer in the run's own configuration |
| `7b11076` | 09-27 | v1.0.0 | GTS | S | UN | side worktree session | L | 32c840b (N) | N | fix: refuse a value object that wraps a value object (CMTK1005) |
| `321fc73` | 09-27 | v1.0.0 | AOT |  | ST | Native AOT compatibility | H | – | N | fix: Option and Result refuse JSON with their own message under Native AOT |
| `c6f0cc2` | 09-27 | v1.0.0 | AOT | S | ST | Native AOT compatibility | H | 321fc73 (M) | N | fix: stored JSON reaches the materializing mode through an interface |
| `a6e8ab3` | 09-27 | v1.0.0 | AOT |  | ST | Native AOT compatibility | H | c6f0cc2 (M) | N | fix: the generated JSON converter and parsing need no reflection |
| `46bc412` | 09-27 | v1.0.0 | AOT |  | ST | Native AOT compatibility | H | a6e8ab3 (M) | N | fix: the EF Core satellite works with compiled models and precompiled queries |
| `a214e7d` | 09-27 | v1.0.0 | GTS | BS | OR | Implementation review feedback | H | 7da0da1 (E1) | N | fix: refuse hand-written constructors (CMTK1009), check every delegate, and translate only the unwrap's signat |
| `ae9b444` | 09-28 | v1.0.0 | POS |  | MS | Value object library comparison (stress run) | H | 1e58ffc (F) | N | fix: find a declared companion-class name by enumerating, never by OfName |
| `53585c3` | 09-28 | v1.0.0 | CRP |  | MS | Value object library comparison (benchmarks) | H | – | N | fix: generated formatting, comparison and JSON cost what the wrapped type's do |
| `7732936` | 09-28 | v1.0.0 | EFT |  | RS | v1.0.0 release polish and review | M | – | N | fix: .Value on the element of a value-object collection translates |
| `38a3a4f` | 09-28 | v1.0.0 | GTS | S | RS | v1.0.0 release polish and review | M | ae9b444 (F) | N | fix: nested value objects get an OpenAPI component each |
| `9961392` | 09-28 | v1.0.0 | GTS | BS | RS | v1.0.0 release polish and review | M | a214e7d (E1) | N | fix: generator defects found in the 1.0.0 audit |
| `3aa5c83` | 09-28 | v1.0.0 | AIS | B | RS | v1.0.0 release polish and review | M | – | N | fix: analyzers report in Razor, in the declaring project, and for more forms |
| `dd24278` | 09-28 | v1.0.0 | FCI | S | RS | Pre-release review (v1.0.0) | M | 3aa5c83 (T) | N | fix: the analyzers run on the code Metalama transformed, so they report in Razor |
| `d1c48e9` | 09-28 | v1.0.0 | API |  | RS | Pre-release review (v1.0.0) | M | – | N | fix: Result<T, TError> no longer converts to bool |
| `aab357f` | 09-28 | v1.0.0 | API |  | RS | Pre-release review (v1.0.0) | M | d1c48e9 (U) | N | fix: remove the TryCast extension on object |
| `c5b2bf7` | 09-28 | v1.0.0 | FCI | S | RS | Pre-release review (v1.0.0) | M | – | N | fix: a container of nullable value objects admits null in the OpenAPI document |
| `d78674d` | 09-28 | v1.0.0 | FCI | S | RS | Pre-release review (v1.0.0) | M | c5b2bf7 (G) | N | fix: an inlined nullable value object admits null in the OpenAPI document |
| `25a468e` | 09-28 | v1.0.0 | GTS | S | RS | Pre-release review (v1.0.0) | M | – | N | fix: refuse instance state besides the wrapped value (CMTK1012) |
| `1261346` | 09-28 | v1.0.0 | GTS | S | RS | Pre-release review (v1.0.0) | M | 9961392 (E1) | N | fix: refuse a hand-written equality on a value object (CMTK1011) |
| `c326c1f` | 09-28 | v1.0.0 | GTS | S | RS | Pre-release review (v1.0.0) | M | 1261346 (E1) | N | fix: refuse a hand-written PrintMembers beside the generated ToString (CMTK1011) |
| `64b9dd5` | 09-28 | v1.0.0 | GTS | S | RS | Pre-release review (v1.0.0) | M | c326c1f (E1) | N | fix: refuse explicit implementations of the interfaces the generators implement |
| `2f4781d` | 09-28 | v1.0.0 | GTS |  | RS | Pre-release review (v1.0.0) | M | – | N | fix: refuse a file-local value object below a directive or in a file-local type |
| `0a30a64` | 09-28 | v1.0.0 | GTS | S | RS | Pre-release review (v1.0.0) | M | 64b9dd5 (E1) | N | fix: refuse an explicit Create beside a public one (CMTK1011) |
| `bdf5e4f` | 09-28 | v1.0.0 | GTS |  | RS | Pre-release review (v1.0.0) | M | 25a468e (E2) | N | fix: a record base from another assembly is not state (CMTK1012) |
| `28ec7fa` | 09-28 | v1.0.0 | GTS | S | RS | Pre-release review (v1.0.0) | M | bdf5e4f (E2) | N | fix: what a base record declares counts as the value object's own |
| `336742e` | 09-28 | v1.0.0 | AIS |  | RS | Pre-release review (v1.0.0) | M | – | N | fix: CMTK0001 leaves comparison guards, assertions and the Try pattern's out alone |
| `d3f4703` | 09-28 | v1.0.0 | DOC |  | RS | Pre-release review (v1.0.0) | M | – | N | fix: OrNull() for an option of a value type, so the JSON refusal's advice compiles |
| `a99b854` | 09-28 | v1.0.0 | CRP |  | RS | Pre-release review (v1.0.0) | M | – | N | fix: Option<T>? and Result<...>? refuse JSON with their own message |
| `f99e40b` | 09-28 | v1.0.0 | DOC |  | RS | Pre-release review (v1.0.0) | M | – | N | fix: every run-time refusal links to its explanation in the README |
| `90c41aa` | 09-28 | v1.0.0 | DOC |  | RS | Pre-release review (v1.0.0) | M | – | N | fix: the generated null refusals link to their explanation too |
| `f66bb79` | 10-11 | v1.5.1 | FCI | S | US | ValueRanges 9.0.0 spike | H | – | Y v1.0.0 | fix: keep ASP.NET's string for a parameter whose wrapped type it does not describe |
| `b514d10` | 10-13 | v2.0.0 | SDL | BS | RS | Review API and documentation outcomes (weaving review) | H | 0a30a64 (E1) | N | fix: closed sets that compiled and did less, and Patch.IfPresent clearing a struct to default |
| `169a00a` | 10-13 | v2.0.0 | SDL | BS | RS | Review API and documentation outcomes (weaving review) | H | b514d10 (Q) | N | fix: closed sets bound by position, refused by EF's compiled model, and silent as dictionary keys |
| `5d2f583` | 10-13 | v2.0.0 | FCI |  | US | Release 2.0.0 defect fixes (flaky test, then user) | H | – | N | fix: Result.Catch answers an HttpClient timeout as the error |
| `b1bf428` | 10-13 | v2.0.0 | EFT | S | FR | Release 2.0.0 defect fixes (feature picks) | M | – | Y v1.0.0 | fix: FirstOrNone on a query runs its predicate in the query, not on every row in memory |
| `fbb7091` | 10-13 | v2.0.0 | SDL | S | RS | Release 2.0.0 defect fixes | L | – | N | fix: Serilog logs a closed set as what it prints, not by its properties |
| `3457c2f` | 10-13 | v2.0.0 | API | S | OR | Release 2.0.0 defect fixes (pasted review) | H | 5d2f583 (K) | N | fix: Catch refuses a task in every form, the command form's value included |
| `a99de55` | 10-13 | v2.0.0 | FCI | S | OR | Release 2.0.0 defect fixes (pasted review) | H | d78674d (G) | N | fix: a patch's schema admits null beside every value its argument admits |
| `a77bdda` | 10-13 | v2.0.0 | AIS | S | OR | Release 2.0.0 defect fixes (pasted review) | H | – | N | fix: CMTK0010 reports a method group whose type arguments the delegate infers |
| `2fa0693` | 10-13 | v2.0.0 | API |  | OR | Release 2.0.0 defect fixes (pasted review) | H | 169a00a (Q) | N | fix: a closed set's All is read-only, so no cast replaces a member |
| `4008323` | 10-13 | v2.0.0 | TXT |  | OR | Release 2.0.0 defect fixes (pasted review) | H | – | N | fix: AsText refuses a value that renders nothing, and a selector that selects nothing |
| `78e6bb0` | 10-13 | v2.0.0 | GTS | S | RS | Codebase analysis for v2.0.0 (Opus review agent) | H | 2fa0693 (Q) | N | fix: a closed set in two files lists every member, a nested set is qualified by its containers, and a non-publ |
| `1e01a7e` | 10-13 | v2.0.0 | FCI |  | RS | Codebase analysis for v2.0.0 (Opus review agent) | H | 78e6bb0 (F) | N | fix: a closed set is a string enum as a parameter and a container's element, and nested sets get their own com |
| `d487fb4` | 10-13 | v2.0.0 | API | B | RS | Codebase analysis for v2.0.0 (Opus review agent) | H | 3457c2f (K) | N | fix: an index from the end on a query, a fault compared with a bare reason, and CatchAsync over no task are re |
| `37317de` | 10-13 | v2.0.0 | AIS | B | RS | Codebase analysis for v2.0.0 (Opus review agent) | H | a77bdda (A) | P | fix: CMTK0010 skips doc-comment crefs, counts arrays sealed and follows an implementation; CMTK0003 reaches br |
| `3a84bc3` | 10-13 | v2.0.0 | GTS | S | RS | TypeKit 2.0.0 release review | M | 1e01a7e (F) | N | fix: refuse an OpenAPI document where a nested type's component name is another type's |
| `2bfb1c7` | 10-13 | v2.0.0 | GTS |  | RS | TypeKit 2.0.0 release review | M | 3a84bc3 (F) | N | fix: qualify a nested fault enum after its nesting chain, and check fault types by type |
| `4489c05` | 10-13 | v2.0.0 | GTS |  | RS | TypeKit 2.0.0 release review | M | 2bfb1c7 (F) | Y v1.5.0 | fix: name a type nested in a generic one in a Testing assertion's failure |
| `1d48edc` | 10-13 | v2.0.0 | API | S | RS | TypeKit 2.0.0 release review | M | d487fb4 (U) | N | fix: a bare reason no longer converts to a Fault<TFault> |
| `851e2f9` | 10-13 | v2.0.0 | POS | S | RS | TypeKit 2.0.0 release review | M | 78e6bb0 (Q) | N | fix: a closed set refuses All and a name lookup while it is initialized |
| `1ef30be` | 10-13 | v2.0.0 | GTS | S | RS | TypeKit 2.0.0 release review | M | – | Y v1.0.0 | fix: an abstract type with a value-object marker is CMTK1022 |
| `50cd09b` | 10-13 | v2.0.0 | DOC |  | RS | TypeKit 2.0.0 release review | M | a99de55 (S) | N | fix: Patch<T>.IfValue is Tap, and Tap and IfPresent return the patch |
| `e779dd3` | 10-13 | v2.0.0 | CRP |  | RS | TypeKit 2.0.0 release review | M | a214e7d (L) | Y v1.0.0 | fix: every async combinator refuses a delegate that returns no task |
| `780353b` | 10-13 | v2.0.0 | AIS |  | RS | TypeKit 2.0.0 release review | M | 37317de (B) | P | fix: CMTK0003 sees ConfigureAwait and WaitAsync hand on the task they are called on |
| `e269ec8` | 10-13 | v2.0.0 | AIS |  | RS | TypeKit 2.0.0 release review | M | 780353b (B) | P | fix: CMTK0003 reports a call picked by ??, a dropped tuple and a dropped dictionary of results |
| `e4361e9` | 10-13 | v2.0.0 | AIS |  | RS | TypeKit 2.0.0 release review | M | 37317de (A) | N | fix: CMTK0010 checks extension blocks, generic attributes and inherited or static implementations |
| `a0ddf61` | 10-13 | v2.0.0 | AIS |  | SF | TypeKit 2.0.0 release review (fix agent) | M | e4361e9 (A) | N | fix: CMTK0010 reports a null-conditional call once, and neither a nameof nor a missing type |
| `370414a` | 10-13 | v2.0.0 | AIS |  | SF | TypeKit 2.0.0 release review (fix agent) | M | a0ddf61 (A) | N | fix: CMTK0010 finds a base class's implementation through a type parameter or base. |
| `63471e7` | 10-13 | v2.0.0 | EFT | S | RS | TypeKit 2.0.0 release review | M | 7732936 (I) | Y v1.0.0 | fix: .Value translates only on a column TypeKit's converter stores, and is refused on any other |
| `92dfe16` | 10-13 | v2.0.0 | EFT | S | RS | TypeKit 2.0.0 release review | M | – | Y v1.0.0 | fix: a value-object key is generated at the root of a hierarchy, and not under TPC on SQLite |
| `e644a8a` | 10-13 | v2.0.0 | EFT | S | RS | TypeKit 2.0.0 release review | M | 92dfe16 (H) | Y v1.0.0 | fix: a value-object key over an unsigned integer, sbyte or char is generated as EF generates its type |
| `717de54` | 10-13 | v2.0.0 | EFT | S | RS | TypeKit 2.0.0 release review | M | 63471e7 (I) | Y v1.0.0 | fix: a query that compares or orders a value object with its own CompareTo is refused |
| `8af8136` | 10-13 | v2.0.0 | DOC |  | RS | v2.0.0 codebase review | M | – | N | fix: the problem schema a fault response refers to describes fault, as decision 49 says |
| `71a4565` | 10-13 | v2.0.0 | AIS |  | RS | v2.0.0 codebase review | M | e269ec8 (B) | P | fix: a chain of taps on a stored result or task no longer reports CMTK0003 |
| `107e5b8` | 10-13 | v2.0.0 | AIS |  | RS | v2.0.0 codebase review | M | – | N | fix: CMTK0012 reports a positional Match followed by another conditional access |
| `7c30840` | 10-13 | v2.0.0 | AIS |  | RS | v2.0.0 codebase review | M | 370414a (A) | N | fix: CMTK0010 reports from generated code only where it maps to Razor markup |
| `44a31b1` | 10-13 | v2.0.0 | AIS |  | RS | v2.0.0 codebase review | M | – | N | fix: CMTK0011 never reports a struct, and says "an internal" constructor |
| `a7003bf` | 10-13 | v2.0.0 | AIS |  | SF | v2.0.0 codebase review findings (fix agent) | M | 71a4565 (B) | P | fix: a tap through WaitAsync on a held task no longer reports CMTK0003 |
| `84aaec2` | 10-13 | v2.0.0 | FCI |  | RS | v2.0.0 codebase review | M | d487fb4 (K) | N | fix: Result.Catch lets a cancellation that Wait() or .Result wraps through, and a wrapped timeout stays the er |
| `39633ff` | 10-13 | v2.0.0 | FCI |  | RS | v2.0.0 codebase review | M | 50cd09b (S) | N | fix: a value a Patch<T> cannot read is refused at the patched property's path |
| `da60907` | 10-13 | v2.0.0 | TXT |  | RS | v2.0.0 codebase review | M | 4008323 (J) | N | fix: no string makes a text guard throw, or compose in quadratic time |
| `0e450c9` | 10-13 | v2.0.0 | TXT |  | RS | v2.0.0 codebase review | M | da60907 (J) | N | fix: the text guards refuse the Khmer inherent vowels and the Khitan filler, and keep the prepended concatenat |
| `ddcb549` | 10-13 | v2.0.0 | TXT | S | RS | v2.0.0 codebase review | M | 0e450c9 (J) | N | fix: AsText() throws in globalization-invariant mode rather than keep text uncomposed |
| `d19972c` | 10-13 | v2.0.0 | DOC |  | RS | v2.0.0 codebase review | M | – | N | fix: Fault.Of(null) says a fault's reason is never null, not that an Option or a Result never holds one |
| `3bddba7` | 10-13 | v2.0.0 | EFT | S | RS | v2.0.0 codebase review | M | e644a8a (H) | N | fix: a value-object key at the root of an abstract TPC hierarchy is generated again |
| `719ee82` | 10-13 | v2.0.0 | EFT | S | RS | v2.0.0 codebase review | M | 717de54 (I) | Y v1.0.0 | fix: a query filter that orders by a hand-written CompareTo is refused when the model is built |
| `8f6af8b` | 10-13 | v2.0.0 | EFT | S | SF | v2.0.0 codebase review findings (fix agent) | M | 719ee82 (I) | Y v1.0.0 | fix: ordering by a hand-written CompareTo through a key cast to object is refused |
| `787ad30` | 10-13 | v2.0.0 | POS | S | RS | v2.0.0 codebase review | M | 851e2f9 (Q) | N | fix: a value object over a closed set of its own project is refused (CMTK1005) |
| `53188ed` | 10-13 | v2.0.0 | GTS | S | SF | v2.0.0 codebase review findings (fix agent) | L | 787ad30 (N) | Y v1.0.0 | fix: a value object over object is refused (CMTK1005) |
| `e4b751c` | 10-13 | v2.0.0 | GTS | B | RS | v2.0.0 codebase review | M | b514d10 (E1) | Y v1.0.0 | fix: declarations that failed as LAMA0611 or a bare CS0111 are refused by name or generated |
| `6805889` | 10-13 | v2.0.0 | POS | S | RS | v2.0.0 codebase review | M | 787ad30 (Q) | N | fix: a value object over a closed set of its own project is generated in full, since ClosedSetAspect runs firs |
| `a4e2fd7` | 10-13 | v2.0.0 | GTS | S | RS | v2.0.0 codebase review | M | – | Y v1.0.0 | fix: an exception from Create leaves TryParse as it leaves Parse, whatever the wrapped type parses through |
| `bef5770` | 10-13 | v2.0.0 | GTS |  | RS | v2.0.0 codebase review | M | – | Y v1.0.0 | fix: a default instance over a reference type formats as an empty string everywhere |
| `84e9776` | 10-13 | v2.0.0 | EFT |  | SF | v2.0.0 codebase review findings (fix agent) | M | 3bddba7 (H) | N | fix: a value-object key over char is the application's to assign, and a default ulong key's throw at Add is do |
| `94beab0` | 10-13 | v2.0.0 | TXT |  | SF | v2.0.0 codebase review findings (fix agent) | L | 6805889 (Q) | N | fix: CMTK1021 refuses two members whose Match parameters share a name, as a Kelvin sign and a K do |
| `390b0fc` | 10-13 | v2.0.0 | FCI | S | SF | v2.0.0 codebase review findings (fix agent) | M | a99de55 (G) | N | fix: an inlined nullable closed set admits null in the OpenAPI document, as a referenced one does |
| `3121d03` | 10-13 | v2.0.0 | FCI |  | SF | v2.0.0 codebase review findings (fix agent) | L | – | N | fix: a problem type is written as the URI it is, its escapes kept |
| `9534751` | 10-13 | v2.0.0 | FCI | S | SF | v2.0.0 codebase review findings (fix agent) | M | 390b0fc (G) | Y v1.0.0 | fix: a nullable value object over an inlined enum admits null among the enum's values |
| `0d6ba1a` | 10-13 | v2.0.0 | AIS |  | SF | v2.0.0 codebase review findings (fix agent) | M | 7c30840 (A) | N | fix: CMTK0010 accepts an enum and a delegate as concrete, and a constraint that proves the attribute |
| `aaae5b3` | 10-13 | v2.0.0 | AIS |  | SF | v2.0.0 codebase review findings (fix agent) | M | 44a31b1 (R) | N | fix: the CMTK0011 code fix keeps the source as written, and is withheld on a partial type with a primary const |
| `ee9214b` | 10-13 | v2.0.0 | API |  | RS | Codebase analysis for v2.0.0 (Opus review agent) | H | 39633ff (S) | N | fix: a Patch<T>'s T is notnull, so every member hands the value as the value's type |
| `189ab9f` | 10-14 | v2.0.0 | API |  | RS | Codebase analysis for v2.0.0 (Opus review agent) | H | ee9214b (U) | N | fix: Choose(selector) reads a sequence whose elements may be null |
| `df4e806` | 10-14 | v2.0.0 | AIS |  | OR | Pre-v2.0.0 defects (pasted review) | H | aaae5b3 (R) | N | fix: the CMTK0011 code fix is withheld where the added constructor's implicit base() binds to nothing |
| `cdab810` | 10-14 | v2.0.0 | AIS |  | SF | Smoke test and issue review | H | 0d6ba1a (A) | N | fix: CMTK0010 no longer overflows the compiler's stack on a default interface method called through a derived  |
| `2e68a70` | 10-14 | v2.0.0 | AIS |  | SF | Smoke test and issue review | H | a7003bf (B) | P | fix: a tap on a held result or task, returned as a plain Task or waited for, no longer reports CMTK0003 |
| `b8f9432` | 10-14 | - | FCI | S | OR | TypeKit package upgrades (consumer review) | H | ee9214b (S) | Y v2.0.0 | fix: a Patch<T> the serializer requires stays required in the document; init properties under RespectRequiredC |
| `f9e61d1` | 10-14 | - | FCI | S | US | TypeKit package upgrades (consumer) | H | – | N | fix: a document whose MVC and minimal-API JSON options describe differently is refused |
| `e6d1704` | 10-14 | - | AIS |  | OR | TypeKit 2.1.0 schema upgrades (pasted outside review) | H | – | N | fix: CMTK0014 reports the constant that reaches its use, unary ones included |
| `36b74b1` | 10-14 | - | API |  | OR | TypeKit 2.1.0 schema upgrades (pasted outside review) | H | 1d48edc (V) | N | fix: two rule types that read alike stay unequal faults |
| `564a51e` | 10-14 | - | FCI | BS | OR | TypeKit 2.1.0 schema upgrades (pasted outside review) | H | – | N | fix: schema descriptions follow the serializer's converter and refuse what a document cannot hold |
| `27c59d0` | 10-14 | - | FCI | S | SF | TypeKit 2.1.0 schema upgrades | H | 564a51e (D) | Y v2.0.0 | fix: a value object the options write with a converter of their own is not described as its wrapped type |
| `1b6c805` | 10-14 | - | FCI | BS | OR | Pre-2.1 converter and schema issues (pasted review) | H | 27c59d0 (D) | N | fix: schema descriptions follow factories and nullable converters, a patch's argument is compared, and a 3.0 d |
| `92d01dd` | 10-14 | - | GTS | BS | OR | outside review (session not in transcripts) | H | 28ec7fa (E2) | Y v1.0.0 | fix: four defects since 1.0.0 from an outside review |
| `415ce73` | 10-14 | - | FCI | BS | OR | Bug fix iteration: five remaining issues (pasted review) | H | 92d01dd (C) | Y v1.0.0 (1 of 5 items) | fix: a base's IEquatable, a resolver's converter, nullable and ignored members under Preserve, and an abstract |
| `66a5cec` | 10-14 | - | FCI |  | US | ADR-0068 code reduction spike (consumer) | H | 8af8136 (P) | N | fix: an operation answering two fault types lists both types' problem types at a shared status, statuses ascen |
| `2f8a854` | 10-14 | - | FCI | BS | OR | v2.1.0 release defects review (pasted review) | H | cdab810 (A) | N | fix: descriptions ask the contract's converter first, a Nullable<T>'s included; an Enum bound proves no [Disti |
| `a5d575c` | 10-14 | - | FCI | S | SF | v2.1.0 release defects review | M | 415ce73 (C) | N | fix: under ReferenceHandler.Preserve a nullable struct is judged by its contract's converter, a resolver's inc |
| `7792801` | 10-14 | - | AIS |  | CA | Code analysis agent / Fix CMTK0010 gaps | H | 2f8a854 (A) | N | fix: CMTK0010 meets a bound built from a type parameter by what the parameter is at each use (decision 84) |
| `d27cf2d` | 10-14 | - | AIS |  | CA | Code analysis agent / Fix CMTK0010 gaps (also found by pasted review) | H | 7792801 (A) | N | fix: CMTK0010 holds a pair argument built from a type parameter as it holds the parameter (decision 84) |
| `1a9d7f2` | 10-14 | - | AIS |  | CA | Code analysis agent / Fix CMTK0010 gaps | H | d27cf2d (A) | N | fix: ShouldHold and ToHttpResult over a composed fault declare [Distinct] (decision 84) |
| `75fb3aa` | 10-14 | - | AIS |  | CA | Code analysis agent / Fix CMTK0010 gaps | H | 1a9d7f2 (A) | N | fix: [ValueObject] refuses an interface, and a marker bound proves it only beside a constraint no interface me |
| `d6b654a` | 10-14 | - | FCI | S | OR | Fix CMTK0010 gaps and JSON description bugs (pasted review; CA confirmed) | H | a5d575c (D) | N | fix: a nullable property's converter is seen through to its attribute only where it is the serializer's adapte |
| `ae1729d` | 10-14 | - | FCI |  | CA | 2.1.0 release code review | L | a5d575c (C) | N | fix: under ReferenceHandler.Preserve a struct collection is judged by its elements |
| `143af09` | 10-14 | - | FCI | S | OR | Reference preservation JSON issues (pasted review) | H | ae1729d (C) | N | fix: under ReferenceHandler.Preserve a property is judged by the converter in charge of it |
| `4e016a2` | 10-14 | - | FCI |  | CA | Reference preservation JSON issues | H | 143af09 (C) | N | fix: under ReferenceHandler.Preserve a converter named on a nullable property is judged, not its adapter |
| `21f4ad9` | 10-14 | - | GTS | S | CA | Reference preservation JSON issues | H | 415ce73 (E2) | N | fix: a deeper base record's explicit IEquatable is the value object's equality too |
| `2141706` | 10-14 | - | FCI |  | CA | Reference preservation JSON issues | H | 4e016a2 (D) | N | fix: an application's schema transform runs once on each node, not again inside a described type |
| `d565948` | 10-14 | - | FCI | S | CA | Reference preservation JSON issues | H | 66a5cec (P) | Y v2.0.0 | fix: OpenAPI reads an MVC action's endpoint, so MapControllers() conventions reach the document |
| `92f9bc4` | 10-14 | - | FCI | S | CA | Reference preservation JSON issues | H | d565948 (P) | Y v2.0.0 | fix: OpenAPI describes fault and rule on a problem response the endpoint declares itself |
| `daafd89` | 10-14 | - | GTS | S | OR | Reference preservation JSON issues (pasted review) | H | 415ce73 (O) | N | fix: a wrapped Parse whose result converts implicitly to the wrapped type is a parser again |
| `6368c1c` | 10-14 | - | AIS |  | OR | Reference preservation JSON issues (pasted review) | H | 75fb3aa (A) | N | fix: CMTK0010 takes dynamic as unsealed, and a [ReadOnlyStruct] or [FlagsEnum] parameter as sealed and concret |
| `821e1b2` | 10-14 | - | FCI |  | OR | Reference preservation JSON issues (pasted review) | H | 92f9bc4 (P) | N | fix: a 2.0 host with two fault types sharing a name starts again, and an exclusion narrows its group's status |
| `1ee02fc` | 10-15 | - | POS | S | PR | Write the design-probing agent (prober) | H | 6805889 (W) | Y v1.0.0 | fix: a value object over a NodaTime type parses after TypeDescriptor.GetConverter described the type first |
| `461c920` | 10-15 | - | FCI |  | PR | Task-centric testing agent design (prober round) | H | – | Y v2.0.0 | fix: a route parameter MVC names no type for no longer fails the OpenAPI document |
| `35b64c3` | 10-15 | - | TOOL |  | SF | Write the design-probing agent (prober) | H | – | N | fix: prober's status line counts a number built in memory, not only one read from a file |
| `8d1ebd8` | 10-15 | - | FCI |  | PR | Fix the fault status order (prober finding) | H | 821e1b2 (P) | N | fix: an operation's fault statuses come out ascending, the ones its endpoint declares itself among them |

Session names are the titles of the local Claude Code sessions, by the first 8 characters of their id: d97881be Implementation review; 91e15090 Review iteration; 74144e4a Implementation review feedback; e7097a28 Implementation plans; e9ed144a Native AOT compatibility; e4e421de Value object library comparison; 1060dfae v1.0.0 release polish and review; f6d91586 pre-release review (v1.0.0); d9c3c58e Review API and documentation outcomes; 55e23683 and cfb3cad1 Release 2.0.0 defect fixes; 181e7fa9 Codebase analysis for v2.0.0; 41c9ec0e CodoMetis.TypeKit 2.0.0 release review; 35e0b081 v2.0.0 codebase review; 28e35f63 v2.0.0 codebase review findings; 71a123c7 Pre-v2.0.0 defects; fe8e4c84 Smoke test and issue review; 792529bd TypeKit package upgrades; f4b9d0ec TypeKit 2.1.0 schema upgrades; c76d4e1c Pre-2.1 converter and schema issues; 62ccbacc Bug fix iteration: five remaining issues; 6da6d4a2 ADR-0068 code reduction spike; 832bbec1 v2.1.0 release defects review; 80c139b3 Code analysis agent; 0c6f7da1 Fix CMTK0010 gaps and JSON description bugs before 2.1.0; 8ac7df60 2.1.0 release code review; 241ebe10 Reference preservation JSON issues; 68fa1f7e Write the design-probing agent (prober); d005e1e8 Task-centric testing agent design; 3a0d987c Fix the fault status order and ApiExplorer docs.
