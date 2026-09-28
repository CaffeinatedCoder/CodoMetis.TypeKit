# CodoMetis.TypeKit.AspNetCore

ASP.NET Core integration for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit)
value objects: in the OpenAPI document produced by `Microsoft.AspNetCore.OpenApi`, every value
object has the schema of the type it wraps, wherever it appears.

```bash
dotnet add package Microsoft.AspNetCore.OpenApi     # AddOpenApi itself; the webapi template has it already
dotnet add package CodoMetis.TypeKit.AspNetCore
```

It recognises a value object at run time by the attribute the generators put on it, so the host
needs this package and the base package, not the generators. The domain project that declares the
value objects references `CodoMetis.TypeKit.Generators`.

## Setup

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());
```

`AddTypeKit()` adds one schema transformer, and names the component of a nested value object (see
below). Nothing is registered per type and no assembly is scanned, and its position among the
document's other transformers does not matter. It lives in
`Microsoft.Extensions.DependencyInjection`, beside `AddOpenApi`, which a web project imports
implicitly.

The host references `Microsoft.AspNetCore.OpenApi` directly. This package depends on it, but its
source generator's switch is imported for a direct reference only, and a host that has the package
only through this one fails with CS9137 ("the feature 'Interceptors' is not enabled").

## What the document says

```csharp
public readonly partial record struct OrderId : IValue<Guid>;
public readonly partial record struct Quantity : IValue<int>;

app.MapGet("/orders/{id}", (OrderId id, Quantity? limit) => orders.Find(id, limit));   // returns OrderDto
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

and the `id` and `limit` parameters are a uuid string and an integer. Without the package, `OrderId`
is the empty schema `{}`, the list and the dictionary lose their element schema (so `Quantity` gets
no component at all), and both parameters are a bare `string`.

- **The wrapped type's schema is ASP.NET's own**, so it follows the host's JSON options exactly
  as the generated converter does. Under the web defaults a number also accepts a quoted number
  (`"5"`), which is what the `integer | string` above says, and the converter reads it. With
  `JsonStringEnumConverter` an enum-backed value object is its names.
- **Each value object keeps its own component**, named after it, so the document still says
  `OrderId` where the API means an order id. A value object nested in another type is named after
  the whole chain, `Shop.Id` as `ShopId`, so `Shop.Id` and `Stock.Id` do not share one. A name your
  own `CreateSchemaReferenceId` gives, set before `AddTypeKit()`, is kept. A host that prefers value
  objects inlined returns `null` for them, the nullable ones included, after `AddTypeKit()`:

  ```csharp
  using System.Reflection;
  using CodoMetis.TypeKit.CompilerServices;   // GeneratedValueObjectAttribute, on every value object

  options.AddTypeKit();
  options.CreateSchemaReferenceId = type =>
      (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
          ? null
          : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
  ```

- **Rules belong in `Create`, not on the property.** A validation attribute on a value-object
  property (`[MaxLength]`, `[Range]`) lands in the value object's shared component, as ASP.NET does
  for any referenced schema, so it then applies to every other use too. `Create` is where every way
  in applies the rule anyway.
- **Everywhere it appears**: a property, a nullable property, a request or response body, the
  elements of a list, an array, a set or a nested container, a dictionary value, and a route,
  query or header parameter, in minimal APIs and in MVC, in OpenAPI 3.1 and 3.0. A nullable
  element (`List<Quantity?>`, `Dictionary<string, Quantity?>`) is the component or null, as a
  nullable property is.
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
app.MapGet("/orders/{id}", (OrderId id) => orders.Find(id));
#pragma warning restore ASP0020
```

A `DiagnosticSuppressor` cannot do this for you: suppressors do not run in a project the generators
compile.

## Native AOT

The transformer is trim- and AOT-safe. With source-generated JSON, which Native AOT requires, the
host's `JsonSerializerContext` lists every type its value objects wrap: ASP.NET builds a value
object's schema from its JSON contract for the wrapped type, and a context has none for a type the
host never serializes itself.

```csharp
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(Guid))]   // what OrderId wraps
[JsonSerializable(typeof(int))]    // what Quantity wraps
internal partial class AppJson : JsonSerializerContext;

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default));
```

Without one, generating the document fails with an `InvalidOperationException` that names the
value object and the `[JsonSerializable]` to add.

## Binding

The generated members cover the request itself:

- **Route, query and header parameters** bind through the generated `TryParse` in minimal APIs and
  through the generated `TypeConverter` in MVC, with the invariant culture, and a value that is
  refused is a 400. The text of that 400 is ASP.NET's own: MVC's "The value '…' is not valid." quotes
  the input, as it does for a `Guid`.
- **Request and response bodies** go through the generated JSON converter. A value a validated
  value object refuses is a `JsonException` naming the type and the rule, never the value, and a
  400. MVC puts that message in its validation problem details. A minimal API answers with an empty
  400 and logs the message; `AddProblemDetails()` gives the response a body.
- **A property missing from the body is not refused.** System.Text.Json calls no converter for an
  absent property, so it stays `default`: a `Guid.Empty` order id, or a validated value object that
  never passed `Create`. Where a value object is required, say so, with `required` on the property or,
  for a record's constructor parameters, with `RespectRequiredConstructorParameters`:

  ```csharp
  builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.RespectRequiredConstructorParameters = true);
  builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.RespectRequiredConstructorParameters = true);
  ```

  Both minimal APIs and MVC then answer a body without it with 400. An optional constructor
  parameter (`Discount? Discount`) then needs a default value (`= null`).
