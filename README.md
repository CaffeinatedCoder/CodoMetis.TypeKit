# CodoMetis.TypeKit

Strong types for .NET 10, with one set of rules from the request to the database.

[![.NET](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/actions/workflows/dotnet.yml/badge.svg)](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/actions/workflows/dotnet.yml)
[![NuGet](https://img.shields.io/nuget/v/CodoMetis.TypeKit.svg)](https://www.nuget.org/packages/CodoMetis.TypeKit)
[![Built with Metalama](https://img.shields.io/badge/built_with-Metalama-informational)](#built-with-metalama)
[![Native AOT: every run-time package](https://img.shields.io/badge/Native%20AOT-every%20run--time%20package-3B82F6)](docs/native-aot.md)

## What you get

**Value objects in one line**
- **Declare it, the rest is generated.** `OrderId : IValue<Guid>` gets its constructor, equality,
  comparison, parsing, formatting, JSON converter and type converter at compile time.
- **Validation in one place.** Write `Create` once, and JSON, route binding, parsing and `TryFrom`
  all run it. Nothing skips it by accident.
- **A drop-in for primitives.** The JSON, the database column and the OpenAPI schema are the same as
  the underlying type's. It works for strings, numbers, `Guid`, dates, enums, `Uri`, NodaTime types
  and your own types.
- **Smart enums.** One line per member gives you parsing by name, JSON as the name, an OpenAPI string
  enum and a text column. `Match` breaks the build when a member is added and not handled.

**Option and Result you can't misuse**
- No `.Value` to forget a check on. A `default` value is a compile error, and an ignored `Result`
  is flagged.
- Pipelines, async chains, collecting many results, and `Result.Catch` for code that throws.
- Neither type serializes silently to `{}`. The serializer refuses them, so a missing value can't
  reach the wire unnoticed.

**ASP.NET Core and EF Core, with nothing to register per type**
- **EF Core:** value objects map to plain columns and keys, identity keys included, and `.Value` in
  a LINQ query becomes plain SQL.
- **ASP.NET Core:** return a `Result` from an endpoint and its errors become problem-details
  responses. The OpenAPI document shows each value object as its underlying type, plus the error
  responses an endpoint can return.
- **JSON Merge Patch:** `Patch<T>` tells "absent", "null" and "a value" apart.

**Mistakes caught at build time**
- Analyzers flag default values, ignored results, bypassed validation, and generic type arguments
  that break declared constraints (`[Sealed]`, `[Distinct]` and more). They also run in Razor files.
- A declaration that can't be generated fails the build with its name on it, instead of compiling
  into an empty type.

**Ready for production**
- **Native AOT and trimming** in every run-time package, checked in CI by publishing real apps as
  native binaries ([what it asks of you](docs/native-aot.md)).
- **Secrets stay out of logs.** Error messages never quote the input, and Serilog logs a value
  object as it prints itself.
- **Test assertions** for `Option`, `Result` and `Patch<T>`, with no dependency on a test framework.
- **Take only what you need.** The `Option`/`Result` package brings no code generator with it.

Two value objects, the second one validated:

```csharp
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value.Trim()) : Result.Error(EmailFault.NoAt);
}
```

That is the whole declaration. The generators add the field, the constructor, `Value`, the
factories, value equality, a JSON converter, `IParsable`, `IFormattable`, `IComparable`, a
`TypeConverter` and the companions that make `.Value` work inside an EF Core query. Every way into
`Email`, whether JSON, a route parameter or a call to `TryFrom`, applies the one `Create` you wrote.
A value object can wrap a primitive, a `Guid`, a date, an enum, a `Uri`, a NodaTime type or a type of
your own, and it can be declared inside another type.

A closed set, the smart enum, is declared the same way, one line per member:

```csharp
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}
```

The generators add `All`, parsing by name, JSON as the name, an OpenAPI string enum and a text
column in EF Core, with nothing registered per set. `Match` takes one required delegate per member,
so a member added later fails every call until it is handled.

The base package also gives you:

- **`Patch<T>`**, a field of a JSON Merge Patch: absent, null or a value.
- **`Result.Catch`**, which turns an exception into a result.
- **`AsText()` and `AsIdentifier()`**, which check text a person wrote and a value another system
  issued.
- **`[Sealed]`, `[Concrete]` and `[RequireParameterlessConstructor]`**, constraints that C# has no
  `where` clause for. The analyzers check them.

## Packages

| Package | What it does | Metalama |
|---|---|---|
| [CodoMetis.TypeKit](src/CodoMetis.TypeKit/README.md) | `Option<T>`, `Result<T, TError>`, `Result<TError>`, the value-object contracts and the text guards `AsText()` and `AsIdentifier()`. Brings the analyzers that check them | no |
| [CodoMetis.TypeKit.Analyzers](src/CodoMetis.TypeKit.Analyzers/README.md) | Rules CMTK0001 to CMTK0013. They flag a `default` value object, `Option` or `Result`, whether written in your code or handed out by `FirstOrDefault` and its kind; a value object nothing generates; an ignored result; skipped validation; a comparison of two value objects' values; a type argument that breaks what its parameter declares with `[Abstract]`, `[Sealed]`, `[Record]`, `[Concrete]`, `[Interface]`, `[Unsealed]`, `[ValueObject]`, `[ClosedSet]`, `[FlagsEnum]` or `[ReadOnlyStruct]`, or two that `[Distinct]` or `[Unrelated]` keep apart; a type without the constructor its host will look for; a closed-set `Match` bound by position; and a closed-set member written after it was named. They run in Razor components too, and arrive with the base package | no |
| [CodoMetis.TypeKit.Generators](src/CodoMetis.TypeKit.Generators/README.md) | Generates your value objects at compile time. Add it to the project that declares them | yes |
| [CodoMetis.TypeKit.EntityFrameworkCore](src/CodoMetis.TypeKit.EntityFrameworkCore/README.md) | Maps value objects to columns with nothing to register per type, and makes `.Value` work in LINQ | no |
| [CodoMetis.TypeKit.AspNetCore](src/CodoMetis.TypeKit.AspNetCore/README.md) | Shows value objects in the OpenAPI document with the schema of their underlying type. Returns a `Result` as the response, and a fault as problem details | no |
| [CodoMetis.TypeKit.Serilog](src/CodoMetis.TypeKit.Serilog/README.md) | Logs value objects as what they print, also where Serilog destructures them, so a value masked by its `ToString()` stays masked | no |
| [CodoMetis.TypeKit.Testing](src/CodoMetis.TypeKit.Testing/README.md) | Test assertions for `Option`, `Result` and `Patch<T>`. They hand back the value or the error, and fail saying what the outcome was. No test framework needed | no |

Take only what you need. If you only want `Option` and `Result`, the base package brings no code
generator with it. You take Metalama on once, on purpose, by adding `CodoMetis.TypeKit.Generators` to
your domain project.

The EF Core and ASP.NET Core packages work at run time through the interfaces. So a host that maps
value objects from a referenced domain assembly needs no generator itself.

## Quick start

Add the generators to your domain project, which brings the base package along. Add the EF Core
package to your data project, if you have one.

```bash
dotnet add package CodoMetis.TypeKit.Generators      # the domain project: brings CodoMetis.TypeKit along
dotnet add package CodoMetis.TypeKit.EntityFrameworkCore   # the data project, if there is one
```

Declare a value object with its validation:

```csharp
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

public enum EmailFault { Blank, NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(EmailFault.Blank);
        if (!value.Contains('@')) return Result.Error(EmailFault.NoAt);

        return new Email(value.Trim());
    }
}
```

Then use it:

```csharp
var id = OrderId.New();                                   // a version 7 Guid

Result<Email, EmailFault> created = Email.Create(input);  // the fault says which rule refused it
Option<Email> maybe = Email.TryFrom(input);               // is it valid?
Email known = Email.FromKnownGood("ops@example.com");     // a literal you vouch for; throws otherwise

string reply = created.Match(
    email => $"Welcome, {email}",
    fault => $"Please check the address ({fault})");

JsonSerializer.Serialize(known);                          // "ops@example.com"
JsonSerializer.Deserialize<Email>("\"nobody\"");          // JsonException naming Email and NoAt, never the text
Email.Parse("ops@example.com", null);                     // IParsable, so it binds as a route or query parameter
```

Set up EF Core and OpenAPI once, for every value object:

```csharp
services.AddDbContext<ShopDb>(options => options.UseNpgsql(connectionString).UseTypeKit());

db.Customers.Where(c => c.Email.Value.EndsWith("@example.com"));        // WHERE c."Email" LIKE '%@example.com'

services.AddOpenApi(options => options.AddTypeKit());                    // OrderId: {"type":"string","format":"uuid"}
```

## Documentation

The pages below are guides, one topic each, built around examples. The package READMEs above are
the reference for each package: what it adds, how to set it up, and the rules it follows. Every
exception message and analyzer help link points into them. Every C# block in the guides and the
READMEs is compiled, and the outputs it states are tested.

| Page | Covers |
|---|---|
| [Value objects](docs/value-objects.md) | Declare one, pick a struct or a class, validate input, design its errors, pick the right factory, write your own members, generic code |
| [Closed sets](docs/closed-sets.md) | Smart enums that carry data: declare one, handle every member with `Match`, send it as JSON, store it as a text column, read it from configuration, use one as an error type, and when an enum is still the right choice |
| [Option](docs/option.md) | A value or nothing: create one, get the value out, combine options, convert at the boundary, and every public member |
| [Result](docs/result.md) | A value or an error: pick a shape, chain steps, async chains, collect many results, an exception as an outcome, translate errors between layers, and every public member |
| [Written text](docs/text.md) | `AsText()` and `AsIdentifier()`: pick a guard, what each cleans and rejects, required and optional fields, a value object on a guard |
| [Type constraints](docs/type-constraints.md) | `[Abstract]`, `[Sealed]`, `[Record]`, `[Concrete]`, `[Interface]`, `[Unsealed]`, `[ValueObject]`, `[ClosedSet]`, `[FlagsEnum]` and `[ReadOnlyStruct]` on a type parameter, `[Distinct]` and `[Unrelated]` on two, `[RequireParameterlessConstructor]` and its code fix |
| [Merge patches](docs/merge-patches.md) | `Patch<T>` for PATCH endpoints: tell absent, null and a value apart, apply and validate a patch, write one, the OpenAPI document, source-generated contexts |
| [JSON](docs/json.md) | A value object's JSON is the underlying type's: options, rejected input, null and missing properties, stored documents, decimals, describing your own converters, `Option` and `Result` at the wire, source-generated contexts |
| [EF Core](docs/ef-core.md) | Value objects as plain columns: the model, keys, queries and the SQL they become, seed data, reading back without validation, compiled models, SQLite in tests |
| [ASP.NET Core](docs/aspnet-core.md) | Take value objects as endpoint parameters and return a `Result`: binding in minimal APIs and MVC, what a rejected value gets back, errors as problem details, and the OpenAPI document |
| [Native AOT](docs/native-aot.md) | What Native AOT asks of your app: project settings, JSON contexts, OpenAPI, EF Core's compiled model and query helpers, configuration binding, written text, Serilog, discovery without reflection, what the smoke test measures |
| [Logging and secrets](docs/logging-and-secrets.md) | Keep a secret out of logs: mask a value, what still carries it, Serilog, Microsoft.Extensions.Logging, rejections that never quote the input |
| [Testing](docs/testing.md) | Assertions for `Option`, `Result` and `Patch<T>`, `Create` as a table, the generated members checked against `Create`, EF Core on SQLite, endpoints on the test server |
| [Migrating from 1.5 to 2.0](docs/migrating-to-2.md) | Move a project from 1.5 to 2.0: dropped results as build errors, queries that now run in the database or throw, names 2.0 adds, fault responses, Native AOT |
| [How it compares](docs/comparison.md) | When another library fits better, and feature tables against the value-object, result and smart-enum libraries, versioned and dated |

## What TypeKit guarantees

- **No `.Value` on `Option` or `Result`.** You reach the content through `Match`, `TryGetValue` and
  the other members, so you handle absence and failure where you use the value. A `default` `Option`
  is `None`. A `default` `Result` is uninitialized, and every branching member throws on it rather
  than inventing a `default(TError)`. `ToString()` never prints the content.
- **`Option` and `Result` stay off the wire.** System.Text.Json throws `NotSupportedException` for
  either, in both directions, instead of writing `{}` that reads back as `None`. Say "absent" with a
  nullable in a serialized shape, and convert at the boundary with `ToOption()` and `OrNull()`. If
  you want a wire format of your own, a converter you register on the options takes precedence.
- **One set of rules per validated value object.** `Create` is the only factory you write. The
  generated `TryFrom`, `FromKnownGood`, JSON converter, parsing and type converter all run it, and
  each reports the fault you return from `Create`: the `JsonException`, the `FormatException` and the
  `FromKnownGood` exception all name your `EmailFault.NoAt`.
- **Only two paths skip validation, and both are named.** The EF Core package reads a database
  column back without validation. `StoredJsonConverterFactory` reads JSON the application stored
  itself. Register that factory only on the options you read your own JSON with, never on the ones
  that read a request.
- **No factory throws on input by accident.** A validated value object has no `From`. You create one
  with `Create`, which returns a `Result`, with `TryFrom`, which returns an `Option`, or with
  `FromKnownGood`. Its name says you vouch for the value, and its exception points at your call site.
- **Rejections never repeat the input.** A JSON or parsing rejection names the value object and the
  rule, or the underlying type it could not read. It carries no inner exception that quotes the text.
  `FromKnownGood` names your expression, and `Option` and `Result` print nothing. So a secret doesn't
  end up in a message or a log through this package, as long as your fault type doesn't carry it.
  One exception comes from System.Text.Json: a rejected dictionary key is in the `JsonException`'s
  `Path`, and so in MVC's 400
  ([Logging and secrets](docs/logging-and-secrets.md#refusals-never-quote-the-input)).
- **Swapping a primitive for a value object keeps the JSON, the column and the schema.** Its JSON is the underlying type's
  under your options, with two exceptions: a `DateTime` is written as UTC, and no converter sees
  number handling set by an attribute on the containing type
  ([JSON](docs/json.md#the-wrapped-types-json)). Its column is the underlying type's, and a key over
  an integer is still an identity column. Its OpenAPI schema is the one ASP.NET Core publishes for the
  underlying type. What changes is what the value object rejects, a JSON `null` for a struct value
  object included. And under a source-generated context, a value object over an enum needs its enum
  listed ([JSON](src/CodoMetis.TypeKit.Generators/README.md#json)).
- **Failures are loud.** A declaration that can't be generated fails the build and names the
  declaration (CMTK1000 to CMTK1022). It never compiles into a type with nothing in it. The analyzers
  make a `default` value object, `Option` or `Result` an error.
- **Discovery by interface.** The EF Core package and the ASP.NET Core package's OpenAPI support
  recognise a value object by the attribute the generators put beside `IValueObject<,>`. The attribute's type arguments are
  constrained to that interface. Renaming a type or a namespace changes nothing, and trimming keeps
  the attribute.
- **Native AOT.** The run-time packages build with the trim and AOT analyzers on. On every CI
  build, an app with value objects, source-generated JSON, OpenAPI and EF Core is published with
  Native AOT and run. Each package's README says what, if anything, Native AOT asks of you.

## How it compares

Good libraries exist for both halves of TypeKit. TypeKit fits when you want:

- **One model from validation to response.** The value objects, the outcomes, and the HTTP and
  database adapters share it. The fault `Email.Create` returns is the error of a `Result` pipeline,
  and then the problem response, with no mapping between a validation library, a result library and
  an adapter.
- **Value objects that change no contract,** on .NET 10 with System.Text.Json, EF Core and ASP.NET
  Core. The JSON, the column and the OpenAPI schema stay the underlying type's, with nothing
  registered per type, and `.Value` works inside a LINQ query.
- **An OpenAPI document that describes what the wire carries.** A converter you register once, such
  as one that writes decimals as strings, is described everywhere it applies, value objects and
  patches included. A document that wouldn't match what the controllers write is rejected, not
  served.
- **One set of rules per value object.** JSON, parsing, route binding, `TryFrom` and `FromKnownGood`
  all run the `Create` you write. You declare a value object by implementing an interface, not with
  an attribute, so generic code and the EF Core and ASP.NET Core packages can use it.
- **An `Option` and a `Result` you can't misuse.** No `.Value`, no `default` in your code, ignored
  results reported, and no silent `{}` on the wire.
- **Native AOT** for your published app.

[How it compares](docs/comparison.md) says what to choose instead when TypeKit doesn't meet your
need. It also has detailed tables against Vogen, Thinktecture.Runtime.Extensions, StronglyTypedId,
LanguageExt, CSharpFunctionalExtensions, ErrorOr, Ardalis.SmartEnum and Intellenum, with versions
and dates.

## Built with Metalama

TypeKit's value-object generation exists in this form because of
[Metalama](https://postsharp.net/metalama):

- **No attribute, nothing to register.** A transitive fabric finds every type that implements a
  TypeKit contract. It looks in the project that references `CodoMetis.TypeKit.Generators` and in
  every project that references that one. So you declare a value object by implementing its
  interface.
- **Generated code you can read.** Templates write the generated members as ordinary C#, and a build
  can write that code out for you
  ([Reading the generated code](src/CodoMetis.TypeKit.Generators/README.md#reading-the-generated-code)).
- **Loud failures.** A declaration that can't be generated fails the build with its name on it
  (CMTK1000 to CMTK1022). It never compiles to less than it appears to.
- **Analyzers in Razor.** Metalama runs the analyzers on the code it transformed, which is how they
  reach Razor components.

Metalama is in `CodoMetis.TypeKit.Generators` alone. `CodoMetis.TypeKit`, the analyzers and the four
other packages don't reference it, so a project that only wants `Option` and `Result` never gets it.
Metalama is open source under the MIT license, and building with it needs no license key, here or in
your projects. Thanks to the Metalama team.

## Status

2.0.0 is the current version. 1.0.0, released on 28 September 2026, was the first.
[Migrating from 1.5 to 2.0](docs/migrating-to-2.md) lists what builds or runs differently since 1.5.

All packages share one version and follow Semantic Versioning. The versioned surface is the public
API, the generated members of a value object, and the analyzer ids and severities. A new analyzer
rule arrives as a warning or a suggestion in a minor version, and becomes an error only in a major
one.

[CHANGELOG.md](CHANGELOG.md) lists what each version changed. The design notes, the decision records
and the roadmap are under [design/](design/plan.md). The measurements that decided the design are in
[spikes/](spikes/).

## Building

```bash
dotnet build CodoMetis.TypeKit.slnx
dotnet test --solution CodoMetis.TypeKit.slnx
```

The SDK is pinned in `global.json`. The PostgreSQL round-trip tests start a container, so they need
Docker. Everything else runs without it.

- [CONTRIBUTING.md](CONTRIBUTING.md) describes the quality bar.
- [SECURITY.md](SECURITY.md) says how to report a vulnerability.
- [AGENTS.md](AGENTS.md) is the guide for coding agents working in this repository.

Much of the code was written with AI assistance, under the maintainer's direction and review.
[CONTRIBUTING.md](CONTRIBUTING.md#ai-assisted-development) says what that does and doesn't mean.

## License

[MIT](LICENSE)
