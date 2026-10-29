---
verified: 2026-10-29
pins:
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
source: EF Core's source at v10.0.12 (ValueGeneratorSelector.CreateFromFactory and TryCreate, RelationalValueGeneratorSelector.FindForType, TemporaryNumberValueGeneratorFactory.Create, the CoreTypeMapping constructor, SqlServerValueGeneratorSelector.TrySelect) and Npgsql's at v10.0.3 (NpgsqlValueGeneratorSelector.TrySelect), read 2026-10-29; measured with plain EF Core and Npgsql, no database opened
turned-on: decision 114, the temporary value of a value-object key over a number (#86)
test: test/CodoMetis.TypeKit.Facts/KB047.cs
---

# KB-047. EF Core consults a converter's mapping-hints value generator after a property's generator and after HiLo

- EF Core chooses a temporary number for a generated property by its type mapping's CLR type, then by its
  converter's provider type (`TemporaryNumberValueGeneratorFactory.Create`). A key of a type that is no number,
  converted to a `decimal` column, gets a negative temporary `decimal`, and a conversion back to `ulong` throws
  `OverflowException` at `Add`.
- A type mapping built on a converter takes the value generator factory of the converter's `ConverterMappingHints`
  where the mapping has none of its own (the `CoreTypeMapping` constructor).
- `ValueGeneratorSelector` uses a generator configured on the property first, then the type mapping's factory, then
  its own choice. The SQL Server and Npgsql selectors choose HiLo before calling it, for a property without a
  generator of its own.
- `ConverterMappingHints.ValueGeneratorFactory` is marked `[Obsolete]` without a message; the constructor parameter
  that sets it is not.
