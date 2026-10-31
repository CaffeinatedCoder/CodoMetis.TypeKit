using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Probes;

// One plain value object per strategy the JSON and parsing aspects distinguish. Declared here, in a
// library, and used from the test assembly: generated members must survive the assembly boundary.

/// <summary>JSON: string. Parse: the string itself.</summary>
public readonly partial record struct ProbeName : IValue<string>;

/// <summary>JSON: Guid. Parse: ISpanParsable and IUtf8SpanParsable.</summary>
public readonly partial record struct ProbeId : IValue<Guid>;

/// <summary>JSON: invariant number. Parse: ISpanParsable. MinValue/MaxValue, IConvertible, comparison.</summary>
public readonly partial record struct ProbeCount : IValue<int>;

/// <summary>JSON: invariant number.</summary>
public readonly partial record struct ProbeAmount : IValue<decimal>;

/// <summary>JSON: invariant number, the one family where named floating-point literals (NaN) apply.</summary>
public readonly partial record struct ProbeRatio : IValue<double>;

/// <summary>JSON: boolean.</summary>
public readonly partial record struct ProbeFlag : IValue<bool>;

/// <summary>JSON: DateTime, always written as UTC.</summary>
public readonly partial record struct ProbeTimestamp : IValue<DateTime>;

/// <summary>JSON: DateOnly as yyyy-MM-dd.</summary>
public readonly partial record struct ProbeDate : IValue<DateOnly>;

/// <summary>JSON: DateTimeOffset.</summary>
public readonly partial record struct ProbeMoment : IValue<DateTimeOffset>;

/// <summary>JSON: TimeOnly.</summary>
public readonly partial record struct ProbeTime : IValue<TimeOnly>;

/// <summary>JSON: TimeSpan.</summary>
public readonly partial record struct ProbeDuration : IValue<TimeSpan>;

/// <summary>JSON: NodaTime, through NodaConverters, resolved from this compilation by name.</summary>
public readonly partial record struct ProbeInstant : IValue<NodaTime.Instant>;

/// <summary>JSON: NodaTime, through NodaConverters.</summary>
public readonly partial record struct ProbeLocalDate : IValue<NodaTime.LocalDate>;

/// <summary>JSON: fallback through the options. Parse: the string constructor. A class as the wrapped type.</summary>
public readonly partial record struct ProbeUri : IValue<Uri>;

/// <summary>An enum as the wrapped type. Parse: by name, through <c>Enum.TryParse</c>. Comparison: non-generic <c>IComparable</c>.</summary>
public readonly partial record struct ProbeWeekday : IValue<DayOfWeek>;

/// <summary>A value object that is a record class rather than a record struct.</summary>
public sealed partial record ProbeLabel : IValue<string>;

/// <summary>Implements the marker through another interface, which the fabric must still find.</summary>
public interface IProbeIdentifier : IValue<Guid>;

public readonly partial record struct ProbeCustomerId : IProbeIdentifier;

/// <summary>Nested inside another type.</summary>
public static partial class ProbeContainer
{
    public readonly partial record struct NestedId : IValue<int>;
}

/// <summary>Internal: its generated extension class must be internal too.</summary>
internal readonly partial record struct ProbeInternalId : IValue<int>;

/// <summary>
/// Declares its own <c>CompareTo</c>, the one comparison seam, ordering descending on purpose so that
/// an operator or object overload not derived from it is observable.
/// </summary>
public readonly partial record struct ProbeDescending : IValue<int>
{
    public int CompareTo(ProbeDescending other) => other.Value.CompareTo(Value);
}

/// <summary>
/// The comparison seam on a record class, whose operand is nullable: the declared ordering must be
/// recognised there too.
/// </summary>
public sealed partial record ProbeReversedLabel : IValue<string>
{
    public int CompareTo(ProbeReversedLabel? other) => other is null ? 1 : string.CompareOrdinal(other.Value, Value);
}

/// <summary>
/// Declares its own <c>ToString()</c>, the formatting seam, to hide what it wraps: it is kept, and no
/// formatting interface is generated, so interpolation and <c>string.Format</c> reach it too. A record
/// class over a string, as a secret usually is.
/// </summary>
public sealed partial record ProbeSecret : IValue<string>
{
    public override string ToString() => "***";
}

/// <summary>
/// The formatting seam on a record struct over a number, whose wrapped type is span-formattable and
/// convertible: the paths interpolation and <c>Convert.ToString</c> take for such a type.
/// </summary>
public readonly partial record struct ProbePin : IValue<int>
{
    public override string ToString() => "****";
}

/// <summary>
/// A wrapped type that parses only through a static <c>Parse(string)</c>, whose exception quotes the
/// input, as many hand-written types' do.
/// </summary>
public sealed record ProbeSkuText
{
    private ProbeSkuText(string text) => Text = text;

    public string Text { get; }

    public static ProbeSkuText Parse(string text) =>
        text.Length == 7 ? new ProbeSkuText(text) : throw new FormatException($"'{text}' is not a SKU.");

    public override string ToString() => Text;
}

/// <summary>Parse: the wrapped type's static <c>Parse(string)</c>.</summary>
public readonly partial record struct ProbeSku : IValue<ProbeSkuText>;

/// <summary>A wrapped type that parses only through its string constructor, whose exception quotes the input.</summary>
public sealed record ProbeIsbnText
{
    public ProbeIsbnText(string text) =>
        Text = text.Length == 13 ? text : throw new ArgumentException($"'{text}' is not an ISBN.", nameof(text));

    public string Text { get; }

    public override string ToString() => Text;
}

/// <summary>Parse: the wrapped type's string constructor.</summary>
public readonly partial record struct ProbeIsbn : IValue<ProbeIsbnText>;

/// <summary>
/// A wrapped type ASP.NET binds through its own <c>TryParse</c> but does not describe, as a range
/// literal's type: its JSON goes through a converter of its own, so ASP.NET's schema for it is empty,
/// and a parameter of it is ASP.NET's <see cref="string"/>.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(ProbeSpanTextJsonConverter))]
public sealed record ProbeSpanText : IParsable<ProbeSpanText>
{
    private ProbeSpanText(int from, int to) => (From, To) = (from, to);

    public int From { get; }

    public int To { get; }

    public static ProbeSpanText Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var span) ? span : throw new FormatException("Not a span.");

    public static bool TryParse(string? s, IFormatProvider? provider, out ProbeSpanText result)
    {
        var parts = s?.Split("..");
        result = null!;
        if (parts is not [var from, var to] || !int.TryParse(from, provider, out var f) || !int.TryParse(to, provider, out var t)) return false;
        result = new ProbeSpanText(f, t);
        return true;
    }

    public override string ToString() => $"{From}..{To}";
}

/// <summary>Writes a <see cref="ProbeSpanText"/> as its text.</summary>
public sealed class ProbeSpanTextJsonConverter : System.Text.Json.Serialization.JsonConverter<ProbeSpanText>
{
    public override ProbeSpanText Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        ProbeSpanText.Parse(reader.GetString()!, null);

    public override void Write(System.Text.Json.Utf8JsonWriter writer, ProbeSpanText value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Parse: the wrapped type's <c>IParsable</c>. JSON: the wrapped type's own converter.</summary>
public readonly partial record struct ProbeSpan : IValue<ProbeSpanText>;

/// <summary>
/// A base record for value objects in the test assembly, where it comes from another assembly: its
/// synthesized members read as declared there, and must not be mistaken for the user's.
/// </summary>
public abstract record ProbeRecordBase;

/// <summary>
/// A base record in another assembly whose methods share a generated member's name and parameter types, but are
/// other methods to C#: a generic <c>Parse</c>, a <c>From</c> that takes its value by <c>in</c>, and a
/// <c>TryParse</c> whose last parameter is by value. The generated members sit beside them (#226).
/// </summary>
public abstract record ProbeOverloadingRecord<TSelf>
{
    /// <summary>A generic overload, never the generated <c>Parse</c>.</summary>
    public static TSelf Parse<TFormat>(string text, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>An overload by <c>in</c>, never the generated <c>From</c>.</summary>
    public static TSelf From(in int value) => throw new NotSupportedException("hand-written");

    /// <summary>An overload whose last parameter is by value, never the generated <c>TryParse</c>.</summary>
    public static bool TryParse(string? text, IFormatProvider? provider, TSelf fallback) => throw new NotSupportedException("hand-written");
}

/// <summary>
/// A base record in another assembly for the test assembly's value objects, whose sealed
/// <c>ToString()</c> is the formatting seam: C# keeps it in every derived record.
/// </summary>
public abstract record ProbeMaskingRecord
{
    /// <summary>What every derived record prints.</summary>
    public sealed override string ToString() => "***";
}

/// <summary>
/// A base record in another assembly whose equality is hand-written, which a value object derived from
/// it in the build-outcome consumer must refuse (CMTK1011): its synthesized members are marked
/// <c>[CompilerGenerated]</c> there, and these are not.
/// </summary>
public abstract record ProbeReferenceEqualityRecord
{
    public virtual bool Equals(ProbeReferenceEqualityRecord? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>
/// A base record in another assembly with a private identity, which a reference assembly does not
/// carry, so no check on the test assembly's value objects sees it: the record's synthesized equality
/// compared it.
/// </summary>
public abstract record ProbeIdentityRecord
{
    private readonly Guid _identity = Guid.NewGuid();

    /// <summary>The identity, computed, so it holds nothing of its own.</summary>
    public Guid Identity => _identity;
}

/// <summary>
/// An interface in another assembly that implements <c>IFormattable</c> explicitly, which a value object
/// declaring it in the build-outcome consumer must refuse (CMTK1011). Metalama lists no private member of a
/// type in another assembly, and an explicit implementation is private, so a check that listed members missed it.
/// </summary>
public interface IProbeFormats : IFormattable
{
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => "***";
}

/// <summary>The same for <c>IMinMaxValue</c>, a static interface.</summary>
public interface IProbeBounded<TSelf> : System.Numerics.IMinMaxValue<TSelf> where TSelf : IProbeBounded<TSelf>
{
    static TSelf System.Numerics.IMinMaxValue<TSelf>.MinValue => throw new NotSupportedException();

    static TSelf System.Numerics.IMinMaxValue<TSelf>.MaxValue => throw new NotSupportedException();
}

/// <summary>The same for <c>IComparable</c>, which CMTK1008 refuses.</summary>
public interface IProbeOrders : IComparable
{
    int IComparable.CompareTo(object? obj) => 0;
}

/// <summary>The same for <c>IClosedSet</c>, on a closed set.</summary>
public interface IProbeSetNames<TSelf> : IClosedSet<TSelf> where TSelf : class, IProbeSetNames<TSelf>
{
    string IClosedSet<TSelf>.Name => "***";
}

/// <summary>A base record in another assembly that implements <c>IFormattable</c> explicitly, which a value object derived from it must refuse.</summary>
public abstract record ProbeFormattableRecord : IFormattable
{
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => "***";
}

/// <summary>The same for <c>IMinMaxValue</c>, which kept answering the interface.</summary>
public abstract record ProbeMinMaxRecord<TSelf> : System.Numerics.IMinMaxValue<TSelf> where TSelf : ProbeMinMaxRecord<TSelf>, System.Numerics.IMinMaxValue<TSelf>
{
    static TSelf System.Numerics.IMinMaxValue<TSelf>.MinValue => throw new NotSupportedException();

    static TSelf System.Numerics.IMinMaxValue<TSelf>.MaxValue => throw new NotSupportedException();
}

/// <summary>A closed set with no data and no constructor: the generators add a private one.</summary>
public sealed partial class ProbeStatus : IClosedSet<ProbeStatus>
{
    public static readonly ProbeStatus Open = new();
    public static readonly ProbeStatus Shipped = new();
    public static readonly ProbeStatus Cancelled = new();
}

/// <summary>A closed set declared in two parts, this one and <c>SplitProbes.cs</c>: <c>All</c> lists the member of either.</summary>
public sealed partial class ProbeSplit : IClosedSet<ProbeSplit>
{
    public static readonly ProbeSplit First = new();
}

/// <summary>Two nested sets of one simple name, told apart by their containers.</summary>
public static class ProbeOrder
{
    public sealed partial class Status : IClosedSet<Status>
    {
        public static readonly Status Open = new();
    }
}

/// <summary>See <see cref="ProbeOrder"/>.</summary>
public static class ProbeShipment
{
    public sealed partial class Status : IClosedSet<Status>
    {
        public static readonly Status Sent = new();
    }
}

/// <summary>A closed set with constant data per member, through a private constructor.</summary>
public sealed partial class ProbeTier : IClosedSet<ProbeTier>
{
    public static readonly ProbeTier Basic = new(1, null);
    public static readonly ProbeTier Premium = new(2, "Priority support.");

    private ProbeTier(int rank, string? perk)
    {
        Rank = rank;
        Perk = perk;
    }

    public int Rank { get; }

    public string? Perk { get; }
}

/// <summary>A closed set with a hand-written <c>ToString()</c>, the one seam: kept, while the name stays what JSON and parsing use.</summary>
public sealed partial class ProbeLabelled : IClosedSet<ProbeLabelled>
{
    public static readonly ProbeLabelled Low = new("low priority");
    public static readonly ProbeLabelled High = new("high priority");

    private readonly string _label;

    private ProbeLabelled(string label) => _label = label;

    /// <summary>The label, not the name.</summary>
    public override string ToString() => _label;
}

/// <summary>A closed set whose own code makes an instance beside its members, which therefore has no name.</summary>
public sealed partial class ProbeLevel : IClosedSet<ProbeLevel>
{
    public static readonly ProbeLevel Low = new();
    public static readonly ProbeLevel High = new();

    /// <summary>Not a member: the private constructor is still the set's own to call.</summary>
    public static ProbeLevel Unlisted() => new();
}

/// <summary>
/// A closed set whose members are created in every form CMTK1019 accepts, with a comment, a qualified
/// name, an object initializer and a literal holding brackets, and which declares overloads of the
/// generated <c>Parse</c> and <c>Match</c> with other signatures: its own, kept.
/// </summary>
public sealed partial class ProbePriority : IClosedSet<ProbePriority>
{
    public static readonly ProbePriority Low = new(/* the lowest (1) */ 1);
    public static readonly ProbePriority Normal = new global::CodoMetis.TypeKit.Generators.Probes.ProbePriority(2) { Label = "normal (the default)" };
    public static readonly ProbePriority High = new(3) { Label = "high" };

    private ProbePriority(int rank) => Rank = rank;

    public int Rank { get; }

    public string? Label { get; private init; }

    /// <summary>The member of that rank: another signature than the generated <c>Parse(string)</c>.</summary>
    public static ProbePriority Parse(int rank) => rank switch { 1 => Low, 2 => Normal, _ => High };

    /// <summary>A switch that answers nothing: another signature than the generated <c>Match&lt;TResult&gt;</c>.</summary>
    public void Match(Action low, Action normal, Action high)
    {
        if (ReferenceEquals(this, Low)) low();
        else if (ReferenceEquals(this, Normal)) normal();
        else high();
    }
}

/// <summary>
/// A closed set whose members are named as C# keywords once lower-cased: <c>Match</c>'s parameters
/// are <c>new</c> and <c>default</c>, written <c>@new:</c> and <c>@default:</c> at a call.
/// </summary>
public sealed partial class ProbeKeyword : IClosedSet<ProbeKeyword>
{
    public static readonly ProbeKeyword New = new();
    public static readonly ProbeKeyword Default = new();
}

/// <summary>
/// A fault set: each member's kind, from which a host derives its status, and a description where the
/// member has one. <c>Locked</c> must not explain itself, so it has none.
/// </summary>
public sealed partial class ProbeOrderFault : IFaultSet<ProbeOrderFault>
{
    public static readonly ProbeOrderFault NotFound = new(FaultKind.NotFound, "The order does not exist.");
    public static readonly ProbeOrderFault NameTaken = new(FaultKind.Conflict, "Another order has that name.");
    public static readonly ProbeOrderFault Locked = new(FaultKind.Forbidden, null);

    private ProbeOrderFault(FaultKind kind, string? description)
    {
        Kind = kind;
        Description = description;
    }

    public FaultKind Kind { get; }

    public string? Description { get; }
}

/// <summary>
/// A fault set shared code decides for every boundary, beside a boundary's own (<c>Fault&lt;ProbeOrderFault,
/// ProbeConcurrencyFault&gt;</c>): <c>NotFound</c> shares its name with <see cref="ProbeOrderFault.NotFound"/>.
/// </summary>
public sealed partial class ProbeConcurrencyFault : IFaultSet<ProbeConcurrencyFault>
{
    public static readonly ProbeConcurrencyFault Conflict = new(FaultKind.Conflict, "The order changed meanwhile; read it again.");
    public static readonly ProbeConcurrencyFault PreconditionFailed = new(FaultKind.PreconditionFailed, null);
    public static readonly ProbeConcurrencyFault NotFound = new(FaultKind.NotFound, null);

    private ProbeConcurrencyFault(FaultKind kind, string? description)
    {
        Kind = kind;
        Description = description;
    }

    public FaultKind Kind { get; }

    public string? Description { get; }
}

/// <summary>A fault set with a member whose kind is <c>default</c>, which is no kind: refused where the faults are mapped.</summary>
public sealed partial class ProbeKindlessFault : IFaultSet<ProbeKindlessFault>
{
    public static readonly ProbeKindlessFault Known = new(FaultKind.Invalid);
#pragma warning disable CMTK0014 // a member of no kind on purpose: the host's refusal of it is the subject
    public static readonly ProbeKindlessFault Unset = new(default);
#pragma warning restore CMTK0014

    private ProbeKindlessFault(FaultKind kind) => Kind = kind;

    public FaultKind Kind { get; }
}
