# ASP.NET Core

A value object binds from a route, a query, a header or a body through the members the generators
add, so an endpoint takes `CustomerId` where it took `Guid`, and every way in applies `Create`.
`CodoMetis.TypeKit.AspNetCore` adds what the host needs beyond that: the value object's schema in
the OpenAPI document, and a `Result` as the response. The
[package README](../src/CodoMetis.TypeKit.AspNetCore/README.md) is the reference for both; this page
follows a request through a minimal API and an MVC controller.

```csharp
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Http.HttpResults;
```

```csharp
public readonly partial record struct CustomerId : IValue<Guid>;

public enum EmailFault { NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value.Trim()) : Result.Error(EmailFault.NoAt);
}

public enum PageFault { NotPositive, TooLarge }

public readonly partial record struct PageSize : IValidatedValue<PageSize, int, PageFault>
{
    public static Result<PageSize, PageFault> Create(int value) =>
        value <= 0 ? Result.Error(PageFault.NotPositive) : value > 100 ? Result.Error(PageFault.TooLarge) : new PageSize(value);
}

public enum CustomerFault { NotFound, EmailTaken }

public sealed record RegisterCustomer(Email Email, string Name);

public sealed record CustomerDto(CustomerId Id, Email Email, string Name);
```

## Setup

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());          // the document: every value object as the schema of what it wraps

builder.Services.AddFaultResponses<CustomerFault>(fault => fault switch   // a Result as a response: the status per fault
{
    CustomerFault.NotFound => StatusCodes.Status404NotFound,
    CustomerFault.EmailTaken => StatusCodes.Status409Conflict,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});
```

Nothing is registered per value object, and the host does not reference the generators. The
`Microsoft.AspNetCore.OpenApi` reference has to be direct, at 10.0.12 or later
([the README says why](../src/CodoMetis.TypeKit.AspNetCore/README.md#setup)).

## Binding in a minimal API

```csharp
app.MapGet("/customers/{id}", (CustomerId id) => customers.Find(id).ToHttpResult());                  // a route parameter, through the generated TryParse
app.MapGet("/customers", (Email? email, PageSize? page) => customers.List(email, page));              // query parameters, optional
app.MapPost("/customers", (RegisterCustomer body) => customers.Register(body).ToHttpResult());        // a body, through the generated JSON converter
```

Route, query and header parameters bind through the generated `TryParse`, with the invariant
culture; a body through the generated JSON converter. A value object declared in the same project as
the endpoint trips ASP0020 on a route parameter, since the route analyzer reads the source before the
generators add `IParsable`; the binding is right, and the README shows the pragma that says so.

## What a refused value answers

| Request | Answer |
|---|---|
| `GET /customers/0199aaaa-…` | 200, or the problem the fault maps to |
| `GET /customers/not-an-id` | 400: `TryParse` refused the route value |
| `GET /customers?page=500` | 400: `Create` refused it, through `TryParse` |
| `POST /customers` with `"email": "nobody"` | 400: the `JsonException` names `Email` and `NoAt`, never the text, and is logged; the body is empty unless the host adds `UseStatusCodePages()` |
| `POST /customers` without `email` | 200 with a default `Email`, unless `RespectRequiredConstructorParameters` is set, then 400 |
| `GET /mvc/customers` without `page` | 400, since the action marks `page` `[BindRequired]`; without it, the action gets a default `PageSize` |

The last two rows are the ones to act on. System.Text.Json calls no converter for a property the body
lacks, so it stays `default`, an instance that passed no factory. The README's
[Binding](../src/CodoMetis.TypeKit.AspNetCore/README.md#binding) shows the two lines that make both
minimal APIs and MVC answer 400 instead.

MVC does the same with a query, header or form value the request lacks: for a non-nullable struct, a
struct value object as much as a `Guid` or an `int`, it binds nothing, keeps the model state valid
and calls the action with `default`. A minimal API answers 400, and MVC already requires a closed set
or a class value object, as any non-nullable reference type. Mark a struct value object's parameter
`[BindRequired]` (`Microsoft.AspNetCore.Mvc.ModelBinding`), as the controller below does for `page`,
and MVC answers 400; or make it nullable where it is optional.

## Binding in MVC

```csharp
[ApiController]
[Route("mvc/customers")]
public sealed class CustomersController(ICustomers customers) : ControllerBase
{
    [HttpGet("{id}")]
    public Results<Ok<CustomerDto>, FaultProblem<CustomerFault>> Get([FromRoute] CustomerId id) => customers.Find(id).ToHttpResult();   // the generated TypeConverter binds it

    [HttpGet]
    public IReadOnlyList<CustomerDto> List([FromQuery] Email? email, [FromQuery, BindRequired] PageSize page) => customers.List(email, page);   // without [BindRequired], no page is a default PageSize

    [HttpPost]
    public Results<Ok<CustomerDto>, FaultProblem<CustomerFault>> Post(RegisterCustomer body) => customers.Register(body).ToHttpResult();
}
```

MVC binds a route or query value object through the generated `TypeConverter`, and a body through
the JSON converter as a minimal API does. The difference is the 400: MVC answers a refused body with
its validation problem details, whose message is the `JsonException`'s, so the client reads
`Email refused the JSON value (NoAt)` and never the text. The one exception is a value object used
as a dictionary key: System.Text.Json puts the key in the `JsonException`'s `Path`, and MVC names the
error after it (`"$.items.SE"`), so keep a secret out of a dictionary's keys
([Logging and secrets](logging-and-secrets.md#refusals-never-quote-the-input)).

## A Result as a response

```csharp
public interface ICustomers
{
    Result<CustomerDto, CustomerFault> Find(CustomerId id);

    Result<CustomerDto, CustomerFault> Register(RegisterCustomer request);

    IReadOnlyList<CustomerDto> List(Email? email, PageSize? page);
}
```

`ToHttpResult()` answers a success with `200 OK` and its value, `204 No Content` for a
`Result<TError>`, and a fault with RFC 9457 problem details, whose status the host mapped in `Setup`
and whose `fault` member names the fault. The domain declares the fault type; only the host
references ASP.NET. A handler's outcome can be read without a host:

```csharp
var response = customers.Find(CustomerId.New()).ToHttpResult();

bool notFound = response.Result is FaultProblem<CustomerFault> { Fault: CustomerFault.NotFound };   // true
```

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Not Found", "status": 404, "fault": "NotFound", "traceId": "…" }
```

A host whose endpoint answers with a fault type nobody mapped does not start, and the exception names
the endpoint. `AddFaultResponses` calls `AddProblemDetails()`, so a `CustomizeProblemDetails` of the
host's own reaches these problems:

```csharp
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["service"] = "customers");
```

A response can carry more, each part opt-in, so the default above stays as it is: a `type` and a
`title` per fault, a closed set as the fault type, answered by its members' kinds and descriptions,
a sentence per occurrence in a `Fault<TFault>`, a status per endpoint, the endpoint's own success
such as a `201 Created`, and the fault on the request for a `CustomizeProblemDetails`. The README's
[Mapping faults to statuses](../src/CodoMetis.TypeKit.AspNetCore/README.md#mapping-faults-to-statuses)
and the sections after it show each.

## The document

With `AddTypeKit()`, every value object is the schema ASP.NET publishes for the type it wraps,
wherever it appears. One in a body keeps a component named after it; a route or query parameter is
described inline, as ASP.NET describes a parameter of the wrapped type, so the `id` below is a `uuid`
string with no component of its own. The fault responses are listed on every
operation that returns the fault type:

```json
"/customers/{id}": { "get": {
    "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ],
    "responses": {
        "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/CustomerDto" } } } },
        "404": { "content": { "application/problem+json": { "schema": { "$ref": "#/components/schemas/ProblemDetails" } } } },
        "409": { "content": { "application/problem+json": { "schema": { "$ref": "#/components/schemas/ProblemDetails" } } } } } } }
```

The `ProblemDetails` component those responses refer to describes `fault` too, as an optional string,
so a client generated from the document has the member it matches on.

A rule belongs in `Create`, not on the property: a validation attribute on a value-object property
lands in the value object's shared component and then applies to every other use.

## Native AOT

The transformer and `ToHttpResult()` are trim- and AOT-safe, and the smoke test publishes a host with
both. What the host's `JsonSerializerContext` has to list for the document, and for the problem
details, is in [Native AOT](native-aot.md#openapi).
