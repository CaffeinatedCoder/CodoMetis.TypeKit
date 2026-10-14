# Roadmap

What is open, and nothing else. Done work is in the [changelog](../CHANGELOG.md) and in git. Each
item names the decision that scoped it, or the review that found it. A Warning may learn and a
refusal may relax in any minor version; an Error that reports more waits for a major
([decision 34](adr/0034-errors-fixed-within-major-version-warnings.md)).

## 2.2.0, additive

Decided, and moved here when 2.1.0 shipped without them ([decision 88](adr/0088-2-1-0-ships-without-its-open-roadmap-items.md)).

- An optional `Cause` on `Fault<TFault>`, an exception kept beside the reason and the detail for the
  log and the fault feature, never written to the problem; `Fault.Of(reason, detail, cause)`, and a
  `MapError` from a `Result<T, Exception>` that keeps the exception ([decision 52](adr/0052-cause-on-fault-joins-2-1.md)).
- An opt-in that withholds the `fault` name from the response for chosen members or a whole fault
  type, so two causes can share a status without a mapping at the boundary
  ([decision 64](adr/0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md)).
- A default value-object key over `ulong` generated as a plain one is, which needs a per-key value
  generator a compiled model can write ([decision 63](adr/0063-char-keys-application-assigned-ulong-documented.md)).

## Gaps, in any 2.x

Small and additive, each closable in a minor version. Found by the reviews named.

**Analyzers**
- CMTK0009, a Warning, reports `FirstOrNone`, `SingleOrNone` and `FirstOrError` on an `IQueryable<T>`
  given a `Func<T, bool>` or a method group, which binds to the sequence overload and reads every row.
- CMTK0003: `WhenAny` over held tasks; an async expression-bodied member in the IDE, where Metalama
  has not run; and decide whether a tap on a property or an indexer, and a result a cache's
  `GetOrAdd` still holds, are drops, which it reports today.
- CMTK0010's cost: `GetSymbolInfo` on every generic name, about 0.5 s on a 6.7k-line file, and since
  2.1.0 a semantic-model query per `foreach`, `await`, deconstruction, positional pattern and
  collection expression. Measure on the same file before and after.
- CMTK0010 refuses what it could prove, each a relaxation a minor may make (audit of 2026-10-14):
  `dynamic` under `[Unsealed]`, which `[Concrete]` takes as `object`; and a `[ReadOnlyStruct]` or
  `[FlagsEnum]` parameter forwarded to `[Sealed]` or `[Concrete]`, though every type either admits
  is sealed and concrete.

**Generators**
- A C# keyword in a namespace or a member name fails with LAMA0041, not a CMTK error.
- A wrapped type's `Parse` that returns another type through an implicit conversion (`Circle` to
  `Shape`, or `string` to `T`) is taken for no parser, so the value object loses `IParsable` without
  a word, where 2.0.0 generated it. Accept the conversion, or report the value object as not parsable
  (audit of 2026-10-14).

**JSON**
- A value object held as its base record type is written as the base, `{}`, as System.Text.Json
  writes any declared type. CMTK1022 refuses the abstract marker, not an abstract base record. Say so
  in the JSON guide, or refuse it (audit of 2026-10-14).

**ASP.NET Core**
- A group's `WithFaultStatus` for a member that an endpoint inside it leaves out with `WithoutFaults`
  stops the host ([decision 78](adr/0078-fault-sets-compose-side-by-side.md) refuses a member also given a status), where
  [decision 61](adr/0061-scope-s-fault-status-applies-where-fault-type-answered.md) has a scope's status apply only where it means something. Decide which reading holds;
  relaxing the refusal is allowed in a minor (audit of 2026-10-14).

**Run time**
- The Serilog policy ignores `ToMaximumStringLength`.
- Testing: an exception error prints its whole `ToString()` in a message, and there are no async
  assertions.
- EF Core: a closed set's `.Name` in a query is not translated (it throws).
- A closed set's JSON read allocates a string per token.
- `CatchAsync` over a `ValueTask`, and `TraverseAsync` on `Option` and `Patch`.

## 2.x, with a spike first

- A NodaTime satellite that registers NodaTime's converters and their descriptions in one call, read
  from the patterns the host configures rather than a fixed set; NodaTime's version floors and its
  converters' trimming warnings decide whether it is a package ([decision 71](adr/0071-converters-say-what-they-write.md)).
- A stable external name for a closed-set member, apart from its C# identifier: parsing, JSON, EF,
  OpenAPI and the fault identifiers at once (proposed in the review of 2026-10-13).
- The Metalama 2027.0 upgrade, once it is stable (`2027.0.4-preview` on 2026-10-14;
  [decision 4](adr/0004-metalama-2026-1-x-latest-stable.md)): a minor if a consumer's own aspects beside
  the packed generators still build and weave, measured as the fabric spike measured 2026.1 against the
  2027.0 preview, and a major otherwise; `[Durable]` returns to the `_value` placeholder with it.
- A `net11.0` target beside `net10.0`, in a 2.x minor when a feature needs it ([decision 44](adr/0044-2-0-0-net10-0-metalama.md)): EF Core 11
  and Microsoft.AspNetCore.OpenApi 11 in the satellites, and a Native AOT smoke run per target.

## Blocked upstream

- Documentation for generated members, so IntelliSense shows them: it needs Metalama to copy a
  template's XML comment to the member it introduces, or to take one through the builder, measured
  absent in 2026.1.29 and 2027.0.3-preview (`spikes/IntroducedMemberDocs`). Ask upstream; then
  document every generated member and test a consumer's XML documentation file.

## 3.0.0

- CMTK0010 checks `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` at a method the compiler binds by
  pattern too, as it checks the kinds 2.1.0 added ([decision 89](adr/0089-cmtk0010-at-methods-bound-by-pattern.md)).
- CMTK1023, a base record's equality that the generated one does not call, becomes an Error
  ([decision 75](adr/0075-equality-over-base-record-generated.md)).
- C# 15 unions and the `net10.0` drop, timed to .NET 12 LTS (November 2027) or to the end of .NET
  10's life, whichever its consumers need ([decision 44](adr/0044-2-0-0-net10-0-metalama.md)). The compiler-made union types have a public
  `object? Value` and are serialized by System.Text.Json, so `Option` and `Result` take part in their
  pattern matching only after a spike shows neither returns, on a branch of its own with no release
  attached.
