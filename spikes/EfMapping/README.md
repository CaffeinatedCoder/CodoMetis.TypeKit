# Spike: how does EF Core map value objects without a per-type registration?

**Question.** Can `CodoMetis.TypeKit.EntityFrameworkCore` make EF Core 10 map every value object,
discovered at run time from the model, with no scan of the consumer's types and no
`Properties<T>()` per type? And do keys, foreign keys, nullable properties and primitive
collections of value objects then behave as with an explicit per-type `HasConversion`?

**Answer: yes, through an additive `IRelationalTypeMappingSourcePlugin`.** Measured 2026-09-27
with EF Core 10.0.12 on SQLite and on PostgreSQL 17 (Npgsql 10.0.3), against value objects woven
by the real generators. A replaced `IValueConverterSelector` maps the same, but conflicts with any
other library that replaces the selector (findings 11 and 12); the plugin does not (finding 14).

```bash
dotnet run --project spikes/EfMapping    # needs Docker for the PostgreSQL half
```

## Mechanisms

Both work because EF asks the type mapping source per CLR type wherever it maps one: property
discovery, keys, foreign keys, collection elements and query parameters. Neither needs a list of
types.

- **Selector** (`Mapping.cs`): `IValueConverterSelector` supplies the conversions EF may apply to a
  type it cannot map directly, which is how an enum reaches an integer column. One that also
  answers "any `IValueObject<TSelf, T>` converts to `T`" replaces EF's.
- **Plugin** (`Plugin.cs`): the type mapping source asks its plugins first. One that answers a
  value object with the provider's mapping for `T`, with the converter composed onto it, is added
  beside EF's services. It fetches that mapping from the type mapping source at lookup time,
  because injecting the source would be a cycle; resolving it lazily is not.

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
| 11 | Selector, then another library's selector (`ReplaceService`, as strongly-typed-id guides do): ours is gone and the model fails, but only because this model has a `List<Tag>`, and the message blames that collection | ✅ fails, misleading | ✅ same |
| 12 | Another library's selector, then ours: ours wins and the other library loses its mappings | ✅ | ✅ |
| 13 | The plugin: findings 2–9 hold unchanged | ✅ | ✅ |
| 14 | The plugin beside another library's replaced selector: findings 2–9 still hold | ✅ | ✅ |

Rows 11 to 14 were added the same day. The first version of this README chose the selector and
rejected the plugin for a dependency cycle that was assumed, not measured; resolving the type
mapping source at lookup time has none.

## Consequences for the design

- **Plugin, not a replaced selector.** Both map the same (findings 2–9, 13), but two replaced
  selectors cannot both win (findings 11, 12), and the loser fails with a message that does not
  name the cause. Plugins are additive (finding 14).
- **Not a pre-convention.** `Properties<T>()` needs the list of types up front, which means a scan.
- **Facets.** The plugin passes the lookup's store type, size, precision, Unicode and key flags on
  to the wrapped type's lookup, so a configured column keeps them.
- **`.Value`** needs the member translator (finding 10), as measured in
  [spikes/ValueTranslation](../ValueTranslation/README.md) for the other libraries.
