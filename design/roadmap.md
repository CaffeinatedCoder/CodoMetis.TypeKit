# Roadmap

What is open, and nothing else. Done work is in the [changelog](../CHANGELOG.md) and in git. Each
item names the decision that scoped it, or the review that found it. A Warning may learn and a
refusal may relax in any minor version; an Error that reports more waits for a major
([decision 34](adr/0034-errors-fixed-within-major-version-warnings.md)).

## 2.2.0, consolidation

2.2.0 adds no public API. The probe gate binds from this release
([decision 97](adr/0097-2-2-0-is-a-consolidation-release-and-the-gate-binds.md)). Any gap below that is a
defect, a cost or a missing guard may land here; one that adds surface waits for 2.3.0.

- The probing harness (WS2 of the agentic issue-finding concept) is done
  ([decision 108](adr/0108-a-probe-check-earns-credit-against-a-baseline-and-ws2-closes.md)).
  WS3 (beyond pairs), WS4 (the mutation nightly), the second half of WS5 (the finder benchmark at equal budgets) and
  WS6 (PostgreSQL in CI) were dropped on 2026-10-17 by the maintainer's decision, and are not scheduled.
- The generated-surface snapshot under "Generators" below.

## 2.3.0, additive

Decided, moved to 2.2 when 2.1.0 shipped without them ([decision 88](adr/0088-2-1-0-ships-without-its-open-roadmap-items.md)),
and then to 2.3 when 2.2.0 became a consolidation release ([decision 97](adr/0097-2-2-0-is-a-consolidation-release-and-the-gate-binds.md)).

- A default value-object key over `ulong` generated as a plain one is, which needs a per-key value
  generator a compiled model can write ([decision 63](adr/0063-char-keys-application-assigned-ulong-documented.md)).

## Gaps, in any 2.x

Small, each closable in a minor version. Found by the reviews named.

**Analyzers**
- CMTK0009, a Warning, reports `FirstOrNone`, `SingleOrNone` and `FirstOrError` on an `IQueryable<T>`
  given a `Func<T, bool>` or a method group, which binds to the sequence overload and reads every row.
- CMTK0003: `WhenAny` over held tasks; an async expression-bodied member in the IDE, where Metalama
  has not run; and decide whether a tap on a property or an indexer, and a result a cache's
  `GetOrAdd` still holds, are drops, which it reports today.
- CMTK0010's cost: `GetSymbolInfo` on every generic name, about 0.5 s on a 6.7k-line file, and since
  2.1.0 a semantic-model query per `foreach`, `await`, deconstruction, positional pattern and
  collection expression. Measure on the same file before and after.

**Generators**
- A C# keyword in a namespace or a member name fails with LAMA0041, not a CMTK error.
- Package validation compares the packages' own public surface, not what the generators write into a
  consumer: a `Parse` that 2.1.0's prerelease stopped generating (a wrapped `Parse` returning a type with
  an implicit conversion) broke a consumer's call with CS0117 and passed ApiCompat. Snapshot the
  generated public members of the test corpus' value objects, and fail a change that removes one
  without a decision (review of 2026-10-14).

**Run time**
- The Serilog policy ignores `ToMaximumStringLength`.
- Testing: an exception error prints its whole `ToString()` in a message.
- EF Core: a closed set's `.Name` in a query is not translated (it throws).
- A closed set's JSON read allocates a string per token.

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
- CMTK0010 refuses `Enum`, `Delegate`, `MulticastDelegate`, `ValueType` and `Array` under `[Unsealed]`: abstract
  classes nothing can derive from, which 2.x accepts since refusing them reports more under an Error
  ([decision 34](adr/0034-errors-fixed-within-major-version-warnings.md); found by `TypeArgumentKinds`, 2026-10-15).
- An endpoint whose fault types write one name for two members stops the host, where 2.x logs a
  warning ([decision 90](adr/0090-faults-alike-are-a-warning-until-3-0.md)).
- CMTK1023, a base record's equality that the generated one does not call, becomes an Error
  ([decision 75](adr/0075-equality-over-base-record-generated.md)).
- C# 15 unions and the `net10.0` drop, timed to .NET 12 LTS (November 2027) or to the end of .NET
  10's life, whichever its consumers need ([decision 44](adr/0044-2-0-0-net10-0-metalama.md)). The compiler-made union types have a public
  `object? Value` and are serialized by System.Text.Json, so `Option` and `Result` take part in their
  pattern matching only after a spike shows neither returns, on a branch of its own with no release
  attached.
