using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-057: a <c>Nullable&lt;T&gt;</c> contract has <c>T</c> as its element type, though its kind is
/// <see cref="JsonTypeInfoKind.None"/>, and a JSON null read into a <c>T?</c> is null without asking <c>T</c>'s converter,
/// even one that handles null: as a property, an element and the root alike, where a plain <c>T</c> hands the null to it.
/// </summary>
public sealed class KB057
{
    /// <summary>A struct whose converter reads a null as a state of its own, as a merge-patch field's does.</summary>
    [JsonConverter(typeof(MarkedConverter))]
    public readonly record struct Marked(bool WasNull);

    private sealed class MarkedConverter : JsonConverter<Marked>
    {
        public override bool HandleNull => true;

        public override Marked Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var wasNull = reader.TokenType == JsonTokenType.Null;
            reader.Skip();
            return new Marked(wasNull);
        }

        public override void Write(Utf8JsonWriter writer, Marked value, JsonSerializerOptions options) => writer.WriteNullValue();
    }

    public sealed record Holder(Marked Plain, Marked? Maybe);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    [Fact]
    public void A_nullable_contract_has_the_underlying_type_as_its_element_type()
    {
        var nullable = Options.GetTypeInfo(typeof(Marked?));

        nullable.Kind.ShouldBe(JsonTypeInfoKind.None);
        nullable.ElementType.ShouldBe(typeof(Marked));
        Options.GetTypeInfo(typeof(int?)).ElementType.ShouldBe(typeof(int), "the same for a built-in type");
        Options.GetTypeInfo(typeof(Marked)).ElementType.ShouldBeNull("the underlying type itself has none");
    }

    [Fact]
    public void A_null_read_into_a_nullable_never_reaches_a_converter_that_handles_null()
    {
        var holder = JsonSerializer.Deserialize<Holder>("""{"plain":null,"maybe":null}""", Options)!;
        holder.Plain.WasNull.ShouldBeTrue("a plain T hands the null to its converter");
        holder.Maybe.ShouldBeNull("a T? property reads it as null");

        JsonSerializer.Deserialize<Holder>("""{"plain":null}""", Options)!.Maybe.ShouldBeNull("the same as a property left out");
        JsonSerializer.Deserialize<List<Marked?>>("[null]", Options).ShouldBe([null], "an element too");
        JsonSerializer.Deserialize<Marked?>("null", Options).ShouldBeNull("and the root");
        JsonSerializer.Deserialize<Marked>("null", Options).WasNull.ShouldBeTrue();
    }
}
