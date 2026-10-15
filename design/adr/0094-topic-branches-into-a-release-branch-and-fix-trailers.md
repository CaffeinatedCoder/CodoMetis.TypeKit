# 0094. From 2.2.0, topics merge into a release branch, and a fix names its defect class and its finder

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, the release of 2.1.0. Its 112 commits since v2.0.0 were made on one branch,
`feat/2.1.0`, and reached `main` in one pull request. Before merging, splitting them into branches by feature,
fix, tooling and docs was weighed and refused: 33 of the 109 commits then on the branch were cited by hash in
tracked files (the decision records, [the fix history](../fix-history.md), the prober's corpus), which a
rewrite would orphan, and 54 touched more than two areas, by design, a fix with its test and its changelog
line. What reorganising afterwards cannot buy, starting a cycle that way can. The same day the fix history
classified the 133 fixes to date by defect class and by who found each one, and had to reconstruct both: from
the commit bodies, the pasted reviews and 44 local session transcripts, which CI and other contributors cannot
read, matching 130 of the commits by the text of their subject.

## Decision

**A release branch per version.** Once the previous release is tagged and its successor opened on `main`
(CONTRIBUTING, "Releasing", step 4), `release/<version>` is cut from `main` and pushed. 2.2.0's is
`release/2.2.0`, cut at 0450143.

**A topic branch per concern.** Work starts on a branch from the release branch, named `<type>/<slug>`, where
`<type>` is the conventional type of its main commit (`feat/cause-on-fault`, `fix/serilog-max-length`,
`docs/…`, `build/…`). It reaches the release branch through a pull request, one concern each, with CI on
Linux, Windows and macOS, merged with a merge commit.

**Merge commits only, everywhere.** No squash and no rebase of a pushed branch, into the release branch or
into `main`: a hash a record cites stays valid. A release branch that falls behind `main` (a patch release)
takes `main` by a merge.

**The release.** The release branch reaches `main` in one pull request, merged with a merge commit, and the
tag goes on that merge commit, as `v2.0.0` and `v2.1.0` did. The release branch is deleted once tagged.

**A patch release.** `release/<x.y.z>` is cut from the tag it patches and follows the same rules; its fixes
reach the open release branch by merging `main` once it is tagged.

**Two trailers on every fix.** A commit whose subject is `fix:` (with or without a scope or `!`) carries,
in the trailer block beside `Co-Authored-By`:

```
Defect-Class: GTS
Found-By: CA
```

exactly one of each, from the vocabulary the fix history counted with. A commit that fixes several defects
names its main one, as the baseline did.

| `Defect-Class` | The defect's primary cause |
|---|---|
| `FCI` | Framework configuration interplay |
| `GTS` | Generator type shape |
| `AIS` | Analyzer input shape |
| `EFT` | EF Core translation and mapping |
| `API` | The public surface let a misuse compile |
| `POS` | Process order and state |
| `DOC` | Contract or docs drift |
| `TXT` | Text and globalization |
| `AOT` | Native AOT or trimming |
| `SDL` | Silent does-less, no more specific cause |
| `CRP` | Crash or performance |
| `TOOL` | Tooling or test infrastructure |

| `Found-By` | Who or what found it |
|---|---|
| `RS` | A review session, its reviewer subagents included |
| `CA` | The `code-analyst` agent |
| `PR` | The prober |
| `ST` | The smoke test, a Native AOT publish, a clean-container run |
| `OR` | An outside review, pasted in |
| `US` | The maintainer's own use, or a downstream consumer's |
| `MS` | A measurement: a benchmark, a stress run |
| `FR` | Feature research |
| `SF` | The session that was fixing something else |
| `UN` | Unknown |

A new code is added here, in a new record, and in the test below together, never in a commit alone.

**Held by a test.** `CommitTrailerTests` in the conventions tests reads `git log v2.1.0..HEAD` without merges
and fails for a fix commit that lacks either trailer, repeats one, or names a code outside the tables. It
fails, rather than passes, on a shallow clone or without the `v2.1.0` tag, so CI's test job checks out the
full history (`fetch-depth: 0`). Its positive control builds a throwaway repository with a fix commit of
each kind and holds the reader to finding each.

**CI on release branches.** `dotnet.yml` runs on a push to `main` or a `release/**` branch and on a pull
request into either.

## Consequences

- The fix history can be run again from `git log --format='%(trailers:key=Defect-Class,valueonly)'` alone, by
  anyone with a clone, and the next classification reads the 2.2.0 cycle from the trailers, not from
  transcripts.
- Which finder catches which class becomes visible per release: the question the baseline left with three
  prober finds and ten from `code-analyst`.
- A topic's CI runs before it meets the others, not only on the release pull request.
- A fix commit without its trailers fails CI on its topic's pull request. Before the branch is pushed, the
  commit is amended. Once pushed it is never rewritten, so its hash joins the test's exemptions, with the
  trailers it should have carried, in the same pull request.

## Evidence

- Counted on `feat/2.1.0` at `dc459b0`, 2026-10-15: 109 commits since `v2.0.0`; 33 of them cited by hash in
  tracked files; 54 touching more than two of `src/`, `test/`, `docs/`, `design/` and `tools/`.
- [The fix history](../fix-history.md): 133 fixes; 130 matched to their session by subject text, 3 by the
  commit body; finder attribution inside a review session "firm where the session's report lists the item
  and a guess where the fix agent had several reports".
- The repository's history is 10 MB (`du -sh .git`, 2026-10-15), so a full checkout in CI costs little.
- `v2.0.0` points at 09df07e, the merge of pull request #2, and `v2.1.0` at f0f5830, the merge of #5.
