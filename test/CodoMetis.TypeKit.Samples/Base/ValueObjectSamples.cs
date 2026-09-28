// sample: CodoMetis.TypeKit/value-objects
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

// end sample
using System.Text.Json;

namespace ReadmeSamples.Base.Declared;

// sample: CodoMetis.TypeKit/value-objects
public readonly partial record struct OrderId : IValue<Guid>;

public enum CodeFault { Blank, TooShort, NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);

        var trimmed = value.Trim();
        if (trimmed.Length < 3) return Result.Error(CodeFault.TooShort);
        if (trimmed != trimmed.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);

        return new ProductCode(trimmed);   // the private constructor is generated
    }
}
// end sample

public static class GenericCode
{
    // sample: CodoMetis.TypeKit/generic-code
    static Option<TSelf> Read<TSelf, TFault>(string field)
        where TSelf : IValidatedValue<TSelf, string, TFault>
        where TFault : notnull =>
        TSelf.Create(field).ToOption();

    static TId NewId<TId>() where TId : IValueObject<TId, Guid>, IPlainValueObject<TId, Guid> =>
        TId.From(Guid.CreateVersion7());
    // end sample

    public static Option<ProductCode> ReadCode(string field) => Read<ProductCode, CodeFault>(field);

    public static OrderId NewOrderId() => NewId<OrderId>();
}

public static class StoredJson
{
    public static JsonSerializerOptions Options()
    {
        // sample: CodoMetis.TypeKit/stored-json
        var storeOptions = new JsonSerializerOptions { Converters = { new StoredJsonConverterFactory() } };
        // end sample

        return storeOptions;
    }
}

public sealed class ValueObjectTests
{
    /// <summary>The generic methods work for any value object: the sample's are called with the sample's own.</summary>
    [Fact]
    public void The_generic_methods_work_for_the_declared_value_objects()
    {
        GenericCode.ReadCode("ABC").ShouldBe(Option.Some(ProductCode.FromKnownGood("ABC")));
        GenericCode.ReadCode("abc").ShouldBe(Option.None<ProductCode>());
        GenericCode.NewOrderId().Value.Version.ShouldBe(7);
    }

    /// <summary>The stored-JSON options read a value today's rules refuse, which the generated converter alone does not.</summary>
    [Fact]
    public void The_store_s_options_read_without_the_rules()
    {
        JsonSerializer.Deserialize<ProductCode>("\"ab\"", StoredJson.Options()).Value.ShouldBe("ab");
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ProductCode>("\"ab\""));
    }
}
