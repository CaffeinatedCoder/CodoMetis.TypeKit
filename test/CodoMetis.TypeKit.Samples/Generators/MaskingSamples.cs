using System.Text.Json;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Generators.Masking;

// sample: CodoMetis.TypeKit.Generators/masking
public sealed partial record ApiKey : IValue<string>
{
    public override string ToString() => "ApiKey(***)";
}
// end sample

public sealed record Client(ApiKey Key, string Name);

public sealed class MaskingSamples
{
    [Fact]
    public void A_masked_value_object_prints_its_mask_and_its_JSON_carries_the_value()
    {
        var key = ApiKey.From("k-123");

        $"{key}".ShouldBe("ApiKey(***)");
        string.Format("{0}", key).ShouldBe("ApiKey(***)");
        new Client(key, "Ann").ToString().ShouldBe("Client { Key = ApiKey(***), Name = Ann }");
        JsonSerializer.Serialize(key).ShouldBe("\"k-123\"");
    }
}
