# CodoMetis.TypeKit

Strong types for .NET 10: `Option` and `Result` types that cannot be misused, and value objects and
closed sets that are written as one line and generated at compile time.

[![.NET](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/actions/workflows/dotnet.yml/badge.svg)](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/actions/workflows/dotnet.yml)
[![NuGet](https://img.shields.io/nuget/v/CodoMetis.TypeKit.svg)](https://www.nuget.org/packages/CodoMetis.TypeKit)
[![Built with Metalama](https://img.shields.io/badge/built_with-Metalama-informational)](#built-with-metalama)

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

Beside the generated types, the base package holds:

- `Patch<T>`, a field of a JSON Merge Patch: absent, null or a value.
- `Result.Catch`, which turns an exception into a result.
- `AsText()` and `AsIdentifier()`, for text a person wrote and a value another system issued.
- `[Sealed]`, `[Concrete]` and `[RequireParameterlessConstructor]`, constraints C# has no `where`
  for, checked by the analyzers.

## Packages

| Package | Role | Metalama |
|---|---|---|
| [CodoMetis.TypeKit](src/CodoMetis.TypeKit/README.md) | `Option<T>`, `Result<T, TError>`, `Result<TError>`, the value-object contracts, the text guards `AsText()` and `AsIdentifier()`, and the analyzers that guard them | no |
| [CodoMetis.TypeKit.Analyzers](src/CodoMetis.TypeKit.Analyzers/README.md) | CMTK0001–CMTK0012: no `default` of a value object, an `Option` or a `Result`, written in source or handed out by `FirstOrDefault` and its kind; no value object nobody generates; no ignored result; no validation bypass; no comparing two value objects' values; no type argument that breaks what its parameter declares with `[Abstract]`, `[Sealed]`, `[Record]` or `[Concrete]`; no type without the constructor its host will look for; no closed-set `Match` bound by position. In Razor components too. Arrives with the base package | no |
| [CodoMetis.TypeKit.Generators](src/CodoMetis.TypeKit.Generators/README.md) | The compile-time generation, in the project that declares value objects | yes |
| [CodoMetis.TypeKit.EntityFrameworkCore](src/CodoMetis.TypeKit.EntityFrameworkCore/README.md) | Value objects as columns with nothing registered per type, and `.Value` in LINQ | no |
| [CodoMetis.TypeKit.AspNetCore](src/CodoMetis.TypeKit.AspNetCore/README.md) | Value objects in the OpenAPI document, with the schema of the type they wrap; a `Result` as the response, a fault as problem details | no |
| [CodoMetis.TypeKit.Serilog](src/CodoMetis.TypeKit.Serilog/README.md) | Value objects logged as what they print, also where Serilog destructures them, so a value masked by its `ToString()` stays masked | no |
| [CodoMetis.TypeKit.Testing](src/CodoMetis.TypeKit.Testing/README.md) | Test assertions for `Option`, `Result` and `Patch<T>` that hand back the value or the error, and fail saying what the outcome was. No test framework dependency | no |

The base package is the cheapest one: someone who only wants `Option` and `Result` never receives a
code generator by accident. Taking Metalama on is a named choice, made once, in the domain project.
The EF Core and ASP.NET Core packages work at run time against the interfaces, so a host that maps
value objects from a referenced domain assembly needs no generator itself.

## Quick start

```bash
dotnet add package CodoMetis.TypeKit.Generators      # the domain project: brings CodoMetis.TypeKit along
dotnet add package CodoMetis.TypeKit.EntityFrameworkCore   # the data project, if there is one
```

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

```csharp
services.AddDbContext<ShopDb>(options => options.UseNpgsql(connectionString).UseTypeKit());

db.Customers.Where(c => c.Email.Value.EndsWith("@example.com"));        // WHERE c."Email" LIKE '%@example.com'

services.AddOpenApi(options => options.AddTypeKit());                    // OrderId: {"type":"string","format":"uuid"}
```

## Documentation

The package READMEs above are the reference for each package: what it adds, its setup, and the
rules it holds to, which is also where every exception message and analyzer help link lands. The
pages below are the guides, one topic each, written around examples. Every C# block in them is
compiled and its stated outputs are tested, as the READMEs' are.

| Page | Covers |
|---|---|
| [Value objects](docs/value-objects.md) | Declaring one, struct or class, identifiers, validated text and numbers, faults, which factory to call, your own members, generic code over value objects |
| [Closed sets](docs/closed-sets.md) | Smart enums: declaring one, data on its members, `Match`, JSON and dictionary keys, EF Core columns, configuration, logging, what the generators refuse, when an `enum` is still right |
| [Option](docs/option.md) | Absence as a type: creating, matching, combining, converting at the boundary, and the complete public surface |
| [Result](docs/result.md) | Outcomes as a type: faults, pipelines, async chains, collecting results, an exception as an outcome, errors across layers, and the complete public surface |
| [Written text](docs/text.md) | `AsText()` and `AsIdentifier()`: what each cleans and refuses, a value object over either, a required and an optional field |
| [Type constraints](docs/type-constraints.md) | `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` on a type parameter, `[RequireParameterlessConstructor]` and its code fix |
| [Merge patches](docs/merge-patches.md) | `Patch<T>`: absent, null or a value, applying a patch, value objects inside one, writing one, the OpenAPI document, source-generated contexts |
| [JSON](docs/json.md) | What a value object writes and reads, refusals, stored documents, `Option` and `Result` at the wire, a converter of your own, source-generated contexts |
| [EF Core](docs/ef-core.md) | Columns, keys, conventions, queries and the SQL they become, seed data, reading back without validation, SQLite in tests |
| [ASP.NET Core](docs/aspnet-core.md) | Binding in minimal APIs and MVC, what a refused value answers, a `Result` as a response, fault responses a client can act on, the OpenAPI document, problem details |
| [Native AOT](docs/native-aot.md) | What is measured, the project settings, JSON contexts, OpenAPI, EF Core's compiled model, configuration binding, discovery without reflection |
| [Logging and secrets](docs/logging-and-secrets.md) | A value that must not be printed, what still carries it, Serilog, Microsoft.Extensions.Logging, refusals that never quote the input |
| [Testing](docs/testing.md) | Assertions for `Option`, `Result` and `Patch<T>`, table-driven tests of `Create`, EF Core on SQLite, endpoints on the test server |
| [Migrating from 1.5 to 2.0](docs/migrating-to-2.md) | What builds or runs differently: CMTK0003 as an error, a query's `FirstOrNone` in the database, names that 2.0 adds, fault responses, Native AOT |
| [How it compares](docs/comparison.md) | What to choose instead for a need TypeKit does not meet, and the detail tables against the value-object, result and smart-enum libraries, versioned and dated |

## What it holds to

- **No `.Value` on `Option` or `Result`.** The content is reached through `Match`, `TryGetValue`
  and the combinators, so absence and failure are handled where the value is used. A `default`
  `Option` is `None`; a `default` `Result` is uninitialized and every branching member throws on
  it rather than inventing a `default(TError)`. `ToString()` never prints the content.
- **Not wire types.** Serializing an `Option` or a `Result` with System.Text.Json throws
  `NotSupportedException`, in both directions, instead of writing `{}` that reads back as `None`. A
  serialized shape says absent with a nullable, `ToOption()` and `OrNull()` convert at the boundary,
  and a converter you register on the options takes precedence if you want a wire format of your own.
- **One rule set per validated value object.** `Create` is the only factory written by hand, and
  the generated `TryFrom`, `FromKnownGood`, JSON converter, parsing and type converter all apply
  it. The fault you return from `Create` is what each of them reports: the `JsonException`, the
  `FormatException` and the `FromKnownGood` exception all name your `EmailFault.NoAt`. The two
  validation-free paths are explicit and named: the EF Core satellite reads a database column
  through the materializer, and `StoredJsonConverterFactory` reads JSON the application stored
  itself. Register that factory only on the options you read your own JSON with, never on those
  that read a request.
- **No factory throws on input by accident.** A validated value object has no `From`. Its ways in
  are `Create`, which returns a `Result`, `TryFrom`, which returns an `Option`, and `FromKnownGood`,
  whose name says the caller vouches for the value and whose exception blames the call site.
- **A refusal never echoes the input.** A JSON or parsing refusal names the value object and the
  rule, or the wrapped type it could not read, and carries no inner exception that quotes the text;
  `FromKnownGood` names the caller's expression, and `Option` and `Result` print nothing. So a value
  that is a secret does not reach a message or a log through this package, as long as your fault
  type does not carry it.
- **Swapping a primitive for a value object keeps the contract.** Its JSON is the wrapped type's
  under your options; its column is the wrapped type's, a key over an integer still an identity
  column; its OpenAPI schema is the one ASP.NET publishes for the wrapped type. What changes is what
  the value object refuses, a JSON `null` for a struct value object included, and under a
  source-generated context an enum-backed value object needs its enum listed
  ([JSON](src/CodoMetis.TypeKit.Generators/README.md#json)).
- **Loud failures.** A declaration that cannot be generated is a build error naming the
  declaration, CMTK1000 to CMTK1022, never a type with nothing in it. The analyzers make `default`
  of a value object, an `Option` or a `Result` an error.
- **Discovery by interface.** The EF Core and OpenAPI satellites recognise a value object by the
  attribute the generators put beside `IValueObject<,>`, whose type arguments are constrained to that
  interface. Renaming a type or a namespace changes nothing, and trimming keeps the attribute.
- **Native AOT.** The run-time packages build with the trim and AOT analyzers on, and a consumer
  with value objects, source-generated JSON, OpenAPI and EF Core is published with Native AOT and run
  on every CI build. Each package's README says what, if anything, Native AOT asks of you.

## How it compares

Good libraries exist for both halves of TypeKit. TypeKit fits when:

- You build on .NET 10 with System.Text.Json, EF Core and ASP.NET Core, and want value objects that
  change no contract: the JSON, the column and the OpenAPI schema stay the wrapped type's, with nothing
  registered per type, and `.Value` works inside a LINQ query.
- You want one rule set per value object: the `Create` you write is what JSON, parsing, route binding,
  `TryFrom` and `FromKnownGood` apply, and a value object is declared by implementing an interface
  that generic code and the satellites can use, not by an attribute.
- You want an `Option` and a `Result` that cannot be misused: no `.Value`, no `default` in source, an
  ignored result reported, no silent `{}` on the wire.
- You publish with Native AOT.

What to choose instead for the needs TypeKit does not meet, and the detail tables against Vogen,
Thinktecture.Runtime.Extensions, StronglyTypedId, LanguageExt, CSharpFunctionalExtensions, ErrorOr,
Ardalis.SmartEnum and Intellenum, versioned and dated, are on [How it compares](docs/comparison.md).

## Built with Metalama

The value-object generation exists in this form because of [Metalama](https://postsharp.net/metalama).
A transitive fabric finds every type that implements a TypeKit contract, in the project that
references `CodoMetis.TypeKit.Generators` and in every project that references that one, so a value
object is declared by implementing its interface, with no attribute and nothing registered.
Templates write the generated members as ordinary C#, which a build can write out for you to read
([Reading the generated code](src/CodoMetis.TypeKit.Generators/README.md#reading-the-generated-code)).
A declaration that cannot be generated fails the build with its name on it, CMTK1000 to CMTK1022,
instead of compiling to less than it appears to. And Metalama runs the analyzers on the code it
transformed, which is how they reach Razor components.

Metalama is in `CodoMetis.TypeKit.Generators` alone. `CodoMetis.TypeKit`, the analyzers and both
satellites do not reference it, so a project that only wants `Option` and `Result` never receives
it. It is open source under the MIT license, and building with it needs no license key, here or in
your projects. Thanks to the Metalama team.

## Status

2.0.0 is the current version; 1.0.0, of 28 September 2026, was the first release, and
[Migrating from 1.5 to 2.0](docs/migrating-to-2.md) lists what builds or runs differently since 1.5.
The packages share one version and follow Semantic Versioning:
the public API, the generated members of a value object, and the analyzer ids and severities are
the contract. A new analyzer rule arrives as a warning or a suggestion in a minor version, and
becomes an error only in a major one.
[CHANGELOG.md](CHANGELOG.md) lists what each version changed. The design notes, the decision records
and the roadmap are under [design/](design/plan.md), and the measurements that decided the design are
in [spikes/](spikes/).

## Building

```bash
dotnet build CodoMetis.TypeKit.slnx
dotnet test --solution CodoMetis.TypeKit.slnx
```

The SDK is pinned in `global.json`. The PostgreSQL round-trip tests start a container, so they need
Docker; everything else runs without it. [CONTRIBUTING.md](CONTRIBUTING.md) describes the quality
bar, [SECURITY.md](SECURITY.md) how to report a vulnerability, and [AGENTS.md](AGENTS.md) is the
guide for coding agents working in this repository. Much of the code was written with AI assistance,
under the maintainer's direction and review; [CONTRIBUTING.md](CONTRIBUTING.md#ai-assisted-development)
says what that does and does not mean.

## License

[MIT](LICENSE)
