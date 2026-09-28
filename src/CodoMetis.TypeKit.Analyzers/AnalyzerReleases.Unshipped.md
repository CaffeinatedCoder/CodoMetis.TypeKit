; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

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
