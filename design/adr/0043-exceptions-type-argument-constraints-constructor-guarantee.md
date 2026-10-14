# 0043. Exceptions, type-argument constraints, a constructor guarantee and the text guards join 2.0.0

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13.

The goals were reopened and four candidates weighed. `Try` stays
out, since it is `Result<T, Exception>` renamed, and `Result.Catch` takes its one job; `Unit`
stays out, since `Result<TError>` is the value-less shape. Constraints a type parameter cannot
declare (`[Abstract]`, `[Sealed]`, `[Record]`, `[Concrete]`) ship as attributes and an analyzer
rule, not as an aspect, because the violation is at a use site that need not compile with
Metalama. `[EnsureParameterlessConstructor]` (shipped as `[RequireParameterlessConstructor]`) ships as an attribute, an analyzer rule and a code
fix, not as an inheritable aspect, because its value is the error and an introduced constructor
would demand `partial` of every implementer and be silent without Metalama. The written-text
guards ship as string extensions and a fault in the base package, with no value object and no
`.Text` package: the base package cannot declare one for the generators, a hand-written one is
what CMTK0002 refuses, a woven one in a package of its own costs a private generators reference,
a boundary exemption, a baseline override and a smoke consumer for one line per field, and the
type cannot be refined (CMTK1005) or told apart from another field of the same type (CMTK0008).
Public aspects are allowed in principle ([Aspects](../aspects.md)) and none exists. Clock-type analyzers and
decimal JSON converters stay out.
