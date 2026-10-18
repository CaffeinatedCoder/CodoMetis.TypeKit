using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-040: under <c>JsonSerializerOptions.MaxDepth</c>, System.Text.Json refuses a value that a converter of one's own
/// writes at the writer's depth equal to the limit, with a <c>JsonException</c> at that value, where its own converter for the same type
/// writes it; one level less deep, the converter's value is written. Reading is not charged: the converter reads its value at the limit.
/// </summary>
public sealed class KB040
{
    public sealed class A<T>
    {
        public T P { get; set; } = default!;
    }

    /// <summary>A converter of the application's for <c>int</c>, which reads and writes the number as the serializer does.</summary>
    public sealed class IntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetInt32();

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private static readonly FactFixture Fixture = FactFixture.Beside("KB-040");

    public static TheoryData<string> Answers() => [.. Fixture.Answers.Select(answer => answer.Id)];

    private static JsonSerializerOptions Options(IReadOnlyDictionary<string, string> when)
    {
        var options = new JsonSerializerOptions { MaxDepth = int.Parse(when["json.MaxDepth"], CultureInfo.InvariantCulture) };
        if (when["written-by"] == "a-converter") options.Converters.Add(new IntConverter());
        return options;
    }

    /// <summary>A list of one <c>int</c> whose element the writer is at the given depth for: inside the root object, then one more object per level.</summary>
    private static object At(string depth) => depth switch
    {
        "2" => new A<List<int>> { P = [1] },
        "3" => new A<A<List<int>>> { P = new() { P = [1] } },
        _ => throw new ArgumentOutOfRangeException(nameof(depth), depth, "the fixture's depths are 2 and 3"),
    };

    /// <summary>Each answer of KB040.fixture.json: the element written by the converter or the serializer, at the depth the answer names.</summary>
    [Theory, MemberData(nameof(Answers))]
    public void The_serializer_writes_or_refuses_as_the_fixture_holds(string id)
    {
        var answer = Fixture[id];
        var value  = At(answer.When["writer-depth"]);

        if (answer.Kind == FactAnswer.Exception)
        {
            var thrown = Should.Throw<Exception>(() => JsonSerializer.Serialize(value, value.GetType(), Options(answer.When)));
            answer.Mismatch(thrown).ShouldBeNull();
            ((JsonException)thrown).Path.ShouldBe("$.P.P");
        }
        else
        {
            answer.Mismatch(JsonNode.Parse(JsonSerializer.Serialize(value, value.GetType(), Options(answer.When)))!).ShouldBeNull();
        }
    }

    /// <summary>Reading is not charged: the converter reads the element at the limit, as the serializer's own does.</summary>
    [Fact]
    public void A_converter_reads_its_value_at_the_limit()
    {
        var options = Options(new Dictionary<string, string> { ["json.MaxDepth"] = "3", ["written-by"] = "a-converter" });

        JsonSerializer.Deserialize<A<A<List<int>>>>("""{"P":{"P":[1]}}""", options)!.P.P.ShouldBe([1]);
    }
}
