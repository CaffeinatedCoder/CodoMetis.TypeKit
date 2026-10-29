# 0119. A gap an Error has, found before the tag, ships in that release

Date: 2026-10-29
Status: accepted

Context: 2026-10-29, the sweep of CMTK0003 before 2.3.0 was tagged (#152, #157, #159 to #162); refines
[decision 34](0034-errors-fixed-within-major-version-warnings.md) and
[decision 118](0118-an-error-may-learn-a-form-only-new-api-can-write.md). Decision 118 lets a minor version's
Error report forms that only that version's new API can write, and names one exception the maintainer took
into 2.3.0: a dropped `Task.WhenAny` of tasks of results (#159). The sweep that followed found three more
forms of the same kind, each written with APIs 2.2.0 had. The maintainer decided, in these words: "decision
0119 widens 0118 to 'a gap found before the tag ships in that release'".

## Decision

**A form an Error misses, found before its version is tagged, is reported in that version**, when the form
loses what the rule protects in every build that contains it. A build that worked on the previous minor
version can then fail on update. The failure names a defect, never correct code, and the changelog's
Changed section says which forms and what to write instead. Decision 34 keeps an Error fixed within a major
so that an update never fails correct code; it does not keep a defect found before the tag unreported for a
major version.

**What 2.3.0 reports that 2.2.0 code can contain**, CMTK0003 on tasks of results:

- a `Task.WhenAny` of tasks the statement made, dropped, awaited, waited for or converted to a plain `Task`
  (#159): `Tasks.PendingOutcome` looked one task deep, and the `Task<Task<Result>>` hid the result;
- `Wait()` on a `?:`, `??` or `switch` expression, at each branch's call (#160): `AnalyzeWait` read only the
  statement's own call, where every other path goes through `Producers`;
- an array of them written in a `Task.WaitAll` or `Task.WaitAny` call (#161), which converts whole, so no
  element's call was converted on its own;
- a dropped `Task.WhenEach` of them (#162), which yields the tasks it was given.

**It relaxes too.** A `Task.WhenAny` of tasks the caller holds completes with one of them and loses nothing.
2.2.0 reported it awaited; 2.3.0 is silent on it.

**The rule stops at the tag.** Once a version is tagged, decision 34 holds for it: an Error that reports
more code its major version could already write waits for the next major, 3.0.0 for 2.x. Decision 118
stands for forms only a minor version's new API writes, and its one exception is this decision's first
case. The Analyzers README's "What a new version may change" lists each form a minor version reports that
the previous one's code can contain.
