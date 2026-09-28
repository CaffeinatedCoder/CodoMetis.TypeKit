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
