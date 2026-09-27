---
name: weaving-review
description: Review a change to CodoMetis.TypeKit for code that compiles and silently does less than it appears to — a type that is not generated, an analyzer that stops firing, a generated entry point that bypasses Create, an Option/Result that leaks its content or reads as success, a satellite that falls back to default behaviour. Use when changing Option/Result, an aspect, the transitive fabric, AspectOrder, the analyzer, the EF converter/convention/translators, the OpenAPI transformers, or package references — and before cutting a release.
---

# Weaving review

The dangerous failure in this repository is not a build error. It is a build that succeeds while
something that should have happened did not: the fabric skipped a type, the analyzer went quiet,
the JSON converter took a path that never calls `Create`, EF mapped a value object as an owned
type instead of a converted scalar. Generic code review does not look for this. This skill is that
lens.

Work through the sections the change touches. Answer each question against the actual code or a
build, not against the docs. When a question has no obvious answer, **write the test that answers
it**. Report findings as a concrete scenario: the declaration a user writes, what the package
does, and what they expected.

## 1. Does the aspect reach every type it should, and only those?

- Which types does the fabric's `Where` select? Consider a class vs struct, an abstract type, a
  type implementing the marker **indirectly** (through a derived interface or a base class), a
  nested type, a generic type, and a type in a project that reaches the package only
  transitively.
- A type the fabric skips compiles cleanly. Is there a test or a diagnostic that would notice?
- A project that references only `CodoMetis.TypeKit` gets no aspects at all. Does CMTK0002
  ("marker without `.Generators`") still fire there?
- Is `AspectOrder` still complete? A new aspect class missing from it runs in an unspecified
  order relative to the others, and reading `ValueObjectAspectState` from a predecessor then
  silently returns nothing (`TryGetState` fails and `BuildAspect` returns early).

## 2. Every entry point applies the same rules

An `IValidatedValue` has exactly one rule set, in `Create`. List every generated way in and check
each one routes through it:

- `TryFrom`, `FromKnownGood`
- the JSON converter (read path), including property-name / dictionary-key reads
- `IParsable.Parse` / `TryParse`, `ISpanParsable`
- the `TypeConverter` (`ConvertFrom`)
- `IConvertible` / explicit conversions, if generated

The **only** exception is the explicit `IValueObjectMaterializer.Materialize`. Is it still
explicit (not callable as `T.Materialize` on the concrete type), and still used only by the EF
satellite?

Does `default(T)` stay unreachable outside the type? The analyzer covers `default`, `default(T)`,
`new T()` and `new()`. A new creation form (a collection expression, `Unsafe.As`,
`RuntimeHelpers.GetUninitializedObject`) is not covered. Say so if the change makes one more
likely.

## 3. Discovery is by interface

- Any new string that names a namespace, assembly or type? Replace it with a symbol or `Type`
  comparison.
- The analyzer: are interface symbols resolved once per compilation (`CompilationStartAction`),
  and does it register nothing when `CodoMetis.TypeKit` is absent, instead of throwing?
- Run-time checks: `IValueObject<,>` assignability, with a cache keyed by `Type`. Does a
  **nullable** struct value object (`OrderId?`) unwrap before the check?

## 4. EF Core

- Does a value-object property map as a **scalar with a converter**, not as an owned or complex
  type, and not fail with "could not be mapped"? Check a key, a foreign key, a nullable property
  and a primitive collection.
- Does the converter's from-provider path materialise without validation, and never produce
  `default`?
- Do `.Value`, `GetValue()` and `ValueOrNull()` translate to the bare column, and does the SQL
  match the snapshot? A translator that stops matching makes EF throw at best, or evaluate on the
  client at worst.
- Is the comparer consistent with the type's equality?

## 5. OpenAPI

- For a value object used only as a property, as a list element, as a dictionary value and as a
  route/query parameter: what schema does the document actually contain? Look at the emitted
  JSON, not at whether the transformer ran. A schema transformer that registered, compiled and
  never ran for a property-only type has been measured before (plan §7).
- Is the schema the **underlying type's** schema (format included: `uuid`, `date`, `int64`), and
  does it match what the JSON converter writes?

## 6. Option and Result

- Does any change add a public member that exposes the content without a check: a `.Value`, an
  `.Error`, a positional record parameter, a `ToString`/`DebuggerDisplay` that ends up in logs?
- Does `default(Option<T>)` still mean None, and `default(Result<…>)` still mean `Uninitialized`
  rather than success? Do `Match`/`TryGetValue` on an uninitialized `Result` throw rather than pick
  a branch?
- Does every new combinator (`Map`, `Bind`, `Tap`, async variants) preserve the error or None
  unchanged, and avoid invoking the callback on it?
- Is the change inside the non-goals in plan §9 (no Either, no effects, no LanguageExt drift)?

## 7. Packaging

- Is `CodoMetis.TypeKit` still free of Metalama in its nuspec?
- Does `CodoMetis.TypeKit` depend on `CodoMetis.TypeKit.Analyzers` **without**
  `exclude="Build,Analyzers"` (i.e. `PrivateAssets="none"`)? With the exclude, the analyzer
  silently never reaches consumers.
- Do the EF and ASP.NET satellites depend on `CodoMetis.TypeKit` only, never on `.Generators`?
- Does the consumer smoke test build a project that references the packages **transitively**,
  the case the fabric spike proved and a refactor could lose?
