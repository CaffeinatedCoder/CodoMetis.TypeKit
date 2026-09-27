# CodoMetis.TypeKit

Strong types for .NET 10: `Option` and `Result` types that cannot be misused, and value objects
that are written as one line and generated at compile time.

[![.NET](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/actions/workflows/dotnet.yml/badge.svg)](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/actions/workflows/dotnet.yml)

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

## Packages

| Package | Role | Metalama |
|---|---|---|
| [CodoMetis.TypeKit](src/CodoMetis.TypeKit/README.md) | `Option<T>`, `Result<T, TError>`, `Result<TError>`, the value-object contracts, and the analyzers that guard them | no |
| [CodoMetis.TypeKit.Analyzers](src/CodoMetis.TypeKit.Analyzers/README.md) | CMTK0001 (no `default` of a value object, an `Option` or a `Result`) and CMTK0002 (a value object nobody generates). Arrives with the base package | no |
| [CodoMetis.TypeKit.Generators](src/CodoMetis.TypeKit.Generators/README.md) | The compile-time generation, in the project that declares value objects | yes |
| [CodoMetis.TypeKit.EntityFrameworkCore](src/CodoMetis.TypeKit.EntityFrameworkCore/README.md) | Value objects as columns with nothing registered per type, and `.Value` in LINQ | no |
| [CodoMetis.TypeKit.AspNetCore](src/CodoMetis.TypeKit.AspNetCore/README.md) | Value objects in the OpenAPI document, with the schema of the type they wrap | no |

The base package is the cheapest one: someone who only wants `Option` and `Result` never receives a
code generator by accident. Taking Metalama on is a named choice, made once, in the domain project,
and it needs no Metalama license key: the generators build on Metalama's Open Source edition, in this
repository and in yours.
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

created.Match(
    email => Send(email),
    fault => Reject(fault));

JsonSerializer.Serialize(known);                          // "ops@example.com"
JsonSerializer.Deserialize<Email>("\"nobody\"");          // JsonException naming Email and NoAt, never the text
Email.Parse("ops@example.com", null);                     // IParsable, so it binds as a route or query parameter
```

```csharp
services.AddDbContext<ShopDb>(options => options.UseNpgsql(connectionString).UseTypeKit());

db.Orders.Where(o => o.Customer.Email.Value.EndsWith("@example.com"));   // WHERE o."Email" LIKE '%@example.com'

services.AddOpenApi(options => options.AddTypeKit());                    // OrderId: {"type":"string","format":"uuid"}
```

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
  it. The fault you return from `Create` is what each of them reports: the JSON 400, the
  `FormatException` and the `FromKnownGood` exception all name your `EmailFault.NoAt`. The two
  validation-free paths, reading a database column and reading JSON the application stored itself,
  are explicit, named, and unreachable from input.
- **No factory throws on input by accident.** A validated value object has no `From`. Its ways in
  are `Create`, which returns a `Result`, `TryFrom`, which returns an `Option`, and `FromKnownGood`,
  whose name says the caller vouches for the value and whose exception blames the call site.
- **A refusal never echoes the input.** A JSON or parsing refusal names the type and the rule,
  `FromKnownGood` names the caller's expression, and `Option` and `Result` print nothing, so a value
  that is a secret cannot reach a message or a log through this package.
- **Loud failures.** A declaration that cannot be generated is a build error naming the
  declaration, CMTK1000 to CMTK1008, never a type with nothing in it. The analyzers make `default`
  of a value object, an `Option` or a `Result` an error.
- **Discovery by interface.** The EF Core and OpenAPI satellites recognise a value object by
  `IValueObject<,>`, never by a name, a namespace or an assembly prefix.

## Status

No version has been released yet. `Option`/`Result`, the contracts and analyzers, the generators,
the EF Core satellite, the OpenAPI satellite and the release pipeline are done; 0.1.0 is next. The
plan, the decisions and their evidence are in [docs/plan.md](docs/plan.md), and the measurements
that decided the design are in [spikes/](spikes/).

## Building

```bash
dotnet build CodoMetis.TypeKit.slnx
dotnet test --solution CodoMetis.TypeKit.slnx
```

The SDK is pinned in `global.json`. The PostgreSQL round-trip tests start a container, so they need
Docker; everything else runs without it. [CONTRIBUTING.md](CONTRIBUTING.md) describes the quality
bar, [SECURITY.md](SECURITY.md) how to report a vulnerability, and [AGENTS.md](AGENTS.md) is the
guide for coding agents working in this repository.

## License

[MIT](LICENSE)
