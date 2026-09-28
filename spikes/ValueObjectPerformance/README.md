# Spike: value-object performance against the raw types and Vogen

**Question.** Do TypeKit's generated members cost more than the types they wrap, and does Vogen,
the most used value-object generator, have the same costs?

**Answer (2026-09-28).** Before the fixes it measured, TypeKit did: `TryFormat` boxed the wrapped
value (32 bytes and 3.5 times a `Guid`'s time per call), comparing an enum-backed value object boxed
both operands (sorting 1,000 took 8 times as long and allocated 367 KB), and the JSON converter wrote
and read numbers through a nested serializer call (1.35–1.40 times). After the fixes (plan phase 9)
TypeKit is at parity with the raw types in every case measured. Vogen 8.0.7 does not box, but its JSON
is 1.3–1.55 times the raw types', and it does not compile an enum-backed value object with its
defaults.

## Evidence

`dotnet run -c Release -- --filter '*' --job short`, Apple M4 Max, .NET 10.0.12, BenchmarkDotNet
0.15.8, Vogen 8.0.7, one run. The short job's error bars are about ±10%. The JSON line holds a
`Guid` and a `decimal`, and every shape writes the same JSON (asserted in the setup).

| Ratio to the raw type | TypeKit | Vogen 8.0.7 |
|---|---|---|
| Sort 1,000 `decimal` value objects | 0.98 | 1.10 |
| Sort 1,000 enum value objects | 0.63, 0 B | does not compile |
| `TryFormat` through `ISpanFormattable` | 1.00, 0 B | 1.08, 0 B |
| JSON read | 1.10, same bytes | 1.55, +32 B |
| JSON write | 0.97 | 1.27 |

- `[ValueObject<DayOfWeek>]` fails with CS0535: Vogen's generated `IConvertible` lacks
  `ToString(IFormatProvider?)`.
- What each library does with the host's number handling was not compared here. TypeKit's converter
  applies it (`AllocationAndNumberHandlingTests`).
- Dated and version-bound: re-run before quoting any of it about Vogen.
