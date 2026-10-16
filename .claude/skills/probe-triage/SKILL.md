---
name: probe-triage
description: Decide what one failing probe row means, by four questions in a fixed order — a harness error, a pinned framework fact, an intended difference the claim does not promise, or a finding for the code-analyst — stopping at the first that applies and recording the step where the corpus keeps it. Use for every failing row at the end of a probing round, for a `prober status` finding (`R-<probe>-<sha>`, `F<n>`), for a row a replay or `prober try` printed as failed, and whenever someone asks whether a probe failure is real.
---

# Probe triage

A failing row is a question, not a finding. Most of what the prober has cost went into answering it by
judgement, mid-round: the gate round of 2026-10-16 wrote 18 probes, found two product defects and about
ten harness traps, and left five failing rows of C07 untriaged. This skill answers the question by
lookup, in a fixed order, so two people (or a person and an agent) reach the same answer for the same
row, and the answer is written where the next replay and the next reader find it.

**The order is the rule.** Ask the four questions in turn and stop at the first that applies. Do not skip
ahead because a row "looks like" a product bug: a harness that compares unlike things produces the most
convincing product bugs there are. Every outcome names its step, `triage 1` to `triage 4`.

The writer of a probe never decides what a refusal means. The probe checks; this skill decides.

## Before the steps: get the row, and make it fail again

You start from one of these:

- **A finding id** from `prober status`: `R-<probe>-<sha>` (the replay saw a probe that passed with the
  same identity fail) or `F<n>`. Its last line in the branch's `findings.jsonl` names the probe and the
  commit. The state directory, `$STATE` below, is `PROBE_STATE_DIR` when set, else the main checkout's
  `.claude/agent-memory-local/prober/`; the branch's directory is `branches/<branch, / as ->/`.
- **A row a run printed:** `prober try` and `prober replay` print the failed probe and how each row ended.
  A replay that says "fails after a change to its corpus, axes, environment or this tool; triage the probe,
  not the product" opened no finding: start at step 1 and expect to stop there.
- **A row's evidence line:** the line `Rec.Run` writes, or the probe's evidence after a replay:

  ```bash
  jq '.rows["358"] | {category, reasons, applied}' "$STATE/branches/release-2.2.0/evidence/BaseProbe.json"
  ```

From it, write down: the claim, the probe, the row's index, its category (`violation`, `harness-error`,
`inconclusive`), its reasons, and the settings that applied. Then run the row at HEAD, three times:

```bash
dotnet tools/Prober/bin/Release/net10.0/prober.dll try --probe BaseProbe --rows 358
dotnet tools/Prober/bin/Release/net10.0/prober.dll try --probe BaseProbe --rows 358 --no-build   # twice more
```

A row that passes at HEAD was fixed meanwhile: say so with the commit, and stop. A row that does not
fail every time is flaky, and flaky is not a finding: record it as `flaky` (a round's ledger line, or
your report) and stop. Only a
row that fails every time goes on to step 1.

## 1. Is it a harness error?

**It applies when** the row's reasons carry `harness-error`, or the failure comes from the harness rather
than from the product:

- the category is `harness-error`: a check threw (`UNEXPECTED <Exception>: ...`, which decision 103 makes
  a harness error and never a violation), a check `prober new` staged and nobody wrote, or the equivalence
  precondition (`inputs not equivalent: wrapped [...] vs value object [...]`), which says the two sides
  were not the same shape and so were never comparable;
- a 404 on a bind route, a context type the source-generated `ProbeContext` does not list, a host that did
  not start for a reason the row's settings do not explain;
- a `violation` whose whole difference lies in something the harness built or named rather than something
  TypeKit wrote: a component or route name made from the twin's own type name, a twin at another
  nullability, kind or position than the value object (concept H2), an input the harness placed
  differently on each side. Read the two sides' answers in the reasons, or in the full log of the `try`
  run, and find the first byte that differs. If it is the harness's, this step applies.

**Then** fix the harness so the row compares like with like, and run the row again. No finding is filed.
A row that now fails with a different difference starts again at step 1.

**Record:**

- the harness fix, as a `test:` commit whose body names the row and says `triage 1`: what the harness
  did and what it does now (94554b3 is the model). In a round, the fix goes to the staging's `harness/`
  and the round's report says it; the session that publishes commits it;
- with a finding id, a line appended to the branch's `findings.jsonl`:

  ```json
  {"schemaVersion":1,"id":"R-BaseProbe-1a2b3c4","status":"withdrawn","reason":"triage 1 harness-error: the /schema/ endpoint had no twin at the value object's nullability; fixed in 94554b3"}
  ```

## 2. Does a fact at the pinned version explain it?

**It applies when** a fact in `design/knowledge/` says the framework, without TypeKit, does what the row
saw: the same refusal at the same operation, or the same document, at the version the repository pins.
A fact about the framework explains a row only where the framework alone makes the difference. A fact
that describes how the framework treats the wrapped type, while the value object differs from it, does
not explain the row; it is the evidence of step 4.

**Check with** the framework, the setting and the operation the row names:

```bash
dotnet tools/DocsQuery/bin/Release/net10.0/docs-query.dll find controller API explorer document visible --kind fact
dotnet tools/DocsQuery/bin/Release/net10.0/docs-query.dll entry KB-002
dotnet tools/DocsQuery/bin/Release/net10.0/docs-query.dll check      # no fact-behind or fact-ahead for it
```

A miss of `find` proves nothing: try the setting's own name and the exception type too. Read the fact's
frontmatter. It is **executable** when its `test:` line names a test under `test/CodoMetis.TypeKit.Facts`
(`test: test/CodoMetis.TypeKit.Facts/KB002.cs`). Run that test:

```bash
dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test --project test/CodoMetis.TypeKit.Facts --filter-class "*KB002"
```

**Then:**

- **An executable fact that passes and explains the row** makes it a framework refusal. The check is
  changed to cite the fact, no finding is filed.
- **A fact whose pin is stale**, or whose test fails, settles nothing until it is verified again
  (decision 93). Verify it first.
- **A fact without a test** (`test: none, ...` or `prose`) that would explain the row is a research task:
  measure the behaviour in `test/CodoMetis.TypeKit.Facts` as `KB<NNN>.cs`, give the fact its `test:`
  line, then cite it. A fact without a test may be cited in a reason and nothing more: it never prunes a
  row and never excuses one.
- **No fact, but the row's own baseline shows the framework alone doing it** (the document
  `Hx.DocRefusedByAspNetAlone` refuses for ASP.NET alone too, or the wrapped type alone refuses under the
  same options): that is a new framework behaviour. It becomes a new fact with its test under
  `design/knowledge/` (its README, "Adding one"), never a new note key. Then cite it.
- Otherwise go to step 3.

**Record:**

- the citation, where the corpus holds one: a `not-promised` entry of the claim whose `source` is the
  fact's file and whose `sentence` is quoted from it (step 3's form), an `exclude` group whose `reason`
  names `KB-NNN`, or a value in the axes file's `refused` object whose text names `KB-NNN`. Where the
  check excuses the row by a note key, the key's line in `NoteKeys` names the fact in its comment;
- with a finding id:

  ```bash
  dotnet tools/Prober/bin/Release/net10.0/prober.dll dismiss --id R-C03ComposedProbe-1a2b3c4 --reason "triage 2 fact KB-002: a controller without [ApiController] is missing from the document for ASP.NET alone, so the row's document part has nothing to check"
  ```

  `dismiss` stages the line; `prober publish` puts it in `tools/probe/corpus/dismissed.jsonl`;
- in a round, a fact to measure goes in the report under "Facts to measure", with the row and the
  configuration. The agent writes no test; the session that takes the report does.

## 3. Does the claim say it does not promise this?

**It applies when** a `not-promised` entry of the row's claim covers the row's settings, or a sentence of
the docs or of a decision record says the difference is intended.

**Check with** the claim's entries and narrowings, and the docs:

```bash
dotnet tools/Prober/bin/Release/net10.0/prober.dll gaps | grep 'C07-fault-responses-blanket'
jq '.[] | select(.id == "C07-fault-responses-blanket") | {narrow, "not-promised"}' tools/probe/corpus/claims.json
dotnet tools/DocsQuery/bin/Release/net10.0/docs-query.dll find nullable value object schema null --top 5
```

An entry covers the row when each of its `axes` values is among the row's applied settings. A docs
sentence counts only when it says this difference, in this place, is intended. Your sense that it "must be
on purpose" does not count, and neither does the axes file's "Intended" list alone: an item there that no
document or record states is a gap of this step, not an answer.

**Then:**

- **An entry covers it:** the difference is intended. The row's check cites the entry by its id, no
  finding is filed.
- **A document or record says so, but the claim has no entry:** that is a docs or claims gap, filed as
  `docs:` work, no finding. Add the entry; if the consumer docs do not say it plainly, add the sentence
  first (the `plain-docs` skill), then quote it.
- **Two documents disagree,** or a record calls intended what a consumer page promises: not this step's
  to settle. It goes to the code-analyst as a question (step 4's hand-off, "a contradiction").
- Otherwise go to step 4.

**Record:**

- the entry, in `claims.json` (in a round, the staging's `claims.json`), in the form of
  `tools/Prober/Schemas/claims.schema.json`. `universe` refuses a sentence its `source` does not hold:

  ```json
  "not-promised": [
    {
      "id": "invariant-globalization-refuses-as-text",
      "source": "docs/text.md#globalization-invariant-mode",
      "sentence": "So `AsText()` throws `PlatformNotSupportedException` there, on its first call, whatever the string.",
      "axes": { "text.culture": ["globalization-invariant"], "entry": ["AsText", "AsOptionalText"] }
    }
  ]
  ```

  committed as `docs:` with the sentence it quotes, beside any docs change;
- with a finding id:

  ```bash
  dotnet tools/Prober/bin/Release/net10.0/prober.dll dismiss --id R-C02OptionalTextProbe-1a2b3c4 --reason "triage 3 not-promised C02-optional-text/invariant-globalization-refuses-as-text"
  ```

  and for a gap, `--reason "triage 3 docs-gap: <the sentence or record>; entry <id> to add"`.

## 4. A finding: hand it to the code-analyst

Only a row that none of steps 1 to 3 explains gets here. It is still not a finding until the
`code-analyst` agent, which reads the product path and seeds the defect in a scratch copy, confirms it.

**Prepare the hand-off:**

- **The row**: claim, probe, index, commit, category, the reasons, the settings that applied.
- **The shrunk row**, from the harness's `Shrinker`. There is no prober command for it yet. Run it in the
  replay's worktree, which the `try` above has just built with the corpus overlaid:

  ```bash
  WT=$(dotnet tools/Prober/bin/Release/net10.0/prober.dll replay --where | awk '/^replay:/ {print $2}')
  cd "$WT" && PROBE_SHRINK="C07-fault-responses-blanket:358:baseline" \
    dotnet "$OLDPWD/tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll" test --no-build \
      --project test/CodoMetis.TypeKit.Samples --filter-class "Probes.ShrinkProbe"
  cat /tmp/shrink.txt
  ```

  `PROBE_SHRINK` is `<rows>:<index,...>:<check>`: the rows file the probe reads (`Rows.Data("...")` in the
  probe), the rows, and the check, `baseline` for the HTTP calibration or a name registered in
  `ShrinkChecks.All` (C03, C05, L02, L03, L04). A check with no name there is handed over unshrunk, and
  the hand-off says so.
- **The obligation** the failing check serves: its id and sentence from the claim's `obligations` (the
  probe's header quotes them). When the check serves none of the claim's obligations, say so: "none
  listed; the check holds <document#heading>: <sentence>". That is itself something for the analyst to
  report (below).
- **Each side's answer**: the wrapped twin's with TypeKit, the value object's with TypeKit, and the
  framework alone where the row ran it, each as the reasons or the log give it.
- **Why steps 1 to 3 did not apply**, one line each, with the facts and entries you looked at.

**Hand it over** with the Agent tool, `subagent_type: code-analyst`, and a prompt that starts
`Target: triage hand-off` and carries the five items above. The analyst reads the path the row exercised,
reproduces it outside the probe, seeds the defect, and answers Confirmed, Likely or Question. Where you
cannot start an agent (the `prober` agent started as a subagent), put the hand-off in your report under
"For code-analyst", marked "awaiting confirmation", file nothing, and let the caller hand it over.

**Then:**

- **Confirmed:** file `F<n>`, the next number not used in the branch's `findings.jsonl` or the corpus's
  `dismissed.jsonl`. The fix is a `fix:` commit with the regression test the analyst named (the
  `verify-the-guard` skill: revert it, then the variants), ending with `Defect-Class:` and
  `Found-By: PR`, and its body names the analyst's confirmation. 6866822 is the model.
- **Likely or Question:** no `F<n>`. The row stays failing, and the question goes to the maintainer in
  the round's report under "For code-analyst", with the analyst's answer.
- **A missing obligation** the analyst reports (a check that holds a promise no claim lists): it is filed
  as claims work, the obligation added to the claim whose source states it, or a new claim, with its seed
  (decision 104).

**Record**, only after the analyst confirms, a line appended to the branch's `findings.jsonl`, with the
shrunk probe kept in the memory's `open/`:

```json
{"schemaVersion":1,"id":"F3","claim":"C07-fault-responses-blanket","status":"open","commit":"00d3b8f6...","probe":"open/F3NullableBodySchemaProbe.cs","reason":"triage 4 finding: an MVC [FromBody] VO? over ulong writes oneOf[null, {type:[integer,string]}] where ulong? writes [null,integer,string]; confirmed by code-analyst 2026-10-16"}
```

An `R-` finding that became `F<n>` gets a `withdrawn` line whose reason is `triage 4 finding: filed as F<n>`,
so one defect is open once.

## What each step leaves behind

| Step | Finding | Change | Commit |
|---|---|---|---|
| 1 harness error | `withdrawn`, `triage 1 harness-error: ...` | the harness | `test:` |
| 2 fact | `prober dismiss`, `triage 2 fact KB-NNN: ...` | the check cites the fact; a new fact gets its test first | `test:` for the citation, `docs:` or `test:` for the fact |
| 3 not promised | `prober dismiss`, `triage 3 not-promised <claim>/<entry>` or `triage 3 docs-gap: ...` | the `not-promised` entry, the docs sentence | `docs:` |
| 4 finding | `F<n>` open, `triage 4 finding: ...; confirmed by code-analyst` | the fix and its test | `fix:` with `Found-By: PR` |

A row without a finding id leaves the change and the commit alone. Every reason starts with
`triage <step>`, so `grep '"triage [123]' findings.jsonl dismissed.jsonl` counts what the rounds cost
and what they found.
