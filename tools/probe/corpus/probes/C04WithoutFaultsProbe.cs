// Claim C04-without-faults (AspNetCore README#leaving-out-what-a-route-cannot-answer): "The operation then lists no status and no problem
// type for a member left out. It lists no response for a status that no remaining member is mapped to." "Reporting a member left out
// anyway throws an InvalidOperationException naming the endpoint and the member". "If a group gives NotFound a status and one endpoint
// inside leaves NotFound out, that endpoint drops it, and the group's other endpoints keep the status." "These stop the host from starting
// ... a status that every endpoint it's set on leaves out (... on a group whose endpoints all leave the member out)".
// scope: who leaves HFault.Down (503, mapped to nothing else) out: one endpoint or action beside a sibling that keeps it; a route group or
// MapControllers()'s builder (README: "A route group or a controller says it for its endpoints"; "Any convention builder that covers several
// endpoints works the same"); a controller class beside a sibling controller. combined: a wider scope also gives Down 507 (a group, the
// controller class, or MapControllers()'s builder). With scope=group the 507's whole scope leaves Down out, so the host must not start.
// Oracle: FaultClaim (README rules over the declarations); the other members (Missing, Clash) answer as mapped. A check per obligation:
//   doc-omits-left-out: every endpoint's operation (statuses, order, problem schema, problem types);
//   reporting-left-out-throws: reporting the member left out throws naming the endpoint and the member, against the refusal; and, outside
//   the rows of narrows-wider-status, what the other members answer, which the README sets against "a status the document doesn't list";
//   narrows-wider-status (scope endpoint or controller, combined): what every member but the one left out answers, the kept 507 among
//   them, and which statuses each operation lists;
//   left-out-everywhere-stops-start (scope group, combined): the host's refusal, naming HFault.Down and 507, the row's one check.
using Probes.Faults;
using Probes.Shapes;
namespace Probes;
public sealed class C04WithoutFaultsProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C04WithoutFaultsProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("C04", idx, r => FaultClaim.Run(r, Spec(r)), probe);

    static readonly Decl Out = new("HFault", "Down", 0), Status = new("HFault", "Down", 507);
    public static Spec Spec(Probes.Shapes.Row r)
    {
        var combined = r["combined"] == "with-WithFaultStatus-on-group";
        var narrows = combined && r["scope"] is "endpoint" or "controller";
        var s = new Spec
        {
            Errors = [FxErr.E("Missing"), FxErr.E("Clash"), FxErr.E("Down")],
            Against = Baseline.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#leaving-out-what-a-route-cannot-answer"),
            Refusals = ["reporting-left-out-throws", "left-out-everywhere-stops-start"],
            Of = (aspect, _) => aspect switch
            {
                Aspect.Start => ["left-out-everywhere-stops-start"],
                Aspect.LeftOut => ["reporting-left-out-throws"],
                Aspect.DocListed when narrows => ["doc-omits-left-out", "narrows-wider-status"],
                Aspect.DocListed or Aspect.DocOrder or Aspect.DocDescribes or Aspect.DocTypes => ["doc-omits-left-out"],
                _ => [narrows ? "narrows-wider-status" : "reporting-left-out-throws"],
            },
        };
        switch (r["scope"])
        {
            case "endpoint":
                s.Minimal = combined
                    ? new MGrp("/fx", [], [new MGrp("/ep", [Status], [], [new MEp("wo", [Out]), new MEp("keep", [])])], [])
                    : new MGrp("/fx", [], [], [new MEp("ep/wo", [Out]), new MEp("ep/keep", [])]);
                s.Controllers = [combined ? "C04EpS" : "C04Ep"];
                break;
            case "group":
                s.Minimal = combined
                    ? new MGrp("/fx", [], [new MGrp("/gr", [Status], [new MGrp("/in", [Out], [], [new MEp("a", []), new MEp("b", [])])], [])], [])
                    : new MGrp("/fx", [], [new MGrp("/gr", [Out], [], [new MEp("a", []), new MEp("b", [])])], [new MEp("keep", [])]);
                s.Controllers = ["C04Gr"];
                s.Builder = combined ? [Status, Out] : [Out];
                if (combined) s.Refused = ["HFault.Down", "507"];
                break;
            default:   // controller: MVC only (scope=controller x http.host minimal, route-group excluded in claims.json)
                s.Controllers = ["C04CtWo", "C04CtKeep"];
                s.Builder = combined ? [Status] : [];
                break;
        }
        return s;
    }
}
