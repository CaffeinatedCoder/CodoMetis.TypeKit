# 0025. The analyzer's Roslyn floor stays at 5.0.0

Date: 2026-09-28
Status: accepted

Context: 2026-09-28.

, the compiler of the 10.0.1xx SDK band,
which is serviced for all of .NET 10 and is what Linux distributions build. Measured: built on
5.9.0 (SDK 10.0.4xx) and run by the 5.0.0 compiler, the analyzer is not loaded (CS9057, a warning)
and `default(Option<int>)` compiles. `AnalyzerPackagingTests` reads the packed analyzer's
references. The build-time `Microsoft.CodeAnalysis.Analyzers` follows the SDK (5.9.0).
