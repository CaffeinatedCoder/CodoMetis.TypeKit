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

  Inlined, a nullable value object or closed set admits null in its own schema wherever a referenced
  one admits it around its reference: a closed set's property is then the enum of its names and null.

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
- **A converter of the host's own wins.** A value object, or a closed set, that the JSON options give a
  converter of their own is written by that converter, not the generated one, so it is described as that
  converter says (a described type, below) or, where it says nothing, left as ASP.NET writes
  any converter of one's own, rather than as a wrapped type it does not write. A parameter binds through
  `TryParse` and is described as before.

**A merge patch.** A property typed `Patch<T>` (CodoMetis.TypeKit, a JSON Merge Patch field) is
described as `T`'s schema:

- inlined where the property is, rather than as a `PatchOfstring` component;
- not required, since absent is a state, unless the serializer requires it: under
  `RespectRequiredConstructorParameters` a patch that is a constructor parameter is, and stays so;
- admitting null, since null is the clear: beside the type, in an enum's list of values, or as a
  branch beside the argument's own.

A value object inside a patch is described through the type it wraps, which is also what a
source-generated host can do: the patch's converter hides the value object from its context.

**A described type.** ASP.NET cannot see through a JSON converter of one's own: it writes the type as
`{}`, or with only the `format` its CLR type maps to, and drops a list's `items` and a dictionary's
`additionalProperties` of it. A converter that says what it writes (`IDescribedJsonConverter`, as
`DecimalStringJsonConverter` does), and a type the host's JSON options describe
(`JsonSchemaDescriptions`), are described as that says, as a property, a body, a nullable one, a list's
element, a dictionary's value, and inside a value object or a patch. A description of a value object or
a closed set in the options comes before its own, and so does a described converter registered on the
options for one, which writes it instead of the generated converter; in a patch of it too. The converter
asked is the one the serializer runs: a factory for the converter it makes, a converter registered for a
`Nullable<T>` for the nullable positions. A route, query
or header parameter binds through `TryParse`, not JSON, and keeps what ASP.NET writes for the type without
converters of one's own. ASP.NET builds the document from the minimal-API JSON options
(`ConfigureHttpJsonOptions`), and controllers write with MVC's (`AddJsonOptions`): a host with controllers
registers the same converters and descriptions on both, in the same order, or building the document fails,
naming the type and what each says of it, a type held inside a patch alone included, rather than describe
JSON the controllers do not write. Building it fails too for a description the document would write
otherwise (a keyword OpenAPI 3.1 does not model, such as `prefixItems`, or one a 3.0 document writes aside
as an `x-jsonschema-` extension or leaves out, such as `if`, `contains` or `patternProperties`, named with
the version), for a described converter one property names for a value object, and for a converter
registered for a nullable value object alone, whose positions refer to the component the document writes
once for every position. A type
nothing describes is left as ASP.NET writes it ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/json.md#describing-what-a-converter-writes)).

**Which decimal spelling.** The choice is the application's, made once on its JSON options and never
on the OpenAPI options, which only follow: `DecimalNumberJsonConverter` where the clients' numbers hold a
decimal, `DecimalStringJsonConverter` where a client's number is a double. Either reaches every decimal
and every value object over one, and the document describes each as that converter writes it; with
neither registered, decimals are the serializer's plain numbers, described as ASP.NET describes them. A
host with controllers registers the converter on MVC's options too, or the document is refused:

```csharp
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
builder.Services.AddOpenApi(options => options.AddTypeKit());
```

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
- **It changes every error response of the host, not only these.** `AddFaultResponses` calls
  `AddProblemDetails()`, without which a host on source-generated JSON cannot write a problem, and
  ASP.NET applies that service to every problem the host writes: `Results.Problem` and
  `Results.ValidationProblem` gain a `traceId`, `UseStatusCodePages()` answers a bare 400 or 404 as
  problem details where it wrote plain text, and `UseExceptionHandler()` without options, which
  refused to start before, answers a 500 as problem details. A `CustomizeProblemDetails` of your own
  reaches all of them. Adding the package to a host whose clients read those responses changes its
  wire; `CustomizeProblemDetails` can take back what you do not want, and `IFaultFeature` tells
  this package's problems from the rest ([below](#what-the-host-adds-to-a-problem)).
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
Both write the type as the URI it is: an escape stays escaped (`/problems/a%20b`, `%C3%BC`), an
absolute URI given unescaped is escaped, and a relative one is written as given.

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

A kind gives the status: `NotFound` 404, `Conflict` 409, `Invalid` 422, `Forbidden` 403,
`Unavailable` 503, `PreconditionFailed` 412 (a condition the request stated, such as the version in
`If-Match`) and `Malformed` 400 (a token or a filter that does not parse). A member whose kind is `default`, which is no kind, fails this call. The
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

### The rule that refused

Where one fault answers for several rules, a client may still need to know which: the address is in
use by another account, or reserved. `WithRule` names it with a member of an enum or a closed set, and
the problem carries it as `rule`, qualified with its type's name so that two types' `Blank` read apart:

```csharp
public enum EmailRule { InUse, Reserved }   // which check refused the address
```

```csharp
Result<Account, Fault<AccountFault>> reserved = Result.Error(Fault.Of(AccountFault.EmailTaken).WithRule(EmailRule.Reserved));
```

The problem reads `"fault": "EmailTaken", "rule": "EmailRule.Reserved"`, a nested type's rule after its
nesting chain, whatever `QualifiedFaultNames` says; the status, the mapping and the document stay the
fault's. A rule is a key, not a sentence: a client renders its own text from it, and a detail beside
it says the same for whoever reads the raw response. It is always a member's name and never the
request's text, and a value no member names, a combination of flags or a cast number, throws where it
is given. A value object's own fault is often the rule: `MapError(fault =>
Fault.Of(SignUpFault.InvalidEmail).WithRule(fault))` says which of its rules refused.

The name carries no namespace, which would publish the application's structure to every client, so
two rule types with the same nesting chain, `Rule` in two namespaces, read alike on the wire; their
faults stay unequal in the application, which compares the rule's type. Like a fault's name, the rule
is the application's vocabulary: name rule types apart, for instance by nesting a rule in its value
object, `Email.Rule.Blank`.

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

A route group says it for its endpoints, and a controller class, with the same attribute, for its
actions. Such a status applies to the endpoints of that group or controller that answer with the
fault type and leaves the others alone, so a group can hold a health check beside them:

```csharp
var v3 = app.MapGroup("/v3").WithFaultStatus(AccountFault.EmailTaken, StatusCodes.Status422UnprocessableEntity);
v3.MapPost("/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult());   // answers with AccountFault: the 422 applies
v3.MapGet("/health", () => "ok");                                                  // answers with none: left alone
```

What stops the host from starting, naming the status and its endpoints:

- a status on one endpoint or action that answers with no fault of that type;
- a status on a group or a controller none of whose own endpoints answers with that type. An endpoint
  of another group or controller does not count, and a controller that derives from a base class
  with the attribute is a controller of its own;
- an attribute naming no member.

The same holds for any convention builder that covers several endpoints, such as the one
`MapControllers()` returns. A status outside 400 to 599 is refused where it is set. Where several
statuses name one member, the last in ASP.NET's metadata order wins: an endpoint's own over its
group's, an inner group's over an outer group's, an action's over its controller's. ASP.NET puts the
metadata of `MapControllers()`'s builder after every attribute, so a status set there wins over an
action's own.

### Leaving out what a route cannot answer

The document lists every status a fault type is mapped to, on every route that answers with it. A route
that never reports some members leaves them out:

```csharp
app.MapPost("/v4/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult())
   .WithoutFaults(AccountFault.NotFound);   // opening an account never reports a missing one: no 404 here
```

The operation lists no status and no problem type for a member left out, and no response for a status
no member is left at. An MVC action or controller says it with `[WithoutFaults<AccountFault>(nameof(AccountFault.NotFound))]`,
and a route group or a controller for its endpoints that answer with the type, as for a status, once per fault
type.

Reporting a member left out anyway throws an `InvalidOperationException` naming the endpoint and the
member, rather than answering with a status the document does not list: leave out only what the route
cannot report. It is the one refusal here that waits for a request: it is thrown as the response is
written, so the client gets the host's 500, where every other mistake in the mapping stops the host at
start. What stops the host from starting, as for a status: a value or a name no member has, a
member the endpoint also gives a status, and a group or a controller none of whose endpoints answers with
the type.

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
fault, the name the problem gives it, its detail and its rule. A `CustomizeProblemDetails` of the host's reads
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
one is added. `rule` is described beside it, an optional string as well. A document without a fault
response keeps the schema as ASP.NET writes it.

### Faults a client must not tell apart

Every problem `ToHttpResult()` writes names its fault in `fault`, for every member, whether it has a
description or not: the name is part of the response's contract, as its status is, and the document
describes it. A status alone hides nothing. A sign-in that answers 401 for an unknown user and for a
wrong password still says `UnknownUser` or `WrongPassword`, and so which accounts exist. Where two
causes must not be told apart, make them one fault before `ToHttpResult()`, mapping the domain's
faults to a member of the boundary's own:

```csharp
public enum SignInFault { UnknownUser, WrongPassword }   // the domain's: which check refused
public enum LoginFault { InvalidCredentials }            // what the client is told

builder.Services.AddFaultResponses<LoginFault>(_ => StatusCodes.Status401Unauthorized);

app.MapPost("/login", (Credentials credentials) => accounts.SignIn(credentials).MapError(_ => LoginFault.InvalidCredentials).ToHttpResult());
```

Both causes then answer alike, status, `type`, `title`, `fault` and `detail`. The domain keeps its two
faults for its own use, a log or a lockout count; a `Fault<TFault>`'s detail stays behind with them.

## NodaTime

ASP.NET cannot see through NodaTime's JSON converters, so once they are on the host's JSON options
it describes a NodaTime type as `{}`, and so, through this package, a value object wrapping one. The
host describes the NodaTime types it uses beside the converters, in the same options, and the document
follows, for the value objects wrapping them too:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    options.SerializerOptions.Converters.Add(new JsonSchemaDescriptions()
        .Describe<Instant>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["format"] = "date-time" }))
        .Describe<LocalDate>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["format"] = "date" })));
});
```

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
  parameter (`Discount? Discount`) then needs a default value (`= null`), and a `Patch<T>` is an
  `init` property rather than a constructor parameter, or a request that leaves the field out is
  refused ([Merge patches](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/merge-patches.md#required-constructor-parameters)).
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
