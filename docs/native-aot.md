# Native AOT

Every TypeKit package works in a trimmed or Native AOT application. The one exception is the query
helpers on an EF Core query ([EF Core](#ef-core) below). This page shows what Native AOT asks of an
application that uses value objects. Each package README has its own section too.

Every package builds with the trim and AOT analyzers on, and warnings as errors. But the analyzers
can't see the code the generators add to your project, and they can't tell whether a run-time path
still works. So on every CI build, the consumer smoke test publishes two apps with `PublishAot` and
runs the native binaries: a web host with JSON and OpenAPI, and a console app with EF Core's compiled
model.

## The project

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <TrimmerSingleWarn>false</TrimmerSingleWarn>   <!-- one line per warning, naming the member, instead of one per assembly -->
  <!-- EF Core's precompiled queries are interceptors: -->
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.EntityFrameworkCore.GeneratedInterceptors</InterceptorsNamespaces>
</PropertyGroup>
```

```bash
dotnet publish -c Release
```

**Good to know**

- **A warning that names a package** (`CodoMetis.*`) **or a generated member** (your value object's
  namespace) is a bug in this repository. Please report it.
- **A warning that names EF's `DbContext` constructor** comes from EF, and EF's documentation covers
  it.

## JSON contexts

```csharp
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Quantity : IValue<int>;

public sealed record OrderLine(OrderId Order, Quantity Quantity);

[JsonSerializable(typeof(OrderLine))]   // the shape; neither Guid nor int is needed for the JSON
internal partial class OrdersJson : JsonSerializerContext;
```

```csharp
string json = JsonSerializer.Serialize(new OrderLine(OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee")), Quantity.From(3)), OrdersJson.Default.OrderLine);   // {"Order":"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee","Quantity":3}
OrderLine back = JsonSerializer.Deserialize(json, OrdersJson.Default.OrderLine)!;
bool roundTrip = back == new OrderLine(OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee")), Quantity.From(3));   // true
```

Native AOT serializes through a source-generated `JsonSerializerContext`. In it, list the value
objects, or the types that hold them, and not the types they wrap. The generated converter builds the
underlying type's contract itself, from a converter on the options, the type's own `[JsonConverter]`,
or the serializer's built-in one.

The one exception is an enum. A context's `UseStringEnumConverter` doesn't reach an enum the context
doesn't list. So a value object over an enum is written as a number, unless you list the enum too.
[JSON](json.md#source-generated-contexts) shows both contexts and their output.

**Good to know**

- **`Option` and `Result` reject JSON here** exactly as they do on the JIT.
- **`StoredJsonConverterFactory`** still reads without the rules.
- **Your own converter over an open `T`** has to be one converter per closed type, not a factory. A
  factory builds the converter for each `T` with `MakeGenericType`
  ([JSON](json.md#option-and-result-at-the-wire)).
- **Two nested types with the same simple name clash in one context.** A value object or a closed set
  `Billing.Status` beside `Shipping.Status` gives the context two properties of one name. The source
  generator reports SYSLIB1031, and serializing the second type throws `NotSupportedException`. Name
  one of them with
  `[JsonSerializable(typeof(Shipping.Status), TypeInfoPropertyName = "ShippingStatus")]`.

## OpenAPI

The OpenAPI document needs more than the JSON does. ASP.NET builds a value object's schema from the
JSON contract of its underlying type. A context has no contract for a type the host never serializes
itself. So the host's context lists every type its value objects wrap as well.

If one is missing, generating the document fails with an `InvalidOperationException` that names the
value object and the `[JsonSerializable]` to add, rather than describing it as `{}`. The
[README](../src/CodoMetis.TypeKit.AspNetCore/README.md#native-aot) shows the context.

A problem response from `ToHttpResult()` needs `AddProblemDetails()`, which `AddFaultResponses`
calls for you.

## EF Core

EF Core runs under Native AOT only through a compiled model and precompiled queries:

```bash
dotnet ef dbcontext optimize --precompile-queries --nativeaot
```

`UseTypeKit()` works there. In the native binary, the smoke test's console app inserts through an
identity key, filters on `.Value`, binds a value-object parameter, and reads a stored value back
without the rules.

EF has its own limitations here ([EF Core](ef-core.md#compiled-models)):

- **A struct value object that wraps a reference type can't be a property of a compiled model.** Make
  such a value object on an entity a `sealed partial record`.
- **A sealed entity class doesn't compile** in precompiled queries.
- **A converter named in `HasConversion` isn't what precompiled queries cast to.** So keep the mapping
  that `UseTypeKit()` sets up.

### Query helpers

EF precompiles a query where your application calls one of EF's operators, and runs no other. The
query helpers compose their query inside the package, where EF never looks. So these throw
`InvalidOperationException: Query wasn't precompiled and dynamic code isn't supported with NativeAOT.`:

- **On a query:** `FirstOrNone`, `FirstOrError`, `SingleOrNone` and `ElementAtOrNone`.
- **From the EF Core package:** `FirstOrNoneAsync` and `SingleOrNoneAsync`.

Write the operators at the call site instead, and take the option from the list:
`(await db.Orders.Where(o => o.Total > 100).Take(1).ToListAsync()).FirstOrNone()`. The smoke test's
console app measures both the exception and that call.

## Configuration binding

```csharp
public enum HostFault { Blank }

public readonly partial record struct SmtpHost : IValidatedValue<SmtpHost, string, HostFault>
{
    public static Result<SmtpHost, HostFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result.Error(HostFault.Blank) : new SmtpHost(value.Trim());
}
```

```csharp
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Host"] = " mail.example.com " }).Build();

Result<SmtpHost, HostFault> host = SmtpHost.Create(configuration["Smtp:Host"] ?? "");            // the wrapped type bound, then Create
string bound = host.Match(h => h.Value, fault => fault.ToString());                                 // "mail.example.com"
```

`PublishAot` and `PublishTrimmed` turn on the configuration binding source generator. It binds only
what it can see. It doesn't bind a value object, and leaves the property `default`. So bind the
underlying type and apply `Create`, as above.

Outside Native AOT, the reflection-based binder binds a value object through its generated
`TypeConverter`. The line above works in both.

A closed set is the same. The reflection-based binder reads it through its type converter, and the
source generator doesn't. So bind its name as a `string` and `Parse` it.

## Written text

`AsText()` composes to Unicode normalization form C, using the runtime's globalization data. A Native
AOT project often turns that data off with `<InvariantGlobalization>true</InvariantGlobalization>`, as
the `webapiaot` template does. In that mode `string.Normalize` returns its input unchanged. So
`AsText()` throws `PlatformNotSupportedException` on its first call, whatever the string, rather than
keep one name in two spellings.

If your application calls `AsText()`:

- **Set `InvariantGlobalization` to `false`.**
- **Make ICU available to the process**: `icu-libs` on Alpine, `libicu` on Debian and Ubuntu. ICU
  installed while the mode stays on does nothing.

`AsIdentifier()` composes nothing and works in either mode. But it's the guard for a value another
system issued, not for written text
([Written text](text.md#globalization-invariant-mode)).

## Serilog

`Destructure.WithTypeKit()` works under Native AOT. A value object or a closed set is logged as what
it prints.

Serilog's own destructuring of other objects reads their properties by reflection, which it reports
(IL2072). A record whose properties the trimmer removed is logged empty, `Held {  }`. Log such an
object's fields by name, or keep its properties with `[DynamicallyAccessedMembers]`.

## Discovery without reflection

```csharp
static string Describe(Type type) =>
    type.GetCustomAttribute<GeneratedValueObjectAttribute>() is { } valueObject
        ? $"{valueObject.ValueObjectType.Name} wraps {valueObject.WrappedType.Name}"
        : $"{type.Name} is no value object";
```

```csharp
string described = Describe(typeof(OrderId));   // "OrderId wraps Guid"
string plain = Describe(typeof(Guid));          // "Guid is no value object"
```

The EF Core and ASP.NET Core packages recognise a value object at run time by the attribute the
generators put on it, `GeneratedValueObjectAttribute<TValueObject, T>`. Its type arguments are
constrained to the value-object interface. When your own code is handed a `Type`, do the same, as
above.

Never use `GetInterfaces()` for this. Trimming removes an interface nothing in the application uses,
and nothing uses `IValueObject<OrderId, Guid>`. So under Native AOT, every value object once looked
like no value object at all.

When your code needs the two types as type parameters, call `Accept` with an
`IValueObjectVisitor<TResult>`, and it's handed them, without `MakeGenericType`. The attribute lives
in `CodoMetis.TypeKit.CompilerServices`. It's hidden from IntelliSense, because only this kind of code
needs it.

## What the smoke test measures

`test/consumer-smoke-test.sh` does this:

1. **Packs the packages** and installs them into throwaway consumers outside the repository.
2. **Publishes the web consumer and the data consumer** with Native AOT.
3. **Keeps every trim or AOT warning** that names a package or a generated member.
4. **Checks the native binaries' output**: JSON through a context that lists no wrapped type, the
   generated converter applying `Create`, the OpenAPI component, parameter and list schemas, a route
   parameter bound, and EF Core's insert, filter, parameter and identity key.

It needs the platform's linker, clang, and zlib on Linux. An `[UnconditionalSuppressMessage]` in a
package needs a measured reason, and that consumer is the measurement.
