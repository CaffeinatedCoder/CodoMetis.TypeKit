# Merge patches

A PATCH endpoint that takes JSON Merge Patch (RFC 7396) reads each field as one of three things: not
mentioned, so it stays as it is; `null`, so it is cleared; or a value, which replaces it.
System.Text.Json reads a missing property and a `null` alike, because it calls no converter for a
property it does not find, so every such endpoint rebuilt the difference by hand. `Patch<T>` is a
field that keeps the three apart. The [base README](../src/CodoMetis.TypeKit/README.md#merge-patches)
is its reference; this page is about using it: reading a body, applying it, the value objects inside
it, writing one, the OpenAPI document and source-generated contexts. The examples import
`CodoMetis.TypeKit`, `System.Text.Json` and `System.Text.Json.Serialization`.

## Three states

```csharp
public enum EmailFault { NoAt }

public sealed partial record Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value) : Result.Error(EmailFault.NoAt);
}

public sealed record ContactPatch(Patch<Email> Email, Patch<string?> Phone, Patch<int> Floor);
```

```csharp
ContactPatch patch = JsonSerializer.Deserialize<ContactPatch>("""{"phone":null,"floor":3}""", JsonSerializerOptions.Web)!;
PatchState email = patch.Email.State;   // Absent, not in the document
PatchState phone = patch.Phone.State;   // Null, a clear
PatchState floor = patch.Floor.State;   // Value
```

| In the document | `State` | `Match` calls | `TryGetValue` | `IfValue` runs | `IfPresent` runs | `ToOption()` |
|---|---|---|---|---|---|---|
| not mentioned | `Absent` | `onAbsent` | false | no | no | `None` |
| `null` | `Null` | `onNull` | false | no | with `null` | `None` |
| a value | `Value` | `onValue` | true | with the value | with the value | `Some` |

`default(Patch<T>)` is `Absent`: the serializer never touches a property the document leaves out, so
it keeps the struct's default, and the analyzers, which refuse a `default` of every other struct in
this package, leave a patch alone. Reading never refuses a `null`, whatever `T` is: whether a clear
is accepted is the application's rule. `ToString()` prints the state and never the value.

## Applying one

Each field's own rule says what a clear means. A required field refuses one, an optional one is
cleared by it, and a field of a value type receives the clear as `null`, never as `0`:

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

        patch.Email.IfValue(email => contact.Email = email);
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

`IfValue` runs only for a value, for a target a clear cannot reach; `IfPresent` runs for a value and
for a clear; `Match` takes all three, for a rule that has to say what each means, and `ToOption()`
is for a caller to whom a clear and an absence are the same. Refusing a clear of a required field,
as `Apply` does, says so to the client; ignoring it with `IfValue` alone would accept the request and
do less than it asked.

## A value object inside a patch

A patch reads its value with the value object's own converter, so a value object inside one keeps its
rule: a value `Create` refuses is refused as anywhere else, and the refusal does not quote it.

```csharp
JsonSerializer.Deserialize<ContactPatch>("""{"email":"nobody"}""", JsonSerializerOptions.Web);   // JsonException naming Email and NoAt, never the text
```

In an endpoint, that is the 400 any body a value object refuses gets, with problem details where the
host adds `UseStatusCodePages()` ([ASP.NET Core](aspnet-core.md#what-a-refused-value-answers)). A `null` is
not a refusal but the `Null` state, which `Apply` turns into a fault of its own, so a `Patch<Email?>`
is never needed.

## Validating one

Where the refusal should be the endpoint's own fault rather than a 400, the request carries a patch
over what was sent, and `Traverse` validates it into a patch over the domain's type: the value is
validated when the field carried one, with `Create`'s own fault, and a clear or an absent field
passes through, since there is nothing to validate. `Map` changes the value the same way and keeps
a clear and an absence as they are.

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

`CodoMetis.TypeKit.Testing` asserts the state: `ShouldBeValue()` hands the value back, `ShouldBeNull()`
is a clear and `ShouldBeAbsent()` a field the document left out ([Testing](testing.md)).

## Writing one

A client, or a test, writes a patch body with the same type. A converter cannot leave its property
out of the document, so an absent patch is refused when it is written, unless the property says to
skip a default:

```csharp
public sealed record ContactChange(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<string?> Phone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<int> Floor);
```

```csharp
string clear = JsonSerializer.Serialize(new ContactChange(Patch.Null<string?>(), Patch.Absent<int>()), JsonSerializerOptions.Web);   // {"phone":null}
string set = JsonSerializer.Serialize(new ContactChange(Patch.Absent<string?>(), Patch.Value(4)), JsonSerializerOptions.Web);       // {"floor":4}
JsonSerializer.Serialize(new ContactPatch(Patch.Absent<Email>(), Patch.Null<string?>(), Patch.Value(4)));                         // NotSupportedException naming Email and WhenWritingDefault
```

`Patch.Value(null)` is the `Null` state, so a client that holds a nullable value can pass it as it is.

## In an endpoint and in the document

```csharp
builder.Services.AddOpenApi(options => options.AddTypeKit());
app.MapPatch("/contacts/{id}", (int id, ContactPatch patch) => TypedResults.NoContent());
```

The body binds as any other: each property of `ContactPatch` is read by its patch, and one the
request leaves out is `Absent`. With `AddTypeKit()`, the document describes a patch property as its
argument's schema, inlined rather than as a component of its own, admitting `null`, and never
required, in OpenAPI 3.1 and 3.0. Null joins whatever restricts the values: the type, an enum's list
(a closed set's names, an enum written as strings), or a branch beside the argument's own branches.
A validator therefore accepts null and judges every other value as it judges the argument's. A value
object inside a patch is described through the type it wraps, so `email` is a `string`. This is not ASP.NET Core's JSON Patch (RFC 6902,
`application/json-patch+json`), which is a list of operations.

## Source-generated contexts and Native AOT

The patch's converter reads `T` itself, so a source-generated context never sees through a patch to
what it holds. A primitive, a `string`, a `Guid`, a date or a `Uri` is read with the serializer's
built-in converter and a value object with its own; any other type, a class or an enum, is listed in
the context, or the serializer refuses with its own message naming it.

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

The same holds under Native AOT, which the consumer smoke test publishes: the converter for each
closed `Patch<T>` is registered by that type's own static constructor, since a converter built by
reflection has no code there.

## What it is not

- **Not a deep merge.** Each field is its own patch, so `Patch<Address>` replaces the whole address.
  For RFC 7396's merge of a nested object, its properties are patches too: `Patch<AddressPatch>`.
- **Not an `Option`.** An option says whether a value exists, and refuses JSON; a patch says whether
  a request mentioned a field, and is a wire type. `ToOption()` crosses from one to the other.
- **Not a model.** A patch is the shape of a request; the entity it is applied to keeps its own
  types, `int?` for a floor, and its own rules.
