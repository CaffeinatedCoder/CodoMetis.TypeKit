# Walkthrough: one orders service

This page follows one small orders service from the request to the database and back. Each guide
covers one boundary. Here you see one e-mail address cross all of them, and at each step what
happens to it, or that nothing does.

The service is a minimal API on SQLite. It uses the generators, the EF Core package, the ASP.NET Core
package and the Serilog package. Its code is a sample application. CI runs it on every change, and
builds it from the packed packages too, so every answer below is one it really gives:
[Orders.cs](../tools/ConsumerSmokeTest/Consumers/walkthrough/Orders.cs),
[OrderEndpoints.cs](../tools/ConsumerSmokeTest/Consumers/walkthrough/OrderEndpoints.cs),
[Program.cs](../tools/ConsumerSmokeTest/Consumers/walkthrough/Program.cs), and the requests in
[Tour.cs](../tools/ConsumerSmokeTest/Consumers/walkthrough/Tour.cs).

| Step | What happens to the e-mail address |
|---|---|
| [Declare the values](#declare-the-values) | `Create` trims it, composes it and checks it, once |
| [Store them](#store-them) | it becomes a plain `TEXT` column |
| [Place an order](#place-an-order) | JSON reads it through `Create` and writes it back as a string. The log masks it |
| [Reject what doesn't fit](#reject-what-doesnt-fit) | a rejected value gets a 400 that never quotes it |
| [Find an order](#find-an-order) | nothing: an `Option` becomes a 404 |
| [Query on `.Value`](#query-on-value) | nothing: `.Value` is the column in SQL |
| [Change an order](#change-an-order) | a `Patch<T>` reads each field through its own `Create` |
| [Read the OpenAPI document](#read-the-openapi-document) | it is described as the string it is |

## Declare the values

```csharp
public readonly partial record struct OrderId : IValue<Guid>;

public enum EmailFault { Blank, Hidden, NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.AsText()                                                                    // trimmed, composed, no hidden characters
             .MapError(fault => fault == TextFault.Blank ? EmailFault.Blank : EmailFault.Hidden)
             .Ensure(text => text.IndexOf('@') > 0, EmailFault.NoAt)
             .Map(text => new Email(text));

    public override string ToString() => $"{Value[0]}***{Value[Value.IndexOf('@')..]}";   // what a log shows
}

public enum QuantityFault { NotPositive }

public readonly partial record struct Quantity : IValidatedValue<Quantity, int, QuantityFault>
{
    public static Result<Quantity, QuantityFault> Create(int value) =>
        value > 0 ? new Quantity(value) : Result.Error(QuantityFault.NotPositive);
}

public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Placed = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}
```

These are the service's whole domain:

- **`OrderId`** wraps a `Guid`. `OrderId.New()` makes a version 7 one.
- **`Email`** is validated. Its `Create` runs the text guard `AsText()`, which trims the address,
  composes it to one spelling and rejects hidden characters. Then it checks for an `@`. Every way an
  `Email` gets created runs this `Create`: JSON, a route or query value, `TryFrom`.
- **`Email.ToString()`** masks the address. The generators keep a `ToString()` you write, and every
  log line prints it.
- **`Quantity`** rejects zero and less.
- **`OrderStatus`** is a closed set: an enum whose members are instances, written as their names.

The [Value objects](value-objects.md), [Written text](text.md) and [Closed sets](closed-sets.md)
guides cover each kind.

## Store them

```csharp
public sealed class Order
{
    public required OrderId Id { get; init; }
    public required Email Email { get; set; }
    public required Quantity Quantity { get; set; }
    public required OrderStatus Status { get; set; }
}

public sealed class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
```

The entity uses the value objects as its properties, with nothing to register per type.
`Program.cs` sets up the host. `UseTypeKit()` maps each value object to a column of the type inside
it, and `EnsureCreated()` shows the table that comes of it. `MapOrders()` maps the endpoints the next
sections show.

```csharp
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using Walkthrough;
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddTypeKit());
builder.Services.AddFaultResponses<OrderFault>(fault => fault switch
{
    OrderFault.NotFound => StatusCodes.Status404NotFound,
    OrderFault.AlreadyShipped or OrderFault.Cancelled => StatusCodes.Status409Conflict,
    OrderFault.Cleared => StatusCodes.Status422UnprocessableEntity,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.RespectRequiredConstructorParameters = true);

var connection = new SqliteConnection("Data Source=:memory:");   // the database lives as long as this connection is open
connection.Open();
builder.Services.AddDbContext<OrdersDb>(options => options.UseSqlite(connection).UseTypeKit()
    .LogTo(Console.WriteLine, [RelationalEventId.CommandExecuted]));

builder.Services.AddSingleton<Serilog.ILogger>(new LoggerConfiguration()
    .Destructure.WithTypeKit()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}")
    .CreateLogger());
```

```csharp
var app = builder.Build();
app.UseStatusCodePages();
app.MapOpenApi();
app.MapOrders();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<OrdersDb>().Database.EnsureCreated();
    // CREATE TABLE "Orders" (
    //     "Id" TEXT NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
    //     "Email" TEXT NOT NULL,
    //     "Quantity" INTEGER NOT NULL,
    //     "Status" TEXT NOT NULL
    // );
}
```

What changes here: a value object becomes its underlying type. The id is a `TEXT` key, as a `Guid` is
on SQLite, and the quantity an `INTEGER`. The status is its member's name. Reading a row back skips
`Create`, because the service wrote that value itself ([EF Core](ef-core.md)).

**Good to know**

- **The database lives in one open connection.** SQLite in memory keeps a database as long as its
  connection is open, so the service opens one and keeps it. Your own service passes a connection
  string instead.
- **`LogTo` prints the SQL** that EF Core runs, which is how this page shows it.
- **`RespectRequiredConstructorParameters`** makes a body that leaves out `email` a 400. Without it,
  the `Email` would be `default`, an address that never went through `Create`
  ([ASP.NET Core](aspnet-core.md#what-a-refused-value-answers)).

## Place an order

```csharp
public sealed record PlaceOrder(Email Email, Quantity Quantity);

public sealed record OrderView(OrderId Id, Email Email, Quantity Quantity, OrderStatus Status)
{
    public static OrderView Of(Order order) => new(order.Id, order.Email, order.Quantity, order.Status);
}

public enum OrderFault { NotFound, AlreadyShipped, Cancelled, Cleared }
```

```csharp
app.MapPost("/orders", async (PlaceOrder request, OrdersDb db, Serilog.ILogger log) =>
{
    var order = new Order { Id = OrderId.New(), Email = request.Email, Quantity = request.Quantity, Status = OrderStatus.Placed };
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    var view = OrderView.Of(order);
    log.Information("Placed {@Order}", view);   // Placed {"Id": "…", "Email": "a***@example.com", "Quantity": "2", "Status": "Placed", "$type": "OrderView"}
    return TypedResults.Created($"/orders/{order.Id}", view);
});
```

The requests are in [Tour.cs](../tools/ConsumerSmokeTest/Consumers/walkthrough/Tour.cs). `Send`
sends one and returns an `Answer`: its status and its body. The comment after each request is what it
gets back, with `…` for an id. The client sends the address with spaces around it:

```csharp
Answer placed = await Send("POST", "/orders", """{"email":"  ann@example.com ","quantity":2}""");   // 201 {"id":"…","email":"ann@example.com","quantity":2,"status":"Placed"}
OrderId id = JsonSerializer.Deserialize<OrderView>(placed.Body, JsonSerializerOptions.Web)!.Id;
```

Here the address crosses three boundaries:

- **On the way in**, the JSON converter calls `Create`. So the handler receives `ann@example.com`,
  trimmed, and never sees the spaces.
- **On the way out**, the JSON is the underlying type's: `"email":"ann@example.com"`, a plain
  string, and the closed set is its name ([JSON](json.md)).
- **In the log**, the address is masked. Serilog destructures `{@Order}` by its properties, and
  `Destructure.WithTypeKit()` logs each value object as what its `ToString()` prints. Without it,
  Serilog would read `Value` and log the address ([Logging and secrets](logging-and-secrets.md)).

The client reads the answer back into the same types. Its id is an `OrderId` again.

## Reject what doesn't fit

```csharp
Answer noAt = await Send("POST", "/orders", """{"email":"ann","quantity":2}""");   // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
Answer noEmail = await Send("POST", "/orders", """{"quantity":2}""");              // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
Answer notAnId = await Send("GET", "/orders/42");                                  // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
Answer noneAtAll = await Send("GET", "/orders?minimum=0");                         // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
```

Each of these is a 400 with problem details, and none of them quotes what the client sent:

- **A body** that `Create` rejects throws a `JsonException` that names `Email` and `NoAt`, never the
  text. The handler never runs.
- **A body without `email`** is rejected because of `RespectRequiredConstructorParameters`.
- **A route value** that isn't an `OrderId`, and **a query value** that `Create` rejects, fail the
  generated `TryParse`.

`UseStatusCodePages()` gives these empty 400s their problem body. The body is ASP.NET's own and
names neither the field nor the rule. ASP.NET logs the reason.

## Find an order

```csharp
app.MapGet("/orders/{id}", async (OrderId id, OrdersDb db) =>
    (await db.Orders.FirstOrNoneAsync(order => order.Id == id))   // an Option: the order, or none
        .ToResult(OrderFault.NotFound)
        .Map(OrderView.Of)
        .ToHttpResult())
   .WithoutFaults(OrderFault.AlreadyShipped, OrderFault.Cancelled, OrderFault.Cleared);   // finding one reports only NotFound
```

```csharp
Answer found = await Send("GET", $"/orders/{id}");               // 200 {"id":"…","email":"ann@example.com","quantity":2,"status":"Placed"}
Answer missing = await Send("GET", $"/orders/{OrderId.New()}");   // 404 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"fault":"NotFound","traceId":"…"}
```

`FirstOrNoneAsync` asks the database for one row and returns an `Option`, so there is no `null` to
forget. `ToResult` turns a missing order into the error `NotFound`, and `ToHttpResult()` answers it
with the status the setup maps it to, as problem details. The `fault` member names the error, so a
client can tell a missing order from any other 404 ([Result](result.md),
[ASP.NET Core](aspnet-core.md#a-result-as-a-response)).

`WithoutFaults` lists the errors this endpoint never reports. The OpenAPI document then shows only
the 404 for it, where it would otherwise list every status `OrderFault` maps to.

Nothing changes for the id here. The route value becomes an `OrderId` through `TryParse`, and the
query compares it with the key column.

**Good to know**

- **ASP0020 is a false alarm here.** The service declares its value objects in the web project, so
  ASP.NET's route analyzer reads `OrderId` before the generators make it parsable. `OrderEndpoints.cs`
  suppresses the warning. Value objects in a domain project of their own need nothing
  ([ASP.NET Core](aspnet-core.md#bind-value-objects-in-a-minimal-api)).

## Query on `.Value`

```csharp
app.MapGet("/orders", async (Quantity minimum, OrdersDb db) =>
    await db.Orders.Where(order => order.Quantity.Value >= minimum.Value)   // WHERE "o"."Quantity" >= @minimum_Value
                   .Select(order => OrderView.Of(order))
                   .ToListAsync());
```

```csharp
Answer atLeastTwo = await Send("GET", "/orders?minimum=2");   // 200 [{"id":"…","email":"ann@example.com","quantity":2,"status":"Placed"}]
Answer atLeastFive = await Send("GET", "/orders?minimum=5");  // 200 []
```

`minimum` comes from the query string, through `Create`. In the query, `order.Quantity.Value` is the
`Quantity` column itself, so the comparison runs in the database. `.Value` works the same way in memory
and in SQL ([EF Core](ef-core.md)).

## Change an order

```csharp
public sealed record OrderPatch
{
    public Patch<Quantity> Quantity { get; init; }
    public Patch<OrderStatus> Status { get; init; }
}

public static class OrderPatching
{
    public static Result<Order, OrderFault> Apply(this Order order, OrderPatch patch)
    {
        if (order.Status == OrderStatus.Shipped) return Result.Error(OrderFault.AlreadyShipped);
        if (order.Status == OrderStatus.Cancelled) return Result.Error(OrderFault.Cancelled);
        if (patch.Quantity.State == PatchState.Null || patch.Status.State == PatchState.Null) return Result.Error(OrderFault.Cleared);   // neither field can be cleared

        patch.Quantity.Tap(quantity => order.Quantity = quantity);
        patch.Status.Tap(status => order.Status = status);
        return order;
    }
}
```

```csharp
app.MapPatch("/orders/{id}", async (OrderId id, OrderPatch patch, OrdersDb db) =>
{
    var changed = (await db.Orders.FirstOrNoneAsync(order => order.Id == id))
        .ToResult(OrderFault.NotFound)
        .Bind(order => order.Apply(patch));
    await db.SaveChangesAsync();
    return changed.Map(OrderView.Of).ToHttpResult();
});
```

```csharp
Answer three = await Send("PATCH", $"/orders/{id}", """{"quantity":3}""");        // 200 {"id":"…","email":"ann@example.com","quantity":3,"status":"Placed"}
Answer zero = await Send("PATCH", $"/orders/{id}", """{"quantity":0}""");         // 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"…"}
Answer cleared = await Send("PATCH", $"/orders/{id}", """{"quantity":null}""");   // 422 {"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Unprocessable Entity","status":422,"fault":"Cleared","traceId":"…"}
Answer shipped = await Send("PATCH", $"/orders/{id}", """{"status":"Shipped"}""");   // 200 {"id":"…","email":"ann@example.com","quantity":3,"status":"Shipped"}
Answer tooLate = await Send("PATCH", $"/orders/{id}", """{"quantity":4}""");      // 409 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,"fault":"AlreadyShipped","traceId":"…"}
```

A `Patch<T>` tells apart a field the request left out, a field set to `null`, and a new value. Each
field reads its value through its own `Create`, so `{"quantity":0}` is a 400 like any other body.
`Apply` decides what a `null` means. Here neither field can be cleared, so a clear gets the error
`Cleared`. A shipped order changes no more, and gets `AlreadyShipped`
([Merge patches](merge-patches.md)).

`Bind` runs `Apply` only on an order that was found, so one `ToHttpResult()` answers every error.

## Read the OpenAPI document

```csharp
JsonNode document = JsonNode.Parse(await http.GetStringAsync("/openapi/v1.json"))!;
JsonNode schemas = document["components"]!["schemas"]!;
JsonNode find = document["paths"]!["/orders/{id}"]!["get"]!;

string idParameter = find["parameters"]![0]!["schema"]!.ToJsonString();                         // {"type":"string","format":"uuid"}
string findAnswers = string.Join(",", find["responses"]!.AsObject().Select(answer => answer.Key));   // 200,404
string emailSchema = schemas["Email"]!.ToJsonString();                                             // {"type":"string"}
string statusSchema = schemas["OrderStatus"]!.ToJsonString();                                      // {"enum":["Placed","Shipped","Cancelled"],"type":"string"}
string patchType = schemas["OrderPatch"]!["properties"]!["quantity"]!["type"]!.ToJsonString();    // ["null","integer","string"]
```

`AddTypeKit()` describes every value object as the schema of the type inside it:

- **The route's `id`** is a `uuid` string, as a `Guid` parameter would be.
- **The errors** are listed beside the success: `GET /orders/{id}` answers 200 or 404.
- **`Email`** is a string in a body, and keeps a component of its own.
- **The closed set** is a string enum of its names.
- **A patch field** is its underlying type's schema, and may also be `null`. The `string` is
  ASP.NET's own: a minimal API reads a number from a string too.

So a client generated from the document sends and receives plain strings and numbers
([ASP.NET Core](aspnet-core.md#see-value-objects-in-the-openapi-document)).

## What changes where

| Boundary | The e-mail address there |
|---|---|
| `Create` | trimmed and composed, or rejected |
| JSON, in a request or a response | the string itself |
| A route or query value | read by the generated `TryParse`, which calls `Create` |
| The database | a `TEXT` column, read back without `Create` |
| A LINQ query | `.Value` is the column |
| A rejected request | a 400 that names neither the value nor the rule |
| A log line | what `ToString()` prints: masked |
| The OpenAPI document | a string |
