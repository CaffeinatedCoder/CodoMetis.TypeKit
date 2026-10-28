# CodoMetis.TypeKit.AspNetCore

Use your value objects and `Result`s in ASP.NET Core with nothing to register per type. The OpenAPI
document shows every value object as the type inside it, wherever it appears. A `Result` returned
from an endpoint becomes its response, with errors as problem details
([A Result as a response](#a-result-as-a-response)).

This page is the reference for the package. The
[ASP.NET Core guide](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/aspnet-core.md)
follows one request through a minimal API and an MVC controller.

```bash
dotnet add package Microsoft.AspNetCore.OpenApi     # AddOpenApi itself; the webapi template has it already
dotnet add package CodoMetis.TypeKit.AspNetCore
```

Your web project needs this package and the base package, but not the generators. The package
recognises a value object at run time by the attribute the generators put on it. The domain project
that declares the value objects references `CodoMetis.TypeKit.Generators`.

## Setup

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());
```

`AddTypeKit()` adds:

- **a schema transformer each** for value objects, closed sets and merge patches;
- **an operation transformer** for the error responses;
- **component names** for nested value objects and closed sets (see below).

You register nothing per type, and no assembly is scanned. Its position among the document's other
transformers doesn't matter. It lives in `Microsoft.Extensions.DependencyInjection`, beside
`AddOpenApi`, which a web project imports implicitly.

`AddTypeKit()` configures only the document it's called for. A document without it describes every
value object as `{}`. If your host has several documents, call it in each `AddOpenApi`, or once for
all of them:

```csharp
using Microsoft.AspNetCore.OpenApi;   // OpenApiOptions

builder.Services.ConfigureAll<OpenApiOptions>(options => options.AddTypeKit());
```

Calling it again for a document that already has it changes nothing.

**Good to know**

- **Reference `Microsoft.AspNetCore.OpenApi` at 10.0.12 or later.** This package depends on that
  version, and an earlier direct reference fails to restore with NU1605 (a package downgrade).
- **Reference it directly.** The switch for its source generator is imported only for a direct
  reference. A host that gets the package only through this one fails with CS9137 ("the feature
  'Interceptors' is not enabled").

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

The `id` and `limit` parameters are a uuid string and an integer.

Without the package, `OrderId` is the empty schema `{}`. The list and the dictionary lose their
element schema, so `Quantity` gets no component at all, and both parameters are a bare `string`.

- **The underlying type's schema is ASP.NET's own.** So it follows the host's JSON options exactly,
  as the generated converter does. Under the web defaults, a number also accepts a quoted number
  (`"5"`). That's what the `integer | string` above says, and the converter reads it. With
  `JsonStringEnumConverter`, a value object over an enum is described by the enum's names.
- **Each value object keeps its own component**, named after it. So the document still says
  `OrderId` where the API means an order id.

  A nested value object is named after its whole nesting chain: `Shop.Id` becomes `ShopId`, so
  `Shop.Id` and `Stock.Id` don't share one. The same goes for a nested closed set: `Order.Status`
  becomes `OrderStatus`.

  Sometimes that name belongs to another type as well, such as a top-level `OrderStatus` beside
  `Order.Status`, from any assembly. Then generating the document throws an
  `InvalidOperationException` naming both, rather than describing both as one.

  Two value objects with the same name in different namespaces (`Billing.InvoiceNo`,
  `Shipping.InvoiceNo`) do share one component. ASP.NET does the same for any two types with one
  name, and describes both as the first it meets. A name your own `CreateSchemaReferenceId` gives,
  set before `AddTypeKit()`, is kept. So give one of them its own name there, or rename one.

  To have value objects inlined instead, return `null` for them, the nullable ones included, after
  `AddTypeKit()`:

  ```csharp
  using System.Reflection;
  using CodoMetis.TypeKit.CompilerServices;   // GeneratedValueObjectAttribute, on every value object

  options.AddTypeKit();
  options.CreateSchemaReferenceId = type =>
      (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
          ? null
          : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
  ```

  Inlined, a nullable value object or closed set allows null in its own schema, wherever a
  referenced one allows it around its reference. A closed set's property is then the enum of its
  names plus null.

  OpenAPI 2.0 can't put null around a reference. ASP.NET writes a nullable property of any component as
  `{"allOf":[{"x-nullable":true}]}` there, without the reference. So under 2.0 a nullable value-object
  property loses its type, where the underlying type's nullable property keeps it. A nullable element of
  a list or a dictionary loses its type the same way, since it's the component or null too. Inline value
  objects if your 2.0 document needs it.

- **Put rules in `Create`, not on the property.** A validation attribute on a value-object property
  (`[MaxLength]`, `[Range]`) ends up in the value object's shared component, as ASP.NET does for any
  referenced schema. From there it applies to every other use too. `Create` is where every way of
  creating the value runs the rule anyway.
- **It works everywhere a value object appears**: a property, a nullable property, a request or
  response body, the elements of a list, an array, a set or a nested container, a dictionary value,
  and a route, query or header parameter. That holds in minimal APIs and in MVC, and in OpenAPI 3.1
  and 3.0.
  - A nullable element (`List<Quantity?>`, `Dictionary<string, Quantity?>`) is the component or
    null, as a nullable property is.
  - A parameter is what ASP.NET makes of a parameter of the underlying type. That's its schema for a
    number, a `Guid`, a date or an enum. For any other parsable type, such as NodaTime's `Instant` or
    a range, it's a bare `string`, whatever its JSON schema or another transformer says.
- **Other transformers compose.** Say the document has a transformer for an underlying type, such as
  one that describes NodaTime's `Instant` as a date-time. Then a value object over `Instant` gets
  that description too, whichever of the two you added first.
- **What's already there is kept.** Whatever a value object's schema already says, from ASP.NET or
  another transformer, isn't overwritten. The package only fills in what is left open.
- **Your own converter wins.** Your JSON options may give a value object or a closed set a converter
  of your own. That converter then writes it instead of the generated one, so it's described as that
  converter says (see "A described type" below). Where the converter says nothing, it's left as
  ASP.NET writes any custom converter. It's never described as an underlying type the converter
  doesn't write. A parameter still binds through `TryParse` and is described as before.

**A merge patch.** A property typed `Patch<T>` (from CodoMetis.TypeKit, a JSON Merge Patch field) is
described as `T`'s schema:

- **inlined** where the property is, rather than as a `PatchOfstring` component;
- **not required**, since absent is a state of its own. The exception is when the serializer
  requires it: under `RespectRequiredConstructorParameters`, a patch that is a constructor parameter
  is required, and stays so;
- **allowing null**, since null means "clear the field". The null sits beside the type, in an enum's
  list of values, or as a branch beside the argument's own.

A value object inside a patch is described through its underlying type. That's also what a host on
source-generated JSON can do, because the patch's converter hides the value object from the context.

**A described type.** ASP.NET can't see through a JSON converter you write yourself. It writes the
type as `{}`, or with only the `format` its CLR type maps to. It also drops a list's `items` and a
dictionary's `additionalProperties` of that type. The same goes for a converter a library registers for
its own type, such as NodaTime's for `Instant`.

Two things fix that, and the document then describes the type as they say:

- **A converter that says what it writes**, by implementing `IDescribedJsonConverter`, as
  `DecimalStringJsonConverter` does.
- **A type your JSON options describe**, through `JsonSchemaDescriptions`.

The description is used as a property, a body, a nullable one, a list's element, a dictionary's
value, and inside a value object or a patch.

- **A description of your own comes first.** A description of a value object or a closed set in the
  options comes before its own. So does a described converter registered on the options for one,
  which then writes it instead of the generated converter. Both hold in a patch of it too.
- **The converter asked is the one the serializer runs.** For a factory, that's the converter it
  makes. For the nullable positions, it's a converter registered for `Nullable<T>`.
- **Parameters aren't JSON.** A route, query, header or form parameter binds through `TryParse`, not
  JSON. It keeps what ASP.NET writes for the type without custom converters, and so do the items of a
  query or header array (`decimal[]`). The same goes for a value object over the type, such as
  `Money : IValue<decimal>`. Where value objects are components, such a parameter is written in place
  rather than as a reference to `Money`, because the `Money` component says what its JSON is.
- **Register the same on both option sets.** ASP.NET builds the document from the minimal-API JSON
  options (`ConfigureHttpJsonOptions`), while controllers write with MVC's (`AddJsonOptions`). A host
  with controllers registers the same converters and descriptions on both, in the same order.
  Otherwise building the document fails rather than describe JSON the controllers don't write. The
  error names the type and what each set of options says of it, also for a type held only inside a
  patch.

Building the document also fails:

- **for a description the document can't write as it is.** That's a keyword OpenAPI 3.1 doesn't
  model, such as `prefixItems`. Or it's one a 3.0 document writes aside as an `x-jsonschema-`
  extension or leaves out, such as `if`, `contains` or `patternProperties`. The error names the
  version.
- **for a described converter that one property names for a value object.**
- **for a converter registered for a nullable value object alone.** Its positions refer to the
  component the document writes once for every position.

A type nothing describes is left as ASP.NET writes it ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/json.md#describing-what-a-converter-writes)).

**Which decimal spelling.** You choose once, on your application's JSON options. The OpenAPI options
only follow, so never set it there.

- **`DecimalNumberJsonConverter`** where your clients' numbers hold a decimal.
- **`DecimalStringJsonConverter`** where a client's number is a double.

Either one reaches every decimal and every value object over one, and the document describes each
as that converter writes it. With neither registered, decimals are the serializer's plain numbers,
described as ASP.NET describes them. A host with controllers registers the converter on MVC's options
too, or the document is rejected:

```csharp
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
builder.Services.AddOpenApi(options => options.AddTypeKit());
```

## A Result as a response

`ToHttpResult()` turns a `Result` into what an endpoint returns. A success becomes `200 OK` with its
value, or `204 No Content` for a `Result<TError>`. An error becomes RFC 9457 problem details, with
the status you map it to.

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

- **Your domain declares the errors, your web project their statuses.** The error type (TypeKit
  calls it a fault) is an enum or a closed set that your domain owns. Only the web project
  references ASP.NET. The package adds nothing from the request to a problem. It names the error,
  and a `Fault<TFault>`'s detail is your own sentence ([below](#a-sentence-per-occurrence)).
- **The document shows it too.** The endpoint's return type is ASP.NET's own
  `Results<Ok<OrderDto>, FaultProblem<OrderFault>>`, so the operation describes the value.
  `AddTypeKit()` adds a problem response for every status the error type is mapped to, as
  `ProducesProblem` would. An MVC action returning the same type is described the same way. A
  response you declare yourself at one of those statuses, with `Produces(404)` or
  `[ProducesResponseType(404)]`, is kept as you declared it: the document doesn't add the problem
  schema there, though the wire still answers a problem.
- **It's not a wire format.** The `Result` is matched to a response, not serialized. Returning the
  `Result` itself from a handler is still rejected, and the document would promise a `Result`.
- **It changes every error response of the host, not only these.** `AddFaultResponses` calls
  `AddProblemDetails()`, without which a host on source-generated JSON can't write a problem. ASP.NET
  applies that service to every problem the host writes:
  - `Results.Problem` and `Results.ValidationProblem` gain a `traceId`.
  - `UseStatusCodePages()` answers a bare 400 or 404 as problem details, where it wrote plain text.
  - `UseExceptionHandler()` without options, which failed to start before, answers a 500 as problem
    details.

  Your own `CustomizeProblemDetails` applies to all of them. If your clients read those responses,
  adding the package changes what they receive. `CustomizeProblemDetails` can take back what you
  don't want, and `IFaultFeature` tells this package's problems apart from the rest
  ([below](#what-the-host-adds-to-a-problem)).
- **Declare the return type.** ASP.NET reads an endpoint's responses from its declared return type,
  as it does for its own typed results. A handler typed `IResult` answers the same, but its operation
  lists no problem response, and the startup check can't see which error type it reports.
- **Test a handler without a host.** A handler's test can read the outcome directly:
  `Find(id).ToHttpResult().Result is FaultProblem<OrderFault> { Fault: OrderFault.NotFound }`.

### Mapping faults to statuses

`AddFaultResponses` maps every member of the error type when you call it. So a mapping that throws
for a member, or gives one a status that isn't an error (outside 400 to 599), fails right there, at
startup. The discard arm above relies on that: a member the `switch` misses throws while the host
starts, not when the error first occurs. Without the arm, C# warns (CS8524) that a cast could produce
a value no member names. Each error type is mapped once.

If an endpoint answers with an error type that isn't mapped, the host doesn't start. An
`InvalidOperationException` names the error type and each endpoint that answers with it.

**Good to know**

- **The check comes with the first `AddFaultResponses` call.** A host that never calls it throws the
  same exception when an endpoint first reports an error, and when its OpenAPI document is generated.
- **A value no member names is rejected.** Only a cast can produce one. It's rejected rather than
  answered with a status the document doesn't list.

### A response per fault

A mapping can say more than a status. RFC 9457 makes the problem's `type` its primary identifier,
which a client matches on, and `title` its summary. Both name the kind of problem and nothing from
the request. Without them, ASP.NET writes the status's own.

```csharp
builder.Services.AddFaultResponses<PaymentFault>(fault => fault switch
{
    PaymentFault.Declined  => FaultResponse.Status(StatusCodes.Status402PaymentRequired).WithType(new Uri("https://example.com/problems/declined")),
    PaymentFault.Duplicate => StatusCodes.Status409Conflict,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});
```

A bare status converts to a `FaultResponse`, so the arms that need nothing else stay a number.
`WithTitle("...")` sets the `title` the same way. Every member is still mapped and checked when this
is called.

In the document, the problem response for a status lists the problem types answered with it in its
description: `Payment Required. Problem types: https://example.com/problems/declined`.

The response and the document both write the type as the URI it is. An escape stays escaped
(`/problems/a%20b`, `%C3%BC`), an absolute URI given unescaped is escaped, and a relative one is
written as given.

### A closed set as the fault type

A closed set whose members are errors declares `IFaultSet<TSelf>`. Each member carries a `FaultKind`,
which is nothing HTTP-specific, and optionally a sentence a person reads.

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

The kind gives the status:

| Kind | Status |
|---|---|
| `NotFound` | 404 |
| `Conflict` | 409 |
| `Invalid` | 422 |
| `Forbidden` | 403 |
| `Unavailable` | 503 |
| `PreconditionFailed` | 412, a condition the request stated, such as the version in `If-Match` |
| `Malformed` | 400, a token or a filter that doesn't parse |

The description becomes the problem's `detail`, so a client can show it. A member without one has no
`detail`.

To change some members, map them all, and let the rest follow their kind:
`fault => fault == AccountFault.Locked ? 423 : FaultResponse.For(fault.Kind)`.

**Good to know**

- **A member must have a kind.** A member whose kind is `default`, which is no kind, fails this call.
- **Any closed set can be an error type**, with a mapping of its own: `AddFaultResponses<TSet>(fault => ...)`.
  `ToHttpResult()` takes a result over either.

### Every fault set at once

If your host has many fault sets that all answer alike, map them in one call instead of one
`AddFaultResponses<TSet>()` per set:

```csharp
builder.Services.AddFaultResponses();   // every fault set an endpoint answers with, by its kind
```

The mapping can be your own. It reads each member's kind and description through `IFault`:

```csharp
builder.Services.AddFaultResponses(fault => FaultResponse.For(fault.Kind).WithType(new Uri($"https://example.com/problems/{fault.Kind}")));
```

- **A set mapped on its own keeps its mapping.** So this call covers the sets that answer like every
  other, and `AddFaultResponses<TSet>(...)` covers the one that doesn't.
- **An enum still needs a mapping of its own**, and so does a closed set that isn't a fault set.
  Neither has a kind, and the host doesn't start while an endpoint answers with one that's unmapped.
- **Each set is mapped as the host starts**, every member at once, for the endpoints that answer
  with it. A member with no kind stops the start, and so does one the mapping throws for or gives a
  status that isn't an error. That's the same as `AddFaultResponses<TSet>()` failing. Make this call
  once per host.

### A sentence per occurrence

Use a `Fault<TFault>` (from CodoMetis.TypeKit) for what varies from one occurrence to the next. It
holds the error, plus a sentence about this occurrence. The response, the mapping and the document
stay keyed on the error type.

```csharp
Result<Account, Fault<AccountFault>> taken = Result.Error(Fault.Of(AccountFault.EmailTaken, "The address has been in use since 3 May."));
```

```csharp
app.MapPut("/accounts/{id:int}/email", (int id, NewAccount body) => accounts.Rename(id, body.Email).ToHttpResult());
```

The sentence becomes the problem's `detail`, in place of the member's description. It's your
application's sentence, written as it is. An error's name never quotes the request. Whether a detail
does is up to your application.

### The rule that refused

Sometimes one error covers several rules, and a client still needs to know which one failed. Is the
address in use by another account, or is it reserved? `WithRule` names the rule with a member of an
enum or a closed set. The problem carries it as `rule`, qualified with its type's name, so two types'
`Blank` can be told apart:

```csharp
public enum EmailRule { InUse, Reserved }   // which check refused the address
```

```csharp
Result<Account, Fault<AccountFault>> reserved = Result.Error(Fault.Of(AccountFault.EmailTaken).WithRule(EmailRule.Reserved));
```

The problem reads `"fault": "EmailTaken", "rule": "EmailRule.Reserved"`. A rule of a nested type is
named after its nesting chain, whatever `QualifiedFaultNames` says. The status, the mapping and the
document stay the error's.

- **A rule is a key, not a sentence.** A client renders its own text from it. A detail beside it says
  the same for whoever reads the raw response.
- **It's always a member's name**, never the request's text. A value no member names, such as a
  combination of flags or a cast number, throws where it's given.
- **A value object's own error is often the rule.**
  `MapError(fault => Fault.Of(SignUpFault.InvalidEmail).WithRule(fault))` says which of its rules
  rejected the value.
- **The name carries no namespace**, which would publish your application's structure to every
  client. So two rule types with the same nesting chain, such as `Rule` in two namespaces, read alike
  on the wire. Their errors stay unequal in your application, which compares the rule's type. Like an
  error's name, the rule is your application's vocabulary. So give rule types distinct names, for
  instance by nesting a rule in its value object: `Email.Rule.Blank`.

### What failed underneath

When an exception causes the error, keep it in the `Fault<TFault>` as its cause. Your log gets the
exception, and the client gets the same problem as before:

```csharp
Result<Account, Fault<AccountFault>> saved = Result.Catch<Account, UniqueViolationException>(() => store.Save(account))
    .MapError(e => Fault.Of(AccountFault.EmailTaken, "Another account took the address a moment ago.", e));
```

`IFaultFeature.Cause` hands the exception to your `CustomizeProblemDetails`, so you can log it with
the response it led to:

```csharp
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.HttpContext.Features.Get<IFaultFeature>() is { Cause: { } cause } fault)
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Faults")
            .LogWarning(cause, "Answered {Fault} for an exception", fault.Name);   // your log gets the exception, the client doesn't
});
```

- **The cause stays out of the response.** The problem has the same members with a cause as without
  one, and names neither the exception's type nor its message. If your `CustomizeProblemDetails`
  copies it into the problem, the client learns what failed underneath.
- **`FaultProblem<TFault>.Cause`** holds it too, for a test or a filter that reads the result before
  it's written.
- **It's one exception, not a chain.** An `AggregateException` is fine as the cause.

### Faults of several types

Some rejections mean the same on every route: a change that raced the request, or a version that
went stale. Code that every boundary shares decides them once, as a fault set of its own. Each
boundary then answers with its own set and the shared one side by side, instead of copying the shared
members into its own:

```csharp
public sealed partial class EditFault : IFaultSet<EditFault>
{
    public static readonly EditFault Changed = new(FaultKind.Conflict, "Someone changed it meanwhile; read it again.");
    public static readonly EditFault Stale = new(FaultKind.PreconditionFailed, null);

    private EditFault(FaultKind kind, string? description) { Kind = kind; Description = description; }

    public FaultKind Kind { get; }
    public string? Description { get; }
}

public static class Edits   // decides these refusals once, for every boundary
{
    public static Fault<EditFault> Stale(int current, int sent) => Fault.Of(EditFault.Stale, $"It is at version {current}, not {sent}.");
}
```

`Fault<AccountFault, EditFault>` (from CodoMetis.TypeKit) holds one error of either set, with its
detail and rule intact. `Fault<T1, T2, T3>` holds one of three. The sets must be different types,
which `[Distinct]` on the type parameters enforces. Using one set twice,
`Fault<AccountFault, AccountFault>`, fails the build (CMTK0010). Every conversion and `Of` of it would
be ambiguous anyway (CS0457 and CS0121).

```csharp
using Refusal = Fault<AccountFault, EditFault>;   // the boundary's own faults, beside the shared ones

public Result<Refusal> Close(int id, int version) =>
    Find(id) is not { } account  ? Refusal.Of(AccountFault.NotFound)
    : account.Version != version ? Refusal.Of(Edits.Stale(account.Version, version))   // the shared fault, as it is
    : Result<Refusal>.Success();
```

```csharp
builder.Services.AddFaultResponses<EditFault>();   // once for the host, beside AccountFault

app.MapDelete("/accounts/{id:int}", (int id, int version) => edits.Close(id, version).ToHttpResult());
```

- **Each error is answered as its own set maps it.** The endpoint returns
  `Results<NoContent, FaultProblem<AccountFault>, FaultProblem<EditFault>>`. So its operation lists
  the statuses of both sets, in ascending order, with the problem types of both where they share a
  status. The host doesn't start while either set is unmapped.
- **A shared rejection reads the same everywhere.** `EditFault.Stale` is the same error from every
  boundary that answers with it. A copy in each boundary's set would be a separate error in each.
- **Name the type where an error becomes a result.** C# applies only one user-defined conversion
  there, and an error needs two to become a `Result<Refusal>`: `return Fault.Of(AccountFault.NotFound)`
  is CS0029. Write `Refusal.Of(reason)`, `Refusal.Of(fault)` or `Result.Error<Refusal>(fault)`
  instead. Anywhere else, an error of either set converts as it is.
- **Keep names distinct.** The sets a route answers with shouldn't write one name for two errors.
  With bare names, the default, that's `AccountFault.NotFound` beside an `EditFault.NotFound`. With
  qualified names, it's two sets with one nesting chain in two namespaces, since the name carries no
  namespace. A client reads the two errors as one, so the host logs a warning at startup naming both.
  From 3.0.0 the host won't start. Fix it with `QualifiedFaultNames`, a rename, or by leaving one out
  of the route ([below](#leaving-out-what-a-route-cannot-answer)).

### A status per endpoint

The same error can mean 404 on one route and 409 on another. An endpoint can change the status of
one error for itself, both in its response and in its document entry:

```csharp
app.MapPost("/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult())
   .WithFaultStatus(AccountFault.EmailTaken, StatusCodes.Status422UnprocessableEntity);
```

An MVC action does it with an attribute that names the member, since an attribute takes only
constants:

```csharp
[HttpPost("/mvc/accounts")]
[FaultStatus<AccountFault>(nameof(AccountFault.EmailTaken), StatusCodes.Status422UnprocessableEntity)]
public Results<Ok<Account>, FaultProblem<AccountFault>> Open(NewAccount body) => accounts.Open(body).ToHttpResult();
```

A route group does it for its endpoints, and a controller class does it for its actions, with the
same attribute. The status applies to those endpoints of the group or controller that answer with the
error type, and leaves the others alone. So a group can hold a health check beside them:

```csharp
var v3 = app.MapGroup("/v3").WithFaultStatus(AccountFault.EmailTaken, StatusCodes.Status422UnprocessableEntity);
v3.MapPost("/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult());   // answers with AccountFault: the 422 applies
v3.MapGet("/health", () => "ok");                                                  // answers with none: left alone
```

The host doesn't start, and names the status and its endpoints, for:

- **a status on one endpoint or action** that answers with no error of that type;
- **a status on a group or a controller** none of whose own endpoints answers with that type. An
  endpoint of another group or controller doesn't count. A controller that derives from a base class
  with the attribute is a controller of its own;
- **an attribute naming no member.**

**Good to know**

- **Any convention builder that covers several endpoints works the same**, such as the one
  `MapControllers()` returns.
- **A status outside 400 to 599 is rejected** where it's set.
- **When several statuses name one member, the last in ASP.NET's metadata order wins.** An
  endpoint's own wins over its group's, an inner group's over an outer group's, and an action's over
  its controller's. ASP.NET puts the metadata of `MapControllers()`'s builder after every attribute,
  so a status set there wins over an action's own.

### Leaving out what a route cannot answer

The document lists every status an error type is mapped to, on every route that answers with it. A
route that never reports some members can leave them out:

```csharp
app.MapPost("/v4/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult())
   .WithoutFaults(AccountFault.NotFound);   // opening an account never reports a missing one: no 404 here
```

The operation then lists no status and no problem type for a member left out. It lists no response
for a status that no remaining member is mapped to.

- **In MVC**, an action or a controller says it with
  `[WithoutFaults<AccountFault>(nameof(AccountFault.NotFound))]`.
- **A route group or a controller** says it for its endpoints that answer with the type, as for a
  status.
- **A [composed fault](#faults-of-several-types)** needs one call per error type:
  `.WithoutFaults(AccountFault.NotFound).WithoutFaults(EditFault.Stale)`.

**Good to know**

- **Leave out only what the route can't report.** Reporting a member left out anyway throws an
  `InvalidOperationException` naming the endpoint and the member, rather than answering with a status
  the document doesn't list. It's the one rejection here that waits for a request. It's thrown as the
  response is written, so the client gets the host's 500. Every other mistake in the mapping stops
  the host at startup.
- **Leaving a member out narrows a wider status.** If a group gives `NotFound` a status and one
  endpoint inside leaves `NotFound` out, that endpoint drops it, and the group's other endpoints keep
  the status.
- **These stop the host from starting**, as for a status: a value or a name no member has, a status
  that every endpoint it's set on leaves out (on the endpoint itself, or on a group whose endpoints
  all leave the member out), and a group or a controller none of whose endpoints answers with the
  type.

### The success the endpoint chooses

A success is `200 OK` with its value, unless the endpoint picks another of ASP.NET's typed results.
The document still describes it:

```csharp
app.MapPost("/v2/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult(account => TypedResults.Created($"/accounts/{account.Id}", account)));
```

The operation lists `201` with the account as its body, beside the problem responses, and the
response carries the `Location` header. A result type of your own, such as one that sets an ETag,
works in the same place. A `Result<TError>` takes a factory without a value:
`ToHttpResult(() => TypedResults.Accepted("/jobs/7"))`.

### What the host adds to a problem

While `ToHttpResult()` writes a problem, the request carries an `IFaultFeature`. It holds the error
type, the error, the name the problem gives it, its detail, its rule and its cause. Your
`CustomizeProblemDetails` reads it to add what the package doesn't name. No other problem the request
writes has it.

```csharp
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.HttpContext.Features.Get<IFaultFeature>() is { } fault)
        context.ProblemDetails.Extensions["code"] = $"ACC-{fault.Name}";   // the host's own extension, for this fault only
});
builder.Services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);   // "fault": "AccountFault.EmailTaken"
```

`QualifiedFaultNames` writes `fault` with its type, so two error types that share a member name stay
apart in one client. The default is the bare name. A nested type is qualified after its nesting
chain, an enum just as a closed set's `QualifiedName` is. So `Billing.Fault` and `Shipping.Fault`
give `Billing.Fault.NotFound` and `Shipping.Fault.NotFound`.

The document describes `fault` as an optional string on the problem schema the error responses point
to, so a generated client has the member to match on. Its description says which of the two forms
this host writes.

- **The schema is shared.** It's the `ProblemDetails` component, which ASP.NET's own problem
  responses point to as well. So the description also says that only a problem answering an error
  carries `fault`.
- **It stays a string.** An enum of every error type would change every operation whenever one is
  added.
- **`rule` is described beside it**, also as an optional string.
- **A document without an error response** keeps the schema as ASP.NET writes it.

### Faults a client must not tell apart

Every problem `ToHttpResult()` writes names its error in `fault`, for every member, with a
description or without one. The name is part of the response's contract, just as its status is, and
the document describes it.

So a status alone hides nothing. A sign-in that answers 401 for an unknown user and for a wrong
password still says `UnknownUser` or `WrongPassword`. That tells the caller which accounts exist.
When two causes must look the same, make them one error before `ToHttpResult()`, by mapping the
domain's errors to a member of the boundary's own:

```csharp
public enum SignInFault { UnknownUser, WrongPassword }   // the domain's: which check refused
public enum LoginFault { InvalidCredentials }            // what the client is told

builder.Services.AddFaultResponses<LoginFault>(_ => StatusCodes.Status401Unauthorized);

app.MapPost("/login", (Credentials credentials) => accounts.SignIn(credentials).MapError(_ => LoginFault.InvalidCredentials).ToHttpResult());
```

Both causes then get the same answer: status, `type`, `title`, `fault` and `detail`. The domain keeps
its two errors for its own use, such as a log or a lockout count. A `Fault<TFault>`'s detail stays
behind with them.

## NodaTime

ASP.NET can't see through NodaTime's JSON converters. Once they're on the host's JSON options, it
describes a NodaTime type as `{}`. Through this package, so is a value object over one.

Describe the NodaTime types you use beside the converters, in the same options. The document then
follows, for the value objects over them too:

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

A value object used as a **minimal-API route parameter**, in the project that also declares it,
fails the build with ASP0020 ("should define a bool TryParse ... or implement IParsable"). ASP.NET's
route analyzer reads your code before the generators add `IParsable`, so it sees an unfinished type.
The request delegate generator and the running application both see the generated `TryParse`, and
the binding is correct.

The error doesn't occur for a value object declared in a project the endpoints reference, for a
query parameter, or in MVC. Where it does occur, suppress it at the endpoint:

```csharp
#pragma warning disable ASP0020 // OrderId implements IParsable once generated
app.MapGet("/orders/{id}", (OrderId id) => orders.Find(id));
#pragma warning restore ASP0020
```

A `DiagnosticSuppressor` can't do this for you, because suppressors don't run in a project the
generators compile.

## Native AOT

The transformer is trim- and AOT-safe. Native AOT requires source-generated JSON, and then your
`JsonSerializerContext` lists every type your value objects wrap. ASP.NET builds a value object's
schema from its JSON contract for the underlying type, and a context has none for a type the host
never serializes itself.

```csharp
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(Guid))]   // what OrderId wraps
[JsonSerializable(typeof(int))]    // what Quantity wraps
internal partial class AppJson : JsonSerializerContext;

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default));
```

Without such an entry, generating the document fails with an `InvalidOperationException` that names
the value object and the `[JsonSerializable]` to add.

## Binding

The generated members handle the request itself:

- **Route, query and header parameters** bind through the generated `TryParse` in minimal APIs, and
  through the generated `TypeConverter` in MVC, with the invariant culture. A rejected value gets a
  400. The text of that 400 is ASP.NET's own: MVC's "The value '...' is not valid." quotes the input,
  as it does for a `Guid`.
- **Request and response bodies** go through the generated JSON converter. A value that a validated
  value object rejects becomes a `JsonException` naming the type and the rule, never the value, and a
  400. MVC puts that message in its validation problem details. A minimal API answers with an empty
  400 and logs the message, even with `AddProblemDetails()`. Adding `app.UseStatusCodePages()` gives
  the response a generic problem body, which names neither the property nor the rule.
- **A property missing from the body isn't rejected.** System.Text.Json calls no converter for an
  absent property, so it stays `default`: a `Guid.Empty` order id, or a validated value object that
  never went through `Create`. Where a value object is required, say so. Use `required` on the
  property or, for a record's constructor parameters, `RespectRequiredConstructorParameters`:

  ```csharp
  builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.RespectRequiredConstructorParameters = true);
  builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.RespectRequiredConstructorParameters = true);
  ```

  Both minimal APIs and MVC then answer a body without it with 400. With this on, an optional
  constructor parameter (`Discount? Discount`) needs a default value (`= null`). And a `Patch<T>`
  must be an `init` property rather than a constructor parameter, or a request that leaves the field
  out is rejected ([Merge patches](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/merge-patches.md#required-constructor-parameters)).
- **A query, header or form value missing from an MVC request isn't rejected either.** MVC binds
  nothing for a non-nullable struct and calls the action with `default`, as it does for a `Guid` or
  an `int`, and the model state stays valid. For a struct value object, that's again an instance
  that went through no factory. A minimal API answers 400. MVC already requires a class value object
  or a closed set, as it does any non-nullable reference type. Mark the parameter `[BindRequired]`,
  and MVC answers 400 ("A value for the 'id' parameter or property was not provided."). Or make it
  nullable where it's optional, and the action receives `null`:

  ```csharp
  using Microsoft.AspNetCore.Mvc.ModelBinding;   // BindRequired

  [HttpGet("/mvc/orders")]
  public OrderDto Find([FromQuery, BindRequired] OrderId id, [FromQuery] Quantity? limit) => orders.Find(id, limit);
  ```
