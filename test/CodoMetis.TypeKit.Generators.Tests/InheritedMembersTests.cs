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

/// <summary>A base record in this assembly whose <c>IEquatable</c> of itself is implemented explicitly, by reference.</summary>
public abstract record LocalExplicitEqualityRecord : IEquatable<LocalExplicitEqualityRecord>
{
    bool IEquatable<LocalExplicitEqualityRecord>.Equals(LocalExplicitEqualityRecord? other) => ReferenceEquals(this, other);
}

// CMTK1023 names the base's equality, which the generated one does not call.
#pragma warning disable CMTK1023
/// <summary>Over a base record whose explicit <c>IEquatable</c> compares by reference, which a <c>HashSet</c> of the base calls.</summary>
public sealed partial record OverAnExplicitEqualityRecord : LocalExplicitEqualityRecord, IValue<int>;

/// <summary>A base record between the value object and one whose explicit <c>IEquatable</c> compares by reference.</summary>
public abstract record LocalBetweenRecord : LocalExplicitEqualityRecord;

/// <summary>Two levels above the explicit <c>IEquatable</c>, which a <c>HashSet</c> of the deeper base calls.</summary>
public sealed partial record OverADeeperExplicitEqualityRecord : LocalBetweenRecord, IValue<int>;

/// <summary>A base record that implements its own base's <c>IEquatable</c> explicitly, by reference.</summary>
public abstract record LocalReimplementingBetweenRecord : LocalEqualityFreeRecord, IEquatable<LocalEqualityFreeRecord>
{
    bool IEquatable<LocalEqualityFreeRecord>.Equals(LocalEqualityFreeRecord? other) => ReferenceEquals(this, other);
}

/// <summary>A base record with no equality of its own.</summary>
public abstract record LocalEqualityFreeRecord;

/// <summary>Over a base between that implements the deeper base's <c>IEquatable</c> explicitly.</summary>
public sealed partial record OverABetweenExplicitEqualityRecord : LocalReimplementingBetweenRecord, IValue<int>;
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

/// <summary>A base class in this assembly whose <c>ToString()</c> is sealed.</summary>
public abstract class LocalMaskingSetBase
{
    /// <summary>What every derived class prints.</summary>
    public sealed override string ToString() => "***";
}

/// <summary>A closed set over a sealed <c>ToString()</c>, which failed the aspect (LAMA0502, #205).</summary>
public sealed partial class MaskedSet : LocalMaskingSetBase, CodoMetis.TypeKit.ClosedSets.IClosedSet<MaskedSet>
{
    /// <summary>The first member.</summary>
    public static readonly MaskedSet Open = new();

    /// <summary>The second member.</summary>
    public static readonly MaskedSet Closed = new();
}

/// <summary>
/// A base record whose private members have the generated members' names and signatures. C# hides them from a
/// derived record, so they stand in for nothing: 2.3.0 generated the value object beside them, and 2.3.1's
/// base-record checks refused it (CMTK1011, CMTK1008, #230).
/// </summary>
public abstract record PrivateNamesBase<TSelf> where TSelf : notnull
{
    private static int MaxValue => 100;
    private static int MinValue => 0;
    private int Value => -1;
    private static TSelf From(int value) => throw new NotSupportedException("hidden");
    private static TSelf Parse(string text, IFormatProvider? provider) => throw new NotSupportedException("hidden");
    private static bool TryParse(string? text, IFormatProvider? provider, out TSelf result) => throw new NotSupportedException("hidden");
    private static TSelf Parse(ReadOnlySpan<char> text, IFormatProvider? provider) => throw new NotSupportedException("hidden");
    private static bool TryParse(ReadOnlySpan<byte> text, IFormatProvider? provider, out TSelf result) => throw new NotSupportedException("hidden");
    private string ToString(string? format, IFormatProvider? provider) => "hidden";
    private bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => throw new NotSupportedException("hidden");
    private int CompareTo(object? other) => 0;
    private int CompareTo(TSelf? other) => 0;
    private static Option<TSelf> TryFrom(int value) => throw new NotSupportedException("hidden");
    private Result<TSelf, PrivateNamesFault> Revalidate() => throw new NotSupportedException("hidden");

    /// <summary>What the base uses its private members for.</summary>
    protected bool InRange(int value) => value >= MinValue && value <= MaxValue && Value < 0 && CompareTo(null) == 0 && ToString(null, null) == "hidden";
}

/// <summary>Why a value object over <see cref="PrivateNamesBase{TSelf}"/> is refused.</summary>
public enum PrivateNamesFault
{
    /// <summary>Below zero.</summary>
    Negative
}

/// <summary>A plain value object over a base record's private members of generated names.</summary>
public sealed partial record OverPrivateNames : PrivateNamesBase<OverPrivateNames>, IValue<int>;

/// <summary>A validated value object over a base record's private <c>TryFrom</c> and <c>Revalidate</c>.</summary>
public sealed partial record ValidatedOverPrivateNames : PrivateNamesBase<ValidatedOverPrivateNames>, IValidatedValue<ValidatedOverPrivateNames, int, PrivateNamesFault>
{
    /// <summary>Zero or more.</summary>
    public static Result<ValidatedOverPrivateNames, PrivateNamesFault> Create(int value) => value < 0 ? Result.Error(PrivateNamesFault.Negative) : new ValidatedOverPrivateNames(value);
}

/// <summary>A base class whose private members have a closed set's generated names and signatures (#230).</summary>
public abstract class PrivateSetNamesBase<TSelf>
{
    private static TSelf Parse(string name) => throw new NotSupportedException("hidden");
    private static bool TryParse(string? name, out TSelf member) => throw new NotSupportedException("hidden");
    private TResult Match<TResult>(Func<TResult> first, Func<TResult> second) => throw new NotSupportedException("hidden");

    /// <summary>What the base uses its private members for.</summary>
    protected static bool Parses(string name) => TryParse(name, out _) || Parse(name) is not null || ((PrivateSetNamesBase<TSelf>)null!).Match(() => 0, () => 1) == 0;
}

/// <summary>A closed set over a base class's private members of generated names.</summary>
public sealed partial class SetOverPrivateNames : PrivateSetNamesBase<SetOverPrivateNames>, CodoMetis.TypeKit.ClosedSets.IClosedSet<SetOverPrivateNames>
{
    /// <summary>The first member.</summary>
    public static readonly SetOverPrivateNames First = new();

    /// <summary>The second member.</summary>
    public static readonly SetOverPrivateNames Second = new();
}

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

    /// <summary>
    /// A base record's explicit <c>IEquatable&lt;TBase&gt;</c> is what a collection of the base calls, and the
    /// generated equality did not reach it: a <c>HashSet</c> of the value object held one of two holding 42,
    /// and a <c>HashSet</c> of the base both (reported 2026-10-14). The value object implements the base's
    /// interface again, as its own equality.
    /// </summary>
    [Fact]
    public void A_base_records_explicit_equality_is_not_the_value_objects()
    {
        var value = OverAnExplicitEqualityRecord.From(42);
        var same  = OverAnExplicitEqualityRecord.From(42);

        new HashSet<OverAnExplicitEqualityRecord> { value, same }.Count.ShouldBe(1);
        new HashSet<LocalExplicitEqualityRecord> { value, same }.Count.ShouldBe(1);
        ((IEquatable<LocalExplicitEqualityRecord>)value).Equals(same).ShouldBeTrue();
        ((IEquatable<LocalExplicitEqualityRecord>)value).Equals(OverAnExplicitEqualityRecord.From(43)).ShouldBeFalse();
    }

    /// <summary>
    /// The runtime maps a listed interface to a method the class declares, or keeps the base's mapping: the
    /// synthesized <c>Equals(TBase)</c> is the value object's own only for its direct base, so an explicit
    /// <c>IEquatable</c> two levels up, or one a base between implements, was kept, and a <c>HashSet</c> of
    /// that base held two value objects holding 42 (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_deeper_base_records_explicit_equality_is_not_the_value_objects()
    {
        new HashSet<LocalExplicitEqualityRecord> { OverADeeperExplicitEqualityRecord.From(42), OverADeeperExplicitEqualityRecord.From(42) }.Count.ShouldBe(1);
        ((IEquatable<LocalExplicitEqualityRecord>)OverADeeperExplicitEqualityRecord.From(42)).Equals(OverADeeperExplicitEqualityRecord.From(43)).ShouldBeFalse();
        new HashSet<LocalEqualityFreeRecord> { OverABetweenExplicitEqualityRecord.From(42), OverABetweenExplicitEqualityRecord.From(42) }.Count.ShouldBe(1);
        new HashSet<LocalReimplementingBetweenRecord> { OverABetweenExplicitEqualityRecord.From(42), OverABetweenExplicitEqualityRecord.From(42) }.Count.ShouldBe(1);
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

    /// <summary>
    /// A base record's private members stand in for nothing: every generated member is there, and is what a call
    /// on the value object reaches (#230).
    /// </summary>
    [Fact]
    public void A_base_record_s_private_members_of_generated_names_are_hidden()
    {
        OverPrivateNames.MaxValue.Value.ShouldBe(int.MaxValue);
        OverPrivateNames.MinValue.Value.ShouldBe(int.MinValue);
        OverPrivateNames.From(7).Value.ShouldBe(7);
        OverPrivateNames.Parse("7", null).ShouldBe(OverPrivateNames.From(7));
        OverPrivateNames.TryParse("7", null, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(OverPrivateNames.From(7));
        OverPrivateNames.Parse("7".AsSpan(), null).ShouldBe(OverPrivateNames.From(7));
        OverPrivateNames.TryParse("7"u8, null, out var utf8).ShouldBeTrue();
        utf8.ShouldBe(OverPrivateNames.From(7));
        OverPrivateNames.From(7).ToString("D3", null).ShouldBe("007");

        Span<char> buffer = stackalloc char[8];
        OverPrivateNames.From(7).TryFormat(buffer, out var written, "D3", null).ShouldBeTrue();
        buffer[..written].ToString().ShouldBe("007");

        (OverPrivateNames.From(1) < OverPrivateNames.From(2)).ShouldBeTrue();
        OverPrivateNames.From(1).CompareTo((object)OverPrivateNames.From(2)).ShouldBeLessThan(0);
        typeof(OverPrivateNames).IsDefined(typeof(CodoMetis.TypeKit.CompilerServices.HandWrittenOrderingAttribute), inherit: false).ShouldBeFalse();
    }

    /// <summary>A base record's private <c>TryFrom</c> and <c>Revalidate</c> are no seams: the generated ones run <c>Create</c> (#230).</summary>
    [Fact]
    public void A_base_record_s_private_TryFrom_and_Revalidate_are_hidden()
    {
        ValidatedOverPrivateNames.TryFrom(50).IsSome().ShouldBeTrue();
        ValidatedOverPrivateNames.TryFrom(-1).IsNone().ShouldBeTrue();
        ValidatedOverPrivateNames.FromKnownGood(50).Revalidate().TryGetValue(out var revalidated, out _).ShouldBeTrue();
        revalidated.ShouldBe(ValidatedOverPrivateNames.FromKnownGood(50));
    }

    /// <summary>A base class's private <c>Parse</c>, <c>TryParse</c> and <c>Match</c> are hidden from a closed set too (#230).</summary>
    [Fact]
    public void A_closed_set_s_base_class_s_private_members_of_generated_names_are_hidden()
    {
        SetOverPrivateNames.Parse("Second").ShouldBeSameAs(SetOverPrivateNames.Second);
        SetOverPrivateNames.TryParse("First", out var member).ShouldBeTrue();
        member.ShouldBeSameAs(SetOverPrivateNames.First);
        SetOverPrivateNames.Second.Match(first: () => 1, second: () => 2).ShouldBe(2);
    }

    /// <summary>
    /// A base class's sealed <c>ToString()</c> is a closed set's seam too (#205): it is what the set prints, while
    /// its name, parsing and JSON are generated as usual.
    /// </summary>
    [Fact]
    public void A_sealed_ToString_in_a_closed_set_s_base_class_is_the_seam()
    {
        MaskedSet.Closed.ToString().ShouldBe("***");
        $"{MaskedSet.Closed}".ShouldBe("***");

        MaskedSet.Closed.Name.ShouldBe("Closed");
        MaskedSet.All.ShouldBe([MaskedSet.Open, MaskedSet.Closed]);
        MaskedSet.Parse("Closed").ShouldBeSameAs(MaskedSet.Closed);
        JsonSerializer.Serialize(MaskedSet.Closed).ShouldBe("\"Closed\"");
    }
}
