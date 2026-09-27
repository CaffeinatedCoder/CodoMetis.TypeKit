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
