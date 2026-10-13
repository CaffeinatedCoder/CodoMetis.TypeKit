; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------
CMTK0010 | Usage | Error | TypeArgumentConstraintAnalyzer, a type argument that breaks [Abstract], [Sealed], [Record] or [Concrete] on its parameter
CMTK0011 | Usage | Error | RequiredParameterlessConstructorAnalyzer, a concrete type under [RequireParameterlessConstructor] without the constructor it requires
CMTK0012 | Usage | Error | PositionalMatchArgumentAnalyzer, a closed set's Match given an argument by position
