# Roadmap

What is open, and nothing else. Done work is in the [changelog](../CHANGELOG.md) and in git. Each
item names the decision that scoped it.

## 2.1, additive

- An optional `Cause` on `Fault<TFault>`, an exception kept beside the reason and the detail for the
  log and the fault feature, never written to the problem; `Fault.Of(reason, detail, cause)`, and a
  `MapError` from a `Result<T, Exception>` that keeps the exception ([decision 52](adr/0052-cause-on-fault-joins-2-1.md)).
- An opt-in that withholds the `fault` name from the response for chosen members or a whole fault
  type, so two causes can share a status without a mapping at the boundary; additive
  ([decision 64](adr/0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md)).
- CMTK0009, a Warning, which may learn ([decision 34](adr/0034-errors-fixed-within-major-version-warnings.md)),
  reports `FirstOrNone`, `SingleOrNone` and `FirstOrError` on an `IQueryable<T>` given a
  `Func<T, bool>` or a method group, which binds to the sequence overload and reads every row.
- Documentation for generated members, so IntelliSense shows them: it needs Metalama to copy a
  template's XML comment to the member it introduces, or to take one through the builder, measured
  absent in 2026.1.29 and 2027.0.3-preview (`spikes/IntroducedMemberDocs`). Ask upstream; then
  document every generated member and test a consumer's XML documentation file.
- A default value-object key over `ulong` generated as a plain one is, which needs a per-key value
  generator a compiled model can write ([decision 63](adr/0063-char-keys-application-assigned-ulong-documented.md)).
- CMTK0003: `WhenAny` over held tasks; an async expression-bodied member in the IDE, where Metalama has
  not run; and decide whether a tap on a property or an indexer, and a result a cache's `GetOrAdd`
  still holds, are drops, which it reports today.
- CMTK0010's cost: `GetSymbolInfo` on every generic name, about 0.5 s on a 6.7k-line file.
- Generators: a C# keyword in a namespace or a member name fails with LAMA0041, not a CMTK error.
- Run time: the Serilog policy ignores `ToMaximumStringLength`; an exception error prints its whole
  `ToString()` in a Testing message, and there are no async assertions; a closed set's `.Name` in a
  query is not translated (it throws); the closed-set JSON read allocates a string per token;
  `CatchAsync` over a `ValueTask` and `TraverseAsync` on `Option` and `Patch`.

## 2.x, with a spike first

- Fault sets that share members: a set that includes another's, its members copied by the generators
  with a conversion from the included set, against waiting for C# 15 unions; what `fault`, the
  document and `AddFaultResponses` then say ([decision 68](adr/0068-fault-set-composition-spiked-first.md)).
- A stable external name for a closed-set member, apart from its C# identifier: parsing, JSON, EF,
  OpenAPI and the fault identifiers at once (proposed in the review of 2026-10-13).
- The Metalama 2027.0 upgrade ([decision 4](adr/0004-metalama-2026-1-x-latest-stable.md)): a minor if a consumer's own aspects beside the packed
  generators still build and weave, measured as the fabric spike measured 2026.1 against the 2027.0
  preview, and a major otherwise; `[Durable]` returns to the `_value` placeholder with it.
- A `net11.0` target beside `net10.0`, in a 2.x minor when a feature needs it ([decision 44](adr/0044-2-0-0-net10-0-metalama.md)): EF Core 11
  and Microsoft.AspNetCore.OpenApi 11 in the satellites, and a Native AOT smoke run per target.

## 3.0.0

- C# 15 unions and the `net10.0` drop, timed to .NET 12 LTS (November 2027) or to the end of .NET
  10's life, whichever its consumers need ([decision 44](adr/0044-2-0-0-net10-0-metalama.md)). The compiler-made union types have a public
  `object? Value` and are serialized by System.Text.Json, so `Option` and `Result` take part in their
  pattern matching only after a spike shows neither returns, on a branch of its own with no release
  attached.
