# Roadmap

What is open, and nothing else. Done work is in the [changelog](../CHANGELOG.md) and in git. Each
item names the decision that scoped it.

## 2.0.0, before the tag

- `2.0.0-preview.1` to nuget.org, on hold since 2026-10-13 until the user says so, then the consumer
  smoke test against it ([decision 42](adr/0042-2-0-0-next-release-bundles.md)).
- The release: date the changelog's section, move the unshipped analyzer rows (CMTK0010 to CMTK0012
  new, CMTK0003 changed) to `AnalyzerReleases.Shipped.md` under the release, merge so the help links'
  headings exist on `main`, and tag `v2.0.0`. The release workflow and `ChangelogTests` check each.

## 2.1, additive

- An optional `Cause` on `Fault<TFault>`, an exception kept beside the reason and the detail for the
  log and the fault feature, never written to the problem; `Fault.Of(reason, detail, cause)`, and a
  `MapError` from a `Result<T, Exception>` that keeps the exception ([decision 52](adr/0052-cause-on-fault-joins-2-1.md)).
- What the outside review of 2026-10-13 rated can-wait: the fault
  name is sent even for a member without a description; `Uri.ToString()` unescapes the problem
  `type`; the CMTK0011 code fix on a partial type whose other part has a primary constructor, and its
  doc comment beside an attribute; CMTK0003 over `WhenAny` of held tasks; `Choose` on a sequence with
  a nullable element; `Patch<string?>` under `Traverse` and `Map`.
- CMTK1021 groups names ignoring case, which misses the one `Match` collision `ToLowerInvariant` makes
  and ordinal case folding does not (the Kelvin sign); it fails loudly, as LAMA0611 and CS0100.
- What the second outside review of 2026-10-13 rated can-wait. Analyzers: the trivia the CMTK0011
  code fix drops; CMTK0010 on an enum or a delegate under `[Concrete]` and on a type parameter whose
  constraint already makes it a record, and its cost (`GetSymbolInfo` on every generic name, about
  0.5 s on a 6.7k-line file); CMTK0003 on an async expression-bodied member in the IDE, where
  Metalama has not run. Generators: a C# keyword in a namespace or a member name fails with LAMA0041,
  not a CMTK error; generated members carry no XML documentation, so IntelliSense shows nothing for
  them. Run time: the Serilog policy ignores `ToMaximumStringLength`; an inlined nullable closed set
  does not admit null in the document; an exception error prints its whole `ToString()` in a Testing
  message, and there are no async assertions; `query.FirstOrNone(func)` binds to the sequence
  overload and reads every row; a closed set's `.Name` in a query is not translated (it throws); the
  closed-set JSON read allocates a string per token; `CatchAsync` over a `ValueTask` and
  `TraverseAsync` on `Option` and `Patch`.

## 2.x, with a spike first

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
