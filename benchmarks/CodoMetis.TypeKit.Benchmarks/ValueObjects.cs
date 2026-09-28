using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Benchmarks;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Amount : IValue<decimal>;

public readonly partial record struct Weekday : IValue<DayOfWeek>;

public enum CodeFault { Length, NotUpperCase }

/// <summary>Three upper-case ASCII letters. The rule allocates nothing, so a measurement shows the wrapping.</summary>
public readonly partial record struct Code : IValidatedValue<Code, string, CodeFault>
{
    public static Result<Code, CodeFault> Create(string value) =>
        Rules.Check(value) is { } fault ? Result.Error(fault) : new Code(value);
}

/// <summary>The same rule for the raw baseline, which has to validate too.</summary>
public static class Rules
{
    public static CodeFault? Check(string value)
    {
        if (value.Length != 3) return CodeFault.Length;

        foreach (var c in value)
        {
            if (c is < 'A' or > 'Z') return CodeFault.NotUpperCase;
        }

        return null;
    }
}
