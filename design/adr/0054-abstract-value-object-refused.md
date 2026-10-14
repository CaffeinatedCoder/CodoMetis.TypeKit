# 0054. An abstract type with a value-object marker is refused (CMTK1022)

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the second outside review of 2.0.0; [Aspects](../aspects.md).

The fabric selected only non-abstract types, so `abstract partial record A : IValue<string>;`
compiled with no member and no diagnostic, and a value held as `A`, of a sealed type derived from
it, was written as `{}` (measured). That is the repository's characteristic failure, a type that
compiles without being generated. CMTK1022 refuses it at Error, reported by the fabric on the
abstract type. A new Error ships only in a major version
([decision 34](0034-errors-fixed-within-major-version-warnings.md)), and 2.0.0 is one; a type that
built against 1.5.1 with such a declaration generated nothing then either. An abstract closed set
was already CMTK1014 (not sealed). A sealed type derived from an abstract one is still generated.
