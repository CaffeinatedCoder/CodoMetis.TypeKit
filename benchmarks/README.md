# Benchmarks

Value objects and `Option`/`Result` against the types they stand in for, with BenchmarkDotNet. Each
category's baseline is the raw type doing the same work.

```bash
dotnet run -c Release --project benchmarks/CodoMetis.TypeKit.Benchmarks -- --filter '*'
```

Run before a release and after a change to a template. What must hold is held by tests instead:
`AllocationAndNumberHandlingTests` fails when a generated member allocates more than the wrapped
type does.

## Results

2026-09-28, Apple M4 Max, .NET 10.0.12, `--job short`: ratio to the raw type, allocation per
operation. The short job's error is about ±10%.

| Operation | Ratio | Allocated |
|---|---|---|
| Equality (`OrderId` against `Guid`) | 1.01 | — |
| Dictionary lookup | 1.02 | — |
| Sort 1,000 (`Amount` against `decimal`) | 1.05 | — |
| Sort 1,000 (enum-backed against the enum) | 0.63 | — |
| `Parse` | 0.98 | — |
| `TryFormat` | parity | — |
| JSON read / write | 1.08 / 0.96 | same bytes |
| `Create` against the same rule returning a nullable fault | 0.93 | — |
| `Create` → `Map` → `Bind` → `Match` against branches | 1.93 | — |

A result pipeline costs about a nanosecond per item over hand-written branches: the delegate calls
that `Map` and `Bind` are made of. Against Vogen:
[spikes/ValueObjectPerformance](../spikes/ValueObjectPerformance/README.md).
