# Spike: can a value object wrap a CodoMetis.ValueRanges range?

**Question.** Ranges are not a TypeKit type family: CodoMetis.ValueRanges already models them, with the
interval algebra in memory and its translation to PostgreSQL range columns. Where the two packages
meet is a value object over a range, a domain rule the range alone cannot state:

```csharp
public readonly partial record struct ContractPeriod : IValidatedValue<ContractPeriod, DateRange, PeriodFault>
{
    public static Result<ContractPeriod, PeriodFault> Create(DateRange value) => …;   // finite, at most a year
}
```

Does that work with the packages as they ship? Three things looked doubtful beforehand. `DateRange` is
an abstract record, and phase 11 left "a wrapped type … that is abstract" as a raw Metalama error.
TypeKit's EF converter has to compose onto a mapping that another plugin provides. And `.Value`'s
translation has to hand the range algebra's translator an expression it recognises.

**Answer: yes, with no bridge package. On 8.0.0 every gap was on the wire, in ValueRanges' JSON, and
TypeKit reproduced it exactly, as decision 26 requires. ValueRanges 9.0.0 closes all three, and the
value object inherits each fix with no change in TypeKit.** Measured 2026-10-11 against value objects
woven by the real generators (main at 1.5.0), CodoMetis.ValueRanges and
CodoMetis.ValueRanges.EFCore.PostgreSQL 8.0.0, then 9.0.0, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3,
Microsoft.AspNetCore.OpenApi 10.0.12, and PostgreSQL 17 in Testcontainers. All three doubts turned out
unfounded:
- The abstract `DateRange` weaves with no diagnostic. It implements `ISpanParsable<DateRange>`.
- `UseTypeKit()` composes its converter onto ValueRanges' `daterange` mapping, in either registration
  order.
- `.Value.Contains(day)` becomes `c."Period" @> @day`, and so do the other range operators.

On 8.0.0 the defects sat on the wire, and TypeKit mirrored each from the wrapped type:
- ValueRanges' JSON converter was opt-in. Without it, a `DateRange` and its value object were written
  as `{"Length":181}` and could not be read back.
- A range could not be a dictionary key.
- The OpenAPI schema was `{}`.

9.0.0 puts `[JsonConverter]` on every range type, makes ranges dictionary keys, and documents an
OpenAPI schema transformer to copy. The value object writes, reads and is described exactly as
`DateRange` is, with nothing registered. The re-run also found a TypeKit defect that 8.0.0 had hidden
(finding 14): a value object's query parameter was `{}`, where a `DateRange` query parameter is
`{"type":"string"}`. It is fixed. EF Core (findings 9 to 11) is identical on both versions. A value
object over a multirange (`RangeSet`) is refused (CMTK1005), although `RangeSet` has value equality.

```bash
dotnet run --project spikes/RangeValueObjects                    # every finding but 2; needs Docker
dotnet build spikes/RangeValueObjects -p:RangeSet=true           # finding 2: CMTK1005 for a RangeSet
```

`Domain.cs` holds `ContractPeriod`, a validated value object over `DateRange` with the rule "finite and
at most a year", and `Availability`, a plain `IValue<DateRange>`. `Program.cs` runs the sections in
order: the generated surface, the ways in, text, equality, JSON under four option sets, EF Core on
PostgreSQL (the model in both registration orders, the create script, a round trip, 13 queries), and
four ASP.NET Core hosts: nothing registered, `AddRangeConverters()`, and ValueRanges' documented
OpenAPI transformer (`ValueRangesSchemaTransformer.cs`, copied verbatim) added before and after
`AddTypeKit()`.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | `IValidatedValue<ContractPeriod, DateRange, PeriodFault>` and `IValue<DateRange>` weave with no diagnostic. They get `Create`/`TryFrom`/`FromKnownGood`/`Revalidate` (`From` for the plain one), `Parse`/`TryParse` (`ISpanParsable`), `IFormattable`, the JSON converter and the type converter. Comparison is not generated, because `DateRange` is not `IComparable` (CS0184 on the probe proves the absence at compile time). The phase-11 gap "a wrapped type … that is abstract" therefore does not cover an abstract type that parses to itself | ✅ |
| 2 | `IValue<RangeSet<DateRange, DateOnly>>` is CMTK1005: "a generic type does not guarantee" value equality. `RangeSet<TRange, T>` is `IEquatable<RangeSet<TRange, T>>` and canonical, so this refusal blocks a valid case, and its advice ("wrap a non-generic type of your own") cannot be followed for a multirange | ⚠️ TypeKit |
| 3 | `Create` runs on every way in. An unbounded, empty or two-year range is refused by `Create`, `TryFrom`, `FromKnownGood` (naming `DateRange.Infinite` and the fault), `Parse`, the type converter and JSON. Every refusal names the fault and none quotes the input, even where ValueRanges' own `JsonException` does (`Cannot parse '[2026-13-01,2026-14-01]' as DateRange.`). `Availability.From(null!)` is refused | ✅ |
| 4 | Text: `ToString()` and interpolation give the PostgreSQL literal `[2026-01-01,2026-06-30]`, `Parse` round-trips it, and the type converter reads it | ✅ |
| 5 | Two value objects built from separately created, equal ranges are equal and have the same hash code | ✅ |
| 6 | JSON with `AddRangeConverters()`, default and web options: the value object writes `"[2026-01-01,2026-06-30]"`, byte for byte what `DateRange` writes, alone or in a property. It reads back equal. A JSON null is refused for `ContractPeriod` and read as null for `Availability?`. `Availability` reads `"(,)"` and `"empty"` | ✅ |
| 7 | JSON **without** `AddRangeConverters()`. On 8.0.0: `DateRange` writes `{"Length":181}`, and reading throws `NotSupportedException` (an abstract type). The value object writes the same `{"Length":181}`, reports "could not read the JSON value as DateRange", and an ASP.NET body holding it is a 400. Parity held, but the wire was wrong for both types, and it failed silently on writing. On 9.0.0: both write `"[2026-01-01,2026-06-30]"` and read it back, under default and web options. A refused range is "refused the JSON value (NotFinite)". With nothing registered, a valid body now binds (200), and a refused one is a 400 | ✅ 9.0.0 |
| 8 | A dictionary key. On 8.0.0: neither `DateRange` nor the value object can be one, with or without the converters. On 9.0.0: both write `{"[2026-01-01,2026-06-30]":1}` under all four option sets. A `Dictionary<ContractPeriod, int>` body binds (200), and a key `Create` refuses is a 400 | ✅ 9.0.0 |
| 9 | EF model: in both registration orders (`UseNpgsql(n => n.UseValueRanges()).UseTypeKit()` and `UseTypeKit().UseNpgsql(…)`), `Period` and the nullable `Availability` map to `daterange`. Their mapping is ValueRanges' `ValueRangeTypeMapping` with TypeKit's converter composed onto it (`CompositeValueConverter`). `HasIndex(c => c.Period).HasMethod("gist")` creates a GiST index | ✅ |
| 10 | Round trip on PostgreSQL: the values read back equal to those written. A row the rule refuses, inserted by SQL (`'[2026-01-01,)'`), materializes without validation, as §4 intends, and `Revalidate()` reports `NotFinite` | ✅ |
| 11 | Translation composes. See the table below; every query ran and returned the in-memory answer | ✅ |
| 12 | ASP.NET Core minimal APIs, in a host with `AddRangeConverters()`: a valid body binds and echoes, and a body `Create` refuses is a 400. A query parameter binds through `TryParse` (`?period=[2026-01-01,2026-06-30]` → 200), and a refused one is a 400. No ASP0020 for a query parameter of a value object declared in the same project | ✅ |
| 13 | OpenAPI with `AddTypeKit()`: the value object's schema is `DateRange`'s, as §7 requires. On 8.0.0 that is `{}` with the converters and an object with `length` without them. On 9.0.0 it is `{}` in every host, since ASP.NET cannot see through a converter. With ValueRanges' documented transformer, in either order with `AddTypeKit()`, both component schemas are `type: string` with the literal's `pattern` and description. TypeKit asks ASP.NET for the wrapped schema (`GetOrCreateSchemaAsync`), so the other transformer reaches the value object with no wiring | ✅ 9.0.0 + the copied transformer |
| 14 | A query parameter. ASP.NET describes a `DateRange` query parameter as `{"type":"string"}`: a `TryParse`-bound parameter is its own schema only for a primitive, an enum and eight listed types (`EndpointModelMetadata.GetDisplayType`, internal), and `string` for anything else. TypeKit replaced its `string` with the wrapped type's JSON schema regardless, which gave `{}` for the value object (and, with the transformer, a pattern a `DateRange` parameter does not get). Fixed in `ValueObjectSchemaTransformer`: the placeholder stays where ASP.NET keeps it for the wrapped type. After the fix the value object's query parameter is `{"type":"string"}` in all four hosts, as `DateRange`'s is | ✅ fixed in TypeKit |
| 15 | Configuration binding. 9.0.0 gives `DateRange` a `TypeConverter` (`RangeTypeConverter`). `"[2026-01-01,2026-06-30]"` binds to both a `ContractPeriod?` and a `DateRange?` property. A range `Create` refuses and text that is no range both throw `InvalidOperationException` ("Failed to convert configuration value … to type 'ContractPeriod'"): the binder's message quotes the value, the value object's own does not | ✅ |
| 16 | A query array (`[FromQuery] ContractPeriod[]`): ASP.NET gives a `DateRange[]` query parameter no `items`, as it gives any converter-backed element none (§7). TypeKit's container branch puts the value object's component back, as for every value object (`{}` on 9.0.0, the literal's pattern with the copied transformer). Two values bind (200) in every host | ✅ |

Queries (finding 11), as `ToQueryString()` prints them:

| LINQ | SQL |
|---|---|
| `c.Period.Value.Contains(day)` | `WHERE c."Period" @> @day` |
| `c.Period.Value.Overlaps(other)` | `WHERE c."Period" && @other` |
| `c.Period.Value.IsFinite()` | `WHERE NOT (lower_inf(c."Period")) AND NOT (upper_inf(c."Period")) AND NOT (isempty(c."Period"))` |
| `OrderByDescending(c => c.Period.Value.LowerBound())` | `ORDER BY lower(c."Period") DESC` |
| `Select(c => c.Period.Value.Intersect(other))` | `SELECT c."Period" * @other` |
| `c.Period == periodParam` (a value object) | `WHERE c."Period" = @periodParam` |
| `c.Period.Value == h1` (a `DateRange`) | `WHERE c."Period" = @h1` |
| `c.Availability.ValueOrNull()!.Contains(day)` | `WHERE c."Availability" @> @day` |
| `c.Availability!.Value.GetValue().Contains(day)` | `WHERE c."Availability" @> @day` |
| `c.Availability.Value.Value.Contains(c.Period.Value)` | `WHERE c."Availability" IS NOT NULL AND c."Availability" @> c."Period"` |
| `GroupBy(…).Select(g => g.Select(c => c.Period.Value).RangeAgg())` | `SELECT range_agg(c0."Period") … GROUP BY c0."Key"` |

## Consequences

- **No bridge package.** Each package registers itself in one line, and nothing composes them by hand:
  not the mapping, the translation, the index, or `Revalidate`. What a `CodoMetis.TypeKit.ValueRanges`
  could have held does not exist.
- **The pattern can be documented**: "a value object over a range" in the Generators README, with
  `ContractPeriod` as the example. On 9.0.0 the host needs no registration for JSON. For OpenAPI it
  copies ValueRanges' transformer, which reaches the value object through `AddTypeKit()`.
  Holding its block as a compiled sample means the Samples project references ValueRanges, a test-only
  dependency.
- **CMTK1005 for a generic wrapped type is wider than its reason** (finding 2), but `IEquatable` of
  itself is not a sufficient test for relaxing it: `ImmutableArray<T>` implements
  `IEquatable<ImmutableArray<T>>` and compares by reference. Relaxing it needs a criterion that holds,
  such as a list of vetted types or an explicit opt-in, and is a generator change with its own
  build-outcome rows. Relaxing an Error is allowed in a minor version; adding one is not.
- **For ValueRanges, not TypeKit** (findings 7, 8, 13), all in 9.0.0. TypeKit inherited each by design, and
  each disappeared for the value object once the wrapped type had it, with no TypeKit change:
  - `[JsonConverter]` on the range types, so the wire form is correct without a host remembering
    `AddRangeConverters()` (the silent `{"Length":181}`).
  - Dictionary-key support in `RangeJsonConverter`.
  - An OpenAPI schema for the range types, as a transformer to copy (no package ships one).
- **For TypeKit** (finding 14): a parameter's schema now follows ASP.NET's rule for the wrapped type,
  not the wrapped type's JSON schema. `ParameterTests` holds a control for every type on ASP.NET's list
  and one for a wrapped type off it (`ProbeSpan`). Seeding out any entry fails its control.
- **Not measured:** EF's compiled model and precompiled queries, and Native AOT (9.0.0 claims it for the
  core package, not the EF plugin; the composed converter is untested); MVC; the other range types, which go through the
  same code paths as `DateRange`.
