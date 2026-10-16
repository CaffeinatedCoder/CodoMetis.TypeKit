// Claim C07-fault-responses-blanket (AspNetCore README#every-fault-set-at-once): "builder.Services.AddFaultResponses(); // every fault set an
// endpoint answers with, by its kind"; "The mapping can be your own. It reads each member's kind and description through IFault"; "A set
// mapped on its own keeps its mapping."; "An enum still needs a mapping of its own ... the host doesn't start while an endpoint answers with
// one that's unmapped."; "A member with no kind stops the start, and so does one the mapping throws for or gives a status that isn't an
// error. That's the same as AddFaultResponses<TSet>() failing."
// mapping: AddFaultResponses() by kind, or AddFaultResponses(fault => FaultResponse.For(fault.Kind).WithType(...)) (README's example), which
// throws for FxThrows and answers FxStatus with 302. set: what the row's endpoints answer with beside HFault (and GFault/KFault, by http.response):
// nothing more (fault-set); HFault mapped on its own (own-mapped-set: own statuses and types); or one more endpoint answering an unmapped
// enum, a set with a member of no kind, or a set the mapping refuses. Under by-kind no mapping for every set throws or gives a non-error
// status, so there the refusing set's own AddFaultResponses<TSet>() does, which the README names as the same failure.
// Oracle: FaultClaim (wire and document per member, as mapped), and the refusals as the README states them. BaseProbe runs the HTTP
// payload calibration over the same rows.
using Probes.Faults;
using Probes.Shapes;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
namespace Probes;
public sealed class C07BlanketMappingProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C07BlanketMappingProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("C07", idx, r => FaultClaim.Run(r, Spec(r)), probe);

    /// <summary>HFault mapped on its own (set=own-mapped-set): a status and a problem type of its own per member.</summary>
    static readonly Dictionary<string, int> Own = new() { ["Missing"] = 410, ["Clash"] = 409, ["Bad"] = 422, ["Denied"] = 451, ["Down"] = 503, ["Stale"] = 428, ["Garbled"] = 400 };
    static string OwnType(string member) => "https://own.example/" + member;

    public static Spec Spec(Probes.Shapes.Row r)
    {
        var own = r["mapping"] == "own-IFault-mapping";
        var set = r["set"];
        var s = new Spec
        {
            Minimal = new MGrp("/c7", [], [], [new MEp("k", [])]),
            Controllers = ["C06K"],
            Errors = [.. Enumerable.Range(1, FxErr.H.Length)],
            Mapping = own ? "none" : "by-kind",
            Typed = own,
            Extra = b => FxSets.Map(b, set),
            ExtraControllers = FxSets.Controllers(set),
        };
        s.Services = sv =>
        {
            if (own)
                sv.AddFaultResponses(fault => fault is FxThrows ? throw new FxMappingRefused()
                                            : fault is FxStatus ? 302
                                            : FaultResponse.For(fault.Kind).WithType(new Uri(FaultClaim.TypeOf(fault.Kind.ToString()))));
            if (set == "own-mapped-set")
                sv.AddFaultResponses<HFault>(fault => FaultResponse.Status(Own[fault.ToString()]).WithType(new Uri(OwnType(fault.ToString()))));
            if (!own && set == "mapping-throws") sv.AddFaultResponses<FxThrows>(_ => throw new FxMappingRefused());
            if (!own && set == "status-not-error") sv.AddFaultResponses<FxStatus>(_ => 302);
        };
        if (set == "own-mapped-set")
            s.Map = m => m.Type == "HFault" ? (Own[m.Name], OwnType(m.Name)) : (m.Status, own ? FaultClaim.TypeOf(m.Kind) : null);
        s.RefusedAs = set switch
        {
            "enum-unmapped" => ex => ex is InvalidOperationException && ex.Message.Contains("FxEnum", StringComparison.Ordinal)
                ? null : $"host refused to start, but not with an InvalidOperationException naming FxEnum: {ex.GetType().Name}: {ex.Message}",
            "member-without-kind" => ex => ex is ArgumentException && (own || ex.Message.Contains("FxNoKind.Blank", StringComparison.Ordinal))
                ? null : $"host refused to start, but not with an ArgumentException{(own ? "" : " naming FxNoKind.Blank")}: {ex.GetType().Name}: {ex.Message}",
            "mapping-throws" => ex => ex is FxMappingRefused || ex.InnerException is FxMappingRefused
                ? null : $"host refused to start, but not with the mapping's own exception: {ex.GetType().Name}: {ex.Message}",
            "status-not-error" => ex => ex is ArgumentException && ex.Message.Contains("302", StringComparison.Ordinal)
                ? null : $"host refused to start, but not with an ArgumentException naming 302: {ex.GetType().Name}: {ex.Message}",
            _ => null,
        };
        return s;
    }
}
