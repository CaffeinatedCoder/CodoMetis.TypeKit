using System.ComponentModel;
using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Over a record from another assembly, whose synthesized <c>EqualityContract</c> read as an
/// auto-property there: the value object was refused as holding state besides its value (CMTK1012).
/// </summary>
public sealed partial record OverALibraryRecord : ProbeRecordBase, IValue<int>;

/// <summary>A base record in this assembly whose <c>ToString()</c> is sealed.</summary>
public abstract record LocalMaskingRecord
{
    /// <summary>What every derived record prints.</summary>
    public sealed override string ToString() => "***";
}

/// <summary>Over a sealed <c>ToString()</c> in this assembly, which failed the aspect (LAMA0502).</summary>
public sealed partial record MaskedOverALocalRecord : LocalMaskingRecord, IValue<string>;

/// <summary>Over a sealed <c>ToString()</c> in another assembly.</summary>
public sealed partial record MaskedOverALibraryRecord : ProbeMaskingRecord, IValue<string>;

/// <summary>
/// What a value object inherits from a base record. Refusals are build errors and live in
/// <see cref="BuildOutcomeTests"/>; this is what is generated.
/// </summary>
public sealed class InheritedMembersTests
{
    [Fact]
    public void A_value_object_over_a_record_from_another_assembly_is_generated()
    {
        var value = OverALibraryRecord.From(3);

        value.Value.ShouldBe(3);
        (value == OverALibraryRecord.From(3)).ShouldBeTrue();
        value.ToString().ShouldBe("3");
        JsonSerializer.Serialize(value).ShouldBe("3");
    }

    public static TheoryData<object> OverASealedToString => [MaskedOverALocalRecord.From("SECRET"), MaskedOverALibraryRecord.From("SECRET")];

    /// <summary>
    /// A base record's sealed <c>ToString()</c> is the seam, as a declared one is: C# keeps it in every
    /// derived record, so it is what the value object prints, and no formatting interface reaches past
    /// it. JSON and the type converter still write the wrapped value.
    /// </summary>
    [Theory]
    [MemberData(nameof(OverASealedToString))]
    public void A_sealed_ToString_in_a_base_record_is_the_seam(object value)
    {
        value.ToString().ShouldBe("***");
        $"{value}".ShouldBe("***");
        string.Format("{0}", value).ShouldBe("***");
        Convert.ToString(value).ShouldBe("***");
        value.ShouldNotBeAssignableTo<IFormattable>();

        JsonSerializer.Serialize(value, value.GetType()).ShouldBe("\"SECRET\"");
        TypeDescriptor.GetConverter(value.GetType()).ConvertToInvariantString(value).ShouldBe("SECRET");
    }
}
