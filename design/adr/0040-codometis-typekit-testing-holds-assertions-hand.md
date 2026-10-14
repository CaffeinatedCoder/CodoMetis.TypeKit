# 0040. `CodoMetis.TypeKit.Testing` holds assertions that hand back the content

Date: 2026-10-11
Status: accepted

Context: 2026-10-11, phase 12.

`ShouldBeSome()`, `ShouldBeSuccess()` and `ShouldBeError()` return the value or the error, so the
test compares it with the assertions it already uses, and the package depends on no test framework:
a failure is a `TypeKitAssertionException`, which every runner reports. A failure names the error
it found and never prints a value, as `Option` and `Result` do not; an uninitialized result fails
every assertion as uninitialized, since `default(TError)` would otherwise read as an expected
first-member fault. **No generators**: one that goes through `Create` is a single filter over the
test's own property-based library (`Gen.String.Where(s => Email.TryFrom(s).IsSome())` in CsCheck),
and shipping it would tie the package to one such library. The package is new in 1.5.0, so its
project clears the package-validation baseline while that is 1.0.0, a condition that lapses on its
own when the baseline moves.
