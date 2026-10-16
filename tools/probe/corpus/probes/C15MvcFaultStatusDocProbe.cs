// Claim C15-mvc-fault-status-doc (CHANGELOG 2.1.0 Fixed): "OpenAPI: a WithFaultStatus on the builder MapControllers() returns reaches the
// document, as it reached the response." Each MVC setter, with a status (HFault.Denied 451) or a member left out (HFault.Down): what
// MapControllers()'s builder, an action's attribute and a controller's attribute declare reaches the document as it reaches the response.
// Precedence (README "A status per endpoint"): "ASP.NET puts the metadata of MapControllers()'s builder after every attribute, so a status
// set there wins over an action's own"; "an action's over its controller's": an action with its own Denied 452 sits beside each scope.
// Minimal hosts take the equivalent of each setter (README: "Any convention builder that covers several endpoints works the same"):
// MapControllers()'s builder = a group over every endpoint, an action = the endpoint, a controller = a group beside a sibling endpoint;
// there "An endpoint's own wins over its group's". Oracle: FaultClaim.
using Probes.Faults;
using Probes.Shapes;
namespace Probes;
public sealed class C15MvcFaultStatusDocProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C15MvcFaultStatusDocProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("C15", idx, r => FaultClaim.Run(r, Spec(r)), probe);

    static readonly Decl Own = new("HFault", "Denied", 452);
    public static Spec Spec(Probes.Shapes.Row r)
    {
        var statusOnly = r["kind"] == "status-only";
        var set = statusOnly ? new Decl("HFault", "Denied", 451) : new Decl("HFault", "Down", 0);
        var s = new Spec { Errors = [FxErr.E("Missing"), FxErr.E("Denied"), FxErr.E("Down")] };
        switch (r["setter"])
        {
            case "MapControllers().WithFaultStatus":
                s.Minimal = new MGrp("/c15", [set], [], [new MEp("s/a", []), new MEp("s/p", [Own])]);
                s.Controllers = ["C15M"];
                s.Builder = [set];
                break;
            case "action-attribute":
                s.Minimal = new MGrp("/c15", [], [], [new MEp("s/a", [set]), new MEp("s/k", [])]);
                s.Controllers = [statusOnly ? "C15AS" : "C15AW"];
                break;
            default:   // controller-attribute
                s.Minimal = new MGrp("/c15", [], [new MGrp("/g", [set], [], [new MEp("s/a", []), new MEp("s/p", [Own])])], [new MEp("s/k", [])]);
                s.Controllers = [statusOnly ? "C15CS" : "C15CW", "C15K"];
                break;
        }
        return s;
    }
}
