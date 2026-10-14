# 0056. `.Value` is translated only on a column TypeKit's converter stores

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the second outside review of 2.0.0; [EF Core](../ef-core.md).

The `.Value`, `GetValue()` and `ValueOrNull()` translators re-typed any value-object column as the
wrapped type. A column the application maps with its own converter stores something else: with
`HasConversion(c => "C:" + c.Value, …)`, `t.Code.Value.StartsWith("a")` became `LIKE 'a%'` and
answered no row where memory answered one, and a quantity stored as text compared as a number
(measured on SQLite, through `HasConversion`, a converter type and `ConfigureConventions`). The
translators now act only where the operand's to-provider expression is
`ValueObjectConverter<,>.ProviderValue`, composed with an enum's own converter where the wrapped type
has one; anywhere else they decline and EF refuses the query. The expression is read rather than the
converter's type, since a compiled model recreates the converters (measured). A query that answered
wrong rows in 1.5.1 now throws, which is the change in behaviour 2.0.0 lists.
