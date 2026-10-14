#if PROBES
using CodoMetis.TypeKit;
using FaultSetComposition.Shared;
using Refusal = FaultSetComposition.Union.Fault<FaultSetComposition.Union.TeamFault, FaultSetComposition.Shared.ConcurrencyFault>;

namespace FaultSetComposition;

public static class Probes
{
    // P1: a shared fault straight into a result of the composed fault.
    public static Result<Refusal> Shared(CommandOutcome outcome) => SharedFaults.Translate(outcome, "The team");

    // P2: an own fault straight in, through Fault.Of.
    public static Result<Refusal> Own() => Fault.Of(FaultSetComposition.Union.TeamFault.NotFound);

    // P3: default of the composed fault.
    public static Refusal Default() => default;

    // P4, the control: default of an Option, which CMTK0001 refuses wherever the analyzer is loaded.
    public static Option<int> Control() => default;
}
#endif
