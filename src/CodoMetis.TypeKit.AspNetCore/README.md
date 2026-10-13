# CodoMetis.TypeKit.AspNetCore

ASP.NET Core integration for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit):
in the OpenAPI document produced by `Microsoft.AspNetCore.OpenApi`, every value object has the schema
of the type it wraps, wherever it appears; and a `Result` becomes the endpoint's response, a fault as
problem details ([A Result as a response](#a-result-as-a-response)).

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

`AddTypeKit()` adds a schema transformer each for value objects, closed sets and merge patches, an
operation transformer for the fault responses, and names the component of a nested value object or
closed set (see below). Nothing is registered per type and no assembly is scanned, and its position among the
document's other transformers does not matter. It lives in
`Microsoft.Extensions.DependencyInjection`, beside `AddOpenApi`, which a web project imports
implicitly.

`AddTypeKit()` configures the one document it is called for, and a document without it describes
every value object as `{}`. A host with several documents calls it in each `AddOpenApi`, or once for
all of them:

```csharp
using Microsoft.AspNetCore.OpenApi;   // OpenApiOptions

builder.Services.ConfigureAll<OpenApiOptions>(options => options.AddTypeKit());
```

Calling it again for a document that already has it changes nothing.

The host references `Microsoft.AspNetCore.OpenApi` directly, at 10.0.12 or later. This package
depends on that version, and an earlier direct reference fails to restore with NU1605 (a package
downgrade). The reference has to be direct: its source generator's switch is imported for a direct
reference only, and a host that has the package only through this one fails with CS9137 ("the
feature 'Interceptors' is not enabled").

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
  the whole chain, `Shop.Id` as `ShopId`, so `Shop.Id` and `Stock.Id` do not share one; a nested
  closed set too, `Order.Status` as `OrderStatus`. Where that name is another type's as well, a
  top-level `OrderStatus` beside `Order.Status`, from any assembly, generating the document throws an
  `InvalidOperationException` naming both, rather than describing both as one. Two value
  objects of one name in different namespaces (`Billing.InvoiceNo`, `Shipping.InvoiceNo`) do share
  one, as ASP.NET does for any two types of one name, and it describes both as the first it meets.
  A name your own `CreateSchemaReferenceId` gives, set before `AddTypeKit()`, is kept, so give one of
  them its own name there, or rename one. A host that prefers value objects inlined returns `null` for them, the
  nullable ones included, after `AddTypeKit()`:

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
  nullable property is. A parameter is what ASP.NET makes of a parameter of the wrapped type: its
  schema for a number, a `Guid`, a date or an enum, and a bare `string` for any other parsable type,
  such as NodaTime's `Instant` or a range, whatever its JSON schema or another transformer says.
- **Other transformers compose.** If the document has a transformer for a wrapped type, such as
  one that describes NodaTime's `Instant` as a date-time, a value object wrapping `Instant` gets
  that description too, whichever of the two was added first.
- **What is already said is kept.** Whatever a value object's schema already says, from ASP.NET or
  another transformer, is not overwritten; only what it leaves open is filled in.

**A merge patch.** A property typed `Patch<T>` (CodoMetis.TypeKit, a JSON Merge Patch field) is
described as `T`'s schema:

- inlined where the property is, rather than as a `PatchOfstring` component;
- never required, since absent is a state;
- admitting null, since null is the clear: beside the type, in an enum's list of values, or as a
  branch beside the argument's own.

A value object inside a patch is described through the type it wraps, which is also what a
source-generated host can do: the patch's converter hides the value object from its context.

## A Result as a response

`ToHttpResult()` turns a `Result` into what an endpoint returns: a success as `200 OK` with its value
(`204 No Content` for a `Result<TError>`), a fault as RFC 9457 problem details with the status the
host maps it to.

```csharp
public enum OrderFault { NotFound, AlreadyShipped }

builder.Services.AddFaultResponses<OrderFault>(fault => fault switch
{
    OrderFault.NotFound       => StatusCodes.Status404NotFound,
    OrderFault.AlreadyShipped => StatusCodes.Status409Conflict,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});

app.MapGet("/orders/{id}", (OrderId id) => orders.Find(id).ToHttpResult());      // Result<OrderDto, OrderFault>
app.MapDelete("/orders/{id}", (OrderId id) => orders.Cancel(id).ToHttpResult()); // Result<OrderFault>
```

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Conflict", "status": 409, "fault": "AlreadyShipped", "traceId": "…" }
```

- **The domain declares faults, the host their statuses.** The fault type is an enum or a closed set
  the domain owns; only the host references ASP.NET. The package adds nothing of the request to a
  problem: it names the fault, and a `Fault<TFault>`'s detail is your sentence ([below](#a-sentence-per-occurrence)).
- **The document says it too.** The endpoint's return type is ASP.NET's own
  `Results<Ok<OrderDto>, FaultProblem<OrderFault>>`, so the operation describes the value, and
  `AddTypeKit()` adds a problem response for every status the fault type is mapped to, as
  `ProducesProblem` would. An MVC action returning the same type is described the same way.
- **Not a wire format.** The `Result` is matched to a response, not serialized: returning the
  `Result` itself from a handler is still refused, and the document would promise a `Result`.
- **The host's problem details apply.** `AddFaultResponses` calls `AddProblemDetails()`, so a
  `CustomizeProblemDetails` of your own reaches these problems, and a host on source-generated JSON
  can write them.
- **Declare the return type.** ASP.NET reads an endpoint's responses from its declared return type,
  as for its own typed results: a handler typed `IResult` answers the same, but its operation lists no
  problem response, and the startup check cannot see which fault type it reports.
- A handler's test can read the outcome without a host:
  `Find(id).ToHttpResult().Result is FaultProblem<OrderFault> { Fault: OrderFault.NotFound }`.

### Mapping faults to statuses

`AddFaultResponses` maps every member of the fault type when it is called, so a mapping that throws for a
member, or gives one a status that is no error (outside 400 to 599), fails there, at startup. That is
what the discard arm above relies on: a member the `switch` misses throws while the host starts, not
when the fault first occurs. Without the arm, C# warns (CS8524) that a cast could produce a value no
member names. A fault type is mapped once.

A host with an endpoint whose fault type is not mapped does not start: an
`InvalidOperationException` names the fault type and each endpoint that answers with it. The check
comes with the first `AddFaultResponses` call. A host that never calls it throws the same exception
when an endpoint first reports a fault, and when its OpenAPI document is generated. A value no member names, which only a cast makes, is refused rather than
answered with a status the document does not list.

### A response per fault

A mapping can say more than a status: RFC 9457 makes the problem's `type` its primary identifier,
which a client matches on, and `title` its summary. Both name the problem type and nothing of the
request; without them, ASP.NET writes the status's own.

```csharp
builder.Services.AddFaultResponses<PaymentFault>(fault => fault switch
{
    PaymentFault.Declined  => FaultResponse.Status(StatusCodes.Status402PaymentRequired).WithType(new Uri("https://example.com/problems/declined")),
    PaymentFault.Duplicate => StatusCodes.Status409Conflict,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});
```

A bare status converts to a `FaultResponse`, so the arms that need nothing else stay a number;
`WithTitle("…")` sets the `title` the same way. Every member is still mapped and checked when this
is called. The document's problem response for a
status lists the problem types answered with it in its description:
`Payment Required. Problem types: https://example.com/problems/declined`.

### A closed set as the fault type

A closed set whose members are faults declares `IFaultSet<TSelf>`: each member carries a `FaultKind`,
nothing HTTP-specific, and optionally a sentence a person reads.

```csharp
public sealed partial class AccountFault : IFaultSet<AccountFault>
{
    public static readonly AccountFault NotFound = new(FaultKind.NotFound, "The account does not exist.");
    public static readonly AccountFault EmailTaken = new(FaultKind.Conflict, "Another account uses that email address.");
    public static readonly AccountFault Locked = new(FaultKind.Forbidden, null);   // a refusal that must not explain itself

    private AccountFault(FaultKind kind, string? description) { Kind = kind; Description = description; }

    public FaultKind Kind { get; }
    public string? Description { get; }
}
```

```csharp
builder.Services.AddFaultResponses<AccountFault>();   // every member by its kind, its description as the detail
```

A kind gives the status: `NotFound` 404, `Conflict` 409, `Invalid` 422, `Forbidden` 403 and
`Unavailable` 503. A member whose kind is `default`, which is no kind, fails this call. The
description becomes the problem's `detail`, so a client can show it; a member without one has no
`detail`. To change some members, map them all, with the rest as their kind says:
`fault => fault == AccountFault.Locked ? 423 : FaultResponse.For(fault.Kind)`. Any closed set can be a
fault type with a mapping of its own, `AddFaultResponses<TSet>(fault => …)`, and `ToHttpResult()`
takes a result over either.

### A sentence per occurrence

What varies per occurrence travels in a `Fault<TFault>` (CodoMetis.TypeKit): the fault, and a sentence
about this one. The response, the mapping and the document stay keyed on the fault type.

```csharp
Result<Account, Fault<AccountFault>> taken = Result.Error(Fault.Of(AccountFault.EmailTaken, "The address has been in use since 3 May."));
```

```csharp
app.MapPut("/accounts/{id:int}/email", (int id, NewAccount body) => accounts.Rename(id, body.Email).ToHttpResult());
```

The detail is the problem's `detail`, in place of the member's description. It is the application's
sentence, written as it is: a fault's name never quotes the request, and whether a detail does is the
application's to decide.

### A status per endpoint

The same fault can mean 404 on one route and 409 on another. An endpoint changes the status of one
fault for itself, in its response and in its document entry:

```csharp
app.MapPost("/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult())
   .WithFaultStatus(AccountFault.EmailTaken, StatusCodes.Status422UnprocessableEntity);
```

An MVC action says it with an attribute, naming the member, since an attribute takes only constants:

```csharp
[HttpPost("/mvc/accounts")]
[FaultStatus<AccountFault>(nameof(AccountFault.EmailTaken), StatusCodes.Status422UnprocessableEntity)]
public Results<Ok<Account>, FaultProblem<AccountFault>> Open(NewAccount body) => accounts.Open(body).ToHttpResult();
```

On a controller class, the attribute applies to every action of it. A status outside 400 to 599 is
refused where it is set. A status for a fault type the endpoint does not answer with, or an attribute
naming no member, stops the host from starting, naming the endpoint.

### The success the endpoint chooses

A success is `200 OK` with its value unless the endpoint says otherwise, with any of ASP.NET's typed
results, so the document still describes it:

```csharp
app.MapPost("/v2/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult(account => TypedResults.Created($"/accounts/{account.Id}", account)));
```

The operation lists `201` with the account as its body, beside the problem responses, and the
response carries the `Location` header. A result of the application's own, one that sets an ETag,
takes the same place. A `Result<TError>` takes a factory without a value, `ToHttpResult(() =>
TypedResults.Accepted("/jobs/7"))`.

### What the host adds to a problem

While `ToHttpResult()` writes a problem, the request carries an `IFaultFeature`: the fault type, the
fault, the name the problem gives it and its detail. A `CustomizeProblemDetails` of the host's reads
it to add what the package does not name, and finds it on no other problem the request writes.

```csharp
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.HttpContext.Features.Get<IFaultFeature>() is { } fault)
        context.ProblemDetails.Extensions["code"] = $"ACC-{fault.Name}";   // the host's own extension, for this fault only
});
builder.Services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);   // "fault": "AccountFault.EmailTaken"
```

`QualifiedFaultNames` writes `fault` with its type, so two fault types that share a member name stay
apart in one client; the default is the bare name. A nested type is qualified after its nesting chain,
an enum as a closed set's `QualifiedName` is, so `Billing.Fault` and `Shipping.Fault` give
`Billing.Fault.NotFound` and `Shipping.Fault.NotFound`.

The document describes `fault` as an optional string on the problem schema the fault responses refer
to, so a generated client has the member to match on; its description says which of the two forms
this host writes. That schema is the shared `ProblemDetails` component, which ASP.NET's own problem
responses refer to as well, so the description also says that only a problem answering a fault
carries it. It stays a string, since an enum of every fault type would change every operation when
one is added. A document without a fault response keeps the schema as ASP.NET writes it.

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
  400 and logs the message, with `AddProblemDetails()` too; `app.UseStatusCodePages()` beside it gives
  the response a generic problem body, which names neither the property nor the rule.
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
- **A query, header or form value missing from an MVC request is not refused either.** MVC binds
  nothing for a non-nullable struct and calls the action with `default`, as it does for a `Guid` or
  an `int`, and the model state stays valid; for a struct value object that is again an instance
  that passed no factory. A minimal API answers 400, and MVC already requires a class value object
  or a closed set, as it does any non-nullable reference type. Mark the parameter `[BindRequired]`,
  and MVC answers 400 ("A value for the 'id' parameter or property was not provided."), or make it
  nullable where it is optional, and the action receives `null`:

  ```csharp
  using Microsoft.AspNetCore.Mvc.ModelBinding;   // BindRequired

  [HttpGet("/mvc/orders")]
  public OrderDto Find([FromQuery, BindRequired] OrderId id, [FromQuery] Quantity? limit) => orders.Find(id, limit);
  ```
