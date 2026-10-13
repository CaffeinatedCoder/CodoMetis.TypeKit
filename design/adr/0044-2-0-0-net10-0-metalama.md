# 0044. 2.0.0 is `net10.0` on Metalama 2026.1; the version number names the API, never the runtime

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13.

Dropping a target framework is a break of its own, so a `net11.0`-only
2.0.0 would have left every consumer on the .NET 10 LTS line (supported to November 2028) on
1.5.x, without the fault responses, the closed sets or `Patch<T>`, for a .NET 11 that is STS. The
breaks 2.0.0 actually has, [decision 34](0034-errors-fixed-within-major-version-warnings.md)'s promotions and two rules at Error, are breaks on
`net10.0`, and none of its parts needs C# 15 unions. A `net11.0` target is additive and joins a
2.x minor when a feature needs it; unions in the public surface would be two contracts under one
version, so they and the `net10.0` drop are 3.0.0, timed to .NET 12 LTS. Metalama 2027.0 joins
the first release after it is stable, minor or major as the fabric measurement says. The
`2.0.0-preview.N` branch of 2026-10-12 becomes a preview of 2.0.0 itself from `main`; the .NET 11
measurement is a 3.0.0 spike with no release attached.
