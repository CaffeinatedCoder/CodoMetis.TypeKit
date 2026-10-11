using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using CodoMetis.ValueRanges;

namespace RangeValueObjects;

public enum PeriodFault { NotFinite, LongerThanAYear }

/// <summary>A contract runs for a finite period of at most a year: the rule the range alone cannot state.</summary>
public readonly partial record struct ContractPeriod : IValidatedValue<ContractPeriod, DateRange, PeriodFault>
{
    public static Result<ContractPeriod, PeriodFault> Create(DateRange value)
    {
        if (value is not DateRange.Finite finite) return Result.Error(PeriodFault.NotFinite);
        if (finite.End > finite.Start.AddYears(1)) return Result.Error(PeriodFault.LongerThanAYear);
        return new ContractPeriod(value);
    }
}

/// <summary>A plain value object over a range: no rule, only a name.</summary>
public readonly partial record struct Availability : IValue<DateRange>;

#if RANGESET
/// <summary>A multirange is a generic type: the generators refuse it (CMTK1005).</summary>
public readonly partial record struct Blackouts : IValue<RangeSet<DateRange, DateOnly>>;
#endif
