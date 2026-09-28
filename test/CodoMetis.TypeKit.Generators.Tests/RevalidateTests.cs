using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// <c>Revalidate()</c>: today's rules applied to a value read back without them, through
/// <see cref="StoredJsonConverterFactory"/> here and through the EF Core satellite in its own tests.
/// It answers which stored values a rule added later refuses.
/// </summary>
public sealed class RevalidateTests
{
    private static readonly JsonSerializerOptions Store = new() { Converters = { new StoredJsonConverterFactory() } };

    [Fact]
    public void A_stored_value_the_rules_now_refuse_reports_its_fault()
    {
        JsonSerializer.Deserialize<ProbeCode>("\"lower\"", Store).Revalidate().TryGetValue(out _, out var code).ShouldBeFalse();
        code.ShouldBe(ProbeCodeFault.NotUpperCase);

        JsonSerializer.Deserialize<ProbePercentage>("101", Store).Revalidate().TryGetValue(out _, out var percentage).ShouldBeFalse();
        percentage.ShouldBe(ProbePercentageFault.OutOfRange);
    }

    /// <summary>What <c>Create</c> returns, normalisation included: <c>ProbeCode</c> trims.</summary>
    [Fact]
    public void A_stored_value_the_rules_accept_comes_back_as_Create_makes_it()
    {
        var stored = JsonSerializer.Deserialize<ProbeCode>("\"  ABC  \"", Store);
        stored.Value.ShouldBe("  ABC  ");

        stored.Revalidate().TryGetValue(out var revalidated, out _).ShouldBeTrue();
        revalidated.Value.ShouldBe("ABC");

        ProbeCode.FromKnownGood("XYZ").Revalidate().ShouldBe(ProbeCode.Create("XYZ"));
    }

    /// <summary>A plain value object has no rules to apply again.</summary>
    [Fact]
    public void Only_a_validated_value_object_gets_it() =>
        typeof(ProbeId).GetMethod("Revalidate").ShouldBeNull();
}
