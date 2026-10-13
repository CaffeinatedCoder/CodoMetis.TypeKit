# Native AOT

The packages work in a trimmed or Native AOT application, with one exception, the query helpers on
an EF Core query ([EF Core](#ef-core) below), and every package builds with the trim and AOT analyzers
on, warnings as errors. The analyzers cannot see the code the generators weave into a consumer, nor
tell that a run-time path still works, so the consumer smoke test publishes two consumers with
`PublishAot` on every CI build, a web host with JSON and OpenAPI and a console application with EF
Core's compiled model, and runs the native binaries. This page is what Native AOT asks of an
application that uses value objects; each package README has its own section.

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

A warning that names a package (`CodoMetis.…`) or a woven member (your value object's namespace) is
a bug in this repository; please report it. A warning that names EF's `DbContext` constructor is
EF's, and its documentation covers it.

## JSON contexts

Native AOT serializes through a source-generated `JsonSerializerContext`. List the value objects, or
the types that hold them, not what they wrap: the generated converter builds the wrapped type's
contract itself, from a converter on the options, the type's own `[JsonConverter]` or the
serializer's built-in one. The one exception is an enum: a context's `UseStringEnumConverter` does not
reach an enum the context does not list, so an enum-backed value object is written as a number
unless the enum is listed too. [JSON](json.md#source-generated-contexts) shows both contexts and
their output.

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

`Option` and `Result` refuse JSON there exactly as on the JIT, and `StoredJsonConverterFactory`
reads without the rules. A converter of your own over an open `T` has to be a converter per closed
type, not a factory: a factory builds the converter for each `T` by `MakeGenericType`
([JSON](json.md#option-and-result-at-the-wire)).

## OpenAPI

The document asks for something the JSON does not. ASP.NET builds a value object's schema from its
JSON contract for the wrapped type, and a context has no contract for a type the host never
serializes itself, so the host's context lists every type its value objects wrap as well. Without
one, generating the document fails with an `InvalidOperationException` that names the value object
and the `[JsonSerializable]` to add, rather than describing it as `{}`. The
[README](../src/CodoMetis.TypeKit.AspNetCore/README.md#native-aot) shows the context. A problem
response from `ToHttpResult()` needs `AddProblemDetails()`, which `AddFaultResponses` calls.

## EF Core

EF Core runs under Native AOT only through a compiled model and precompiled queries:

```bash
dotnet ef dbcontext optimize --precompile-queries --nativeaot
```

`UseTypeKit()` works there, and the smoke test's console consumer inserts through an identity key,
filters on `.Value`, binds a value-object parameter and reads a stored value back without the rules,
in the native binary. EF's own limitations: a struct value object that wraps a reference type cannot
be a property of a compiled model, so such a value object on an entity is a `sealed partial record`;
a sealed entity class does not compile in precompiled queries; and a converter named in
`HasConversion` is not what the precompiled queries cast to, so the mapping stays `UseTypeKit()`'s
([EF Core](ef-core.md#compiled-models)).

EF precompiles a query where the application calls one of EF's operators, and runs no other. The
query helpers compose their query inside the package, where it never looks: `FirstOrNone`,
`FirstOrError`, `SingleOrNone` and `ElementAtOrNone` on a query, and the EF satellite's
`FirstOrNoneAsync` and `SingleOrNoneAsync`, throw `InvalidOperationException: Query wasn't
precompiled and dynamic code isn't supported with NativeAOT.` Write the operators at the call site
and take the option of the list: `(await db.Orders.Where(o => o.Total > 100).Take(1).ToListAsync()).FirstOrNone()`.
The smoke test's consumer measures both the refusal and that call.

## Configuration binding

`PublishAot` and `PublishTrimmed` turn on the configuration binding source generator, which binds
only what it can see: it does not bind a value object, and leaves the property `default`. Bind the
wrapped type and apply `Create`:

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

The reflection-based binder, outside Native AOT, binds a value object through its generated
`TypeConverter`; the line above works in both. A closed set is the
same: the reflection-based binder reads it through its type converter, the source generator does not,
so bind its name as a `string` and `Parse` it.

## Discovery without reflection

The satellites recognise a value object at run time by the attribute the generators put on it,
`GeneratedValueObjectAttribute<TValueObject, T>`, whose type arguments are constrained to the
value-object interface. Never by `GetInterfaces()`: trimming removes an interface nothing in the
application uses, and nothing uses `IValueObject<OrderId, Guid>`, so under Native AOT every value
object once looked like no value object at all. Code of your own that is handed a `Type` does the
same:

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

Code that needs the two types as type parameters calls `Accept` with an `IValueObjectVisitor<T>`
and is handed them, without `MakeGenericType`. The attribute lives in
`CodoMetis.TypeKit.CompilerServices`, hidden from IntelliSense because nothing but this kind of code
needs it.

## What the smoke test measures

`test/consumer-smoke-test.sh` packs the packages, installs them into throwaway consumers outside the
repository, publishes the web and the data consumer with Native AOT, keeps every trim or AOT warning
that names a package or a woven member, and asserts on the native binaries' output: JSON through a
context that lists no wrapped type, the generated converter applying `Create`, the OpenAPI
component, parameter and list schemas, a route parameter bound, EF Core's insert, filter, parameter
and identity key. It needs the platform's linker, clang, and zlib on Linux. An
`[UnconditionalSuppressMessage]` in a package needs a measured reason, and that consumer is the
measurement.
