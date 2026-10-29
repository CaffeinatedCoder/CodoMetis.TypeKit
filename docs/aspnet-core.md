# ASP.NET Core

Your endpoints can take value objects as parameters and return a `Result`. An endpoint that took a
`Guid` takes a `CustomerId` instead, and every request value goes through `Create` on its way in.
Errors become problem-details responses, and the OpenAPI document shows each value object as the
type inside it.

Binding needs nothing beyond the generated code. `CodoMetis.TypeKit.AspNetCore` adds the other two
parts: the OpenAPI schemas, and a `Result` as the response. This page follows one request through a
minimal API and an MVC controller. The [package README](../src/CodoMetis.TypeKit.AspNetCore/README.md)
is the reference for every option.

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

## Set it up

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());          // the document: every value object as the schema of what it wraps

builder.Services.AddFaultResponses<CustomerFault>(fault => fault switch   // a Result as a response: the status per fault
{
    CustomerFault.NotFound => StatusCodes.Status404NotFound,
    CustomerFault.EmailTaken => StatusCodes.Status409Conflict,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});
```

That's all the setup there is. You register nothing per value object, and the web project doesn't
reference the generators.

**Good to know**

- **Reference `Microsoft.AspNetCore.OpenApi` directly, at 10.0.12 or later.** The package README
  explains why ([Setup](../src/CodoMetis.TypeKit.AspNetCore/README.md#setup)).

## Bind value objects in a minimal API

```csharp
app.MapGet("/customers/{id}", (CustomerId id) => customers.Find(id).ToHttpResult());                  // a route parameter, through the generated TryParse
app.MapGet("/customers", (Email? email, PageSize? page) => customers.List(email, page));              // query parameters, optional
app.MapPost("/customers", (RegisterCustomer body) => customers.Register(body).ToHttpResult());        // a body, through the generated JSON converter
```

Route, query and header parameters bind through the generated `TryParse`, with the invariant
culture. A body goes through the generated JSON converter.

**Good to know**

- **ASP0020 on a route parameter is a false alarm.** It appears when the value object is declared in
  the same project as the endpoint. ASP.NET's route analyzer reads your code before the generators
  add `IParsable`, so it sees an unfinished type. The binding itself is correct, and the package
  README shows the pragma that suppresses it.

## What a refused value answers

Here is what each request gets back from the endpoints above, and from the MVC controller further
down:

| Request | Answer |
|---|---|
| `GET /customers/0199aaaa-...` | 200, or the problem the fault maps to |
| `GET /customers/not-an-id` | 400: `TryParse` rejected the route value |
| `GET /customers?page=500` | 400: `Create` rejected it, through `TryParse` |
| `POST /customers` with `"email": "nobody"` | 400. The `JsonException` names `Email` and `NoAt`, never the text, and is logged. The body is empty unless the host adds `UseStatusCodePages()` |
| `POST /customers` without `email` | 200 with a default `Email`, unless `RespectRequiredConstructorParameters` is set, then 400 |
| `GET /mvc/customers` without `page` | 400, because the action marks `page` `[BindRequired]`. Without it, the action gets a default `PageSize` |

The last two rows need something from you. In both, your code receives a `default` value that never
went through `Create`.

- **A property missing from the body.** System.Text.Json calls no converter for a property the body
  doesn't have, so the property stays `default`. The package README's
  [Binding](../src/CodoMetis.TypeKit.AspNetCore/README.md#binding) section shows the two lines that
  make both minimal APIs and MVC answer 400 instead.
- **A query, header or form value missing from an MVC request.** For a non-nullable struct, MVC
  binds nothing, keeps the model state valid, and calls the action with `default`. That happens to a
  struct value object just as it does to a `Guid` or an `int`. Mark the parameter `[BindRequired]`
  (from `Microsoft.AspNetCore.Mvc.ModelBinding`), as the controller below does for `page`, and MVC
  answers 400. Or make the parameter nullable where it is optional.

A minimal API already answers 400 for a missing parameter. MVC already requires a closed set or a
class value object, as it does any non-nullable reference type.

## Bind value objects in MVC

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

MVC binds a route or query value object through the generated `TypeConverter`. A body goes through
the JSON converter, as in a minimal API.

The 400 is where the two differ. MVC answers a rejected body with its validation problem details,
and the message in it is the `JsonException`'s. So the client reads
`Email refused the JSON value (NoAt)`, never the text it sent.

**Good to know**

- **The OpenAPI document lists what ASP.NET's API explorer lists.** A controller is in it only with
  `[ApiController]`, with `[ApiExplorerSettings(IgnoreApi = false)]`, or through a convention that sets
  `ApiExplorer.IsVisible`, and TypeKit describes what is there.
- **Keep secrets out of dictionary keys.** When a value object is a dictionary key, System.Text.Json
  puts the key in the `JsonException`'s `Path`. MVC names the error after that path (`"$.items.SE"`),
  so the key ends up in the response
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

`ToHttpResult()` turns a `Result` into a response:

- **A success** becomes `200 OK` with its value, or `204 No Content` for a `Result<TError>`.
- **An error** becomes RFC 9457 problem details. The status is the one you mapped in
  [Set it up](#set-it-up), and the `fault` member names the error.

Your domain project declares the error type and knows nothing about HTTP. Only the web project
references ASP.NET. You can check a handler's outcome without starting a host:

```csharp
var response = customers.Find(CustomerId.New()).ToHttpResult();

bool notFound = response.Result is FaultProblem<CustomerFault> { Fault: CustomerFault.NotFound };   // true
```

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Not Found", "status": 404, "fault": "NotFound", "traceId": "…" }
```

If an endpoint can answer with an error type you haven't mapped, the host doesn't start. The
exception names the endpoint.

`AddFaultResponses` calls `AddProblemDetails()`, so your own `CustomizeProblemDetails` applies to
these problems too:

```csharp
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["service"] = "customers");
```

### Say more in a response

The response above is the default. You can opt in to more, one piece at a time, and the default
stays as it is:

- **A `type` and a `title` per error.**
- **A closed set as the error type**, answered by its members' kinds and descriptions.
- **A sentence per occurrence**, in a `Fault<TFault>`, and the rule that rejected the value.
- **Errors of several types side by side** (`Fault<T1, T2>`): a boundary's own errors beside the ones
  shared code decides.
- **A status per endpoint, route group or controller.** It applies to those of their endpoints that
  answer with the error type.
- **Members a route leaves out** of its document.
- **The endpoint's own success**, such as a `201 Created`.
- **The error on the request**, for your `CustomizeProblemDetails` to read.

The package README shows each one, starting at
[Mapping faults to statuses](../src/CodoMetis.TypeKit.AspNetCore/README.md#mapping-faults-to-statuses).

**Good to know**

- **The client always sees which error it was.** Every problem names its error in `fault`, for
  every member, with a description or without one. The name reaches the client just as the status
  does. Sometimes two causes must look the same, such as an unknown user and a wrong password, which
  would otherwise tell a caller which accounts exist. Map them to one member before
  `ToHttpResult()`, as the package README's
  [Faults a client must not tell apart](../src/CodoMetis.TypeKit.AspNetCore/README.md#faults-a-client-must-not-tell-apart)
  shows.

## See value objects in the OpenAPI document

With `AddTypeKit()`, every value object gets the schema ASP.NET publishes for the type inside it,
wherever it appears.

- **In a body**, a value object keeps a component named after it.
- **As a route or query parameter**, it is described inline, just as ASP.NET describes a parameter
  of the underlying type. So the `id` below is a `uuid` string with no component of its own.
- **The error responses** are listed on every operation that returns the error type.

```json
"/customers/{id}": { "get": {
    "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ],
    "responses": {
        "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/CustomerDto" } } } },
        "404": { "content": { "application/problem+json": { "schema": { "$ref": "#/components/schemas/ProblemDetails" } } } },
        "409": { "content": { "application/problem+json": { "schema": { "$ref": "#/components/schemas/ProblemDetails" } } } } } } }
```

The `ProblemDetails` component those responses point to describes `fault` too, as an optional
string. A client generated from the document then has the member it matches on.

**Good to know**

- **Put rules in `Create`, not on the property.** A validation attribute on a value-object property
  ends up in the value object's shared component, and from there it applies to every other use of
  that value object.

## Describe value objects as plain types

Some clients work better without a type per value object. A generator that turns each component into a
class gives you a `CustomerId` wrapper where you wanted a `string`. Turn on `InlineValueObjects`, and
every value object is described in place by the schema of the type inside it:

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit(typeKit => typeKit.InlineValueObjects = true));
```

The `CustomerDto` from above then has plain properties, and the document has no `CustomerId` or `Email`
component:

```json
"CustomerDto": { "type": "object", "properties": {
    "id": { "type": "string", "format": "uuid" },
    "email": { "type": "string" },
    "name": { "type": "string" } } }
```

The same goes for every place a value object appears: a body, a response, a list's elements, a
dictionary's values, a route, query or header parameter, and anything nested in those. Each one gets
exactly the schema the underlying type gets in that place.

**Good to know**

- **Without the setting, nothing changes.** `AddTypeKit()` writes the same document as before, with a
  component per value object.
- **A nullable value object allows null in its own schema**, as a nullable property of the underlying
  type does. That holds under OpenAPI 2.0 too, where a nullable property of a component loses its type.
- **Closed sets keep their components**, and so does an enum inside a value object. The value object's
  schema holds the enum's content in place.
- **Two value objects with one name no longer clash.** Neither has a component, so neither needs a
  name of its own.
- **Your own `CreateSchemaReferenceId` still names your other types** when you set it before
  `AddTypeKit`. One set after `AddTypeKit` replaces TypeKit's naming, and this setting with it.

## Native AOT

The transformer and `ToHttpResult()` are trim- and AOT-safe, and the smoke test publishes a host
that uses both. Your `JsonSerializerContext` needs a few entries for the document and the problem
details. [Native AOT](native-aot.md#openapi) lists them.
