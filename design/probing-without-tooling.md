# Probing without the tooling

How BLite applied CodoMetis.TypeKit's probing concept by hand, with no prober, no covering arrays and no
ledger, in two days of October 2026; what it produced; and how to do the same in any repository.

Written 2026-10-18 from the Claude Code session transcripts of the BLite work, the branches they left on
EntglDb/BLite, the issues they filed, and TypeKit's own files for contrast. Every statement carries a
source tag in square brackets; the appendix resolves each tag to a transcript position, a file path or an
issue number. Nothing here is reconstructed from memory.

A note on names. TypeKit calls the thing a *probe*; BLite called the same thing an *oracle test*. The
document uses BLite's word where it describes BLite and TypeKit's where it describes TypeKit, and says
"catalogue test" when it means the generic idea: a test whose rows are the product of the settings a
consumer varies, each judged by an exact reference.

---

## 1. The concept that was ported

This section defines the vocabulary of TypeKit's probing in plain terms and says what each part is for.
The definitions come from TypeKit's axes file, the prober agent's instructions and the Prober README
[K1] [K3] [K2]. Knowing what each part exists *for* is what made it possible to drop the part and keep
the purpose.

**Claim.** A sentence the project's own documents state about its behaviour, in a form a program can
check: "under these conditions, this observable result". TypeKit keeps them in `claims.json`, each with
an `id`, the `source` as `file#heading`, the surfaces it belongs to, and later `obligations` (sentences
quoted verbatim from the source) and `not-promised` entries (the sentence that makes a difference
intended) [K1 "Claim sources", "Axes"] [K2 "Files"]. A claim exists so that a test checks a promise the
project made, not an expectation the tester invented. A vague sentence ("works well with EF Core") is not
a claim and is skipped [K3 §4].

**Surface and axis.** An axis is a dimension a consumer can vary; its values are what they can choose. A
surface groups axes: a claim about JSON takes every JSON setting, every type shape and every position,
"because that is what a consumer can combine with it" [K3 §3]. Axes exist because, in TypeKit's own
words, "defects that survive review are combinations nobody listed: an option of a serializer, a hosting
model, a document version, a type shape. So you do not imagine the dimensions to vary. You derive them"
[K3 preamble]. TypeKit derives them two ways: *generated* axes, enumerated by reflection over every public
settable property of each options type the feature reads (`enumerate.cs`, 56 lines), and *hand-listed*
axes and a *type-shape catalogue* in a fenced `json probe-axes` block of `.claude/probe-axes.md` [K1
"Axes"] [K8].

**Covering array and row.** A row sets every axis of a claim to one value. A pairwise covering array is
the smallest set of rows such that every pair of values of any two axes appears in at least one row; "a
claim with ten thousand pairs takes about three hundred rows" [K3 §6.3]. It exists to make a large
product affordable: the full product of ten axes with five values each is nearly ten million rows, the
pairwise array a few hundred. `prober rows` computes it, and "do not choose pairs yourself" [K3 §6.3].

**Probe.** One parameterised test per claim, fed by its rows, written in the consumer's own position (in
TypeKit, the Samples project, "the closest thing to a consumer") [K1 "Harness"]. A probe applies a row to
the consumer's configuration (options, host, declared type) and runs one *check per obligation*. A check
returns null or the failure text; it never decides what a refusal means [K1 "Harness"] [K3 preamble].

**Verdict, outcome, evidence.** A row ends in a category the harness assigns from the check's note key,
not from the text: pass, violation, harness error, inconclusive, expected refusal, inapplicable. "Never
match a failure's text to excuse it" [K1 "Harness"]. The replay writes one evidence line per row with its
checks, the settings that applied and the reasons; a pair counts as covered only by a pass "in a row that
was expected and executed, for the settings that actually applied, while the result's identity holds"
[K2 "Files"]. This exists so that coverage is counted by a script from facts, never by the model's
impression of what it ran.

**Triage.** A failing row "is a question, not a finding" [K5 preamble]. TypeKit's triage skill asks four
questions in a fixed order and stops at the first that applies: a harness error; a fact about the
framework at the pinned version explains it; the claim's `not-promised` says it is intended; else a
finding, handed to the code-analyst agent for confirmation [K5 headings]. The order exists because "a
harness that compares unlike things produces the most convincing product bugs there are" [K5 preamble],
and because a wrong finding costs more than a missed one: the caller stops trusting the rounds [K3
preamble].

**Seed.** A patch against `src/` that breaks what the claim promises, kept beside the probe, which the
probe was shown to catch. "A probe that passes proves something only if it can fail" [K1 "Seeds"]. Since
decision 104 a seed per *obligation*, so that a probe is shown to check each promise, not merely to be
able to fail somewhere [K1 "Seeds"].

**Corpus and replay.** Everything a round wrote (probes, rows, claims, registry, seeds) is tracked under
`tools/probe/corpus/`, and `prober replay` re-runs it with no model after each commit, "only for the
probes whose coverage footprint the commit changed", opening a finding when a probe that passed starts
failing [K3 preamble] [K2 "What runs where"]. The footprint is the set of `src/` files a probe executed in
its own process, measured by code coverage, plus declared `watched` paths [K2 "Files"]. The corpus exists
so that exploration is paid for once and the check is free afterwards.

**Round, budget, lock, gate.** A round is a bounded exploration: "two claims or ten minutes of work after
the first build, whichever comes first", enforced by the script, not the model [K3 §6.7] [K4]. A lock
makes two rounds on one branch impossible [K2 "Files"]. The gate is the release check: it refuses while a
claim has uncovered pairs, unless a reviewed waiver says so [K2 "What runs where"]. These exist to keep
exploration from running away and to make "done" a word a script says [K3 §8 last paragraph].

**Fix trailers.** Every fix commit carries `Found-By:` and `Defect-Class:` trailers, so that six months
later the question "which finder found what" is answered by `git log` alone [K7 header] [K1].

---

## 2. What TypeKit's tooling does, and what BLite did with each function

TypeKit's probing is about 6,600 lines of tool and harness [K6 Appendix A: "Harness size: `wc -l`, 6,617
lines"]: `tools/Prober` (about 3,900 lines plus schemas), the `BuildVerdict` tool, `tools/probe/corpus/`
with its harness and generators, `enumerate.cs`, the agent, two skills and a hook [V105 inventory] [K8].
BLite's whole framework is 188 lines: `Candidates.cs` (52), `OracleSeedsTests.cs` (58) and
`tools/oracle-seeds.cs` (78) [F2] [F3] [F4]. The table says, function by function, what happened to
each.

| TypeKit function | What it does mechanically | BLite | How, and evidence |
|---|---|---|---|
| `.claude/probe-axes.md` | Prose for the writer plus a `json probe-axes` block: hand axes, ignored settings, refused values, surfaces, replay configuration, harness rules [K1] | Dropped; replaced by the test's own source | Each test's `<summary>` and `<remarks>` state the contract and the ways it runs; the axes are the nested loops that build its `TheoryData` [F5 lines 1–30] [F6 lines 9–50]. A fork-only `.claude/oracle-catalogue.md` (27 lines) lists written and not-yet-written oracles with what each compares and catches [F13] |
| `tools/probe/enumerate.cs`, generated axes | Reflection over every public settable property of each options type at the pinned versions; unassigned settings reported [K1 "Axes"] | Dropped | BLite's "options" are the engine's modes (indexed or plain, sync or async, crash mode, backend, publish variant), which the writer listed by hand from the API and the README section [T2 brief] [T5 brief] [T7 report] |
| Type-shape generators (`tools/probe/generators/*.cs`) | File-based apps that emit the declarations of the shape catalogue as `*.g.cs`, held to their output by a conventions test [K1 "Harness"] | Dropped; replaced by one hand-written document type per test | `DiffDoc` with string, int, long, double, decimal, DateTime, bool, nullable and enum properties; the value lists carry the boundary cases (`-0.0`, `5m` beside `5.00m`, `"p7"` beside `"p70"`, int and long extremes, nulls) [F5 `Generate`] |
| `claims.json` with `surfaces`, `narrow`, `exclude`, `obligations`, `not-promised`, `inapplicable` | The claim catalogue the universe is built from; quotes checked verbatim against the source file; a schema refuses unknown fields [K1 "Axes"] [K2 "Files"] | Dropped | The README section *is* the claim. The brief names the sections to read and the recent fix commits whose shapes the test must cover [T2 brief]; the test's `<remarks>` quotes the contract ("README 'Transactions & ACID', the write methods' docs") [F6 lines 18–27]; the issue body quotes the README line it contradicts [I260] |
| `prober universe` | Builds each claim's axes and pairs from the surfaces; prints narrowings and exclusions for a reviewer [K2] [K3 §6.2] | Dropped | Not needed without a claims file |
| `prober rows` (pairwise covering array) | The smallest row set covering every pair not yet current [K3 §6.3] | Replaced by the full product, kept small by hand | 331 shapes × 2 collections × 2 modes [F5]; 9 key generators × up to 33 checks [T5 report]; 6 seeds × 60 steps × 3 crash modes × every write [T7 report]; 49 scenarios × 3 backends × 4 publish variants [T13 report] |
| `prober new`, `prober try` | Scaffold a probe with one throwing check per obligation; run one probe's rows in the replay's worktree [K2] | Dropped | The writer copies the previous oracle's shape; the brief says "following the pattern of the two oracle tests in PR #250" and "read docs/ORACLE_TESTS.md and tests/BLite.Tests/Oracles/*.cs on that branch before writing anything" [T5 brief] |
| Harness (`Row`, `Rec.Run`, `NoteKeys`, `Cite`, `Rows.Data`, 639-line `Row.cs`) | Outcome categories from compile-checked note keys; skips tied to open findings; citations to facts and records [K1 "Harness"] [K8] | Replaced by a 52-line `Candidates` helper | Three outcomes: unlisted disagreement fails; listed with an empty issue fails labelled; listed with an issue passes and logs `SKIPPED`; a listed row that agrees fails with `candidate fixed` [F2] |
| `BuildVerdict` tool | One-line build and test verdict, pinned English output, failing tests by name [K1 "Harness"] | Dropped | `dotnet test --filter FullyQualifiedName~BLite.Tests.Oracles` and a TRX log read with a short script when the skip lines were needed [T8 07:41] [F4] |
| `prober register`, `publish`, staging | A probe is staged in the agent's memory and published into the corpus by a command; a registry entry per probe [K3 §6.5] | Dropped | The test file is committed like any test; the "registry" is the `oracles.json`-free folder `tests/BLite.Tests/Oracles/`, discovered by `OracleSeedsTests` as `*Tests.cs` [F3] |
| Replay by coverage footprint, `watched` paths | Re-run only the probes a commit touched; a footprint per probe measured by coverage [K2 "Files"] | Dropped | Every oracle test runs on every `dotnet test` and in CI; each class is kept to about a minute [T5 report "about 55 s"] [T7 report "in about a minute"] |
| Ledger (`universe.json`, `ledger.jsonl`, `findings.jsonl`, `footprints.jsonl`, `runs.jsonl`, `dismissed.jsonl`) | Per-branch state; pairs covered, findings opened and closed, dismissals as decisions [K3 §8] | Dropped; replaced by the registry, the drafts file and the issues | `Candidates` in the test, `Seeds/candidate-issues.md` with the drafts and a table mapping each family to its draft, and the GitHub issue [F9] [T4 22:26] |
| `prober round`, lock, budget | A round under caps the script enforces; one round per branch [K4] | Replaced by one agent session per oracle | Each test was one session with a written brief and a worktree; the parent held the sequencing ("hold the PRs until #250 has had its first review") [T1 06:19] |
| `prober gate`, waivers | Refuses a release while a claim has uncovered pairs [K2] | Replaced by a rule in the epic | "A section counts as stable in the README's stability matrix once its oracle is merged and its registry is empty" [I288 "Oracle coverage by library section"] |
| `prober seeds` (decisions 100, 104) | Applies each seed in a scratch worktree, builds, replays, requires a violation the unseeded probe lacks; one per obligation [K1 "Seeds"] | Kept, reduced | `OracleSeedsTests` (CI, no build: a seed exists per test, touches only `src/`, still applies) and `tools/oracle-seeds.cs` (local: scratch worktree, build, run the class, require a failure not labelled `[known candidate:`) [F3] [F4]. One seed per test, not per obligation; a second seed where the first proves only part (two in the dynamic tests, two in the AOT tool) [T9 report] [T11 report] |
| `prober status`, `gaps` | Worklist and what the corpus leaves out, with reasons [K2] | Replaced by a table in the epic | The epic's "Oracle coverage by library section": written and in review, not pushed, next, and "not covered by any oracle, stays with review" [I288] |
| `prober dismiss` | A dismissal is a decision recorded in the corpus [K3 §8] | Dropped | A row registered as intended cites the README line in the registry comment; none was needed in the end ("No README-intended rows") [T9 report] |
| `probe-triage` skill, four steps with pinned facts | Harness error; fact at the pinned version; not-promised; finding for the analyst [K5] | Kept, rewritten for the domain, fork-only | `oracle-triage`: the oracle or model is wrong; both paths agree but the reference differs (a semantics question for the README); only the indexed path diverges (an `IDX` bug); hand off to the code-analyst [F13 SKILL.md] |
| `prober` agent, `/probe` skill | A headless explorer under caps; modes replay, explore, full, status, gate, triage [K3] [K4] | Dropped | The parent session wrote a brief per oracle and started an ordinary session [T1 throughout] |
| `code-analyst` agent | Confirms a finding and lists siblings [K5 §4] | Kept as is | "The code-analyst agent is already global in your home directory and runs here today" [T1 21:11]; confirmation in practice was throwaway repro `Fact`s read against the code [T2 22:03] [T9 08:17] |
| Fix trailers | `Found-By:`, `Defect-Class:` [K7] | Kept, with BLite's classes | `RV OT IS US CA`; `IDX WAL TXN BSON LINQ MP PLT`; a `git log` one-liner counts them [F12 "Commit trailers"] |
| Checkout-guard hook on the agent | Refuses edits to the caller's checkout [K3 frontmatter] | Dropped | Worktrees per session instead [T2 header] |

The one thing BLite added that TypeKit does not have in this form: the registry row carries the issue
number and the rule is self-cleaning. TypeKit's `r.Skip("F7", why)` ties a skip to an open finding in the
ledger [K1 "Harness"]; BLite ties it to the GitHub issue and makes the fix PR delete the row [F2] [F1
"Candidate registry"].

---

## 3. The lightweight process, step by step, as BLite ran it

### 3.1 The decision, and the order of entry

The question was put on 2026-10-16 at 21:09: "I want to try out the bug-detection machinery that is
already implemented in CodoMetis.TypeKit and adopt it to an up-to-date fork of the upstream repo" [T1
21:09]. Two minutes later the answer was: "your instinct is right about the approach, but wrong about
which part to port" [T1 21:11]. The reasoning was TypeKit's own record:

| Finder | Fixes of 133 | Cost recorded |
|---|---|---|
| Review sessions and outside reviews | 89 | hours per session, 44 sessions |
| code-analyst agent | 10 | three runs on one day |
| prober | 3 real, 1 false | about a week, 6,617 lines of tool and harness |

[T1 21:11, from K7 "Class by found-by": RS 69 + OR 20, CA 10, PR 3]. "Harness traps outnumbered product
defects found by probes roughly ten to one" [T1 21:11]. And TypeKit's concept note had already written
the transfer rule for exactly this case: "Two ordinary tests in the existing test project first, no
tooling: the model-based or differential test for the product's central obligation, and a round-trip or
refusal test for its second, each with a seed that shows it can fail. If they find nothing in a week,
stop; the rest is not worth installing there. If they find what the profile predicts, they have earned
the obligations file, the seeds and the corpus" [K6 §12]. The session quoted this and added why BLite
would do better than TypeKit: "BLite has exact ones where TypeKit had to construct a twin": LINQ-to-Objects
for the query provider, a dictionary for the store, round trips for BSON and encryption [T1 21:11].

What to keep was decided the same evening, after the first results: "exact oracles with a seed each, the
rule that every guard is shown to fail, fix trailers that make finders countable from git log, a fixed
triage order, and the code-analyst agent. What did not pay off at its cost: pairwise covering arrays,
axes enumerated by reflection, generated type-shape harnesses, the ledger with locks and rounds, the
obligations schema and the gate. The first list is a few hundred lines. The second is six thousand, and it
is TypeKit-shaped" [T1 21:55]. The split by location followed from the repository being someone else's:
"only tests and plain conventions go there. Agents and skills stay in your fork and your home directory"
[T1 21:55].

### 3.2 Who derives the axes, from what, and where they are written

Nobody enumerated settings by reflection. The axes of each test were chosen by the session that wrote
it, from three inputs the brief named: the README sections that make the promise, the public API's modes,
and the recent fix commits whose defect shapes the test must reach [T2 brief: "Read README.md 'Advanced
Indexing', 'Powerful Query Engine (LINQ)' and the BLQL sections for what is promised; look at the recent
upstream fix commits (7dbe21d, f8f437b, 33c6bbf, ffdaaba, 7e37f33, 70eb3c3) for the defect shapes this
must cover"]. The axes are written nowhere but in the test file, as the loops that build the theory data,
and in the row's name, which carries its coordinates. The documentation then describes them in prose
[F1 "What it is"].

What the axes were, per oracle:

| Oracle | Axis 1: the case | Axis 2: the ways | Axis 3 | Reference | Source |
|---|---|---|---|---|---|
| LINQ differential | 331 hand-listed query shapes, grouped by property type and operator (`count == 5`, `count < 9.5 (double bound)`, `count two lower bounds`, `orderby maybe take 20 (nulls)`) | indexed collection, plain collection | sync, async | LINQ-to-Objects over the seeded list | [F5 `BuildCatalogue`] [T2 report] |
| Store model | 8 seeds of 300 random steps over 13 operation kinds | single-process, multi-process | plaintext, encrypted (added later) | dictionary with committed and pending views | [F6] [T2 report] |
| B-tree model | 9 key generators (int, long and double extremes, prefix strings, control bytes, keys up to the size limit, unique, multi-process) | up to 33 checks (each query primitive, cursor walks, structure, page accounting) | | sorted dictionary in the index's own key order, plus "reachable pages plus free list plus queued frees equals pages allocated" | [T5 report] [F1] |
| WAL crash | the store model's sequences, 6 seeds of 60 steps | 3 crash modes (process crash, torn write, power loss) | 8 checks; every write a crash point | the store model's committed view after reopen, plus the page invariant | [T7 report] [F8] |
| DynamicCollection | the same 331 shapes | dynamic indexed, dynamic plain | API, BLQL | LINQ-to-Objects **and** the typed collection over the same documents | [T9 brief and report] [F14] |
| Native AOT | the consumer's sections (store sequence, encryption, DynamicCollection with CDC and BLQL, key-value store, BLite.Caching) | JIT, Native AOT | the publish log | the JIT dump, byte for byte; the publish log against the document's promise | [T11 report] [F10] |
| Browser | 49 scenarios (later 54) | OPFS, IndexedDB, in-memory | 4 publish variants; later 3 browsers and 2 context modes | the scenario's own contract (a committed write survives reload; two tabs keep both writes) | [T13 report] [T15 report] |

Two details show how the axes were kept honest without a script. First, the generators were guarded: the
B-tree test fails if a key generator did not force what it was meant to force ("Every generator gave pages
back at least 4 times, and the longest duplicate runs held 133–388 entries per key"; "generator guards"
counted among the seed's failures) [T5 report]. Second, where a way cannot express a case, the row says so
rather than passing: "Six query shapes BLQL can't express are listed with their reason and print
`INAPPLICABLE`", and a new one fails a test until it is listed [T9 report] [F14 lines 43–44].

The value lists were chosen for boundaries, not breadth: counts `[-7, -1, 0, 0, 1, 2, 3, 5, 5, 5, …,
100]`, ratios including `-0.0`, `0.0`, `1e300`, `double.Epsilon`, prices with `5m` and `5.00m`, nicks with
two nulls and an empty string, and exactly one document at each integer extreme "for Single and for bound
tests" [F5 `Generate`]. Documents are inserted in a shuffled order "so that no index order is the scan
order" [F5 `Fixture`].

### 3.3 How claims were collected and tagged

There is no claims file. The unit of a claim is a README section, and the unit of coverage is "one
oracle for every library section the documentation makes a promise about" [I288]. The tagging that
TypeKit does with `surfaces` happened in three places:

1. **In the brief.** The parent session named the sections and the API the oracle must read before
   writing ("Read README.md 'Transactions & ACID', 'Hot Backup', 'Multi-Process WAL' and docs on
   checkpointing for what is promised") [T7 brief].
2. **In the test's remarks.** The store model's `<remarks>` begins "The contract the model encodes (README
   'Transactions & ACID', the write methods' docs):" and then states it as rules: a write without a
   transaction commits on its own; a `BLiteValidationException` leaves the transaction usable while any
   other failure rolls it back; a checkpoint, a backup and a reopen change nothing [F6 lines 18–27].
3. **In the issue.** The body quotes the sentence the behaviour contradicts, with its line: `README
   "Transactions & ACID" (README.md:394): "Durable: WAL ensures data safety even in power loss."` [I260].

The parent session named this as one of the three reasons it worked: "Claims as the test list. Every
test started from a sentence the repository makes: durable in power loss, the index is used
transparently, this API is AOT-safe, BLQL supports these operators. The oracle checks the sentence, which
is why the findings read as broken promises rather than opinions" [T1 08:48].

### 3.4 How a probe was written

**File layout.** `tests/BLite.Tests/Oracles/<Name>Tests.cs` in namespace `BLite.Tests.Oracles`; entity
contexts in `tests/BLite.Shared/`; seeds in `Oracles/Seeds/<Name>Tests.<defect>.patch`; drafts in
`Oracles/Seeds/candidate-issues.md`; a two-line `Oracles/README.md` pointing at `docs/ORACLE_TESTS.md`
[F1 "How to use it"] [T3 report step 1]. Tools that cannot be unit tests (a Native AOT publish, a
browser) live under `tools/aot-smoke/` and `tests/BLite.Wasm.BrowserTests/` with the same conventions:
a `candidates.md` table the runner reads, a `seeds/` directory, a `--prove-seeds` mode [F10] [F11].

**Test framework.** xunit 2.9.3, as the repository already had; a `[Theory]` over
`[MemberData(nameof(Cases))]` where `Cases` is a `TheoryData<string>` of row names, and the test looks the
case up by name [F5 lines 561–580]. The model tests use `TheoryData<int, bool, bool>` built by nested
loops over seeds, process mode and encryption [F6 lines 40–50]. Nothing was added to the test project's
references.

**Shape of the theory body.** For each way the case runs, run it, capture the value *or the exception*
as an `Outcome` ("both are compared"), compare against the reference with the case's own rule (sets by id
for unordered queries, key sequences for ordered ones, values or the exception type for terminals),
collect one line per disagreement naming the way (`indexed/async: …`), and hand the lines to the registry:

```csharp
s_candidates.Verify(name, errors.Count == 0 ? null
    : $"seed {_f.Seed}: query '{name}' disagrees with LINQ-to-Objects:\n  " + string.Join("\n  ", errors), _output);
```

[F5 lines 561–586]. The failure text carries the seed and the row name, so a run can be repeated
(`BLITE_DIFF_SEED=<n>`) [F1 "How to use it"].

**Shared pieces.** The store model's generator and model were moved into `StoreModel.cs` when the WAL
test needed the same sequences; the dynamic tests reused the differential catalogue by extracting it; the
AOT consumer copied the generator [T7 report] [T9 08:00] [T11 08:17]. The page-accounting walk of the WAL
test was made `internal` for the dynamic model test "rather than copied" [T9 08:07].

**Local tools follow the same shape.** The AOT tool's `candidates.md` has a machine-readable table at the
top (`Id | Kind | Match | Issue | Summary`) and the drafts below it; `run.sh` prints `KNOWN … [known
candidate: A<n>]` for a matching warning or dump line and fails on anything else [F10]. The browser
harness's `Candidates.cs` reads the same kind of table, with a `Kind` of `always` or `sometimes`, because
"a pass proves nothing" for a row that fails only in some runs [F11 Candidates.cs].

**Naming and endings.** Row names are human sentences with the operand in them (`count < 9.5 (double
bound)`), family labels state the hypothesis (`null equality on an indexed property finds nothing`) [F5
lines 136–155]. Upstream uses CRLF, the user's global git has `autocrlf=input`, so every new file was
converted and staged with `git -c core.autocrlf=false add`; this cost time in every session and is in
every brief [T4 report] [T5 brief].

### 3.5 How the covering array, or its substitute, was chosen

BLite ran the full product every time and kept it small by choosing values, not by choosing pairs.
The sizes:

| Oracle | Rows per run | Run time | Source |
|---|---|---|---|
| LINQ differential | 331 cases, each 4 ways | not recorded on its own; the whole oracle filter of the first branch (both tests) ran inside the full suite's few minutes | [T2 report] [T4 report] |
| Store model | 16 rows (8 seeds × 2 modes), 300 steps each | about 3 s plaintext; longer encrypted | [T1 21:45] |
| B-tree model | 270 tests (9 generators × up to 33 checks) | about 55 s | [T5 report] |
| WAL crash | 24 rows (3 modes × 8 checks), 1,886 crash states | about a minute | [T7 report] |
| DynamicCollection | 3,335 tests | 1 min 16 s | [T16 11:02] |
| Encrypted, whole oracle set | 680 tests | 3 min 2 s | [T16 11:07] |
| AOT tool | 871 dump lines, two publishes | about 20 s; 37 s with seeds | [T11 report] |
| Browser | 53–54 rows | about 5 minutes per browser | [T15 brief] |

Where the product would have been too large, it was sampled by a rule stated in the code, not by a
pairwise algorithm: the WAL test crashes recovery only at "sampled crash states" ("every third such step,
or every one when shrinking") [F8 line 252] [T7 report]; the browser harness publishes four variants but
runs the same 49 scenarios in each [T13 report]. The parent session's position on pairwise arrays was
explicit: "If the catalogue of query shapes grows past what a hand-written list holds, a pairwise
generator is a fifty-line addition to the test, decided then" [T1 21:55]. It was not needed in the two
days.

### 3.6 How a run was budgeted

There was no round budget in the TypeKit sense. The units of budget were:

- **One session per oracle**, with a written brief that fixed the scope, the files to read first, the
  base branch, the seed recipe and "done means" [T2 brief] [T5 brief] [T7 brief]. Sessions took 30 to 55
  minutes each (3.4 below). The brief forbade pushing until the maintainer had reviewed the first PR, so
  the sequencing was the parent's, not the tool's [T5 brief last sentence].
- **Per-class run time**, held to about a minute so the class can stay in the normal suite [T5 report]
  [T7 report].
- **Shrink budgets** inside a failing row: 45 s for the store model, 20 s then 10 s for the WAL test, and a
  60 s watchdog around the B-tree walk after a seeded run hung "for over 10 minutes inside an engine
  traversal" [F6 line 88] [F8 line 100] [T5 report].
- **Drafts before the session ends.** Every writing session ended with drafts for what it found, so that a
  filing session could run without re-deriving anything [T5 report] [T7 report] [T9 report].

### 3.7 How a failing row was judged and shrunk

**The oracle judges.** A row fails only when its reference disagrees; the writer writes no expectation.
The three outcomes are the registry's, quoted in full from the helper: "Passes a row that agrees and is
not listed, or that disagrees and is listed with an issue; fails every other row" [F2 `Verify` doc
comment]. A listed row that agrees fails with `candidate fixed: remove the entry for '<name>'`, "so the
list never goes stale" [F2 class comment].

**Shrinking.** The model tests shrink a failing sequence by removing chunks "half the sequence, then
quarters, … down to single steps", replaying after each removal, within the budget, and print the
shortest sequence that still fails, one operation per line, with the seed and the step [F6 lines 164–192]
[F1 "Read a failure"]. The WAL test shrinks to "the shortest sequence that still fails at a crash in its
last step" and prints the crash point as "crash after FreePage(24): page 24 written during step 3" [F9
WalCrashTests seed header]. A shrunk sequence is the repro that goes into the draft and the issue [I260
"Repro"].

**Confirmation before filing.** Every family was reproduced outside the oracle, as "throwaway repro Facts
for D1–D9 (not to be committed) so every draft's repro is actually run, not just reasoned" [T9 08:17], or
"standalone repros for the drafts, including what each defect does at the collection level" [T5 23:13],
and the mechanism was read in the code with `file:line` [I249 "Mechanism (verified on 0450b97)"].

**The triage order**, as BLite's skill states it, in the order the differential test makes natural [F13
SKILL.md]:

1. Is the oracle or the model wrong? Cases that stopped here: the store model expected
   `InvalidOperationException` for a duplicate key where BLite throws `BLiteValidationException` and
   leaves the transaction active [T2 report]; the AOT consumer's CDC count "was my arithmetic (50 inserts,
   not 51)" [T11 08:22]; the dynamic model's rule that "an explicit null counts as a value for the unique
   index" [T9 report].
2. Do both engine paths agree with each other but differ from the reference? Then it is a semantics
   question to settle against the README before anything is filed. The `!=` family was this: both paths
   drop null-valued documents, LINQ keeps them; the issue "should ask rather than assert" [T1 22:09].
3. Does only the indexed path diverge? "That is a bug with no further question: the index serves a
   different answer than a scan of the same documents" [F1 "Triage"].
4. Hand the family to the code-analyst for confirmation and siblings; file only when confirmed.

**A correction that shows why the order matters.** The parent session first reported that all fourteen
rows diverged on the indexed path only; a rerun "with a complete filter" showed its own filter had
dropped the plain collection's lines, and two rows were in fact wrong on both paths [T1 22:09]. The
lesson it drew is TypeKit's first triage rule in different words: read every way's line before deciding
which step applies.

### 3.8 How a finding became an issue

The route, in the order it was run every time:

1. **Family label, issue empty.** The rows go under one label in the test's registry with an empty issue,
   so they keep failing, "so nothing is quarantined before it is filed" [F1 "Candidate registry"].
2. **Draft.** `Seeds/candidate-issues.md` gets a section per family: rows, repro (three or four documents,
   the plain collection as control), expected, actual, hypothesis with code references, and at the top a
   table mapping family label to draft number [F9 candidate-issues.md head].
3. **Cross-check.** Open issues first; after the WAL round, closed issues and PRs too, with `git log -S`
   for the commit that introduced the behaviour: "Extend step 1 before filing: cross-check W1–W3 against
   CLOSED issues and PRs too, and reference them in the bodies" [T8 cross-session message]. That check
   rewrote W1's history (the fsync went missing in three steps between v1.0.0 and v4.0.0, not in the commit
   the draft had named) and reframed half of W2 as a trade-off two merged PRs had already accepted [T8
   07:51].
4. **Group by root cause.** D6 and D7 became one issue "since both live in `BsonValueToIndexKey` /
   `CreateIndexKeyFromObject`"; A3, A4, A6 and A7 became one issue, the pragma pattern; BW2, BW3, BW4 and
   BW7 became one, the blocking wait [T10 08:45] [T12 report] [F11 candidates.md]. Families 1 and 4 of the
   differential shared a cause and a decision, so one issue [T1 22:12].
5. **File**, with the labels the repository has (`bug`, `package/core`, `area/indexing` or `area/linq`,
   `severity/*` where it is clear; `encryption`, `package/generator`, `package/client` where they apply)
   [T6 report] [T12 report].
6. **Number into the registry**, commit, run the oracle filter and confirm every family's rows print
   `SKIPPED` at the draft's row counts and nothing says `candidate fixed` [T10 08:50].
7. **Comment on siblings** (#237, #234, #254 got one line each saying the test reproduces them and carries
   them as known) and link the test PR from each issue [T6 report] [T10 report].
8. **Fix PR removes the entry** and carries `Found-By: OT` and `Defect-Class:` [F12].

**The issue body**, as the filed issues have it [I249] [I260] [I273]:

- First paragraph: what fails in one sentence, then "Found by <test> (<path>, <PR>), which carries the
  affected rows as a known candidate until this is fixed."
- The README or document sentence contradicted, quoted with its line.
- `## Repro`: the shrunk sequence or three documents and the call, with expected and actual as comments;
  the `dotnet test` command and how to make the row fail again ("With the issue number removed from the
  family in `s_candidates`, the row fails and prints the seed, the crash point and the shortest sequence").
- `**Expected:**` / `**Actual:**`.
- `## Mechanism (verified on <sha>)`: the code path with `file:line` for every step.
- `## History` where a decision is behind the behaviour: commits, dates, versions, the design note that
  intended something else.
- `**Affected versions:**` from `git describe --contains` of the introducing commit; "Not reproduced on
  hardware; the test models a power loss as …" where that is true.
- `## Fix` or `## Fix considerations`: options, framed as the maintainer's choice where it is a trade-off
  ("This is a durability/latency trade-off for the maintainers to decide"), with the per-commit cost
  stated; "Happy to send the fix if you want it."
- `## Workaround` where one exists.
- `Related:` the sibling issues and PRs, and the other findings of the same test.

### 3.9 How candidates that are not fixed yet are kept

The registry lives in the test that owns the rows, not in one central file, and it is the only
quarantine. Its rules, verbatim from the documentation [F1 "Candidate registry"]:

- "it still disagrees and its issue is **filed**: the row passes and writes `SKIPPED [known candidate:
  <family>, <issue>] <disagreement>` to the test output (xunit 2 has no dynamic skip, so this line is the
  trace a run shows);"
- "it still disagrees and its issue is **empty**: the row keeps failing with `[known candidate:
  <family>]`, so nothing is quarantined before it is filed;"
- "it **agrees**: the row fails with `candidate fixed: remove the entry for '<name>'`, so the fix's PR
  removes the entry and the registry cannot outlive the defect."

A `Fact` per test fails on an entry that names no row, "for example after a row is renamed" [F1] [F2
`AssertAllIn`]. For tools outside xunit the same table lives in `candidates.md`, with a `Kind` column so a
row that fails only sometimes is never reported as fixed [F11 Candidates.cs]. Already-known issues go in
with their number from the start, even in a session told to leave issues empty: "That departs from your
'empty Issue' instruction, because the registry's own rule is that a filed issue goes in the entry" [T5
report]. The registry is the thing the maintainer sees: "a quarantine that is visible, classified, and
self-cleaning, which is the one piece of the replay machinery worth keeping" [T1 21:55].

### 3.10 How the corpus is kept alive without per-commit replay

- **It is the test suite.** The oracle tests are ordinary tests in `tests/BLite.Tests`; CI runs them on
  every push with no extra job. The cost of "replay everything" is the per-class minute of 3.5 [F1 "Run
  the oracle tests"].
- **Seeds stay applicable by a CI test without a build**, and are re-proven locally before a release by
  the tool [F3] [F4] [F1 "Seeds"]. "A refactor that strands a seed fails CI instead of leaving the oracle
  unproven" [F1].
- **The registry cleans itself** through the `candidate fixed` rule, and the fix PR is the one that removes
  the row [F1].
- **Growth is by catalogue, not by test.** The documentation states the rule per oracle: "A new LINQ
  operator or terminal is one more `QueryCase` in the `BuildCatalogue` method"; "A new index key type or
  key shape is one more key generator"; "A new collection operation or storage feature is one more
  `StoreOpKind`" and "once it is a `StoreOpKind`, one more step the injector of `WalCrashTests.cs`
  interrupts"; "A new public API is one more call in `tools/aot-smoke/Consumer`" [F1 "How detection grows
  with the feature set"].
- **What is written and what is next** is a table in the epic, with the rule that makes it a gate by
  convention: stable in the stability matrix only when the oracle is merged and its registry is empty
  [I288]. The fork keeps the same list as `.claude/oracle-catalogue.md`, with the instruction "Add a row
  to 'Written' in the same commit that adds the test and its seed" [F13].
- **Stacked branches until merged.** Because every oracle depends on the machinery of the first PR, the
  test PRs were stacked (#250 ← #259 ← #263 ← {#272, #277} ← #294) and the parent held them until the
  maintainer's first review [T1 06:19] [T1 11:22]. A late commit on the bottom branch then had to be
  merged up through every branch by hand [T16 report]; see 4.3.

---

## 4. What it produced in BLite

### 4.1 The issues

28 issues, 27 of them straight from failing rows and one found while fixing another; plus one draft not
filed. Labels as filed; every one also carries the repository's default `triage` label [G1].

| Issue | One line | Found by | Labels beyond `bug`, `package/core` |
|---|---|---|---|
| #247 | `== null` and `OrderBy(nullable).Take(n)` served from a typed index return no null-keyed documents | LINQ differential, families 1 and 4 | area/indexing, area/linq |
| #248 | `x.Prop != constant` does not match documents whose property is null; the evaluator has no branch for a stored null | LINQ differential, family 2 | area/linq |
| #249 | Indexed double equality misses `-0.0`: the key encoding gives the two zeros different keys | LINQ differential, family 3 | area/indexing |
| #254 | DynamicCollection range filters and sorts on an indexed double are wrong: keys are little-endian `BitConverter` bytes | noticed in #249's body, filed while fixing it | area/indexing |
| #255 | `BTreeIndex.Equal/In/GreaterThan/LessThan/Between/StartsWith` miss duplicate keys and fail past the last key | B-tree model, B1 | area/indexing |
| #256 | Equal keys come back in reverse insertion order on a forward scan | B-tree model, B2 | area/indexing |
| #257 | A NaN with the sign bit clear sorts above `+Infinity` in an index on a double | B-tree model, B3 | area/indexing |
| #258 | Every reopen strands the unwritten rest of the last page growth block | B-tree model, B4 (page invariant) | area/indexing |
| #260 | WAL commits are never flushed to disk: a power loss drops acknowledged commits and can leave documents pointing at freed pages | WAL crash, W1 | severity/high |
| #261 | A crash leaks pages freed or allocated outside the WAL | WAL crash, W2 | area/indexing |
| #262 | A crash between the two page writes of `FreePage`/`AllocatePage` leaves a Free page off the free list | WAL crash, W3 | area/indexing |
| #264 | BLQL filters and sorts cannot reach a nested field, though an index can be built on one | Dynamic differential, D1 | area/linq |
| #265 | BLQL orders and compares ObjectIds by their little-endian bytes | Dynamic differential, D2 | area/linq |
| #266 | BLQL `First`/`FirstOrDefault` ignore Sort and Skip | Dynamic differential, D3 | area/linq |
| #267 | BLQL range operators match fields of another BSON type: null under `$lt`, strings under `$gt` | Dynamic differential, D4 | area/linq |
| #268 | BLQL compares two Int64 values through double | Dynamic differential, D5 | area/linq |
| #269 | A dynamic index misses keys for a numeric bound of another BSON type and keys no Decimal128 or Boolean value | Dynamic differential, D6 + D7 | area/indexing |
| #270 | `QueryIndexAsync` rejects a DateTime bound | Dynamic differential, D8 | area/indexing |
| #271 | A failed unique `CreateIndexAsync` leaves the half-filled index registered | Dynamic store model, D9 | area/indexing |
| #273 | Typed collections cannot write under Native AOT: the generated mapper registers its BSON keys by reflection that trimming removes | AOT twin, A1 | severity/critical, package/generator |
| #274 | Encrypted database: a page allocated by a transaction that never commits cannot be decrypted afterwards | AOT twin, A2 (JIT too) | severity/high, encryption |
| #275 | `#pragma warning disable IL…` hides reflection from BLite's build but not from the consumer's publish | AOT twin, A3 + A4 + A6 + A7 | |
| #276 | BLite.Caching uses reflection-based System.Text.Json: throws in any trimmed or AOT app | AOT twin, A5 | package/client |
| #289 | BLite.Wasm: a committed write is lost on a page reload unless a checkpoint ran | Browser, BW1 | severity/critical |
| #290 | BLite.Wasm: synchronous waits block or kill the browser runtime | Browser, BW2 + BW3 + BW4 + BW7 | severity/high |
| #291 | BLite.Wasm: two tabs on one database lose a committed write (IndexedDB) or cannot open it (OPFS) | Browser, BW5 | area/multi-process |
| #292 | BLite.Wasm: a 50 MB database cannot be reopened; `CheckpointAsync` returns at once while a background checkpoint runs | Browser, BW6 | severity/medium |
| #293 | BLite.Wasm: the package lacks its JS files and offers no typed-context factory | Browser, BW8 + BW9 | |
| BW10 | WebKit without persistent storage has no OPFS and `Auto` picks it anyway; drafted, not filed | Browser on WebKit | |

[G1] [T1 22:37, 06:08, 08:07, 08:55, 09:05, 11:22] [T15 report]. Two things the tally should not hide: the
store model found nothing on upstream HEAD in its first run, "which is also information: the
collection-level contract holds in one process without crashes" [T1 06:18]; and three of the issues are
design questions as much as bugs, filed as questions (#247, the `!=` half of #248's framing, #260's sync
mode) [T1 06:18] [T1 22:12].

Alongside the issues: 7 test PRs (#250, #259, #263, #272, #277, #294, #295), 12 fix PRs (#251–#253,
#278–#286), Discussion #287 and epic #288 with the coverage table [G2] [T1 11:22].

### 4.2 The effort

Wall clock from the first question to the last PR of the stack: 2026-10-16 21:09 to 2026-10-17 11:22,
about 14 hours including a night with no activity between 23:48 and 06:00 [T1 timestamps]. The sessions,
each a brief from the parent and a worktree:

| Session | Start | End | Result |
|---|---|---|---|
| Decision and orchestration (parent) | 16th 21:09 | 17th 12:00 | briefs, filing of the first three issues and PR #250, relays |
| First two tests [T2] | 21:14 | 21:44 | differential (14 rows in four families) and store model, two seeds |
| Triage of the four families | 21:54 | 22:03 | standalone repros, drafts |
| Machinery [T3] | 21:58 | 22:08 | Oracles folder, seeds test, registry, CONTRIBUTING, seeds tool, fork-only skill and catalogue |
| Assemble and document [T4] | 22:15 | 22:26 | skip-while-open rule, CRLF, `docs/ORACLE_TESTS.md` |
| Fix PRs #251–#253 | 22:40 | 23:16 | three PRs, #254 filed |
| B-tree model [T5] | 22:53 | 23:47 | 73 rows in four new families, two known reproduced |
| B-tree filing [T6] | 06:00 | 06:08 | #255–#258, PR #259 |
| WAL crash [T7] | 06:22 | 07:16 | three families, 1,886 crash states |
| WAL filing [T8] | 07:37 | 07:51 | #260–#262, PR #263, history cross-check |
| DynamicCollection [T9] | 07:47 | 08:32 | nine drafts, 252 candidate rows |
| Native AOT [T11] | 08:12 | 08:51 | seven candidates, two seeds |
| Dynamic filing [T10] | 08:43 | 08:54 | #264–#271, PR #272 |
| AOT filing [T12] | 08:58 | 09:05 | #273–#276, PR #277 |
| Fix round 2 | 09:08 | 09:59 | PRs #278–#286 |
| Browser harness [T13], in parallel | about 08:57 | 10:23 | 49 scenarios, 20 failing, nine findings |
| Restack [T16] | 10:58 | 11:19 | five merge commits, PR #294 |
| Browser into the frame [T14] | about 11:00 | 11:22 | registry, seed, #289–#293, PR #295 |
| Firefox and WebKit [T15] | 11:22 | 12:17 | BW10 drafted, persistent-context mode |

Code written on the top of the stack, excluding the dynamic, AOT and browser branches: `BTreeModelTests`
1,419 lines, `WalCrashTests` 1,325, `DifferentialQueryTests` 839, `StoreModel.cs` 428, `StoreModelTests`
193, `candidate-issues.md` 744, `docs/ORACLE_TESTS.md` 243, the four seed patches 231, the three framework
files 188 [F-wc]. The dynamic tests add 1,684 [F14 wc]. The browser harness is two projects [T13 report].

### 4.3 What went wrong, what it missed, and the lessons the transcripts record

**Mistakes made and caught.**

- The parent misreported the `!=` family as indexed-only because its own output filter dropped the plain
  collection's lines; corrected after a rerun with a complete filter [T1 22:09]. Lesson: read every way's
  line before classifying.
- A seeded B-tree run hung for over ten minutes inside an engine traversal of a corrupted tree. The test
  gained its own cycle-safe walk before any engine traversal and a 60 s watchdog, "so a broken tree is
  reported as a `Structure` failure instead of a hang" [T5 report].
- Two tests backed up into the same temp folder because the backup manifest has a fixed name; the fix
  landed as a late commit on the bottom branch and broke the mergeability of the four stacked PRs above
  it, which then needed a merge commit each, one with a conflict [T9 08:25] [T1 10:56] [T16 report].
  Lesson: a stack is cheap to build and expensive to move; land the base before stacking, or expect one
  restack.
- A `sed` whose delimiter clashed with `#277` emptied four issue bodies for a minute; restored, visible in
  the edit history [T12 report].
- The bottom branch had been rewritten upstream with new commit ids and the same tree; the B-tree branch
  was replayed onto it and force-pushed with lease before its PR existed [T6 report].
- zsh did not word-split a label variable, so an issue was created without labels on the first try [T6
  06:01]; zsh also mangled a browser configuration string [T15 11:36].
- The registry's "skip" is a logged pass in xunit 2.9.3, "so the quarantine is invisible in CI counts"
  [T1 22:27] [T1 06:18].
- CRLF: every file needed converting and staging with `core.autocrlf=false`; the seed patches had to stay
  LF because a CRLF copy no longer applied [T4 report].
- Pre-existing concurrency tests flake under the load of the full suite; every full run was followed by
  reruns in isolation [T16 11:12] [T5 brief].
- The AOT publish needed `LIBRARY_PATH` for Homebrew's OpenSSL and the `xcrun` clang shim; both are handled
  in `run.sh` [T11 08:26, 08:37].

**What the method cannot see**, as its own documentation states: targets other than `net10.0`, real
multi-process races, crashes in multi-process mode and inside `Backup`, torn page writes, performance,
"and anything whose correct answer has no exact reference, such as API design and documentation drift.
Those stay with review" [F1 last paragraph]. Two concrete misses: #212 (keys not prefix-free) "does not
show at this level: a raw `BTreeIndex` key has no document id appended", so the oracle sat below the
defect [T5 report]; and the first-run claim that the store contract held was limited to one process with
no crashes, which the WAL test then contradicted under power loss [T1 06:18] [T7 report].

**Lessons the transcripts record**, in their words where possible:

1. "Exact oracles. LINQ-to-Objects, a sorted dictionary, the committed view of a model, the JIT build of
   the same program. None of them is an approximation, so no finding needed an argument about what the
   right answer was" [T1 08:48]. "That is what the principle 'model budget goes where the oracle is
   weakest' predicts: an exact oracle plus deep state is where reading finds least and programs find
   most" [T1 21:11].
2. "Claims as the test list" [T1 08:48] (3.3 above).
3. "Seeds. Each test was shown to fail before it was trusted, so none of the twenty-six is a test that
   happens to pass or fail for reasons unrelated to the product" [T1 08:48]. The seeds tool had to count
   only unlabelled failures, because a test with registered candidates "would always pass" the naive
   check [T3 report step 5].
4. "Findings make the case for the machinery" and "agents and workflows never go into someone else's
   repository on the strength of an invitation" [T1 21:11] [T1 22:12 "What not to send yet"].
5. Hold the test PRs until the maintainer has seen the pattern once, "so the maintainer sees the pattern
   once before it multiplies" [T1 22:50]; but file a durability finding at once, because "the issue stands
   on its own without the test PR" [T1 07:18].
6. Cross-check closed issues and PRs and the commit that introduced the behaviour; "an issue that cites
   that history and frames the fix as a trade-off lands better with the maintainer than one that reads as
   a discovery" [T1 07:40].
7. One issue per root cause, and ask rather than assert where the README is silent [T1 22:12] [T10
   08:45].
8. The registry must be self-cleaning and visible; a deleted row loses the guard [T1 22:12 step 2].
9. "The method finds what has an exact reference. API misuse that compiles, documentation drift and
   unclear error messages are invisible to it and stay with review. The TypeKit fix history showed exactly
   that split, and it holds here too" [T1 08:48].
10. A side effect worth naming: several of the later issues were found by review or by running the
    README samples for the v6 documentation work, not by oracles (#299 "while compiling and running the
    README samples for #298"; #301 "while checking the secure-erase behaviour for #299"; #304, #305 and
    #307 "while reviewing … for #296"), and two of them (#304, #305) propose the oracle row that would
    hold them [G1 bodies]. The catalogue tests created a habit of asking "which oracle would hold this".

---

## 5. Adoption guide for any repository

### 5.1 Prerequisites

- A test project the repository already runs in CI, in whatever framework it uses. Nothing here needs a
  particular one; the templates below are given for xunit (BLite) and MSTest (ValueRanges).
- At least one **exact reference** for a central promise: a simpler implementation of the same
  computation (LINQ-to-Objects, a dictionary, set theory over a small universe, PostgreSQL itself), an
  invariant (pages reachable plus free equals allocated), a twin build (JIT against AOT), or an identity
  (round trip). If the only reference you can think of is "what the code did last week", stop: that is a
  snapshot test, and this method will produce noise [K6 §12 "Which oracle a domain gets"].
- Documents that make promises: a README, a docs folder, a limitations file. The promise is the claim.
- Issues with labels, so a finding has somewhere to go that is not a list in a file.
- `git apply` and a scratch worktree, for seeds.

### 5.2 The artefacts, with templates

Create them in this order. Each one is small; none depends on a tool.

**1. The first catalogue test**, with its seed, before anything else. Pick the section with the densest
recent bug history and an exact reference ("the last 35 upstream commits are almost all index/LINQ/WAL
correctness fixes found one at a time" [T2 brief]). Write it in the consumer's position.

xunit skeleton, the shape of `DifferentialQueryTests` [F5]:

```csharp
public class <Area>DifferentialTests : IClassFixture<<Area>DifferentialTests.Fixture>
{
    // Fixture: seeded data (override with an environment variable), one instance per "way".
    public sealed class Fixture : IDisposable { /* seed, data, the ways */ }

    // Known candidates: family label (the hypothesis), issue (empty until filed), the row names.
    private static readonly Candidates s_candidates = new()
    {
        { "<family label stating the hypothesis>", "", "<row name>", "<row name>" },
    };

    // The catalogue: one named case per shape. The name carries the operand.
    private static readonly Dictionary<string, Case> s_cases = BuildCatalogue();
    public static TheoryData<string> Cases => new(s_cases.Keys);

    [Fact] public void Candidates_NameExistingRows() => s_candidates.AssertAllIn(s_cases.Keys);
    [Fact] public void Fixture_ReachesWhatTheAxesPromise() { /* guards: indexes exist, splits happened, … */ }

    [Theory, MemberData(nameof(Cases))]
    public async Task Case_AgreesWithTheReference(string name)
    {
        var c = s_cases[name];
        var errors = new List<string>();
        foreach (var (way, run) in Ways())                 // the product of the hand-listed axes
        {
            var outcome = await Guard(() => run(c));       // value or exception, both compared
            var error = c.Compare(Reference(c), outcome);  // the case's own rule: set, sequence, scalar
            if (error != null) errors.Add($"{way}: {error}");
        }
        s_candidates.Verify(name, errors.Count == 0 ? null
            : $"seed {Seed}: '{name}' disagrees with the reference:\n  " + string.Join("\n  ", errors), _output);
    }
}
```

MSTest skeleton, for a repository that already has a `Candidates.Verdict` over a list of failure lines
(as ValueRanges does [V-cand]):

```csharp
[TestClass]
public class <Area>ProbeTests
{
    public static IEnumerable<object[]> Rows()
    {
        foreach (var type in DiscoveredTypes())
            foreach (var declaredAs in new[] { "union", "variant", "interface", "object" })
                foreach (var options in OptionsCatalogue())
                    yield return [type, declaredAs, options.Name];
    }

    [DataTestMethod, DynamicData(nameof(Rows), DynamicDataSourceType.Method)]
    public void Row_AgreesWithTheReference(Type type, string declaredAs, string optionsName) { /* one row */ }

    // Or one sweep method that collects every disagreement and ends in the verdict,
    // which is how the existing small-model sweeps are written:
    [TestMethod]
    public void Sweep()
    {
        var failures = new List<string>();
        foreach (var row in Rows()) { var line = Check(row); if (line != null) failures.Add(line); }
        Candidates.Verdict(oracle: nameof(<Area>ProbeTests), failures, header: "<what the sweep compares>", method: nameof(Sweep));
    }
}
```

The row's failure line must start with a stable prefix (the operands and the operation, never the actual
value), because the registry matches by that prefix [V-cand remarks].

**2. The seed**, in the same commit. Revert a known fix restricted to `src/`, or plant a defect by hand;
run the test; confirm it fails *for the defect*; save the diff with a header [F1 "Add a new oracle test"]
[F9 seed header]:

```text
Seeded defect for tests/<Project>/Oracles/<Name>Tests.cs

<Hand-made | Revert of <sha> (#<issue>)>: <what is broken, in one paragraph, naming the file and the method>.

With it applied, rows that pass on <base sha> fail, e.g. <row>, <row> (<n> of <m>). <Row> shrinks to:
    1. <step>
    2. <step>
  <the failure as the test prints it>

Reproduce: git apply <path to this patch> && dotnet test <project> --filter <class>
Restore:   git checkout -- src

Never commit the seeded state.

diff --git a/src/... b/src/...
```

**3. The registry helper** (52 lines in BLite [F2]); if the repository already has one, reuse it. The
three outcomes are the contract; the implementation is incidental. For a framework with a real dynamic
skip, use it; otherwise log the skip and say so in CONTRIBUTING.

**4. The seeds test** (58 lines [F3]): every `*Tests.cs` under the oracle folder has a seed; every seed
touches only `src/` (`git apply --numstat`); every seed still applies (`git apply --check`). No build.

**5. The seeds prover** (78 lines [F4]): for each seed, a detached scratch worktree of HEAD, apply, build,
run the class, read the results file, require at least one failure whose message does not contain the
known-candidate label; print `CAUGHT`, `MISSED` or `ERROR`; exit 1 unless all caught; remove the
worktree. Local only.

**6. The drafts file**, `Seeds/candidate-issues.md`: a status line at the top, a table from family label
to draft number and row count, then one section per draft [F9]:

````markdown
## <n>. <title as the issue will have it>

**Rows:** `<row>`, `<row>`, …

**Repro:**
```<language>
<three or four inputs; the control path; expected and actual as comments>
```

**Expected:** <what the reference and the control path say>.
**Actual:** <what the path under test says>.

**Hypothesis** (checked against the code): <mechanism with file:line>.
**Affected versions:** <from git describe --contains of the introducing commit>.
**Existing issues checked:** <numbers, and why none covers it>.
````

**7. The issue body template**, the structure of 3.8 reduced to headings:

```markdown
<One sentence: what fails.> Found by <test> (`<path>`, <PR>), which carries the affected rows as a known
candidate until this is fixed.

<Document> "<section>" (`<file>:<line>`): "<the promise, quoted>."

## Repro
<shrunk sequence or inputs and call; expected and actual as comments; the test command; how to make the
row fail again>

**Expected:** …
**Actual:** …

## Mechanism (verified on <sha>)
- <step, file:line>

## History            <!-- only where a decision is behind the behaviour -->
- <commit, date, version: what it changed and why, quoting the design note>

**Affected versions:** <range>. <What was not reproduced on real hardware, if anything.>

## Fix considerations
<options; the trade-off named as the maintainer's call; cost stated; "happy to send the fix">

## Workaround          <!-- where one exists -->

Related: <siblings, closed fixes it neighbours, the other findings of the same test>
```

**8. The registry row**, as a C# entry or a table row:

```csharp
{ "<family label stating the hypothesis>", "#<issue or empty>", "<row>", "<row>" },
```

```markdown
| <Id> | always or sometimes | `<test method or row pattern>` | `<substring of the failure>` | #<issue or empty> | <summary> |
```

**9. CONTRIBUTING section and trailers** (about 22 lines in BLite [T3 report step 4]): what an oracle test
is, that every one ships a seed, how the registry behaves, and the two trailers with the repository's own
defect classes.

**10. The method document**, `docs/ORACLE_TESTS.md` or its equivalent, in four parts: why it exists (the
bug history it answers), what it is (each mechanism in one paragraph), how to use it (run, read a
failure, triage order, register, retire, add a test), how detection grows with the feature set (one
sentence per catalogue), and what it does not see [F1 headings] [T1 22:21].

**11. The catalogue of oracles**, written and not yet written, each with what it compares, what it would
catch, and a seed idea (27 lines in BLite's fork; a table in the epic upstream) [F13] [I288]. This is the
axes file reduced to what a human needs: where the next oracle goes.

**12. The triage skill or checklist**, four questions in the order the oracle makes natural, each with
"applies when" and "then", and a table of what each step leaves behind in the registry, the issue and the
commit [F13 SKILL.md]. In a repository without agents, this is a section of the method document.

### 5.3 Fitting it to an existing suite and an existing claim catalogue

- **If the repository already has claims** (a `claims.json` that quotes document sentences and names the
  test holding each, as ValueRanges has [V-claims]), do not write a second list. A catalogue test names
  the claim ids it holds in their `heldBy`; the axes it varies go in a short hand-written file beside the
  claims (`test/oracles/axes.md`), one section per surface family, each axis with its values, the test
  that already varies it, and the pairs no test varies [V105 comment]. The unheld claims and the uncovered
  pairs are the worklist that TypeKit's `status` and `gaps` would print.
- **If it already has a registry and a seed convention** (`candidates.json`, `seeds/*.patch`, a prover),
  register the new class where the others are registered and give it a seed; do not add a second
  mechanism [V-oracles].
- **If it has sweeps that already vary one axis each** (every culture, every type, every shape pair), the
  catalogue test's job is the *combinations* those sweeps do not reach: declared type × serializer
  option, entry point × culture × type, one literal across every parser against the external system's
  own answer [V105 comment]. List the existing sweeps as the first row of each axis so the new test does
  not repeat them.
- **Put each class in the project that already has its harness**: the conventions project for type
  discovery, the web test project for `TestServer`, the integration project for a live server [V105
  comment]; BLite put the AOT and browser tools beside the tests because a publish takes minutes and needs
  a toolchain [F1 "Native AOT smoke test"].

### 5.4 Keeping it cheap

- A class runs in about a minute; above that, sample by a rule written in the code (every third crash
  state), or split by mode so one slow axis does not multiply the rest [F8 line 252].
- Shrink under a budget and with a watchdog; print the shortest sequence, the seed and the step, so the
  repro is the failure message [F6 lines 164–192] [T5 report].
- Shared generators and models: one `StoreModel.cs` fed four oracles and a tool [T7 report] [T9 08:07]
  [T11 08:17].
- One session, one oracle, one brief; drafts before the session ends; a separate short session files [4.2].
- No tooling until the hand-written list hurts; then "a pairwise generator is a fifty-line addition to the
  test" [T1 21:55]. Replay by footprint is worth building only when the suite's minute becomes an hour.
- Do not send agents, hooks or skills into a repository you do not own; send tests, a document and a
  CONTRIBUTING section [T1 21:55] [T1 22:12].

### 5.5 Failure modes to avoid

1. **An inexact reference.** "A harness that compares unlike things produces the most convincing bugs
   there are" [K5] [F13]. Step 1 of triage exists for this; three of BLite's first disagreements were the
   model's [3.7].
2. **Reading one way's output and deciding.** The parent's filter error [T1 22:09].
3. **A seed that proves the wrong thing.** A test with registered candidates always "fails", so the prover
   must ignore labelled failures [T3 report]; a seed that breaks some assertion shows the test can fail,
   not that the claim is checked [K1 "Seeds", decision 104]; a seed whose build fails proves nothing [K1].
4. **A registry that cannot go stale, going stale.** Without the `candidate fixed` rule the quarantine
   outlives the defect; without the `AssertAllIn` check a renamed row leaves a ghost entry [F2].
5. **Quarantine by deletion.** "Quarantining by deleting rows loses the guard" [T1 22:12].
6. **Filing before confirming, or before checking closed issues.** Both were caught in BLite by an explicit
   step [T8 cross-session message] [T9 08:17]; TypeKit's record of a false prober finding is why [K7].
7. **Assertions in the issue where the maintainer owns the decision.** Frame semantics and trade-offs as
   questions with options and costs [I260 "Fix considerations"].
8. **A catalogue that grows as new tests instead of new rows.** The growth rules in the method document
   are what keep one new case checking against everything old [F1 "How detection grows"].
9. **Oracles at the wrong level.** A defect in the layer above the oracle's seam does not show (#212 at the
   raw B-tree) [T5 report]; a tool that steps aside (the WAL recorder during `Backup`) leaves a documented
   hole [T7 report]. Say what each oracle does not see.
10. **Mechanical traps that cost hours:** line endings, shell word splitting, a `sed` delimiter, a stack
    that moves, flaky neighbours under load [4.3]. Put each in the brief of the next session.

---

## 6. Appendix: sources

### 6.1 Source key

Transcripts are JSON Lines files under `~/.claude/projects/`; a position is the assistant message's
timestamp on 2026-10-16 or 2026-10-17 as printed in the file. The `local_…` id is the desktop app's
session id, for `search_session_transcripts`.

| Tag | What | Where |
|---|---|---|
| T1 | The adoption and orchestration session ("TypeKit bug-detection adoption", `local_5f13179c-c4e6-4a17-a5a5-76adc4a039ee`) | `-Users-ricardo-gross-RiderProjects-BLite/2fff4cfa-ef17-42b6-9be3-0c572e0b2aa1.jsonl` |
| T2 | First two oracle tests (`local_0ff03db5-0549-47d3-8b13-d3468efee6ba`) | `-Users-ricardo-gross-RiderProjects-BLite--claude-worktrees-awesome-nobel-c18118/da25b03b-e4b5-4e8a-a3ef-cd38f4ac2917.jsonl` |
| T3 | Minimal machinery (`local_7e91d830-d86b-45b6-b7e1-410856125ace`) | `…--claude-worktrees-amazing-shirley-d6a17b/29e567f2-7168-4b21-9853-2daf71f9c86a.jsonl` |
| T4 | Assemble the upstream branch and write the doc (`local_f348875c-7895-417b-b6a7-f41aebb94b03`) | `…--claude-worktrees-bold-jemison-ab4112/d11cfd8c-ecac-4e34-b91d-72826be4677b.jsonl` |
| T5 | B-tree model test (`local_59590bd2-73d2-4b18-aadc-c29124f62c46`) | `…--claude-worktrees-gallant-dhawan-099ff2/d7e134a2-0062-4103-bb54-b0fc3aefb69d.jsonl` |
| T6 | B-tree issues and PR (`local_0bcf26c6-c767-4fea-ad0d-c9dc8f7a3d96`) | `-Users-ricardo-gross-RiderProjects-BLite/aaa634d7-5790-4cab-8f8c-6d0e8df33e80.jsonl` |
| T7 | WAL crash test (`local_8c7a6120-02cd-4ae6-8d90-6c70111de988`) | `…--claude-worktrees-nice-kepler-e43635/237a51b1-e924-4cae-be95-87c7d8571796.jsonl` |
| T8 | WAL issues and PR (`local_299441f5-a412-408c-b780-e74a8d2da9d2`) | `-Users-ricardo-gross-RiderProjects-BLite/d93cd37d-94a9-4a59-a781-9b8b97ba6700.jsonl` |
| T9 | DynamicCollection tests (`local_1662d46c-7300-4b1b-957d-92b109d821ba`) | `-Users-ricardo-gross-RiderProjects-BLite/f8230ab4-f7f2-4dff-983a-638cddf35031.jsonl` |
| T10 | DynamicCollection issues and PR (`local_cd76b880-e49d-4abe-9c07-6f01b45b3b77`) | `-Users-ricardo-gross-RiderProjects-BLite/e548a07a-2716-4d2f-96ca-a86923239b18.jsonl` |
| T11 | Native AOT smoke test (`local_86a39a6c-aa47-4858-85a8-7fd9077afc1d`) | `…--claude-worktrees-compassionate-swirles-586c00/7c9352d3-753f-430b-91c1-0f9e3551203e.jsonl` |
| T12 | AOT issues and PR (`local_01a36223-675a-40ae-bacc-518cd341b617`) | `-Users-ricardo-gross-RiderProjects-BLite/e4803599-52d8-4f4c-9944-c64b4537f29e.jsonl` |
| T13 | Browser harness built (`local_43b916c1-3f2a-4db3-abd1-9c0ac45ca64a`) | `-Users-ricardo-gross-RiderProjects-BLite/1dbc59f3-97f4-4d6b-b2a9-ff8956ed7b19.jsonl` |
| T14 | Browser harness into the oracle frame (`local_9f981451-221d-4943-9140-ec0280ac0083`) | `-Users-ricardo-gross-RiderProjects-BLite/be98704b-fd11-4efa-8eed-54621ef1d1d0.jsonl` |
| T15 | Firefox and WebKit runs (`local_a2d52883-225a-4309-b283-95dc2dad45b8`) | `…--claude-worktrees-festive-villani-1d94c1/d956d69f-3ff8-4422-a5d0-7a557bd9c862.jsonl` |
| T16 | Restack of the PR stack (`local_0edf32a2-08d7-488b-bad6-eb13fd916412`) | `-Users-ricardo-gross-RiderProjects-BLite/c02d43e2-c1b2-4108-8c2a-9cae41be9363.jsonl` |
| F1 | `docs/ORACLE_TESTS.md`, 243 lines | EntglDb/BLite branch `test/oracle-encrypted` at `0de4379` (local worktree `BLite/.claude/worktrees/eloquent-mcclintock-d7bc82`) |
| F2 | `tests/BLite.Tests/Oracles/Candidates.cs`, 52 lines | same |
| F3 | `tests/BLite.Tests/Oracles/OracleSeedsTests.cs`, 58 lines | same |
| F4 | `tools/oracle-seeds.cs`, 78 lines | same |
| F5 | `tests/BLite.Tests/Oracles/DifferentialQueryTests.cs`, 839 lines | same |
| F6 | `tests/BLite.Tests/Oracles/StoreModelTests.cs` (193) and `StoreModel.cs` (428) | same |
| F7 | `tests/BLite.Tests/Oracles/BTreeModelTests.cs`, 1,419 lines | same |
| F8 | `tests/BLite.Tests/Oracles/WalCrashTests.cs`, 1,325 lines | same |
| F9 | `tests/BLite.Tests/Oracles/Seeds/*.patch` (four) and `candidate-issues.md` (744 lines) | same |
| F10 | `tools/aot-smoke/candidates.md`, `run.sh`, `README.md` | same branch (merged up from `test/oracle-aot`) |
| F11 | `tests/BLite.Wasm.BrowserTests/Candidates.cs`, `candidates.md`, `FINDINGS.md` | branch `test/wasm-browser-harness` at `f95a63e` (worktree `wasm-browser-harness`) |
| F12 | `CONTRIBUTING.md`, "Oracle tests" and "Commit trailers" | branch `test/oracle-encrypted` |
| F13 | `.claude/oracle-catalogue.md` and `.claude/skills/oracle-triage/SKILL.md` | fork-only commit `23f7182` on branch `claude/amazing-shirley-d6a17b` (worktree `amazing-shirley-d6a17b`) |
| F14 | `tests/BLite.Tests/Oracles/DynamicDifferentialTests.cs` (887), `DynamicStoreModelTests.cs` (797) | branch `test/oracle-dynamic` (worktree `oracle-dynamic`) |
| F-wc | Line counts | `wc -l` in the `eloquent-mcclintock-d7bc82` worktree on 2026-10-18 |
| K1 | TypeKit `.claude/probe-axes.md`, 751 lines | `/Users/ricardo.gross/RiderProjects/CodoMetis.TypeKit` |
| K2 | TypeKit `tools/Prober/README.md` | same |
| K3 | The `prober` agent definition | `~/.claude/agents/prober.md` |
| K4 | The `probe` skill | `~/.claude/skills/probe/SKILL.md` |
| K5 | TypeKit `.claude/skills/probe-triage/SKILL.md`, 261 lines | TypeKit checkout |
| K6 | TypeKit concept note, section 12 and Appendix A | `.claude/agent-memory-local/concept-2026-10-16-agentic-issue-finding.md` |
| K7 | TypeKit `design/fix-history.md`, "Class by found-by" | TypeKit checkout |
| K8 | Sizes of `tools/Prober/*.cs`, `tools/probe/enumerate.cs`, `tools/probe/corpus/harness/Row.cs` | `wc -l` in the TypeKit checkout on 2026-10-18 |
| I<n> | Issue n on EntglDb/BLite, read with `gh issue view` | GitHub |
| G1 | `gh issue list --repo EntglDb/BLite --state all` and the bodies of the user's issues since 2026-10-16 | run 2026-10-18 |
| G2 | `gh pr list --repo EntglDb/BLite --state all --author CaffeinatedCoder` | run 2026-10-18 |
| V105 | CodoMetis.ValueRanges issue #105, body and the 2026-10-18 comment | GitHub |
| V-claims, V-oracles, V-cand | `test/oracles/claims.json`, `oracles.json`, `Candidates.cs` in ValueRanges | this checkout |

### 6.2 Statement index

Section 1: every definition is from K1 ("Harness", "Seeds", "Claim sources", "Axes"), K2 ("What runs
where", "Files"), K3 (preamble, §3, §4, §6, §8) and K5 (preamble, headings), as tagged inline. The pairwise
size ("about three hundred rows") is K3 §6.3. The 56-line enumerator and 639-line `Row.cs` are K8.

Section 2: the 6,617 figure is K6 Appendix A; the 188 figure is F2 + F3 + F4; every table row carries its
tags. The three-outcome rule is F2; the stability-matrix rule is I288; the BLQL `INAPPLICABLE` list is T9
report and F14 lines 43–44.

Section 3.1: T1 at 21:09, 21:11, 21:55; K6 §12; K7 table.
Section 3.2: T2 brief and report; T5 brief and report; T7 report; T9 brief and report; T11 report; T13 and
T15 reports; F5 `Generate` and `Fixture`; F14.
Section 3.3: T7 brief; F6 remarks; I260; I288; T1 08:48.
Section 3.4: F1 "How to use it"; T3 report; F5 lines 561–586; F6 lines 40–50; T7, T9, T11 as tagged; F10;
F11; T4 report; T5 brief.
Section 3.5: T2 report; T1 21:45; T5 report; T7 report; T16 11:02 and 11:07; T11 report; T15 brief; F8
line 252; T13 report; T1 21:55.
Section 3.6: T2, T5, T7 briefs; F6 line 88; F8 line 100; T5 report.
Section 3.7: F2; F6 lines 164–192; F1 "Read a failure" and "Triage"; F9 WAL seed header; I260; T9 08:17;
T5 23:13; I249; F13; T2 report; T11 08:22; T9 report; T1 22:09.
Section 3.8: F1 "Candidate registry"; F9; T8 cross-session message and 07:51; T10 08:45; T12 report; F11;
T1 22:12; T6 report; T10 08:50 and report; F12; I249, I260, I273.
Section 3.9: F1 "Candidate registry"; F2; F11; T5 report; T1 21:55.
Section 3.10: F1 as tagged; F3; F4; I288; F13; T1 06:19 and 11:22; T16 report.
Section 4.1: G1 (titles and labels); T1 at 22:37, 06:08, 08:07, 08:55, 09:05, 11:22; T15 report; T1 06:18
and 22:12; G2.
Section 4.2: the timestamps of the first and last user or assistant message of each transcript as listed
in the key; F-wc; F14; T13 report.
Section 4.3: T1 22:09; T5 report; T9 08:25; T1 10:56; T16 report; T12 report; T6 report and 06:01; T15
11:36; T1 22:27 and 06:18; T4 report; T16 11:12; T11 08:26 and 08:37; F1 last paragraph; T7 report; T1
08:48, 21:11, 22:12, 22:50, 07:18, 07:40; T10 08:45; T3 report; G1 bodies of #299, #301, #304, #305, #307.
Section 5: templates are reduced from F5, F2, F3, F4, F9, I249/I260/I273, F11, F12, F1, F13; the
ValueRanges fit is from V105's comment and V-claims, V-oracles, V-cand; the failure modes cite the
incidents of 4.3 and K1, K5, K7.
