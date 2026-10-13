using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="Patch{T}"/>, a JSON Merge Patch field: absent, null or a value. Measured first in
/// <c>spikes/MergePatch</c>, under the reflection resolver and a source-generated context, and under
/// Native AOT; each claim there is a test here, Native AOT in the consumer smoke test.
/// </summary>
public sealed class PatchTests
{
    private static readonly JsonSerializerOptions Reflection = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    private static readonly JsonSerializerOptions Generated  = new(JsonSerializerDefaults.Web) { TypeInfoResolver = PatchJson.Default };

    public static TheoryData<string> Resolvers => ["reflection", "source-generated"];

    private static JsonSerializerOptions Options(string resolver) => resolver == "reflection" ? Reflection : Generated;

    private static string Show<T>(Patch<T> patch) => patch.Match(value => $"Value({value})", () => "Null", () => "Absent");

    [Fact]
    public void Default_is_absent_and_the_states_are_told_apart()
    {
        Patch<int> absent = default;

        absent.State.ShouldBe(PatchState.Absent);
        Patch.Absent<int>().ShouldBe(absent);
        Patch.Null<int>().State.ShouldBe(PatchState.Null);
        Patch.Value(3).State.ShouldBe(PatchState.Value);
        Patch.Value<string?>(null).State.ShouldBe(PatchState.Null, "a null value is the Null state");
    }

    [Fact]
    public void Match_TryGetValue_IfValue_and_IfPresent_follow_the_state()
    {
        var seen = new List<string>();

        Show(Patch.Value("Ada")).ShouldBe("Value(Ada)");
        Show(Patch.Null<string>()).ShouldBe("Null");
        Show(Patch.Absent<string>()).ShouldBe("Absent");

        Patch.Value("Ada").TryGetValue(out var value).ShouldBeTrue();
        value.ShouldBe("Ada");
        Patch.Null<string>().TryGetValue(out _).ShouldBeFalse();

        Patch.Value("Ada").IfValue(seen.Add);
        Patch.Null<string>().IfValue(seen.Add);
        Patch.Absent<string>().IfValue(seen.Add);
        seen.ShouldBe(["Ada"]);

        seen.Clear();
        Patch.Value("Ada").IfPresent(v => seen.Add(v ?? "<null>"));
        Patch.Null<string>().IfPresent(v => seen.Add(v ?? "<null>"));
        Patch.Absent<string>().IfPresent(v => seen.Add(v ?? "<null>"));
        seen.ShouldBe(["Ada", "<null>"]);
    }

    [Fact]
    public void ToOption_is_Some_for_a_value_and_None_for_a_clear_or_an_absence()
    {
        Patch.Value(3).ToOption().ShouldBe(Option.Some(3));
        Patch.Null<int>().ToOption().IsNone().ShouldBeTrue();
        Patch.Absent<int>().ToOption().IsNone().ShouldBeTrue();
    }

    [Fact]
    public void Equality_is_by_state_and_value_and_ToString_prints_the_state_only()
    {
        Patch.Value("secret").ShouldBe(Patch.Value("secret"));
        Patch.Value("secret").ShouldNotBe(Patch.Value("other"));
        Patch.Null<string>().ShouldNotBe(Patch.Absent<string>());

        Patch.Value("secret").ToString().ShouldBe("Patch { State = Value }");
        Patch.Absent<string>().ToString().ShouldBe("Patch { State = Absent }");
    }

    [Theory]
    [MemberData(nameof(Resolvers))]
    public void An_absent_property_reads_as_Absent_a_null_as_Null_and_a_value_as_Value(string resolver)
    {
        var options = Options(resolver);

        var absent = JsonSerializer.Deserialize<Update>("{}", options)!;
        Show(absent.Name).ShouldBe("Absent");
        Show(absent.Quantity).ShouldBe("Absent");
        Show(absent.Code).ShouldBe("Absent");
        Show(absent.Address).ShouldBe("Absent");

        var nulls = JsonSerializer.Deserialize<Update>("""{"name":null,"quantity":null,"code":null,"address":null}""", options)!;
        Show(nulls.Name).ShouldBe("Null");
        Show(nulls.Quantity).ShouldBe("Null");
        Show(nulls.Code).ShouldBe("Null");
        Show(nulls.Address).ShouldBe("Null");

        var values = JsonSerializer.Deserialize<Update>("""{"name":"Ada","quantity":3,"code":"ABC","address":{"city":"Oslo"}}""", options)!;
        Show(values.Name).ShouldBe("Value(Ada)");
        Show(values.Quantity).ShouldBe("Value(3)");
        Show(values.Code).ShouldBe("Value(ABC)");
        Show(values.Address).ShouldBe("Value(Address { City = Oslo })");
    }

    /// <summary>A type with a converter of its own, a value object's shape, is read with it, whatever the resolver; what it refuses is refused as anywhere else, the input not quoted.</summary>
    [Theory]
    [MemberData(nameof(Resolvers))]
    public void A_type_with_its_own_converter_inside_a_patch_is_read_with_it_and_its_refusal_stands(string resolver)
    {
        var refusal = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Update>("""{"code":"abc"}""", Options(resolver)));

        refusal.Message.ShouldContain("Code refused");
        refusal.Message.ShouldNotContain("abc");
    }

    [Theory]
    [MemberData(nameof(Resolvers))]
    public void Writing_leaves_an_absent_patch_out_under_WhenWritingDefault_and_refuses_it_otherwise(string resolver)
    {
        var options = Options(resolver);

        JsonSerializer.Serialize(new Outgoing(Patch.Null<string?>(), Patch.Absent<int>()), options).ShouldBe("""{"name":null}""");
        JsonSerializer.Serialize(new Outgoing(Patch.Value<string?>("Ada"), Patch.Value(2)), options).ShouldBe("""{"name":"Ada","quantity":2}""");

        var refusal = Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(new BareOutgoing(Patch.Absent<string?>()), options));
        refusal.Message.ShouldContain("WhenWritingDefault");
    }

    /// <summary>The patch's converter hides its argument from the source generator, so a class not listed in the context is the serializer's own refusal, naming the context.</summary>
    [Fact]
    public void A_class_the_source_generated_context_never_saw_is_refused_by_the_serializer_naming_the_context()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = WithoutAddressJson.Default };

        var refusal = Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<UpdateWithoutAddress>("""{"address":{"city":"Oslo"}}""", options));

        refusal.Message.ShouldContain(nameof(Address));
        refusal.Message.ShouldContain(nameof(WithoutAddressJson));
    }

    public sealed record Address(string City);

    /// <summary>A value object's shape: a struct with a converter of its own that refuses, naming the type and never the input.</summary>
    [JsonConverter(typeof(CodeConverter))]
    public readonly record struct Code
    {
        public string Value { get; }
        public Code(string value) => Value = value == value.ToUpperInvariant() ? value : throw new JsonException("Code refused the JSON value (NotUpperCase).");
        public override string ToString() => Value;
    }

    public sealed class CodeConverter : JsonConverter<Code>
    {
        public override Code Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, Code value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    public sealed record Update(Patch<string?> Name, Patch<int> Quantity, Patch<Code> Code, Patch<Address?> Address);

    public sealed record UpdateWithoutAddress(Patch<Address?> Address);

    public sealed record Outgoing(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<string?> Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Patch<int> Quantity);

    public sealed record BareOutgoing(Patch<string?> Name);
}

// A class a patch wraps is listed, since the patch's converter hides it from the generator; a primitive
// and a type with its own converter need not be.
[JsonSerializable(typeof(PatchTests.Update))]
[JsonSerializable(typeof(PatchTests.Address))]
[JsonSerializable(typeof(PatchTests.Outgoing))]
[JsonSerializable(typeof(PatchTests.BareOutgoing))]
internal partial class PatchJson : JsonSerializerContext;

[JsonSerializable(typeof(PatchTests.UpdateWithoutAddress))]
internal partial class WithoutAddressJson : JsonSerializerContext;
