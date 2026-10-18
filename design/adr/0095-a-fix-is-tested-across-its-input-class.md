# 0095. A fix is tested across its input class, from a variant catalogue

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, the opening of the 2.2.0 cycle. [The fix history](../fix-history.md) counted 133 fixes, and
64 of them are a later member of a tight chain: the same rule or function in the same file, repaired again for
another shape of the same input. CMTK0010 took 14 fixes in two days (chain A), the converter and
`ReferenceHandler.Preserve` checks 12 on one day (chains C and D), CMTK0003 7 over three weeks (chain B). Every one
of those fixes was proven by reverting it ([verify-the-guard](../../.claude/skills/verify-the-guard/SKILL.md)),
which shows that the guard catches the input the review named, and says nothing about that input's neighbours. The
next review found the neighbours, one at a time.

## Decision

**The variant step.** Before a fix is reported done, the fixer

1. **names the input class** the defect lives in: its `Defect-Class` ([decision 94](0094-topic-branches-into-a-release-branch-and-fix-trailers.md))
   and, in one sentence, the input inside it ("a type argument reaching a constrained type parameter", "a value
   object declared over a base record", "the converter `JsonSerializerOptions` puts in charge of a property");
2. **runs the fix's test across that class's catalogue**, the input reached in every shape the catalogue lists. An
   entry passes, or is silent by a decision the rule's test cites beside it;
3. **fixes what fails** as part of the same work: in the same commit when it has the same cause, otherwise in its
   own `fix:` commit on the same topic branch, before the pull request;
4. **teaches the catalogue**: a shape the step missed and a review later found goes into the catalogue in the
   commit that fixes it, so every rule over that input is tested on it from then on;
5. **says so** in the commit body: the input class, the catalogue run, and the siblings it found.

**The catalogues.**

| Class | Catalogue | Run |
|---|---|---|
| `AIS`, a type argument | `TypeArgumentSites` (`test/CodoMetis.TypeKit.Analyzers.Tests/Variants/`): 62 shapes through which an argument reaches a parameter, each run with an argument that breaks the rule's constraint and one that meets it | a `TypeArgumentSubject` per constraint, as `TypeArgumentConstraintVariantTests` does for CMTK0010 |
| `AIS`, any other input | The syntax and operation kinds the rule registers, plus method groups, crefs, extension blocks (method, property, static member, operator, lowered call), generic attributes, `nameof`, null-conditional access, lambdas and local functions, and generated code (`GeneratedCodeTests`) | Built in `Variants/` the first time a fix needs it, on `TypeArgumentSites`' pattern: the rule supplies a subject, the catalogue the shapes |
| `GTS` | The `shape.*` axes of `.claude/probe-axes.md` (kind, site, inheritance, wrapped, position, containing, refused kind) and the value objects of `test/CodoMetis.TypeKit.Samples` | A declaration per value of each axis the fixed code reads, in the fix's test project, or the rows of the claim that covers it (`prober rows --claim C --full`) |
| `FCI` | The settings of the API the fix touches: the generated `json.*`, `openapi.*` and `mvc.*` axes (`tools/probe/enumerate.cs`, values from `hand`) | The claim's pairwise rows (`prober rows --claim C`), as a theory or as the claim's probe |

Another class names its catalogue the first time a chain of its own shows up in the fix history.

**A new finder.** A defect found by the variant step of another fix is `Found-By: VA`, so that the next
classification counts what the step found against what reviews still find. `CommitTrailerTests` takes the code.

**Not a trailer.** A `Variants:` trailer naming the catalogue run was weighed and refused: its value is free text,
and no test could tell a run from a claim. The commit body says it.

## Consequences

- A fix to a rule over type arguments is tested on 62 shapes and two directions (a breaking argument reported once,
  a meeting one never) instead of the one the review named.
- `TypeArgumentSites` declares its own subjects, so a shape the language adds (a new member kind in an extension
  block, a new pattern the compiler binds) is added once and holds every rule that reads type arguments.
- Where an input class has no catalogue, the first fix in it pays for one. The step asks for the smallest that
  covers the forms named above, not for completeness.
- The step proves breadth across shapes, not semantics: what an argument *is* (an enum, an array, `dynamic`, a type
  built from a type parameter) is a second axis, the one chain A's other seven fixes ran along, and has no
  catalogue yet.

## Evidence

Measured 2026-10-15 by copying `TypeArgumentSites` and `TypeArgumentConstraintVariantTests` (the `[Sealed]` and
`[Concrete]` subjects; `[ReadOnlyStruct]` did not exist yet) into a checkout of each chain A commit and running them
there. The catalogue's shapes are the forms this record lists and the language's, with the reported locations
taken from CMTK0010's current tests; it was written knowing the chain, so the run measures what the catalogue
catches, not what a fixer would have thought of unaided. The receivers through `base.`, a constrained type
parameter, an unqualified call and a static implementation were added after a first run, as the call-resolution
shapes were thin.

At the chain's origin, `a77bdda` (the method-group fix), 20 sites fail (46 of 249 cases), and each turns green at
the later fix that repaired it:

| Fixed by | Sites failing at `a77bdda` |
|---|---|
| `37317de` | a cref to the generic type, a cref to the generic method (both reported for any argument), an interface method's implementation, an override |
| `e4361e9` | an extension block's method, property, static member, method group and operator; a generic attribute, also named in full; a base class's method implementing an interface for a derived class, and called unqualified there; a static method implementing a static abstract one |
| `a0ddf61` | a null-conditional call, written (reported twice); a `nameof` of the closed type and of its member; an argument that does not exist (CMTK0010 beside CS0246) |
| `370414a` | a base class's implementation through `base.` and through a constrained type parameter |

Run at each later fix, it also catches two regressions those fixes introduced, which the fix history did not count
as repairs: from `e4361e9` on, a breach in generated code (an auto-generated header, a `.g.cs` file,
`#line hidden`) is reported, since `e4361e9` moved CMTK0010's reporting to `context.ReportDiagnostic`, until
`7c30840` (4 sites); and a default interface method called through a derived interface overflows the test host's
stack (exit 134) from `e4361e9` until `cdab810`. At `cdab810` and at `6368c1c`, the chain's last fix, every case
passes; on `release/2.2.0` all 373 do.

So six of chain A's thirteen later fixes (`37317de`, `e4361e9`, `a0ddf61`, `370414a`, `7c30840`, `cdab810`) would
have been found by the fixer of the one before. The other seven (`0d6ba1a`, `2f8a854`, `7792801`, `d27cf2d`,
`1a9d7f2`, `75fb3aa`, `6368c1c`) are about what an argument is, the axis above that has no catalogue.

Seeded on `release/2.2.0`: without CMTK0010's `MethodReference` and `PropertyReference` registrations, 15 cases
fail, the five method-group and extension-property sites under each of the three subjects.
