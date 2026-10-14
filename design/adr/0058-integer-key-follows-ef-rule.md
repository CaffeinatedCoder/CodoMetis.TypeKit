# 0058. A value-object key is generated wherever EF generates its wrapped type's

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the second outside review of 2.0.0; refines
[decision 23](0023-value-object-key-over-integer-generated.md).

Decision 23 named `int`, `long` and `short`. EF generates every type its `IsInteger()` names except
`byte`: also `sbyte`, `uint`, `ulong`, `ushort` and `char`, so an `IValue<uint>` key stored 0 where a
plain `uint` key was generated (measured). The convention follows EF's rule exactly, `char`
included, whose own key SQLite refuses (`TEXT … AUTOINCREMENT`) as it refuses a plain `char` key.
Parity covers the hierarchy too: SQLite re-decided a TPH root's key from its CLR type when the
mapping strategy was set, after the convention had run, and stored 0; the convention now runs after
the hierarchy-mapping conventions, and a TPC key stays generated only where the provider gives it a
sequence, as a plain `int` key does (measured on SQLite and Npgsql; SQL Server's TPC read from EF's
source).
