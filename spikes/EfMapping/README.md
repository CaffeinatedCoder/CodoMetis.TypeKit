# Spike: how does EF Core map value objects without a per-type registration?

**Question.** Can `CodoMetis.TypeKit.EntityFrameworkCore` make EF Core 10 map every value object,
discovered at run time from the model, with no scan of the consumer's types and no
`Properties<T>()` per type? And do keys, foreign keys, nullable properties and primitive
collections of value objects then behave as with an explicit per-type `HasConversion`?

**Answer: yes, through a custom `IValueConverterSelector`.** Measured 2026-09-27 with EF Core
10.0.12 on SQLite and on PostgreSQL 17 (Npgsql 10.0.3), against value objects woven by the real
generators.

```bash
dotnet run --project spikes/EfMapping    # needs Docker for the PostgreSQL half
```

## Mechanism

EF asks `IValueConverterSelector` for the conversions it may apply to a CLR type it cannot map
directly; that is how an enum reaches an integer column. A selector that also answers "any
`IValueObject<TSelf, T>` converts to `T`" makes EF treat value objects as scalars everywhere it
consults the type mapping source: property discovery, keys, foreign keys, collection elements and
query parameters. It needs no list of types, because EF asks per type.

The converter's from-provider expression calls a plain generic helper that calls
`IValueObjectMaterializer<TSelf, T>.Materialize`, since an expression tree cannot call a static
abstract member directly (CS8927).

## Findings

| # | Claim | SQLite | PostgreSQL |
|---|---|---|---|
| 1 | Without any mechanism, the model does not build | ✅ fails (`List<Tag>` mapped as an entity type) | ✅ same |
| 2 | A value-object key maps as a scalar with the converter | ✅ `TEXT`, key | ✅ `uuid`, key |
| 3 | A value-object foreign key | ✅ | ✅ `uuid`, foreign key |
| 4 | A nullable value-object property | ✅ `INTEGER`, nullable | ✅ `integer`, nullable |
| 5 | A primitive collection of value objects, elements converted | ✅ JSON column, element converter | ✅ `text[]`, element converter |
| 6 | Round trip with a navigation loaded through the value-object foreign key | ✅ | ✅ |
| 7 | A stored value that `Create` refuses is read back as stored (the materializer, not `Create`) | ✅ | ✅ |
| 8 | Equality, `Contains` over a list, nullable comparison, collection `Contains`, `OrderBy` translate | ✅ | ✅ `= ANY (@ids)`, `@tag = ANY (o."Tags")` |
| 9 | The selector works whether it is registered before or after the provider | ✅ | ✅ |
| 10 | `.Value` in a predicate translates | ❌ needs the member translator | ❌ same |

## Consequences for the design

- **Selector, not a plugin or a convention.** A type-mapping-source plugin would have to produce
  the provider's mapping for the wrapped type itself, which it cannot reach without a dependency
  cycle on the type mapping source. A pre-convention needs the list of types up front, which means
  a scan. The selector is the extension point EF already uses for its own conversions.
- **Registration:** the options extension replaces `IValueConverterSelector` in its service
  collection. Finding 9 shows the order against the provider does not matter. A second library
  that replaces the selector too wins or loses by order, and the loser's types stop mapping, which
  surfaces as the model error of finding 1, not silently.
- **`.Value`** needs the member translator (finding 10), as measured in
  [spikes/ValueTranslation](../ValueTranslation/README.md) for the other libraries.
