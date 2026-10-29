# 0114. A value object's temporary number is its wrapped type's, carried by its converter's mapping hints

Date: 2026-10-29
Status: accepted

Context: [#86](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/86), the roadmap's 2.3.0 item;
[EF Core](../ef-core.md); supersedes the `ulong` half of [decision 63](0063-char-keys-application-assigned-ulong-documented.md).

Decision 63 left a default value-object key over `ulong` throwing `OverflowException` at `Add` on SQL
Server and PostgreSQL, whose column is a `decimal`, and ruled out three routes to a temporary `ulong`: a
delegate given to `HasValueGenerator` (refused by `dbcontext optimize`), a factory type on the property
(it comes before the providers' HiLo), and a type mapping's own factory (no public setter on the
provider's mapping). Read in EF Core 10.0.12's source, the cause is narrower than the column: EF gives a
generated property a temporary number by its type mapping's CLR type
(`TemporaryNumberValueGeneratorFactory.Create`), which for a value object is the value object, so it
falls back to the converter's provider type, the column's `decimal`. A plain `ulong` key's mapping says
`ulong`.

**A value object over a number gets the temporary values a property of its wrapped type gets.** The
converter `UseTypeKit()` composes carries `ConverterMappingHints` with a value generator factory, which
`CoreTypeMapping` takes into the value object's mapping where the provider's has none. That is the fourth
route, and the only one in the right place: `ValueGeneratorSelector` consults a mapping's factory after a
factory on the property, and the SQL Server and PostgreSQL selectors choose HiLo before either. So an
application's own generator and HiLo still come first, as for a plain key.

- The numbers are EF's list (`RelationalValueGeneratorSelector.FindForType`): every integer type, `char`
  included, and `decimal`, `float` and `double`. Where the column is of the wrapped type, the factory
  returns EF's own temporary generator, as before; where the provider stores the wrapped type as another,
  it asks the provider for a key of the wrapped type generated on add, in a model of its own, as the Guid
  and string keys' generator already does.
- **Siblings, found by the variant catalogue** (`KeyGenerationVariantTests`, configured `ValueGeneratedOnAdd()`
  over every key kind on three providers): a key over `char` configured on add got one character of a GUID
  string on SQL Server, from the column's `nvarchar(1)`; it now gets a temporary `char`, as a plain one does.
  The catalogue's two pinned departures are gone.
- **A compiled model keeps it, except under `--nativeaot`.** `dbcontext optimize` leaves the type mappings to
  be found when the model is first used, where the plugin composes them. `--nativeaot` writes each mapping
  out as code (`SqlServerDecimalTypeMapping.Default.Clone(converter: new ValueConverter<UInt64Key, decimal>(…))`),
  and a factory in the hints is a delegate it cannot write: a default `ulong` key still throws at `Add`
  there, and `ValueGeneratedNever()` remains the workaround. A test pins it.
- **Nor does a converter the application names.** `HasConversion<ValueObjectConverter<UInt64Key, ulong>>()` builds the
  mapping on the public converter, which carries no hints, and the key, declared generated, still throws at `Add`.
  Hints on that converter would make `dbcontext optimize` write the mapping of every property that names it out as
  code (`CSharpRuntimeModelCodeGenerator`, a converter with hints), which loses the factory there too, so it is
  documented and pinned instead.
- `ConverterMappingHints.ValueGeneratorFactory` is marked `[Obsolete]` without a message in EF Core 10; the
  constructor parameter is not. A later EF that removes it fails the tests that hold this, not silently.
- PostgreSQL's provider refuses HiLo on a `ulong` key, plain or value object ("Sequences can only be used
  with integer properties"), so only SQL Server's HiLo is tested for one.

Measured with EF Core 10.0.12, Npgsql 10.0.3 and SQL Server's provider 10.0.12, at `Add` and in the
selector, no database opened. EF caches a property's generator by the model's id
(`ValueGeneratorCache.CacheKey`), which a compiled model keeps from the model it was written from: a test
that builds a model twice in one service provider reads the first one's generator, so the compiled-model
tests each get a service provider of their own.

`KeyGenerationTests` holds it: a default `ulong` key gets the temporary values a plain one does on three
providers, SQL Server's HiLo is still chosen, an application's generator still wins, a compiled model gives
the temporary value, and a `--nativeaot` one still throws. Without the hints, seven of them fail; with the
factory set on the property instead, the HiLo test fails.
