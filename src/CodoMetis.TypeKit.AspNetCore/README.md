# CodoMetis.TypeKit.AspNetCore

ASP.NET Core integration for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit)
value objects: their schema in the OpenAPI document produced by `Microsoft.AspNetCore.OpenApi`.

**Status: in development.** This package does not contain the transformers yet. It is published
so the package map is complete; referencing it changes nothing in a host today.

## What it will do

A value object appears in the document as the schema of the type it wraps, format included, so an
`OrderId` is a `string` with `format: uuid` and a `Quantity` an `integer`, matching what the
generated JSON converter writes. That holds wherever the value object appears: as a property, as a
list element, as a dictionary value, and as a route or query parameter.

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());   // planned
```

Like the EF Core satellite, it works at run time against the `IValueObject<,>` interface, so the host
needs this package and the base package, not the generators.

## What already works without it

The generated members cover the request path on their own:

- **Route and query parameters** bind through the generated `IParsable`, with the invariant
  culture, and a refused value is a 400.
- **Request and response bodies** go through the generated JSON converter. A value a validated
  value object refuses is a `JsonException`, which minimal APIs and MVC answer with 400 and a
  message naming the type and the rule, never the value.
- **Model binding** in MVC also finds the generated `TypeConverter`.

Only the document is missing: without this package, `Microsoft.AspNetCore.OpenApi` cannot see
through the generated JSON converter, so the schema it publishes for a value object says nothing
about the uuid, number or date the API accepts.
