// Claim C06-fault-kinds (AspNetCore README#a-closed-set-as-the-fault-type): "builder.Services.AddFaultResponses<AccountFault>(); // every member
// by its kind"; "The kind gives the status": PreconditionFailed 412, Malformed 400, Invalid 422, Unavailable 503; "A member without one
// [a description] has no detail."; "The description becomes the problem's detail"; "A member must have a kind. A member whose kind is
// default, which is no kind, fails this call." The member of the row's kind (HFault.Stale, Garbled, Bad, Down, none with a description) and
// HFault.Missing (described "Missing.") are reported on a minimal endpoint and an MVC action; the operation lists its kind's status among
// every member's, with no problem type (the mapping by kind gives none). The fault sets are mapped per set (AddFaultResponses<HFault>(),
// <GFault>(), <KFault>()). Oracle: FaultClaim; and AddFaultResponses<FxNoKind>() refused, naming its member of no kind. A check per obligation:
//   kind-gives-status: each member's status, fault and type, and the operation (statuses, order, problem schema, problem types);
//   description-as-detail: HFault.Missing's detail is its description; no-description-no-detail: the row's member has none;
//   member-must-have-kind: AddFaultResponses<FxNoKind>() refused, against the refusal, in every row.
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Probes.Faults;
using Probes.Shapes;
namespace Probes;
public sealed class C06FaultKindsProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C06FaultKindsProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("C06", idx, Check, probe);

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var noKind = r.Check("member-must-have-kind", Baseline.Refusal, _ => NoKindRefused());
        var answered = await FaultClaim.Run(r, Spec(r));
        return noKind ?? answered;
    }

    /// <summary>"A member must have a kind. A member whose kind is default, which is no kind, fails this call."</summary>
    static string? NoKindRefused()
    {
        try { new ServiceCollection().AddFaultResponses<FxNoKind>(); }
        catch (ArgumentException ex)
        {
            if (!ex.Message.Contains("FxNoKind.Blank", StringComparison.Ordinal)) return $"AddFaultResponses<FxNoKind>() refused, but not with an ArgumentException naming FxNoKind.Blank: {ex.GetType().Name}: {ex.Message}";
            return null;
        }
        catch (Exception ex) { return $"AddFaultResponses<FxNoKind>() refused, but not with an ArgumentException naming FxNoKind.Blank: {ex.GetType().Name}: {ex.Message}"; }
        return "AddFaultResponses<FxNoKind>() took FxNoKind.Blank, a member of no kind";
    }

    public static Spec Spec(Probes.Shapes.Row r) => new()
    {
        Minimal = new MGrp("/c6", [], [], [new MEp("k", [])]),
        Controllers = ["C06K"],
        Typed = false,
        Against = Baseline.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#a-closed-set-as-the-fault-type"),
        Of = (aspect, m) => aspect == Aspect.Detail
            ? [m?.Q == "HFault.Missing" ? "description-as-detail" : "no-description-no-detail"]
            : ["kind-gives-status"],
        Errors = [FxErr.E(r["kind"] switch { "PreconditionFailed" => "Stale", "Malformed" => "Garbled", "Invalid" => "Bad", _ => "Down" }), FxErr.E("Missing")],
    };
}
