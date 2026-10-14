# 0042. 2.0.0 is the next release, and it bundles the 1.x candidates

Date: 2026-10-12
Status: accepted

Context: 2026-10-12, phase 13.

The
fault responses, `Patch<T>` and the smart-enum family ship together in the major that already
waits for Metalama 2027.0 stable, [decision 34](0034-errors-fixed-within-major-version-warnings.md)'s promotions and C# 15 unions. A 1.6.0 first was the
alternative. Every part is additive, so it would have been a minor version, but the fault mapping
would have been designed on enums with `FaultKind` attributes read by reflection, then rebuilt
once smart enums exist. The cost is time: nothing ships before the later of .NET 11's GA and
Metalama 2027.0 stable.
