using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Over a record from another assembly, whose synthesized <c>EqualityContract</c> read as an
/// auto-property there: the value object was refused as holding state besides its value (CMTK1012).
/// </summary>
public sealed partial record OverALibraryRecord : ProbeRecordBase, IValue<int>;

/// <summary>
/// What a value object inherits from a base record. Refusals are build errors and live in
/// <see cref="BuildOutcomeTests"/>; this is what is generated, where the base is in another assembly.
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
}
