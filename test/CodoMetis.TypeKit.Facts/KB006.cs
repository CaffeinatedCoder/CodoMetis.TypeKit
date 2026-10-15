using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-006: under <see cref="ReferenceHandler.Preserve"/>, references are numbered per
/// serializer call. A converter that hands its value to a nested <c>JsonSerializer</c> call starts again at
/// <c>"$id":"1"</c> inside a document that already has one, and the serializer refuses to read that document.
/// </summary>
public sealed class KB006
{
    public sealed class Node
    {
        public string Name { get; set; } = "";
    }

    /// <summary>A wrapper whose converter writes the node it holds through a nested serializer call.</summary>
    [JsonConverter(typeof(NestedCallConverter))]
    public sealed class Wrapper(Node inner)
    {
        public Node Inner { get; } = inner;
    }

    public sealed class NestedCallConverter : JsonConverter<Wrapper>
    {
        public override Wrapper Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<Node>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Wrapper value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Inner, options);
    }

    public sealed class Direct
    {
        public Node? First { get; set; }
        public Node? Second { get; set; }
    }

    public sealed class Nested
    {
        public Node? First { get; set; }
        public Wrapper? Second { get; set; }
    }

    private static readonly JsonSerializerOptions Preserve = new() { ReferenceHandler = ReferenceHandler.Preserve };

    [Fact]
    public void One_call_numbers_its_references_once_and_refers_back()
    {
        var node = new Node { Name = "n" };

        JsonSerializer.Serialize(new Direct { First = node, Second = node }, Preserve)
                      .ShouldBe("""{"$id":"1","First":{"$id":"2","Name":"n"},"Second":{"$ref":"2"}}""");
    }

    [Fact]
    public void A_nested_call_starts_again_at_one()
    {
        var node = new Node { Name = "n" };

        JsonSerializer.Serialize(new Nested { First = node, Second = new Wrapper(node) }, Preserve)
                      .ShouldBe("""{"$id":"1","First":{"$id":"2","Name":"n"},"Second":{"$id":"1","Name":"n"}}""", "the nested call's $id 1 repeats the document's, and no $ref is written");
    }

    /// <summary>The same nested call, typed by the contract the options give the node, as a trim-safe converter makes it.</summary>
    public sealed class TypedNestedCallConverter : JsonConverter<Wrapper>
    {
        public override Wrapper Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<Node>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Wrapper value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Inner, (JsonTypeInfo<Node>)options.GetTypeInfo(typeof(Node)));
    }

    [Fact]
    public void A_nested_call_typed_by_a_contract_starts_again_at_one_too()
    {
        var node    = new Node { Name = "n" };
        var options = new JsonSerializerOptions(Preserve);
        options.Converters.Add(new TypedNestedCallConverter());

        JsonSerializer.Serialize(new Nested { First = node, Second = new Wrapper(node) }, options)
                      .ShouldBe("""{"$id":"1","First":{"$id":"2","Name":"n"},"Second":{"$id":"1","Name":"n"}}""");
    }

    [Fact]
    public void A_document_with_a_repeated_id_is_refused_on_reading()
    {
        var node     = new Node { Name = "n" };
        var document = JsonSerializer.Serialize(new Nested { First = node, Second = new Wrapper(node) }, Preserve);

        var refused = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Direct>(document, Preserve));
        refused.Message.ShouldContain("'$id' metadata property '1' conflicts with an existing identifier");
    }
}
