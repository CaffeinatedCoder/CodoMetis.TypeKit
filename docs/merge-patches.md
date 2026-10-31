# Merge patches

`Patch<T>` lets a PATCH endpoint tell three things apart: a field the request didn't mention, a field
set to `null`, and a field with a new value. That's what JSON Merge Patch (RFC 7396) asks for.
Unmentioned fields stay as they are, `null` clears a field, and a value replaces it.

System.Text.Json can't tell a missing property from a `null` on its own, because it calls no
converter for a property it doesn't find. So every such endpoint used to rebuild the difference by
hand. A `Patch<T>` field keeps the three apart for you.

This page shows how to read a body, apply it, validate the value objects inside it, write one, and
describe it in the OpenAPI document and in source-generated contexts. The
[base README](../src/CodoMetis.TypeKit/README.md#merge-patches) is the reference. The examples import
`CodoMetis.TypeKit`, `System.Text.Json` and `System.Text.Json.Serialization`.

## Tell absent, null and a value apart

```csharp
public enum EmailFault { NoAt }

public sealed partial record Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value) : Result.Error(EmailFault.NoAt);
}

public sealed record ContactPatch(Patch<Email> Email, Patch<string> Phone, Patch<int> Floor);
```

```csharp
ContactPatch patch = JsonSerializer.Deserialize<ContactPatch>("""{"phone":null,"floor":3}""", JsonSerializerOptions.Web)!;
PatchState email = patch.Email.State;   // Absent, not in the document
PatchState phone = patch.Phone.State;   // Null, a clear
PatchState floor = patch.Floor.State;   // Value
```

| In the document | `State` | `Match` calls | `TryGetValue` | `Tap` runs | `IfPresent` runs | `ToOption()` |
|---|---|---|---|---|---|---|
| not mentioned | `Absent` | `onAbsent` | false | no | no | `None` |
| `null` | `Null` | `onNull` | false | no | with `null` | `None` |
| a value | `Value` | `onValue` | true | with the value | with the value | `Some` |

**Good to know**

- **`default(Patch<T>)` is `Absent`.** The serializer never touches a property the document leaves
  out, so it keeps the struct's default. That's why the analyzers, which reject a `default` of every
  other struct in this package, leave a patch alone.
- **Reading always accepts a `null`, whatever `T` is.** Whether a clear is allowed is your
  application's rule.
- **`ToString()` prints the state**, never the value.

## Apply a patch

```csharp
public sealed class Contact
{
    public required Email Email { get; set; }
    public string? Phone { get; set; }
    public int? Floor { get; set; }
}

public enum ContactFault { EmailRequired }

public static class ContactPatching
{
    public static Result<ContactFault> Apply(this Contact contact, ContactPatch patch)
    {
        if (patch.Email.State == PatchState.Null) return ContactFault.EmailRequired;   // a required field refuses a clear

        patch.Email.Tap(email => contact.Email = email);
        patch.Phone.IfPresent(phone => contact.Phone = phone);   // an optional one: null clears it
        patch.Floor.IfPresent(floor => contact.Floor = floor);   // a struct's clear arrives as int?, null, never as 0
        return Result.Success();
    }
}
```

```csharp
var contact = new Contact { Email = Email.FromKnownGood("ann@example.com"), Phone = "555-0100", Floor = 2 };
ContactPatch clearPhone = JsonSerializer.Deserialize<ContactPatch>("""{"phone":null,"floor":3}""", JsonSerializerOptions.Web)!;
ContactPatch clearEmail = JsonSerializer.Deserialize<ContactPatch>("""{"email":null}""", JsonSerializerOptions.Web)!;

Result<ContactFault> applied = contact.Apply(clearPhone);   // Success
string? phone = contact.Phone;                               // null, cleared
int? floor = contact.Floor;                                  // 3
Result<ContactFault> refused = contact.Apply(clearEmail);   // Error: EmailRequired
```

Each field's own rule decides what a clear means. A required field rejects one, and an optional field
is cleared by it. A field of a value type receives the clear as `null`, never as `0`.

Pick the member that fits the field:

- **`Tap`** runs only for a value, as `Option`'s `Tap` does. Use it for a target a clear can't reach.
- **`IfPresent`** runs for a value and for a clear.
- **`Match`** takes all three, for a rule that has to say what each one means.
- **`ToOption()`** is for code that treats a clear and an absence the same.

`Tap` and `IfPresent` both return the patch unchanged.

Rejecting a clear of a required field, as `Apply` does, tells the client what went wrong. Ignoring it
with `Tap` alone would accept the request and quietly do less than it asked.

## A value object inside a patch

A patch reads its value with the value object's own converter, so the value object keeps its rule. A
value `Create` rejects is rejected here as anywhere else, and the error doesn't quote it.

```csharp
JsonSerializer.Deserialize<ContactPatch>("""{"email":"nobody"}""", JsonSerializerOptions.Web);   // JsonException naming Email and NoAt, never the text
```

The exception's `Path` is the field's, `$.email`, as it is for any property. So your answer to the
client can name the field. In an endpoint, the client gets the same 400 as for any body a value
object rejects, with problem details where the host adds `UseStatusCodePages()`
([ASP.NET Core](aspnet-core.md#what-a-refused-value-answers)).

**Good to know**

- **A `null` isn't an error.** It's the `Null` state, which `Apply` turns into an error of its own.
  So you never need a `Patch<Email?>`.
- **A patch's `T` is `notnull`, as an option's is.** `Patch<Email?>` or `Patch<string?>` gets a
  warning at the declaration (CS8714). Over the value's own type, every member hands you the value as
  it is, never null.

## Validate a patch

```csharp
public sealed record ContactRequest(Patch<string> Email, Patch<int> Floor);
```

```csharp
ContactRequest nobody = JsonSerializer.Deserialize<ContactRequest>("""{"email":"nobody"}""", JsonSerializerOptions.Web)!;
ContactRequest cleared = JsonSerializer.Deserialize<ContactRequest>("""{"email":null}""", JsonSerializerOptions.Web)!;
ContactRequest moved = JsonSerializer.Deserialize<ContactRequest>("""{"floor":3}""", JsonSerializerOptions.Web)!;

Result<Patch<Email>, EmailFault> refused = nobody.Email.Traverse(Email.Create);    // Error: NoAt, the endpoint's own fault
Result<Patch<Email>, EmailFault> clear = cleared.Email.Traverse(Email.Create);     // Success: Null, nothing to validate
Result<Patch<Email>, EmailFault> untouched = moved.Email.Traverse(Email.Create);   // Success: Absent
Patch<int> tenfold = moved.Floor.Map(floor => floor * 10);                          // Value: 30, a clear and an absence kept as they are
```

Sometimes the endpoint should answer with its own error type rather than a 400. Then let the request
carry a patch over what was sent, and validate it with `Traverse` into a patch over your domain type:

- **A value** is validated, and a failure gives `Create`'s own error.
- **A clear or an absent field** passes through, since there's nothing to validate.

`Map` changes the value the same way, and keeps a clear and an absence as they are.

`CodoMetis.TypeKit.Testing` checks the state in tests. `ShouldBeValue()` hands the value back,
`ShouldBeNull()` expects a clear, and `ShouldBeAbsent()` a field the document left out
([Testing](testing.md)).

## Write a patch

```csharp
public sealed record ContactChange(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<string> Phone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<int> Floor);
```

```csharp
string clear = JsonSerializer.Serialize(new ContactChange(Patch.Null<string>(), Patch.Absent<int>()), JsonSerializerOptions.Web);   // {"phone":null}
string set = JsonSerializer.Serialize(new ContactChange(Patch.Absent<string>(), Patch.Value(4)), JsonSerializerOptions.Web);       // {"floor":4}
JsonSerializer.Serialize(new ContactPatch(Patch.Absent<Email>(), Patch.Null<string>(), Patch.Value(4)));                         // NotSupportedException naming Email and WhenWritingDefault
```

A client, or a test, writes a patch body with the same type. A converter can't leave its property out
of the document. So writing an absent patch throws a `NotSupportedException`, unless the property says
to skip a default, as above.

`Patch.Value(null)` is the `Null` state, so a client that holds a nullable value can pass it as it is.

## Use it in an endpoint and in the document

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());
app.MapPatch("/contacts/{id}", (int id, ContactPatch patch) => TypedResults.NoContent());
```

The body binds like any other. Each property of `ContactPatch` is read by its patch, and one the
request leaves out is `Absent`.

With `AddTypeKit()`, the OpenAPI document describes a patch property as its argument's schema, in
OpenAPI 3.1 and 3.0:

- **Inlined**, rather than as a component of its own.
- **Admitting `null`.** The null joins whatever restricts the values: the type, an enum's list (a
  closed set's names, an enum written as strings), or a branch beside the argument's own branches. So
  a validator accepts null, and judges every other value as it judges the argument's.
- **Not required.** Under `RespectRequiredConstructorParameters`, make a patch an `init` property
  ([below](#required-constructor-parameters)).

A value object inside a patch is described through its underlying type, so `email` is a `string`.

A patch inside a list or a dictionary is described the same way, element by element. So a
`Dictionary<string, Patch<string>>`, where a request sets a key with a value and clears it with `null`,
is an object whose values are a string or `null`.

**Good to know**

- **This isn't ASP.NET Core's JSON Patch** (RFC 6902, `application/json-patch+json`), which is a list
  of operations.

## Required constructor parameters

```csharp
public sealed record StrictContactPatch
{
    public Patch<Email> Email { get; init; }
    public Patch<string> Phone { get; init; }
    public Patch<int> Floor { get; init; }
}
```

```csharp
var strict = new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };
StrictContactPatch moved = JsonSerializer.Deserialize<StrictContactPatch>("""{"floor":3}""", strict)!;
PatchState untouched = moved.Phone.State;                             // Absent
JsonSerializer.Deserialize<ContactPatch>("""{"floor":3}""", strict);   // JsonException naming ContactPatch and phone
```

`RespectRequiredConstructorParameters` is the option that rejects a body without a value object
([JSON](json.md)). It makes every constructor parameter without a default required, a patch's too. So
a request that leaves a field out, the very state a patch exists for, gets rejected.

Declare the patch fields as `init` properties instead. The option requires constructor parameters
only, and a property the request leaves out keeps its default, which is `Absent`.

The document follows the serializer. `AddTypeKit()` leaves a patch out of `required` where the
serializer lets it be absent, and keeps it there where the options reject a body without it. So the
document never calls a field optional when the endpoint rejects a request without it.

**Good to know**

- **`= default` on a patch parameter breaks the document.** It satisfies the serializer too. But
  System.Text.Json's schema exporter, which ASP.NET Core builds the document with, throws on a
  parameter of a struct type whose default is `default`. So the endpoint's document fails.

## Source-generated contexts and Native AOT

```csharp
public sealed record Address(string Street, string City);

public sealed record MovePatch(Patch<Address> Address, Patch<int> Floor);

[JsonSerializable(typeof(MovePatch))]
[JsonSerializable(typeof(Address))]   // a class inside a patch is listed: the generator never sees through the patch
internal partial class MoveJson : JsonSerializerContext;
```

```csharp
MovePatch move = JsonSerializer.Deserialize("""{"Address":{"Street":"Main 1","City":"Bern"}}""", MoveJson.Default.MovePatch)!;
string city = move.Address.Match(address => address.City, () => "cleared", () => "unchanged");   // "Bern"
string floor = move.Floor.Match(f => f.ToString(), () => "cleared", () => "unchanged");           // "unchanged"
```

The patch's converter reads `T` itself, so a source-generated context never sees through a patch to
what it holds. What that means for `T`:

- **A primitive, a `string`, a `Guid`, a date or a `Uri`** is read with the serializer's built-in
  converter.
- **A value object** is read with its own converter.
- **Any other type, a class or an enum,** must be listed in the context. Otherwise the serializer
  throws with its own message naming the type.

The same holds under Native AOT, which the consumer smoke test publishes. The converter for each
closed `Patch<T>` is registered by that type's own static constructor, because a converter built by
reflection has no code there.

## What it is not

- **Not a deep merge.** Each field is its own patch, so `Patch<Address>` replaces the whole address.
  For RFC 7396's merge of a nested object, make its properties patches too: `Patch<AddressPatch>`.
- **Not an `Option`.** An option says whether a value exists, and it rejects JSON. A patch says
  whether a request mentioned a field, and it's made for the wire. `ToOption()` crosses from one to
  the other.
- **Not a model.** A patch is the shape of a request. The entity you apply it to keeps its own types,
  like `int?` for a floor, and its own rules.
