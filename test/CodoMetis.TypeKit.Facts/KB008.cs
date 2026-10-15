using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-008: the converter of a <c>Nullable&lt;T&gt;</c> contract is System.Text.Json's own
/// adapter: a non-public type of its assembly whose <see cref="JsonConverter.Type"/> is the nullable type, with
/// no public member that names the converter it wraps. One built by hand with
/// <see cref="JsonMetadataServices.GetNullableConverter{T}(JsonTypeInfo{T})"/> is the same kind and writes what
/// the contract it was built over writes.
/// </summary>
public sealed class KB008
{
    private static void ShouldBeTheAdapter(JsonConverter converter)
    {
        converter.Type.ShouldBe(typeof(decimal?));
        converter.GetType().Assembly.ShouldBe(typeof(JsonSerializer).Assembly);
        converter.GetType().IsVisible.ShouldBeFalse("the adapter's type is not public");
        converter.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(property => typeof(JsonConverter).IsAssignableFrom(property.PropertyType)).ShouldBeEmpty("nothing public names the wrapped converter");
    }

    [Fact]
    public void The_reflection_resolver_s_nullable_converter_is_the_serializer_s_adapter()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new DecimalStringConverter() }, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

        ShouldBeTheAdapter(options.GetTypeInfo(typeof(decimal?)).Converter);
        JsonSerializer.Serialize((decimal?)1.2m, options).ShouldBe("\"1.2\"", "it writes what T's converter writes");
    }

    /// <summary>A resolver building the nullable contract by hand over a converter other than the one <c>T</c> resolves to.</summary>
    private sealed class HandBuilt : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(decimal?)
                ? JsonMetadataServices.CreateValueInfo<decimal?>(options, JsonMetadataServices.GetNullableConverter(JsonMetadataServices.CreateValueInfo<decimal>(options, new DecimalNumberConverter())))
                : null;
    }

    [Fact]
    public void A_hand_built_adapter_is_the_same_kind_and_writes_what_its_own_contract_writes()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new DecimalStringConverter() }, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.TypeInfoResolverChain.Insert(0, new HandBuilt());

        ShouldBeTheAdapter(options.GetTypeInfo(typeof(decimal?)).Converter);
        JsonSerializer.Serialize(new Holder(1.2m, 1.2m), options).ShouldBe("""{"maybe":1.2,"plain":"1.2"}""", "a number where T's own contract writes a string");
    }
}
