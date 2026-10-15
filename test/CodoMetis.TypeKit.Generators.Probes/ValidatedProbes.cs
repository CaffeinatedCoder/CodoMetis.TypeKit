using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Probes;

public enum ProbeCodeFault { Blank, TooShort, NotUpperCase }

/// <summary>
/// A validated value with three distinct faults and a normalisation (trimming), so a generated entry
/// point that skipped <c>Create</c> or reimplemented it would disagree with it somewhere.
/// </summary>
public readonly partial record struct ProbeCode : IValidatedValue<ProbeCode, string, ProbeCodeFault>
{
    public static Result<ProbeCode, ProbeCodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(ProbeCodeFault.Blank);

        var trimmed = value.Trim();

        if (trimmed.Length < 3) return Result.Error(ProbeCodeFault.TooShort);
        if (trimmed != trimmed.ToUpperInvariant()) return Result.Error(ProbeCodeFault.NotUpperCase);

        return new ProbeCode(trimmed);
    }
}

/// <summary>A fault set as a validated value's fault type, where the other probes use an enum.</summary>
public sealed partial class ProbeArticleFault : IFaultSet<ProbeArticleFault>
{
    public static readonly ProbeArticleFault Blank = new("An article number is never blank.");
    public static readonly ProbeArticleFault NotUpperCase = new("An article number is written in upper case.");

    private ProbeArticleFault(string description) => Description = description;

    public FaultKind Kind => FaultKind.Invalid;

    public string? Description { get; }
}

/// <summary>
/// <see cref="ProbeCode"/>'s rules, trimming included, over a fault set: every generated entry point
/// refuses through the set's member as it does through an enum's, and names it the same way.
/// </summary>
public readonly partial record struct ProbeArticle : IValidatedValue<ProbeArticle, string, ProbeArticleFault>
{
    public static Result<ProbeArticle, ProbeArticleFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(ProbeArticleFault.Blank);

        var trimmed = value.Trim();

        return trimmed == trimmed.ToUpperInvariant() ? new ProbeArticle(trimmed) : Result.Error(ProbeArticleFault.NotUpperCase);
    }
}

public enum ProbePercentageFault { OutOfRange }

/// <summary>
/// A validated number: int.MinValue and int.MaxValue are not percentages, so it must get no
/// MinValue/MaxValue, and JSON and parsing must refuse 101.
/// </summary>
public readonly partial record struct ProbePercentage : IValidatedValue<ProbePercentage, int, ProbePercentageFault>
{
    public static Result<ProbePercentage, ProbePercentageFault> Create(int value) =>
        value is >= 0 and <= 100 ? new ProbePercentage(value) : Result.Error(ProbePercentageFault.OutOfRange);
}

public enum ProbeOverrideFault { Blank }

/// <summary>
/// Declares its own <c>TryFrom</c>, which deliberately disagrees with <c>Create</c> so the
/// suppression of the generated one is observable.
/// </summary>
public readonly partial record struct ProbeOverride : IValidatedValue<ProbeOverride, string, ProbeOverrideFault>
{
    public static Result<ProbeOverride, ProbeOverrideFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result.Error(ProbeOverrideFault.Blank) : new ProbeOverride(value);

    public static Option<ProbeOverride> TryFrom(string value) => Option.None<ProbeOverride>();
}
