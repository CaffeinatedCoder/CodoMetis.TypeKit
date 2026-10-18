# 0107. A fact that prunes or excuses is executable, its answer is a fixture beside its test, and seeds run by footprint

Date: 2026-10-17
Status: accepted

Context: 2026-10-17, WS2b of the probing concept (facts at a pinned version shorten every loop), after the probe
triage of 2026-10-16 and 2026-10-17 pinned KB-026 to KB-038 and the harness began to cite them
(`Cite.Fact("KB-037")`, PR #53). [Decision 93](0093-verified-facts-are-pinned-dated-and-executable-where-they-can-be.md)
made a fact executable where it can be; nothing held the probe corpus to citing only those. A prose fact cited by a
`refused` entry, a `not-promised` entry or an applicability condition would prune or excuse rows on the strength of a
sentence no test re-reads when a dependency moves, which is the substring excuse in a nicer form. The facts whose
tests compare a document or a status held the measured answer inside the test's code, where the harness, which will
read a fact as the framework's side of a row (`Baseline.Fact`), cannot reach it. And `prober seeds` built once per
seed of every probe on every run, a release step's cost paid after every probe change.

## Decision

**A fact that prunes or excuses is executable.** `docs-query check` gains `fact-not-executable-cited`. It fails, at
the citing place and naming the fact, when a fact without a test is cited by a `refused` entry of
`.claude/probe-axes.md` (its reason names `KB-NNN`), a `not-promised` entry of `tools/probe/corpus/claims.json` (its
`source` is the fact's file, or a field says `fact:KB-NNN`), an obligation's `inapplicable` declaration (its `cite` is
`fact:KB-NNN`), or a `Cite.Fact("KB-NNN")` or `Baseline.Fact("KB-NNN")` literal of the harness and probe sources, read
as text with comments blanked out as `SourceLint` reads them. A fact without a test is one whose `test:` is `none, …`,
names no file, or names a file that does not exist or declares no class of the fact's id; a cited id that is no fact
fails too. `Baseline.Fact` takes a fixture as well. The check's summary counts the citations of each kind, and
`RepositoryTests` holds floors on the places read and the citations found, so a scan that finds nothing cannot pass.
A fact without a test keeps its pins and its date and may be named in a reason's prose; it never decides a row.

**A fact's measured answer is a fixture beside its test.** Where the answer is data (a document, a status, an
exception type at an operation), it is `test/CodoMetis.TypeKit.Facts/KBNNN.fixture.json`, named by the fact's
`fixture:` line: the fact's id and its answers, each with an `id`, a `kind` (`document` with `at`, a JSON pointer;
`status`; `exception`), the settings it was measured under (`when`, axis to value, as a row names them), the `value`
and a `note`. The fact's test measures every answer of the fixture, a theory over its ids, so an answer the test does
not measure fails it. `docs-query check` fails a `fixture:` whose file is missing (`fact-fixture-missing`), one that is
not `KBNNN.fixture.json` beside the test or belongs to a fact without a test (`fact-fixture`), and a fixture no fact
names (`fact-orphan-fixture`). The reader, `FactFixture`, is BCL only, so the harness can compile it as it is, and
states the rule the harness will follow: only a fixture a fact names, only while the check is clean; the one answer
whose every `when` setting is the row's, none meaning the harness measures the framework itself and several a harness
error; compared by kind; never written by the harness. KB-001, KB-026, KB-027 and KB-037 are in this form.

**Seeds run by footprint, the release runs them all.** Without `--all`, `prober seeds` runs only the probes whose seeds
identity (the probe's identity, its seeds and their patches' content) changed, or whose product footprint or seeded
file changed, since the run that caught every one of its seeds, and prints each probe it skips and why.
`seeds-last.json` records, per probe, the identity and the commit of that run; a probe with a seed missed loses its
record, so it runs next time whatever changed, and a probe without a recorded footprint runs. `--all` runs every probe
whatever the record says, and `--probe P` runs P. A release runs `seeds --all` locally before the tag, beside
`replay --all` and the gate; CI still never builds a seed ([decision 100](0100-every-probe-is-shown-to-fail-against-a-seeded-defect.md)).

## Evidence

On 2026-10-17 the citation rule read 3 refused entries, 41 not-promised entries, 11 obligation declarations and 87
harness and probe sources, and found 4 citations: KB-001 and KB-027 by not-promised entries, KB-037 twice by the
harness; every one is executable. Each kind was seeded in a scratch copy of the repository and failed the check: a
cited fact's `test:` line removed, one pointed at a missing file, and a prose fact (KB-005) cited from a refused entry,
an obligation declaration and the harness. A wrong answer seeded into KB001's and KB037's fixtures failed
`KB001` and `KB037`. Each rule of the seeds selection was seeded in the prober (identity, footprint, seeded file,
`--all`, a missed seed's record, a missing footprint, never skipping) and failed its `SeedsTests`.

Rejected: a fixture kept in `design/knowledge/` beside the entry, which no build copies and the facts project would
reach by path.

## Consequences

- A triage that settles a row on a framework fact writes the test first, then cites it; a prose fact that would
  explain a row is research to do, not a citation.
- `prober seeds` after a probe or product change runs seconds to minutes instead of one build per seed of every probe.
- Wiring `Baseline.Fact` to the fixtures is the harness's next step; this record only fixes the rule it follows. A
  probe's identity does not yet include the facts it cites, so a changed fixture does not stale the probes reading it
  until that step adds it.
