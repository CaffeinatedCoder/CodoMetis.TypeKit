# CodoMetis.TypeKit.AspNetCore

ASP.NET Core integration for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit)
value objects: in the OpenAPI document produced by `Microsoft.AspNetCore.OpenApi`, every value
object has the schema of the type it wraps, wherever it appears.

```bash
dotnet add package CodoMetis.TypeKit.AspNetCore
```

It works at run time against the `IValueObject<,>` interface, so the host needs this package and
the base package, not the generators. The domain project that declares the value objects
references `CodoMetis.TypeKit.Generators`.

## Setup

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());
```

`AddTypeKit()` adds one schema transformer. Nothing is registered per type and no assembly is
scanned, and its position among the document's other transformers does not matter.

## What the document says

```csharp
public readonly partial record struct OrderId : IValue<Guid>;
public readonly partial record struct Quantity : IValue<int>;

app.MapGet("/orders/{id}", (OrderId id, Quantity? limit) => …);   // returns OrderDto
public sealed record OrderDto(OrderId Id, List<OrderId> Related, Dictionary<string, Quantity> PerWarehouse);
```

```json
"OrderId":  { "type": "string", "format": "uuid" },
"Quantity": { "type": ["integer", "string"], "pattern": "^-?(?:0|[1-9]\\d*)$", "format": "int32" },
"OrderDto": { "properties": {
    "id":           { "$ref": "#/components/schemas/OrderId" },
    "related":      { "type": "array", "items": { "$ref": "#/components/schemas/OrderId" } },
    "perWarehouse": { "type": "object", "additionalProperties": { "$ref": "#/components/schemas/Quantity" } } } }
```

and the `id` and `limit` parameters are a uuid string and an integer. Without the package, both
components are the empty schema `{}`, the list and the dictionary lose their element schema, and
both parameters are a bare `string`.

- **The wrapped type's schema is ASP.NET's own**, so it follows the host's JSON options exactly
  as the generated converter does. Under the web defaults a number also accepts a quoted number
  (`"5"`), which is what the `integer | string` above says, and the converter reads it. With
  `JsonStringEnumConverter` an enum-backed value object is its names.
- **Each value object keeps its own component**, named after it, so the document still says
  `OrderId` where the API means an order id. A host that prefers value objects inlined returns
  `null` for them from `OpenApiOptions.CreateSchemaReferenceId`.
- **Everywhere it appears**: a property, a nullable property, a request or response body, the
  elements of a list, an array, a set or a nested container, a dictionary value, and a route,
  query or header parameter, in minimal APIs and in MVC, in OpenAPI 3.1 and 3.0.
- **Other transformers compose.** If the document has a transformer for a wrapped type, such as
  one that describes NodaTime's `Instant` as a date-time, a value object wrapping `Instant` gets
  that description too, whichever of the two was added first.
- **What is already said is kept.** Whatever a value object's schema already says, from ASP.NET or
  another transformer, is not overwritten; only what it leaves open is filled in.

## NodaTime

ASP.NET cannot see through NodaTime's JSON converters, so once they are on the host's JSON options
it describes a NodaTime type as `{}`, and so, through this package, a value object wrapping one. A host that serves NodaTime values
configures NodaTime's converters on its JSON options and adds a schema transformer for the NodaTime
types it uses; that transformer then describes the value objects as well.

## A route parameter declared in the same project

A value object used as a **minimal-API route parameter**, in the project that also declares the
value object, fails the build with ASP0020 ("should define a bool TryParse … or implement
IParsable"). ASP.NET's route analyzer reads the source before the generators add `IParsable`, so
it sees the type unfinished. The request delegate generator and the running application both see
the generated `TryParse`, and the binding is correct.

The error does not occur for a value object declared in a project the endpoints reference, for a
query parameter, or in MVC. Where it does, suppress it at the endpoint:

```csharp
#pragma warning disable ASP0020 // OrderId implements IParsable once generated
app.MapGet("/orders/{id}", (OrderId id) => …);
#pragma warning restore ASP0020
```

A `DiagnosticSuppressor` cannot do this for you: suppressors do not run in a project the generators
compile.

## Binding

The generated members cover the request itself:

- **Route, query and header parameters** bind through the generated `IParsable`, with the
  invariant culture, and a value that is refused is a 400.
- **Request and response bodies** go through the generated JSON converter. A value a validated
  value object refuses is a `JsonException`, which minimal APIs and MVC answer with 400 and a
  message naming the type and the rule, never the value.
- **Model binding** in MVC also finds the generated `TypeConverter`.
