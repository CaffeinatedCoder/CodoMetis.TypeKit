using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-007: a resolver of one's own decides a type's contract alone. Its converter writes the
/// type over a converter for it in <see cref="JsonSerializerOptions.Converters"/>, and the reflection
/// resolver builds <c>Nullable&lt;T&gt;</c>'s contract from the contract the chain gives <c>T</c>.
/// </summary>
public sealed class KB007
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new DecimalStringConverter() }, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.TypeInfoResolverChain.Insert(0, new ResolverOf<decimal>(new DecimalNumberConverter()));
        return options;
    }

    [Fact]
    public void The_options_converter_writes_when_no_resolver_claims_the_type() =>
        JsonSerializer.Serialize(new Holder(1.2m, 1.2m), new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new DecimalStringConverter() } })
                      .ShouldBe("""{"maybe":"1.2","plain":"1.2"}""");

    [Fact]
    public void A_resolver_s_converter_writes_over_the_options_converter_in_the_nullable_position_too() =>
        JsonSerializer.Serialize(new Holder(1.2m, 1.2m), Options()).ShouldBe("""{"maybe":1.2,"plain":1.2}""");
}
