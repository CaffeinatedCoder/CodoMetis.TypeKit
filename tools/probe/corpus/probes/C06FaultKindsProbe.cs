// Claim C06-fault-kinds (AspNetCore README#a-closed-set-as-the-fault-type): "builder.Services.AddFaultResponses<AccountFault>(); // every member
// by its kind"; "The kind gives the status": PreconditionFailed 412, Malformed 400, Invalid 422, Unavailable 503; "A member without one
// [a description] has no detail." The member of the row's kind (HFault.Stale, Garbled, Bad, Down, none with a description) is reported on
// a minimal endpoint and an MVC action; the operation lists its kind's status among every member's, with no problem type (the mapping by
// kind gives none). The fault sets are mapped per set (AddFaultResponses<HFault>(), <GFault>(), <KFault>()). Oracle: FaultClaim.
using Probes.Faults;
using Probes.Shapes;
namespace Probes;
public sealed class C06FaultKindsProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C06-fault-kinds");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run("C06", idx, r => FaultClaim.Run(r, Spec(r)), claim);

    public static Spec Spec(Probes.Shapes.Row r) => new()
    {
        Minimal = new MGrp("/c6", [], [], [new MEp("k", [])]),
        Controllers = ["C06K"],
        Typed = false,
        Errors = [FxErr.E(r["kind"] switch { "PreconditionFailed" => "Stale", "Malformed" => "Garbled", "Invalid" => "Bad", _ => "Down" })],
    };
}
