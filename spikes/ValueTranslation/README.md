# Spike: can other libraries query the underlying value in EF Core LINQ?

**Question.** Is TypeKit's `.Value` query translation (`ValueObjectMemberTranslatorPlugin`)
unique? Measured 2026-09-27 with EF Core 10.0.12 and the SQLite provider, using `ToQueryString()`
only (no database).

| Probe | Vogen 8.0.7 | Thinktecture 10.5.0 | Plain EF Core 10 (control, no library) |
|---|---|---|---|
| `c.Email == Email.From("a@b")` | ✅ `WHERE "c"."Email" = 'a@b'` | ✅ | — |
| `c.Email.Value == "a@b"` | ❌ `InvalidOperationException` (could not be translated) | n/a (no public `Value`) | ❌ same |
| `c.Email.Value.StartsWith("a")` | ❌ same | n/a | ❌ same |
| `((string)c.Email).StartsWith("a")` (conversion operator) | n/a (no operator generated here) | ✅ `CAST("c"."Email" AS TEXT) LIKE 'a%'` | ✅ **same SQL, no library involved** |
| `Select(c => c.Email.Value)` | ✅ but evaluated on the client (`SELECT "c"."Email"`) | — | — |

## Conclusion

- **`.Value` in a query predicate is unique.** Vogen fails, and so does plain EF. Thinktecture
  generates no public `.Value`: its key member is a private `_value`, reached through an implicit
  conversion operator.
- **The capability is not unique.** Querying the underlying value works in plain EF Core 10
  through a conversion-operator cast, which is how Thinktecture users get it. The control proves
  that this is EF's own behaviour and not a library feature.
- **What we can honestly claim:**
  - You write `.Value` in queries exactly as in memory: no cast, and no conversion operator the
    type has to expose.
  - The same works for optional members through `GetValue()` / `ValueOrNull()`.
- **What we must not claim:** "the only library whose value objects can be queried by their
  underlying value".

```bash
dotnet run --project spikes/ValueTranslation/vogen
dotnet run --project spikes/ValueTranslation/thinktecture
dotnet run --project spikes/ValueTranslation/plainef
```
