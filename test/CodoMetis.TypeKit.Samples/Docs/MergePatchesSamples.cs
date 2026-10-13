using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ReadmeSamples.Docs.MergePatches;

// sample: merge-patches/validate
public sealed record ContactRequest(Patch<string> Email, Patch<int> Floor);
// end sample

// sample: merge-patches/types
public enum EmailFault { NoAt }

public sealed partial record Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value) : Result.Error(EmailFault.NoAt);
}

public sealed record ContactPatch(Patch<Email> Email, Patch<string?> Phone, Patch<int> Floor);
// end sample

// sample: merge-patches/apply
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
// end sample

// sample: merge-patches/writing
public sealed record ContactChange(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<string?> Phone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<int> Floor);
// end sample

// sample: merge-patches/context
public sealed record Address(string Street, string City);

public sealed record MovePatch(Patch<Address> Address, Patch<int> Floor);

[JsonSerializable(typeof(MovePatch))]
[JsonSerializable(typeof(Address))]   // a class inside a patch is listed: the generator never sees through the patch
internal partial class MoveJson : JsonSerializerContext;
// end sample

/// <summary>The same context without <see cref="Address"/>, for the refusal the page describes.</summary>
[JsonSerializable(typeof(MovePatch))]
internal partial class MoveJsonWithoutAddress : JsonSerializerContext;

public static class Usage
{
    public static (PatchState Email, PatchState Phone, PatchState Floor) Read()
    {
        // sample: merge-patches/read
        ContactPatch patch = JsonSerializer.Deserialize<ContactPatch>("""{"phone":null,"floor":3}""", JsonSerializerOptions.Web)!;
        PatchState email = patch.Email.State;   // Absent, not in the document
        PatchState phone = patch.Phone.State;   // Null, a clear
        PatchState floor = patch.Floor.State;   // Value
        // end sample

        return (email, phone, floor);
    }

    public static (Result<ContactFault> Applied, string? Phone, int? Floor, Result<ContactFault> Refused, Contact Contact) Apply()
    {
        // sample: merge-patches/apply-use
        var contact = new Contact { Email = Email.FromKnownGood("ann@example.com"), Phone = "555-0100", Floor = 2 };
        ContactPatch clearPhone = JsonSerializer.Deserialize<ContactPatch>("""{"phone":null,"floor":3}""", JsonSerializerOptions.Web)!;
        ContactPatch clearEmail = JsonSerializer.Deserialize<ContactPatch>("""{"email":null}""", JsonSerializerOptions.Web)!;

        Result<ContactFault> applied = contact.Apply(clearPhone);   // Success
        string? phone = contact.Phone;                               // null, cleared
        int? floor = contact.Floor;                                  // 3
        Result<ContactFault> refused = contact.Apply(clearEmail);   // Error: EmailRequired
        // end sample

        return (applied, phone, floor, refused, contact);
    }

    public static void Refused()
    {
        // sample: merge-patches/value-object
        JsonSerializer.Deserialize<ContactPatch>("""{"email":"nobody"}""", JsonSerializerOptions.Web);   // JsonException naming Email and NoAt, never the text
        // end sample
    }

    public static (Result<Patch<Email>, EmailFault> Refused, Result<Patch<Email>, EmailFault> Clear, Result<Patch<Email>, EmailFault> Untouched, Patch<int> Tenfold) Validate()
    {
        // sample: merge-patches/validate-use
        ContactRequest nobody = JsonSerializer.Deserialize<ContactRequest>("""{"email":"nobody"}""", JsonSerializerOptions.Web)!;
        ContactRequest cleared = JsonSerializer.Deserialize<ContactRequest>("""{"email":null}""", JsonSerializerOptions.Web)!;
        ContactRequest moved = JsonSerializer.Deserialize<ContactRequest>("""{"floor":3}""", JsonSerializerOptions.Web)!;

        Result<Patch<Email>, EmailFault> refused = nobody.Email.Traverse(Email.Create);    // Error: NoAt, the endpoint's own fault
        Result<Patch<Email>, EmailFault> clear = cleared.Email.Traverse(Email.Create);     // Success: Null, nothing to validate
        Result<Patch<Email>, EmailFault> untouched = moved.Email.Traverse(Email.Create);   // Success: Absent
        Patch<int> tenfold = moved.Floor.Map(floor => floor * 10);                          // Value: 30, a clear and an absence kept as they are
        // end sample

        return (refused, clear, untouched, tenfold);
    }

    public static (string Clear, string Set) Write()
    {
        // sample: merge-patches/writing-use
        string clear = JsonSerializer.Serialize(new ContactChange(Patch.Null<string?>(), Patch.Absent<int>()), JsonSerializerOptions.Web);   // {"phone":null}
        string set = JsonSerializer.Serialize(new ContactChange(Patch.Absent<string?>(), Patch.Value(4)), JsonSerializerOptions.Web);       // {"floor":4}
        // end sample

        return (clear, set);
    }

    /// <summary>The block's last line, which throws: the same region, apart, so the lines above can run.</summary>
    public static void WriteRefused()
    {
        // sample: merge-patches/writing-use
        JsonSerializer.Serialize(new ContactPatch(Patch.Absent<Email>(), Patch.Null<string?>(), Patch.Value(4)));                         // NotSupportedException naming Email and WhenWritingDefault
        // end sample
    }

    public static void Configure(WebApplicationBuilder builder)
    {
        // sample: merge-patches/endpoint
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        // end sample
    }

    public static void Map(WebApplication app)
    {
        // sample: merge-patches/endpoint
        app.MapPatch("/contacts/{id}", (int id, ContactPatch patch) => TypedResults.NoContent());
        // end sample
    }

    public static (string City, string Floor) Context()
    {
        // sample: merge-patches/context-use
        MovePatch move = JsonSerializer.Deserialize("""{"Address":{"Street":"Main 1","City":"Bern"}}""", MoveJson.Default.MovePatch)!;
        string city = move.Address.Match(address => address.City, () => "cleared", () => "unchanged");   // "Bern"
        string floor = move.Floor.Match(f => f.ToString(), () => "cleared", () => "unchanged");           // "unchanged"
        // end sample

        return (city, floor);
    }
}

public sealed class MergePatchesTests
{
    private const string Page = "docs/merge-patches.md";

    private static string Quoted(string text) => $"\"{text}\"";

    private static string Outcome(Result<ContactFault> result) => result.Match(() => "Success", fault => $"Error: {fault}");

    [Fact]
    public void A_body_reads_as_the_three_states_the_comments_say()
    {
        var (email, phone, floor) = Usage.Read();

        email.ShouldBeShownIn(Page, state => state.ToString(), "patch.Email.State");
        phone.ShouldBeShownIn(Page, state => state.ToString(), "patch.Phone.State");
        floor.ShouldBeShownIn(Page, state => state.ToString(), "patch.Floor.State");
    }

    [Fact]
    public void Applying_a_patch_does_what_the_comments_say()
    {
        var (applied, phone, floor, refused, contact) = Usage.Apply();

        applied.ShouldBeShownIn(Page, Outcome, "contact.Apply(clearPhone)");
        phone.ShouldBeShownIn(Page, p => p ?? "null", "contact.Phone");
        floor.ShouldBeShownIn(Page, f => f?.ToString() ?? "null", "contact.Floor");
        refused.ShouldBeShownIn(Page, Outcome, "contact.Apply(clearEmail)");

        contact.Email.Value.ShouldBe("ann@example.com", "an absent field leaves the value as it is");
    }

    /// <summary>A struct's clear is null, not its default: <c>IfPresent</c> hands a <c>Patch&lt;int&gt;</c>'s clear over as <c>int?</c>.</summary>
    [Fact]
    public void A_cleared_struct_field_is_null_not_zero()
    {
        var contact = new Contact { Email = Email.FromKnownGood("a@b"), Floor = 2 };

        _ = contact.Apply(JsonSerializer.Deserialize<ContactPatch>("""{"floor":null}""", JsonSerializerOptions.Web)!);

        contact.Floor.ShouldBeNull();
    }

    [Fact]
    public void Validating_a_patch_answers_what_the_comments_say()
    {
        var (refused, clear, untouched, tenfold) = Usage.Validate();
        string Validated(Result<Patch<Email>, EmailFault> result) => result.Match(patch => $"Success: {patch.State}", fault => $"Error: {fault}");

        refused.ShouldBeShownIn(Page, Validated, "nobody.Email.Traverse(Email.Create)");
        clear.ShouldBeShownIn(Page, Validated, "cleared.Email.Traverse(Email.Create)");
        untouched.ShouldBeShownIn(Page, Validated, "moved.Email.Traverse(Email.Create)");
        tenfold.ShouldBeShownIn(Page, patch => patch.Match(value => $"Value: {value}", () => "Null", () => "Absent"), "moved.Floor.Map(floor => floor * 10)");
    }

    [Fact]
    public void A_value_object_inside_a_patch_keeps_its_rule()
    {
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<ContactPatch>("""{"email":"nobody"}""", JsonSerializerOptions.Web), Page, input: "nobody");
        Should.Throw<JsonException>(Usage.Refused).Path.ShouldBe("$.email", "the page says the path is the field's");
    }

    [Fact]
    public void Writing_a_patch_answers_what_the_comments_say()
    {
        var (clear, set) = Usage.Write();

        clear.ShouldBeShownIn(Page, json => json, "JsonSerializer.Serialize(new ContactChange(Patch.Null<string?>(), Patch.Absent<int>()), JsonSerializerOptions.Web)");
        set.ShouldBeShownIn(Page, json => json, "JsonSerializer.Serialize(new ContactChange(Patch.Absent<string?>(), Patch.Value(4)), JsonSerializerOptions.Web)");
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Serialize(new ContactPatch(Patch.Absent<Email>(), Patch.Null<string?>(), Patch.Value(4))), Page, input: "555");
        Should.Throw<NotSupportedException>(Usage.WriteRefused);
    }

    /// <summary>What the page says of the document: every patch property inlined as its argument's schema, admitting null, none required, an Email as the string it wraps.</summary>
    [Fact]
    public async Task The_document_describes_a_patch_as_the_page_says()
    {
        var document = await SampleOutputs.OpenApiDocumentAsync(Usage.Configure, Usage.Map);
        var patch = document.Component(nameof(ContactPatch));

        patch["required"].ShouldBeNull(patch.ToJsonString());
        foreach (var (name, type) in new[] { ("email", "string"), ("phone", "string"), ("floor", "integer") })
        {
            var property = patch["properties"]![name]!;
            property["$ref"].ShouldBeNull($"{name} is inlined: {property.ToJsonString()}");
            // The argument's own schema, admitting null: ASP.NET's web defaults describe an int as ["integer","string"].
            var types = property["type"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
            types.ShouldContain("null", $"{name}: {property.ToJsonString()}");
            types.ShouldContain(type, $"{name}: {property.ToJsonString()}");
        }
    }

    [Fact]
    public void A_source_generated_context_reads_a_patch_as_the_comments_say()
    {
        var (city, floor) = Usage.Context();

        city.ShouldBeShownIn(Page, Quoted, "move.Address.Match(address => address.City, () => \"cleared\", () => \"unchanged\")");
        floor.ShouldBeShownIn(Page, Quoted, "move.Floor.Match(f => f.ToString(), () => \"cleared\", () => \"unchanged\")");

        // Without the class listed, the serializer refuses with its own message naming it, as the page says.
        Should.Throw<Exception>(() => JsonSerializer.Deserialize("""{"Address":{"Street":"Main 1","City":"Bern"}}""", MoveJsonWithoutAddress.Default.MovePatch))
              .Message.ShouldContain("Address");
    }
}
