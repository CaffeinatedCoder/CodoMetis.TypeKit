# Changelog

All notable changes to CodoMetis.TypeKit and its satellite packages.

The packages share one version number (the `Version` property in `Directory.Build.props`) and
are released together, so a version appears here even when a given package saw no change in it.
Versions follow [Semantic Versioning](https://semver.org/). Entries are newest first.

A version's heading reads `## <version> — Unreleased` until the release is prepared, when it gets
the release date. The release workflow refuses a tag whose section still says `Unreleased`, and the
section is the GitHub release's notes.

## 2.4.0 — Unreleased

### Changed

- Packages: the public surface is checked against 2.3.0's, so nothing 2.3.0 ships is removed or changed in 2.4.0.

## 2.3.0 — 2026-10-29

### Added

- AspNetCore: `FaultResponse.WithoutFaultName()` answers a member without naming it: no `fault`, no `rule` and no member description in the problem, so two causes on one status look alike; the document says that status names no fault, and `IFaultFeature.NameWithheld` tells the host ([Faults a client must not tell apart](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.AspNetCore/README.md#faults-a-client-must-not-tell-apart)).
- `Result.CatchAsync` takes a call that returns a `ValueTask`, `stream.ReadAsync(buffer)` without `.AsTask()`; an `async` lambda binds as before ([An exception as an outcome](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/docs/result.md#an-exception-as-an-outcome)).
- `TraverseAsync` on `Option<T>` and `Patch<T>`: an asynchronous check of an optional input or a patch field, with a token form; nothing is called without a value ([Collect many results](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/docs/result.md#collect-many-results)).
- `Choose()`, `Choose(selector)`, `SequenceAsync` and `TraverseAsync` read an `IAsyncEnumerable<T>`, in order and without a list first; the token reaches the stream, nothing is read after the first error, and the enumerator is disposed however they stop ([Chain steps](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit/README.md#chain-steps)).
- `Fault<TFault>.Cause`: `Fault.Of(reason, detail, exception)` keeps the exception underneath for your log; equality and `ToString()` leave it out, and `ToHttpResult()` hands it to the host as `IFaultFeature.Cause` and `FaultProblem<TFault>.Cause`, never into the problem ([What failed underneath](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-failed-underneath)).
- Testing: every assertion works on a `Task` or `ValueTask` of what it checks, `await service.FindAsync(id).ShouldBeSome()`; a task that throws fails the test with its own exception, and an assertion left without `await` is reported (CMTK0003): through `ConfigureAwait` or `WaitAsync`, in a branch of a `?:`, `??` or `switch`, and handed to `Task.WhenAll`, `Task.Run` or `Task.WhenEach` and dropped, or to `Task.WhenAny` or `Task.WaitAny`, nested at any depth, or awaited in an `async` lambda whose `catch` throws again or can't catch it ([CodoMetis.TypeKit.Testing](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.Testing/README.md)).
- OpenAPI: `AddTypeKit(typeKit => typeKit.InlineValueObjects = true)` describes every value object in place by its underlying type's schema, in every position and in OpenAPI 3.1, 3.0 and 2.0, with no component of its own; closed sets keep theirs, and without the setting the document is unchanged ([Describe value objects as plain types](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/docs/aspnet-core.md#describe-value-objects-as-plain-types)).
- Analyzers: CMTK0009 warns at `FirstOrNone`, `SingleOrNone` or `FirstOrError` on a query given a `Func` variable or a method group, which binds to the `IEnumerable<T>` overload and reads every row; a lambda at the call or an `Expression` stays silent ([A condition that reads every row](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.Analyzers/README.md#a-condition-that-reads-every-row)).
- Docs: the guides, the package READMEs, the changelog and the API of every run-time package as one site with a menu and search, built from the latest release ([caffeinatedcoder.github.io/CodoMetis.TypeKit](https://caffeinatedcoder.github.io/CodoMetis.TypeKit/)).
- Analyzers: a validated value object without `Create` gets a code fix, **Add Create**, that writes a `Create` accepting every value, ready for your rules ([Add Create](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.Analyzers/README.md#add-create)).
- Docs: a walkthrough follows one orders service from request to response, saying what changes at each boundary; the service is a sample that CI builds from the packages and runs, and the answers the page states are checked against it ([Walkthrough](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/docs/walkthrough.md)).
- Docs: the root README and the Generators README show, folded, the code the generators write for `OrderId`, `Email` and a closed set; a test holds each block to what the build emits ([Declare a value object](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.Generators/README.md#declare-a-value-object)).
- EF Core: queries, keys and round trips are tested on a real SQL Server, and the README says what differs there from memory: text compared ignoring case, Guids sorted by their last group, `decimal(18,2)` by default, a `TimeSpan` under a day, `Greatest` from compatibility level 160 ([On SQL Server](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#on-sql-server)).

### Changed

- Analyzers: CMTK0003 now reports four forms that code built on 2.2.0 can contain, so your build can fail with it when you upgrade. They are a `Task.WhenAny` of `Result` tasks you don't hold (dropped, awaited, waited for or returned as a plain `Task`), a dropped `Task.WhenEach` of them, `Wait()` on a `?:`, `??` or `switch` of them, and an array of them written in a `Task.WaitAll` or `Task.WaitAny` call. Each one loses results that nobody reads: handle them, or discard them with `_ =`. An awaited `Task.WhenAny` of tasks you hold is no longer reported ([What a new version may change](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.Analyzers/README.md#configuration)).
- EF Core: a default value-object key over `ulong` gets a temporary value at `Add`, as a plain `ulong` key does, where it threw `OverflowException` on SQL Server and PostgreSQL; a `char` key configured `ValueGeneratedOnAdd()` gets a temporary `char` on SQL Server, not a character of a GUID string. HiLo and your own generator still come first. A compiled model made with `--nativeaot`, and a key you configure with `ValueObjectConverter` yourself, still throw ([Keys](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#keys)).
- Packages: nuget.org's Release Notes tab and Visual Studio's Updates tab show the version's section of this changelog, as plain text, where they showed a link to the file.
- Changelog: a released version's links name its tag, `blob/v2.2.0/`, so its notes open the pages as they were released, not as they read on `main` today.
- Comparison: SuperStrong.Types 1.0.0 joins the value-object table, Thinktecture.Runtime.Extensions is compared at 10.6.0 and LanguageExt at 5.0.0-beta-78, and the page's facts are re-read before every release ([How it compares](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/docs/comparison.md)).
- README: introduces TypeKit by example, a one-line `OrderId`, a validated `Email`, an `Option` lookup and a `Result` chain, and thanks Vogen, which inspired the value objects; every README says what Metalama is where it first names it ([CodoMetis.TypeKit](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/README.md#what-you-get)).

### Fixed

- Generators: the aspects keep no compilation objects in static fields, which in an IDE that keeps Metalama's process across edits held every earlier compilation in memory.
- Generators: a generated `TryParse(string?, IFormatProvider?, out)` ends at its `return`; the unreachable statements after it are gone from the woven code ([Reading the generated code](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.3.0/src/CodoMetis.TypeKit.Generators/README.md#reading-the-generated-code)).

## 2.2.0 — 2026-10-18

### Fixed

- Generators: a value object over `string` implements `ISpanParsable`, as `string` does, and its span `Parse` runs `Create` as `Parse` does; a span `Parse` you wrote on one now fails the build (CMTK1011) ([What is generated](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.Generators/README.md#what-is-generated)).
- EF Core: a query or a query filter that orders a value object with its own `CompareTo` through `EF.Functions.Greatest` or `Least`, or through a row-value comparison such as Npgsql's `EF.Functions.GreaterThan((o.Priority, o.Id), (priority, id))`, is refused and names the value object, where it used to order by the column ([Queries](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#queries)).
- OpenAPI: a route, query or header parameter of a type a converter describes, such as a `decimal` under `DecimalStringJsonConverter`, is what ASP.NET writes without the converter: an OpenAPI 2.0 document builds, and a nullable one no longer admits `null` ([Parameters aren't JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a query or header array of a type a converter describes, such as `decimal[]` or `decimal?[]` under `DecimalStringJsonConverter`, keeps its `items`, which are what ASP.NET writes for the element without the converter ([Parameters aren't JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a value object over a type a converter describes, such as `Money : IValue<decimal>` under `DecimalStringJsonConverter`, is what ASP.NET writes for the underlying type without the converter as a route, query, header or form parameter and as a query or header array's items, no longer its JSON spelling; where value objects are components, such a parameter is written in place ([Parameters aren't JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: the default of a parameter of a type a converter describes, or of a value object over one, such as `[DefaultValue(typeof(decimal), "1.5")]` under `DecimalStringJsonConverter`, is written as ASP.NET writes it without the converter, `1.5`, not `"1.5"` ([Parameters aren't JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a non-abstract `[JsonPolymorphic]` base's own branch (`{Base}Base`) describes its value objects, patches, closed sets and described types as the derived types' branches do, where it had `{}` and a list without `items`, in OpenAPI 3.1, 3.0 and 2.0 ([What the document says](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a list or dictionary of a nullable value object whose converter a described one replaces, such as `List<Amount?>` under a converter that writes cents, admits `null` in its elements where value objects are components ([A described type](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a nullable value-object property where value objects are components is the component or `null` wherever the underlying type's property admits `null`: get-only, a field, typed by a type parameter, under `[MaybeNull]` or `[DisallowNull]`, or without nullable annotations, where it was the bare reference; under OpenAPI 2.0 it's ASP.NET's `x-nullable` without the reference there too, as a settable one already was ([What the document says](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a nullable value object is its wrapped type at the same nullability in every position: an MVC `[FromBody] Quantity?` admits `null` inside its schema as `int?` does, and, where value objects are inlined, a query, header or form parameter no longer admits `null`, nor does a body, response or element of a nullable struct over `string` or `Uri` ([What the document says](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a nullable value object over an enum written by name lists `null` among its values as a route, query, header or form parameter, as the enum's own `DayOfWeek?` does; where value objects alone are inlined, a nullable one over an enum no longer puts a second null-or inside a body's or a response's, and as an element or a dictionary value keeps its type in OpenAPI 2.0 ([What the document says](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a problem response an endpoint declares as `application/json`, such as `Produces<ProblemDetails>(404)` or `[ProducesResponseType<ProblemDetails>(404)]`, describes `fault` and `rule` and names its problem types, and keeps its media type; so does one of a type derived from `ProblemDetails`, such as `ProducesValidationProblem(404)` ([A Result as a response](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.AspNetCore/README.md#a-result-as-a-response)).
- EF Core: a key over a `Guid` or a `string` configured `ValueGeneratedOnAdd()` is generated as the plain key is, not as a random Guid: a version 7 Guid, or its text, on PostgreSQL, and a Guid sequential in SQL Server's order there ([Keys](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.2.0/src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#keys)).

## 2.1.0 — 2026-10-15

Still `net10.0` and Metalama 2026.1. What builds or runs differently, and what to write instead:
[Migrating from 2.0 to 2.1](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/250f11ae0a838e3ad68e3c7cce091028b504ea25/docs/migrating-to-2-1.md).

### Added

- **Decimals in one spelling**: `DecimalNumberJsonConverter` and `DecimalStringJsonConverter` write the smallest scale, keys included, and read either token ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/docs/json.md#decimals-in-one-spelling)).
- **Converters that say what they write**: `IDescribedJsonConverter` and `JsonSchemaDescriptions`; the schema exporter (`DescribedJsonSchema`) and `AddTypeKit()` describe them everywhere ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/docs/json.md#describing-what-a-converter-writes)).
- **The rule that refused**: `Fault.Of(reason).WithRule(member)` names an enum's or a closed set's member, written as the problem's `rule` and on `IFaultFeature` ([The rule that refused](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#the-rule-that-refused)).
- **`FaultKind.PreconditionFailed`** (412) and **`FaultKind.Malformed`** (400), answered with no mapping ([closed sets as faults](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#a-closed-set-as-the-fault-type)).
- **CMTK0014** (Warning): a `FaultKind` or `ConstructorAccessibility` that names no member, such as `(FaultKind)9` or `default` ([CMTK0014](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0014)).
- **`AsOptionalText()`/`AsOptionalIdentifier()`**: a blank is no text, a hidden character still `Hidden` ([Written text](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/docs/text.md#required-and-optional)).
- **Faults of several types**: `Fault<T1, T2>` and `Fault<T1, T2, T3>`, each answered as its type is mapped ([Faults of several types](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#faults-of-several-types)).
- **`WithoutFaults`** and `[WithoutFaults<T>]`: a route leaves out of its document the members it never answers with, and reporting one throws ([Leaving out what a route cannot answer](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#leaving-out-what-a-route-cannot-answer)).
- **CMTK1023** (Warning): a base record's `Equals`, `GetHashCode` or `IEquatable`, which the value object's equality no longer calls ([Build errors](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.Generators/README.md#build-errors)).
- **`[Interface]`, `[Unsealed]`, `[ValueObject]`, `[ClosedSet]`, `[FlagsEnum]`, `[ReadOnlyStruct]`** (CMTK0010): six more kinds a type argument can be required to be ([Type constraints](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/docs/type-constraints.md#the-kind-of-a-type-argument)).
- **CMTK0010 at the methods the compiler calls for you**: a `foreach`'s `GetEnumerator`, an `await`'s `GetAwaiter`, a `Deconstruct`, and a collection expression's `Add` or builder, for every attribute but 2.0's four ([Type constraints](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/docs/type-constraints.md)).
- **`[Distinct]` and `[Unrelated]`** (CMTK0010): two type arguments that must differ, or must not derive from, implement or box to each other ([Type constraints](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/docs/type-constraints.md#two-parameters-apart)).
- **One mapping for every fault set**: `AddFaultResponses()` by kind, or `AddFaultResponses(fault => …)` over `IFault`, for each set with no mapping of its own ([Every fault set at once](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#every-fault-set-at-once)).
- **Testing: `ShouldHold(reason)`** on `Fault<T1, T2>` and `Fault<T1, T2, T3>` hands back the fault it holds ([CodoMetis.TypeKit.Testing](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.Testing/README.md)).

### Changed

- The OpenAPI problem schema describes `rule` beside `fault`: regenerate clients.
- A host whose endpoint answers two fault types that write one fault name logs a warning at startup naming both; 3.0.0 refuses it ([Faults of several types](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#faults-of-several-types)).
- The new attributes in the `CodoMetis.TypeKit` namespace (`[ValueObject]`, `[Interface]`, …) can be ambiguous beside another library's of the same name (CS0104): qualify or alias one.
- OpenAPI: in a host with controllers, a document whose MVC and minimal-API JSON options write a type differently is refused, naming what differs ([OpenAPI](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).

### Fixed

- OpenAPI: a value object or closed set the JSON options write with a converter of their own is described as that converter says, or as ASP.NET writes it, not as its wrapped type ([OpenAPI](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a `Patch<T>` the serializer requires stays `required`; under `RespectRequiredConstructorParameters`, declare patches as `init` properties ([Merge patches](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/docs/merge-patches.md#required-constructor-parameters)).
- OpenAPI: a `WithFaultStatus` on the builder `MapControllers()` returns reaches the document, as it reached the response.
- OpenAPI: a problem response an endpoint declares itself (`ProducesProblem`, `[ProducesResponseType]`) describes `fault`, and names its problem types.
- OpenAPI: an operation with fault responses lists every status ascending, the ones its endpoint declares itself among them; where an endpoint declares its own, the document's response order changes, so update snapshot tests.
- Over a base record, a value object's equality is its wrapped value's, in a collection of the base too, whatever equality the base declares.
- Under `ReferenceHandler.Preserve`, a value object over a type that could hold an `$id` throws `NotSupportedException` instead of writing a duplicate `$id` ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.1.0/src/CodoMetis.TypeKit.Generators/README.md#json)).
- A wrapped type's `Parse` whose result can't be assigned to it, or that takes its text by `ref`, or an abstract class's string constructor, no longer breaks the build; the value object has no parsing.
- A value object over a NodaTime type parses after MVC bound a plain one first; it threw `TypeInitializationException` for the rest of the process.
- OpenAPI: an MVC action binding a complex type from the route under a name of its own (`[FromRoute(Name = "id")]`) no longer fails the whole document with `AddTypeKit()`.

## 2.0.0 — 2026-10-14

Still `net10.0` and Metalama 2026.1. What builds or runs differently, and what to write instead:
[Migrating from 1.5 to 2.0](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/v2.0.0/docs/migrating-to-2.md).

### Breaking

- CMTK0003, a dropped `Result` or `Option`, is an Error, and reports more forms (`Wait()`, loops that never read, `??`, tuples).
- `FirstOrNone`/`FirstOrError` on an `IQueryable` run in the query (a `Func` variable still reads every row); `LastOrNone`/`LastOrError` on a query no longer compile.
- CMTK1022 refuses an abstract value object; CMTK1005 refuses one over `object` or `System.Enum`, or named as a generated member.
- A generated `TryParse` lets an exception from `Create` through instead of answering `false`.
- EF Core refuses `.Value` on a column with the application's own converter, and ordering by a hand-written `CompareTo`, in queries and query filters.
- EF Core: value-object keys over `sbyte`, `ushort`, `uint` and `ulong` are generated as EF generates the plain type, so a migration may alter the column.
- OpenAPI: a component name claimed by two types throws.
- Async combinators throw `ArgumentNullException` for a delegate that returns no task.
- `FaultProblem<TFault>` takes any `notnull` fault type.

### Added

- **Closed sets** (`IClosedSet<TSelf>`): a fixed set of named instances with `Match`, `Parse`, JSON, EF Core, OpenAPI and Serilog support; CMTK1013 to CMTK1021 refuse what cannot be generated.
- **Fault responses a client can act on**: `Fault<TFault>`, fault sets with a kind per member, `FaultResponse`, `WithFaultStatus`/`[FaultStatus<T>]` per endpoint, group or controller, `ToHttpResult(success)` and `IFaultFeature`.
- **`Patch<T>`**: a JSON Merge Patch field (absent, null or a value; `T` is `notnull`, so `Patch<string>` is a clearable string), with `Map`, `Traverse`, OpenAPI and test assertions.
- **`Result.Catch`/`CatchAsync`**: an exception as a result; a cancellation passes through, a timeout is the error.
- **Text guards**: `AsText()` and `AsIdentifier()` trim, compose to NFC (`AsText`) and refuse invisible characters.
- **Type constraints**: `[Abstract]`, `[Sealed]`, `[Record]`, `[Concrete]` (CMTK0010) and `[RequireParameterlessConstructor]` (CMTK0011, with a code fix).
- **CMTK0012**: a closed set's `Match` called by position, with a code fix.
- **CMTK0013**: a closed set's member written outside its declaration, after it was named.
- **`Option`**: async steps and `Task<Option<T>>` continuations, `SingleOrNone`, `ElementAtOrNone`, `Choose`, `Transpose`, `Traverse`; EF Core's `FirstOrNoneAsync` and `SingleOrNoneAsync`.
- **`TraverseAsync`** on a sequence: one at a time, cancellable.
- **Docs**: topic pages under `docs/`, every code block compiled.

### Changed

- The OpenAPI problem schema describes `fault`: regenerate clients.
- CMTK0003 no longer reports a chain of taps on a result the caller holds.
- EF Core: a `char` key stays the application's to assign; a default `ulong` key throws `OverflowException` at `Add` (use `ValueGeneratedNever()`).
- Testing failures name a nested type with its containing types.
- `CodoMetis.TypeKit.Generators` depends on `Metalama.Framework` 2026.1.30 or later (was 2026.1.29).

### Fixed

- A value object marked `[Obsolete(error: true)]` builds.
- A default value object over a reference type formats as `""` instead of throwing.
- Where the host inlines enums, a nullable value object over one admits null in the OpenAPI document.

## 1.5.1 — 2026-10-11

### Fixed

- **`CodoMetis.TypeKit.AspNetCore`**: a value object bound as a route, query or header parameter is
  described as ASP.NET describes a parameter of the wrapped type. ASP.NET uses a type's own schema there
  only for a primitive, an enum, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`,
  `decimal`, `Guid` and `Uri`, and `string` for any other parsable type. `AddTypeKit()` used the wrapped
  type's JSON schema regardless, so a value object over a type with a JSON converter of its own, such as
  a CodoMetis.ValueRanges `DateRange`, was the empty schema `{}` as a parameter, where a `DateRange`
  parameter is `string`. Request and response bodies are unchanged.

## 1.5.0 — 2026-10-11

### Added

- **`CodoMetis.TypeKit.AspNetCore`**: a `Result` as an HTTP response. `result.ToHttpResult()` answers a
  success with `200 OK` and its value (`204 No Content` for a `Result<TError>`) and a fault with RFC 9457
  problem details, whose status the host maps per fault type with
  `services.AddFaultResponses<TFault>(fault => …)` and whose `fault` member names the fault. The fault
  type is an enum the domain declares; only the host references ASP.NET. The return type is ASP.NET's
  own `Results<Ok<T>, FaultProblem<TFault>>`, in minimal APIs and MVC, and `AddTypeKit()` lists a
  problem response for every mapped status in the OpenAPI document. A host with an endpoint whose
  fault type is not mapped does not start; a mapping that gives a member no error status, or throws
  for one, fails where it is registered. `AddFaultResponses` calls `AddProblemDetails()`, which a
  Native AOT host needs to write a problem.
- **`CodoMetis.TypeKit`**: `Recover` on both `Result` shapes, the error branch's `Bind`: the selector's
  result replaces an error, a success or another error, and a success passes untouched.
  `RecoverAsync` takes an asynchronous selector and continues a pending `Task<Result<…>>`, as the other
  combinators do.
- **`CodoMetis.TypeKit.Serilog`**, a new package: `Destructure.WithTypeKit()` logs every value object as
  what it prints, also where Serilog destructures it (`{@Key}`, or a record holding one). Without it,
  Serilog read the public `Value`, so a value object masked by a hand-written `ToString()` was logged
  unmasked there. Nothing is registered per type; Serilog 4.0.0 or later.
- **`CodoMetis.TypeKit.Generators`** README: "A value that must not be printed", the `ToString()` seam
  as the way to mask a value, what still carries it, and why a compliance classification belongs on
  the logging method's parameter, not on the type.
- **`CodoMetis.TypeKit.Testing`**, a new package for test projects: `ShouldBeSome()`, `ShouldBeNone()`,
  `ShouldBeSuccess()` and `ShouldBeError()` (with or without the expected error) on `Option`, both
  `Result` shapes. They hand back the value or the error for the test's own assertions, and otherwise
  throw a `TypeKitAssertionException` that names the error found and never prints a value. An
  uninitialized result fails every assertion as such. No test framework dependency.

### Changed

- **`CodoMetis.TypeKit.Generators`** depends on `Metalama.Framework` 2026.1.29 or later (was 2026.1.28),
  the latest of the 2026.1 line that MIT maintenance covers.

### Fixed

- **`CodoMetis.TypeKit.AspNetCore`** README: a minimal API answers a body a value object refuses with
  an empty `400`, with `AddProblemDetails()` too; it said that call gives the response a body, which
  takes `UseStatusCodePages()` as well.

## 1.0.0 — 2026-09-28

The first release.

### Added

- **`CodoMetis.TypeKit`**: `Option<T>`, `Result<T, TError>` and `Result<TError>`, with no public
  `.Value` or `.Error`, and the `Option.None()`, `Result.Success(...)` and `Result.Error(...)`
  markers that convert to whichever one a method returns. A `default` `Option` is `None`; a
  `default` `Result` is uninitialized, and every member that would pick a branch throws on it.
  `Result<TError>` converts to `bool` for a success; `Result<T, TError>` does not, since its value
  could itself be a `bool`.
  `ToString()` never prints the content, and none of them serializes: System.Text.Json refuses them
  with `NotSupportedException` in both directions instead of writing `{}`, a nullable one too, and a
  converter registered on the options takes precedence. `OrNull()` converts an `Option` of a value
  type or of a reference type to a nullable at the boundary.
  - Every deliberate refusal, of the base package and of generated code, says what to do instead and
    ends with a link to its explanation under "Why does this throw?" in the package README; the
    generators' build errors link to the README's Build errors table.
  - Combinators: `Map`, `Bind`, `MapError`, `Tap`, `TapError`, `Ensure` (a rule checked inside a
    pipeline), `Match`, `TryGetValue`, and query syntax with any number of `from` clauses, for
    `Option` and `Result` alike.
  - Pipelines: `MapAsync`/`BindAsync`, and an `…Async` continuation of `Map`, `Bind`, `MapError`,
    `Tap`, `TapError` and `Ensure` on a `Task<Result<…>>`, so a chain is awaited once; `Zip` for two
    to six results; `Sequence` and `Traverse` over a sequence. All stop at the first error. A
    callback that returns a `Task` is awaited, in `TapAsync` and `TapErrorAsync` alike.
  - Lookups that return an `Option`: `GetValueOrNone(key)` on a dictionary, and `FirstOrNone()` and
    `LastOrNone()` with or without a predicate.
  - The value-object contracts (`IValue<T>`, `IValidatedValue<TSelf, T, TFault>`,
    `IValueObject<TSelf, T>`), `OrderId.New()` for version 7 Guid identifiers, and
    `StoredJsonConverterFactory` for JSON the application stored itself.
- **`CodoMetis.TypeKit.Analyzers`**, arriving with the base package, in C# files and Razor
  components alike; where Metalama compiles a project, on the code it transformed. Each rule links to
  its section of the analyzer README:
  - Errors: CMTK0001 (no `default` of a value object, an `Option`, a `Result` or a
    `[RequireCustomInitialization]` struct; a `default` only compared (`==`, `Equals`, `ThrowIfEqual`,
    an assertion such as `Assert.NotEqual` or `ShouldNotBe`) or assigned to the `out` parameter of a
    `bool` Try method is allowed), CMTK0002 (a value object in a project that does not reference the
    generators), CMTK0004 (`Materialize`, which skips validation, called outside the EF Core
    satellite).
  - Warnings: CMTK0003 (an ignored `Result` or `Option`, or a collection of them the statement made,
    such as `await Task.WhenAll(ids.Select(orders.CancelAsync))`; a `Task` of one converted to a plain
    `Task`, a method group's included), CMTK0005 (an array or span of such a struct created with a
    length), CMTK0006 (a member of such a type that nothing assigns), CMTK0008 (the values of two
    different value objects compared, through `==`, `Equals`, `string.Equals`, a comparer or a LINQ
    join, `join … on a.Value equals b.Value` and `Join`/`GroupJoin`/`LeftJoin`/`RightJoin`, or two
    value objects of different types compared through `Equals`), CMTK0009 (a call that hands out a
    `default` instance when it finds nothing: `FirstOrDefault`, `Find`, `GetValueOrDefault`,
    `Option.OrDefault()` and their kind, on immutable collections too, and `FirstOrDefaultAsync` and
    its kind on `IAsyncEnumerable` and in EF Core).
  - Info: CMTK0007 (`FromKnownGood` given a parameter).
  - The analyzer loads in every .NET 10 SDK: it compiles against Roslyn 5.0.0, the compiler of the
    10.0.1xx band.
- **`CodoMetis.TypeKit.Generators`**: a one-line value-object declaration gets its constructor,
  `Value`, `From`, `TryFrom` and `FromKnownGood` (both derived from the hand-written `Create`),
  `Revalidate()` for values read back without validation, a JSON converter,
  `IParsable`/`ISpanParsable`/`IUtf8SpanParsable`, formatting in the invariant culture, comparison,
  `IConvertible`, a `TypeConverter` and `GetValue()`/`ValueOrNull()` extensions.
  - Every generated way into a validated value object applies `Create`.
  - The JSON is the wrapped type's, byte for byte, under the application's options, as a value and as
    a dictionary key; formatting, comparison and JSON cost about what the wrapped type's own do.
  - A refusal names the value object and the fault, or the wrapped type it could not read, and never
    quotes the input.
  - Seams: a hand-written `TryFrom`, `FromKnownGood`, `Revalidate`, `CompareTo(TSelf)` or `ToString()`
    is kept, and what depends on it derived from it; a base record's `sealed` `ToString()` counts.
  - A value object holds its wrapped value alone, and its equality is that value's. A declaration
    that cannot be generated, or that would be generated wrongly (state beside the wrapped value, a
    hand-written equality, an explicit implementation of an interface the generators implement), is a
    build error naming it (CMTK1000–CMTK1012).
  - Metalama 2026.1, and no Metalama license is needed to build.
- **`CodoMetis.TypeKit.EntityFrameworkCore`**: `UseTypeKit()` maps every value object to a column
  of the type it wraps, with nothing registered per type, including keys, foreign keys, nullable
  properties and primitive collections, and translates `.Value`, `GetValue()` and `ValueOrNull()`
  in queries to the bare column, collection elements included. A key over an integer is generated by
  the database, as a key of that integer type is; on SQLite, with `UseAutoincrement()` (see the
  README). EF Core 10.0.12 or later, with a relational provider.
- **`CodoMetis.TypeKit.AspNetCore`**: `AddTypeKit()` on `AddOpenApi` gives every value object the
  schema ASP.NET publishes for the type it wraps, wherever it appears: properties, bodies,
  containers, and route, query and header parameters, a nullable one admitting null there as the
  wrapped type does, whether it is a component or inlined. A nested value object's component is named
  after its nesting chain. Microsoft.AspNetCore.OpenApi 10.0.12 or later.
- `UseTypeKit()`, `AddEntityFrameworkTypeKit()` and `AddTypeKit()` live in EF Core's and
  dependency injection's namespaces, so a host adds them without a `using`.
- **Native AOT**, for every package that runs in an application. The run-time packages are built with
  the trim and AOT analyzers on, and the generated code is published with Native AOT and run in the
  consumer smoke test. With source-generated JSON a context lists the value objects, not what they
  wrap. The EF Core satellite works with a compiled model and precompiled queries, which is how EF
  Core runs under Native AOT. The OpenAPI satellite needs the wrapped types in the host's
  `JsonSerializerContext`, and says which one is missing.
