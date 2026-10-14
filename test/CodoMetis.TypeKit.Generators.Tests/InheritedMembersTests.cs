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

/// <summary>Over a record from another assembly with a private identity, which no check here can see.</summary>
public sealed partial record OverAnIdentityRecord : ProbeIdentityRecord, IValue<int>;

/// <summary>A base record in this assembly whose equality is by reference.</summary>
public abstract record LocalReferenceEqualityRecord
{
    public virtual bool Equals(LocalReferenceEqualityRecord? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

// CMTK1023 names the base's equality, which the generated one does not call.
#pragma warning disable CMTK1023
/// <summary>Over a base record whose equality is by reference: two holding 42 were unequal while <c>CompareTo</c> answered 0.</summary>
public sealed partial record OverAReferenceEqualityRecord : LocalReferenceEqualityRecord, IValue<int>;
#pragma warning restore CMTK1023

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

    /// <summary>
    /// A base record's private fields are out of sight in another assembly, and the record's synthesized
    /// equality compared them: two value objects holding 42 were unequal, a <c>HashSet</c> held both and a
    /// <c>SortedSet</c> one, and a JSON round trip gave back a value unequal to the one written (reported
    /// 2026-10-14, there since 1.0.0). Over a base record the equality is generated, the wrapped value's.
    /// </summary>
    [Fact]
    public void Over_a_base_record_equality_is_the_wrapped_values()
    {
        var value = OverAnIdentityRecord.From(42);
        var same  = OverAnIdentityRecord.From(42);

        value.Identity.ShouldNotBe(same.Identity);
        (value == same).ShouldBeTrue();
        value.Equals((object)same).ShouldBeTrue();
        ((ProbeIdentityRecord)value).Equals(same).ShouldBeTrue();
        value.GetHashCode().ShouldBe(same.GetHashCode());
        new HashSet<OverAnIdentityRecord> { value, same }.Count.ShouldBe(1);
        new SortedSet<OverAnIdentityRecord> { value, same }.Count.ShouldBe(1);
        JsonSerializer.Deserialize<OverAnIdentityRecord>(JsonSerializer.Serialize(value)).ShouldBe(value);

        (value == OverAnIdentityRecord.From(43)).ShouldBeFalse();
        value.Equals(null).ShouldBeFalse();
        (value != null).ShouldBeTrue();
    }

    /// <summary>
    /// A base record's equality was called by the derived record's synthesized one, so a base comparing
    /// by reference made two value objects holding 42 unequal while <c>CompareTo</c> answered 0, and a
    /// <c>HashSet</c> and a <c>SortedSet</c> of them differed in size (reported 2026-10-14, there since
    /// 1.0.0). The generated equality does not call it.
    /// </summary>
    [Fact]
    public void A_base_records_equality_is_not_the_value_objects()
    {
        var value = OverAReferenceEqualityRecord.From(42);
        var same  = OverAReferenceEqualityRecord.From(42);

        (value == same).ShouldBe(value.CompareTo(same) == 0);
        (value == same).ShouldBeTrue();
        value.GetHashCode().ShouldBe(same.GetHashCode());
        new HashSet<OverAReferenceEqualityRecord> { value, same }.Count.ShouldBe(new SortedSet<OverAReferenceEqualityRecord> { value, same }.Count);
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
