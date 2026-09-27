---
name: verify-the-guard
description: Prove a guard actually fails without the thing it guards — revert the source fix, or seed the defect for a convention test where there is no source fix to revert. Use whenever adding a test alongside a bug fix or a new validation in this repo, whenever a check's subject is packaging or release wiring rather than code, and before reporting the work as done.
---

# Verify the guard

A regression test that passes both with and without the fix is not a regression test. It is a test
that happens to pass, and it will keep happening to pass while the bug comes back.

The discipline is one extra step: **after the test goes green, revert the source change and confirm
the test goes red.** Then restore and re-run.

This is cheap and it earns its keep. The sibling repositories (CodoMetis.ValueRanges,
EFCore.ComplexIndexes) each found convention tests that were vacuous when first written: one
iterated an empty reflection query and reported four green tests after examining zero types, and
one passed with the rule removed because its probes agreed with the broken behaviour by accident.
Both were found by seeding the defect, not by review.

This repository has its own special case: **generated code**. A test of a woven member proves
nothing if the member it calls was written by hand in the test assembly, or if the aspect was never
applied and the test compares a stub against itself.

## The loop

1. Write the test. Run it — it should **fail**, for the reason you expect. Read the failure
   message; if it fails for a different reason than the bug, the test is testing the wrong thing.
2. Apply the fix. Run it — it should pass.
3. **Revert only the source change**, keeping the test. Run again — it must fail.
4. Restore the fix. Run the full suite and confirm the count is back where it started.

Step 1 is often skipped when the fix is already written. Step 3 recovers the same guarantee after
the fact, so do step 3 always, even when you did step 1.

## Reverting cleanly

```bash
SCRATCH=$(mktemp -d)
cp src/CodoMetis.TypeKit.Generators/ValueObjectJsonAspect.cs "$SCRATCH/"
# remove just the new guard, then:
dotnet test test/CodoMetis.TypeKit.Generators.Tests
cp "$SCRATCH/ValueObjectJsonAspect.cs" src/CodoMetis.TypeKit.Generators/
```

Revert the **source**, never the test. Reverting via `git checkout --` on a file that also contains
unrelated work will lose it — copy aside instead.

**`git checkout -- .` does not undo a `git mv`.** The rename is staged, so the working tree is
restored *to the renamed state* and every later step in a batch runs against a missing file. That
happened while seeding the release-wiring tests: four seeds "failed" with `FileNotFoundException`
rather than the defect they were meant to demonstrate, which is a failure for the wrong reason and
proves nothing. Use `git reset --hard` when a seed touches file names, and never leave the working
tree partially restored.

## What counts as failing for the right reason

- **Right:** the asserted collection lacks the expected element; `Assert.ThrowsExactly` reports no
  exception; the asserted SQL fragment is absent; the convention test names the property it could
  not find.
- **Wrong:** a `NullReferenceException`, a `FileNotFoundException`, a compile error, or a failure
  in a *different* test. A compile error usually means the test depends on API introduced by the
  fix — restructure it to exercise behaviour that exists either way, or accept that it is a feature
  test rather than a regression test and say so.

## When the guard's subject is weaving, an analyzer, or packaging

There is often no one-line source fix to revert. The counterfactual is to **seed the defect** and
confirm the guard notices:

- **Aspect:** stop the fabric from selecting the type (e.g. invert its `Where`), or delete one
  `IntroduceMethod` call. The behaviour test must fail, not the compile. If it fails to compile,
  the test is pinned to the member's existence, not its behaviour.
- **Analyzer:** make `IsValueObjectImplementation` return `false`, or drop one `SyntaxKind` from the
  registration. Each positive control for that form must go red.
- **`CodoMetis.TypeKit` stays Metalama-free:** add a `Metalama.Framework` reference to its
  csproj. The packaging guard must fail.
- **Packaging:** drop `PrivateAssets="none"` from the base package's reference to
  `.Analyzers` (the consumer smoke test must then fail to see CMTK0001), drop `PackageReadmeFile`, add a
  `PrivateAssets=all` reference without excluding it from the SBOM.

`scratchpad/seed*.sh` in a working session is the usual shape: apply, run the filtered test,
`git reset --hard`.

Two traps specific to this class:

**Text matching reads its own explanation.** A test asserting that a workflow job holds no
`id-token` permission matched the *comment* explaining why it has none. Match structure (a YAML
key, an XML element), not a substring that also occurs in prose.

**A discovery-driven test that finds nothing passes everything.** Every assertion that loops over a
reflection query, a file glob, or "every value object in the probe assembly" needs a floor
assertion on how many items it found.

## Ways a guard passes vacuously

Worth checking before believing a green run:

- It iterated an empty list (see above). Assert the count.
- The aspect never ran and the test compared two unwoven things, such as `default` against `default`.
- It asserted on something both the correct and broken paths produce — an input that `Create` accepts
  unchanged, so a dropped normalisation is invisible; a value object whose JSON is identical with
  or without the converter.
- It read a cached or previously packed artifact rather than the one just built.
- It greps for a phrase that no longer occurs, because the phrase was reworded.

## Tests that legitimately pass either way

Some tests are not regression guards and should not be forced through this loop:

- Tests pinning behaviour that was *already* correct, added for documentation.
- Tests guarding against a **future** over-correction — that a value the model currently accepts
  stays acceptable, protecting against a validation being made too strict later.

Both are worth having. Just do not count them as evidence that a fix works, and say which they are
when reporting.

## Reporting

State the counterfactual result explicitly, with the failure output. "Added a test" says nothing
about whether the bug is caught; "reverting the fix makes these three tests fail, with this output"
is the claim that matters.

When several fixes land together, revert them all at once and confirm the failure count matches the
number of guards — a fix whose test still passes stands out immediately.
