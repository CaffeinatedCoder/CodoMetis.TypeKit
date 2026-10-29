; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------
CMTK0001 | Usage | Error | No default instance of a value object or [RequireCustomInitialization] struct
CMTK0002 | Usage | Error | Value object in a project without CodoMetis.TypeKit.Generators
CMTK0003 | Usage | Warning | A Result or Option a call returns, ignored
CMTK0004 | Security | Error | Materialize, which skips validation, called outside the EF Core satellite
CMTK0005 | Usage | Warning | An array or span of a no-default struct created with a length
CMTK0006 | Usage | Warning | A no-default member of a class that nothing assigns
CMTK0007 | Usage | Info | FromKnownGood given a value that arrives from the caller
CMTK0008 | Usage | Warning | The wrapped values of two different value objects compared
CMTK0009 | Usage | Warning | A call that returns a default instance when it finds nothing

## Release 1.5.0

; No new rules: 1.5.0 changes no analyzer. Release tracking still wants a table after the
; header, or a release that is not the last in this file is RS2007.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------

## Release 1.5.1

; No new rules: 1.5.1 changes no analyzer. Release tracking still wants a table after the
; header, or a release that is not the last in this file is RS2007.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------

## Release 2.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------
CMTK0010 | Usage | Error | TypeArgumentConstraintAnalyzer, a type argument that breaks [Abstract], [Sealed], [Record] or [Concrete] on its parameter
CMTK0011 | Usage | Error | RequiredParameterlessConstructorAnalyzer, a concrete type under [RequireParameterlessConstructor] without the constructor it requires
CMTK0012 | Usage | Error | PositionalMatchArgumentAnalyzer, a closed set's Match given an argument by position
CMTK0013 | Usage | Error | ClosedSetMemberAssignedAnalyzer, a closed set's member written outside its declaration

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------------------------------------------------------------
CMTK0003 | Usage | Error | Usage | Warning | IgnoredOutcomeAnalyzer, raised in a major version; also a Wait, a WaitAll and a loop that drop results

## Release 2.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------
CMTK0014 | Usage | Warning | UndefinedEnumValueAnalyzer, a FaultKind or ConstructorAccessibility that names no member

## Release 2.3.0
