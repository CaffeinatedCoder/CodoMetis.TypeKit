# 0098. A probe row says how it ended: notes are a closed catalogue, a row that earned nothing is a skipped test, and a skip for a finding binds the gate

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, an outside review of the probing infrastructure, pasted into a session and checked
against the code and the last replay's evidence on `docs/consolidation-release`. The review's main concern
was the soft-pass paths: a check that notes something and returns no failure. Measured:

- **Most rows of the HTTP probes earned nothing, and every test runner showed them green.** BaseProbe passed
  46 of 346 rows, C03 94 of 345, C05 81 of 347. The rest ended as `expected-refusal` or `inapplicable`,
  which the ledger rightly does not credit, but xUnit reported a pass, and `prober status` showed only pairs.
- **A note's outcome was read from its text.** `NoteCategories` matched substrings (`"-NA"`, `"base64"`,
  `"cannot-bind"`), and a note that matched none left the row a pass: an unrecognised note failed open.
- **A skip outlived its finding.** C03 skipped a document failure in `ClosedSetSchemaTransformer.Describe`
  for an own record class, "open finding F2". F2 was fixed in `461c920`; the skip stayed, so F2 coming back
  would have been filed as `inapplicable`, never as a finding.
- **The document's baseline kept TypeKit on both sides.** F2 had first been hidden by `HttpBaseline`, which
  isolated a failing document by comparing the value object's with the wrapped type's, both with
  `AddTypeKit()`, and filed it `doc-refused-for-wrapped-too`.
- **Two settings made pairs uncoverable.** Under `json.ReferenceHandler=Preserve` or `json.MaxDepth=3`,
  System.Text.Json refuses the OpenAPI document of a problem response for plain ASP.NET too. C03 and C05
  noted it by substring and ended the whole row `expected-refusal`, its passed wire checks included, so no
  pair with either value could ever be covered: 2049 of C03's pairs and 2518 of C05's stayed uncovered.
- **A harness error raised without a failure text passed the test.** `Rec.Run` asserted only on the
  returned failure; `Outcome("harness-error", …)` followed by `return null` was green in xUnit.

## Decision

**Notes are a closed catalogue.** `NoteKeys` in `harness/Row.cs` maps every note key to its outcome
(`pass`, `inapplicable`, `expected-refusal`, `inconclusive`). A check calls `r.Note(key, detail)`; the key
alone decides, the detail only explains. A key not in the catalogue makes the row a harness error. No
failure text is matched to excuse a row.

**A row that earned nothing is a skipped test.** `Rec.Run` fails the test for a `violation` or a
`harness-error`, however it was raised, and ends every other outcome but a pass with `Assert.Skip` and its
reasons. Every runner then counts what the ledger counts ("47 passed, 130 skipped"). Each row is labelled
with its index and the settings it sets that are not defaults (`Row [#6 input.json=valid json.MaxDepth=3 …]`),
through xunit v3's `TheoryDataRow.Label`, so a test list and a failure name the row without the ledger.
`build-verdict` keeps such a label whole in its headings.

**A skip for a finding names it.** A row that would fail for an open finding calls `r.Skip("F7", why)`: it
ends as `known-finding`, and the replay records the finding with the row. The replay fails a probe that
skips for a finding the ledger has fixed (in HEAD's history) or the corpus has dismissed. The gate refuses
while any row skips for any finding: a skip is an open finding that the tracked corpus records, so the gate
sees it from an empty state, as in CI and the release, where the ledger's own findings are absent.

**A document failure is judged against ASP.NET alone.** When the document fails with `AddTypeKit()`, the
same host is started without it (`Hx.DocRefusedByAspNetAlone`). The same exception there means the
document part has nothing to check, and the row is judged by its other checks
(`doc:refused-by-aspnet-alone`, a pass). ASP.NET alone writing the document, or failing otherwise, is a
violation. This replaces the substring rules for `Preserve`, `MaxDepth=3`, `char` with `WriteAsString`
and F2 in C03 and C05, and the wrapped-against-value-object isolation in `HttpBaseline`. A controller
without `[ApiController]` is checked absent from the document before its document part counts as nothing
to check (docs/aspnet-core.md, "Bind value objects in MVC").

**`prober status` shows how each claim's rows ended** at their last replay, beside its pairs, and the
findings rows skip for.

## Consequences

- Replayed on the same commit with an empty state, every probe passes. C03 passes 345 of 345 rows and C05
  347 of 347; BaseProbe 107 of 346. Uncovered pairs fell from 2049 to 59 for C03, from 2518 to 79 for C05,
  and from 4093 to 1737 for C07. The other probes' outcome counts are unchanged, row for row, which checks
  that the catalogue kept each note's outcome.
- Seeded and caught: a document transformer that throws only with `AddTypeKit()` fails 16 of the first 32
  rows of BaseProbe and C03 with "ASP.NET alone writes it"; under the previous harness the same seed left
  every BaseProbe row green. A key removed from the catalogue fails its rows as harness errors. A skip for
  a finding marked fixed fails the replay. The prober's own tests hold the gate's refusal, the stale skip
  and the status counts.
- A note's outcome is a reviewed line of code, so a new excuse is visible in a diff rather than in a
  string. Rows that skip for any reason are still credited nothing; only how they are reported changed.
- `.claude/probe-axes.md` tells a probe writer the same rules.
