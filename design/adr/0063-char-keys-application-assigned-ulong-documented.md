# 0063. A key over `char` is the application's to assign; a default key over `ulong` throws at `Add`

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the pre-tag review of 2.0.0; [EF Core](../ef-core.md); refines
[decision 58](0058-integer-key-follows-ef-rule.md).

Decision 58 declared a value-object key generated wherever EF declares a key of its wrapped type
generated, and called that parity. It is narrower than parity. EF separates declaring a key generated
(`ValueGenerated`) from supplying what generates it, which the provider decides (an identity, a
sequence), and it picks the generator that fills a key at `Add` by the property's CLR type, else by its
converter's provider type, which for a value object is the column's. Where the provider stores the
wrapped type through a converter of its own, a value object's default key gets another generator than
a plain key. Measured with EF Core 10.0.12, Npgsql 10.0.3 and SQL Server's provider 10.0.12: the models
and `Add` on all three providers, the tables and inserts on PostgreSQL 18.1 (no SQL Server here).

| Key over | PostgreSQL | SQL Server | SQLite |
|---|---|---|---|
| `char` | `character(1)`, identity; refused with 22023 at creation and in the `AlterColumn` from a 1.5.1 snapshot, for a plain key too | `nvarchar(1)`, no identity; the value object's default key got a permanent character of a GUID string at `Add` (`9`, `b`), a plain one a temporary value | `TEXT`; a plain key's `AUTOINCREMENT` refused at creation, the value object's key a temporary value |
| `ulong` | `numeric(20,0)`, no identity; the value object's default key throws `OverflowException` at `Add`, a plain one gets a temporary `ulong` and fails at insert (23502) | `decimal(20,0)`, identity; the value object's default key throws `OverflowException` at `Add`, a plain one gets a temporary `ulong` | `INTEGER`; numbered 1, 2 |

**A key over `char` is never declared generated**, on any provider, as 1.5.1 left it. No provider
generates one, and declared generated it was the one case where the value object did worse than a
plain key without a word: on SQL Server the invented character was stored. This stops the invented
value and checks nothing more: a default `'\0'` is stored as it is, and a second such row fails on the
primary key. The application assigns the key; an explicit `ValueGeneratedOnAdd()` or a value generator
of its own still applies (convention precedence).

**A key over `ulong` stays declared generated**, as a plain one is, which keeps SQL Server's identity
column. It fails loudly, at `Add`, where the column is a `decimal`: EF's temporary `decimal` is negative,
and converting it back to `ulong` throws. Making `Add` give it a temporary `ulong` needs a value
generator per key. A delegate given to `HasValueGenerator` is refused by `dbcontext optimize` ("has a
value generator configured. Use 'HasValueGeneratorFactory' to configure the value generator factory
type"), a factory type would have to be public for the compiled model's code to name it, and either
takes precedence over the providers' HiLo, which they select only for a property without one. A type
mapping's own factory, which comes after HiLo, has no public setter on the provider's mapping a value
object's is composed onto. The workaround is `ValueGeneratedNever()` and a key the application assigns.

`KeyGenerationTests` holds both: a `char` key is `Never` on the three providers and `Add` leaves a
default key as it is (with the key declared generated again, SQL Server's test failed with `b`, the
others' with a temporary value), the application's own configuration still applies, and a default
`ulong` key throws at `Add` on SQL Server and PostgreSQL while `ValueGeneratedNever()` takes an
assigned one.
