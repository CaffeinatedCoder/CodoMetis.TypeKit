# 0103. A harness error is never a catch, and note keys are constants the compiler checks

Date: 2026-10-16
Status: accepted

Context: 2026-10-16, the round that gave the last 14 probes their seeds (`87f797f`). E11's seed, `UseTypeKit()`
ordered after the provider, was reported caught on 14 of 14 rows, but every one of those rows had ended by
an exception the check did not expect, not by the order differential firing. The probe was fixed so that
each order's run returns its refusal as a result, and the seed was then caught by the check itself. The
miscount was not E11's. [Decision 100](0100-every-probe-is-shown-to-fail-against-a-seeded-defect.md) says a
harness error is no catch, and two places counted one anyway:

- **`Rec.Run` made a thrown exception a violation.** Its catch branch raised `harness-error` and kept the
  exception's text as the failure, and a non-null failure then raised `violation` too. `violation` ranks above
  `harness-error` in `Evidence`, so the row ended as a violation.
- **`prober seeds` read the category alone.** It counted every row that ended as a `violation` and not in the
  unseeded run, and did not look at the reasons, so a row that also raised a harness error counted.

The same review looked at the note keys of [decision 99](0099-a-probe-row-says-how-it-ended.md). `NoteKeys`
was a dictionary of string keys, and each check called `r.Note("some-key", detail)`. A misspelled key compiled,
and came up only as a harness error after a build and a replay. Five probes had built keys from text, and
three of those keys had fired.

## Decision

**An exception is a harness error and nothing else.** `Rec.Run` (`harness/Row.cs`) records the exception as
`harness-error` and does not raise `violation`. The row's category stays `harness-error`, and the test still
fails with the exception named (`UNEXPECTED InvalidOperationException: ...`). A check that returns a failure
still raises `violation`.

**`seeds` credits only a clean violation.** A seed is caught only by a row whose category is `violation`, whose
reasons carry no `harness-error`, and which does not violate unseeded. A missed seed's detail counts the rows
that ended in a harness error, "which is no catch".

**Note keys are a typed catalogue.** `NoteKeys` is a sealed class with a private constructor. Each of its
`public static readonly` members is one key, with its spelling and its outcome on the same line, and that line
is the only place the key is declared. `Row.Note` takes a `NoteKeys`, and the overload that took a string is
gone. A check writes `r.Note(NoteKeys.Compared, detail)`. A misspelled member, a string literal and a key
built from text all fail to compile, for example `CS0117: 'NoteKeys' does not contain a definition for
'Comparde'`.

A static class of `const string` members was weighed and refused. With it, `Note` would still take a string,
so `r.Note("msg:" + x)` would compile, and only a lint could catch it. An enum would need an attribute for
the outcome and another for spellings such as `schema:compared-with-nullable-wrapped`, and reflection to read
both. A class whose only instances are its members carries both values and lets the compiler hold the
whole rule. The spellings did not change, so notes and reasons read as before.

**`Skip` stays a string, and is read before a build.** `r.Skip("F7", why)` names a finding id, which no
compile-time catalogue holds. `prober register` and `prober publish` scan the staged harness and probe
sources before they do anything else, blanking out comments and string literals first. They refuse,
naming the file and the line, where:

- a `Note` call's first argument is not `NoteKeys.Name` for a member the corpus or the staging declares;
- a two-argument `Skip` call (the harness's, not LINQ's) has a first argument that is not a plain string
  literal;
- that literal names a finding that this ledger does not hold open: one it never saw, one fixed in HEAD's
  history, one withdrawn, or one the corpus dismissed.

The prober references the base class library only ([decision 92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)),
so this scan reads text, not syntax. `ProbeNoteKeyTests` in the conventions tests holds the same shape over the
tracked corpus with Roslyn's syntax tree: a `Note` call's first argument is a member of `NoteKeys`, and a
two-argument `Skip` call's first argument is a string literal. A positive control and a floor of 130 note
calls keep the test from passing on a walker that finds nothing.

## Consequences

- Every harness and probe source changed, so every probe's identity changed, and the next replay runs them all.
- A seed that only makes a check throw is now reported missed. That is the outcome decision 100 asked for: the
  probe is fixed until its own check fires, as E11's was.
- A new key goes into `NoteKeys` with its outcome, on one line. A probe in another namespace imports the
  catalogue with `using NoteKeys = Probes.Shapes.NoteKeys;` (C09, C18 and C19 do), because the rule reads the
  name `NoteKeys` and nothing more.
- `HarnessErrorTests` in the prober tests compile `harness/Row.cs` in. They run the real `Rec.Run` on a
  throwing check, read the line it writes, and feed lines written that way to `seeds` through an adapter
  that runs the harness in-process.

## Evidence

Measured 2026-10-16 on macOS (arm64) with the pinned SDK:

- **The catalogue.** 63 keys: 25 pass, 20 inapplicable, 17 expected refusal and 1 inconclusive. All of them are
  used. There are 136 `Note` calls in 30 files, 14 in the harness (`Row.cs` among them) and 16 in the probes. 127 calls passed a
  string literal. Six passed a local constant, `Alone` in `TextRow.cs`, and one a conditional between two
  literals, now an `if`. The last one is `Row.Substituted` itself. No `Skip` call names a finding in the
  corpus today.
- **Reverting the `Rec.Run` change alone** fails two of the four `HarnessErrorTests`. One fails with "should
  be "harness-error" but was "violation"". The other expects the seed to be missed with "harness-error 1,
  pass 1; 1 ended in a harness error, which is no catch", and fails because the row it reads is now a
  violation.
- **Reverting the `seeds` filter alone** fails `A_violation_whose_reasons_carry_a_harness_error_is_no_catch`.
  Its seed is reported "caught, 1 of 2 rows violate; e.g. #1: violation:and failed anyway".
- **A probe with `r.Note("msg:" + x, ...)`.** Put in the corpus, it fails `ProbeNoteKeyTests` with
  `tools/probe/corpus/probes/ZzSeedProbe.cs:7: Note("msg:" + x, ...) does not name a key of NoteKeys`. Staged,
  it is refused by `prober register` and by `prober publish` with the same line.
- **The rule switched off.** With the scan's structural rule off, `SourceLintTests` fails. With the
  conventions rule accepting any expression, its positive control fails.
- **A misspelled member** in a probe, `NoteKeys.Comparde`, fails the Samples build with CS0117. The corpus as
  converted compiles in the Samples.
