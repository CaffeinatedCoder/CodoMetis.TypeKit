// Claim C14-declared-problem (CHANGELOG 2.1.0 Fixed): "OpenAPI: a problem response an endpoint declares itself (ProducesProblem,
// [ProducesResponseType]) describes fault, and names its problem types." and "an operation with fault responses lists every status ascending,
// the ones its endpoint declares itself among them". declared: the endpoint declares a 404 problem itself, a status HFault.Missing answers:
// minimal .ProducesProblem(404) / MVC [ProducesResponseType<ProblemDetails>(404, "application/problem+json")] (what ProducesProblem declares),
// [ProducesResponseType<ProblemDetails>(404)] on the lambda or the action, or nothing (the control). The mapping gives a problem type per kind.
// Oracle: FaultClaim: the 404's schema describes fault and rule, its description names https://p.example/NotFound, the statuses ascending.
using Probes.Faults;
using Probes.Shapes;
namespace Probes;
public sealed class C14DeclaredProblemProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C14DeclaredProblemProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("C14", idx, r => FaultClaim.Run(r, Spec(r)), probe);

    public static Spec Spec(Probes.Shapes.Row r)
    {
        var (ep, ctl) = r["declared"] switch
        {
            "ProducesProblem" => (new MEp("d", [], [404], "pp"), "C14Pp"),
            "ProducesResponseType" => (new MEp("d", [], [404], "prt"), "C14Prt"),
            _ => (new MEp("d", []), "C14None"),
        };
        return new Spec { Minimal = new MGrp("/c14", [], [], [ep]), Controllers = [ctl], Errors = [FxErr.E("Missing"), FxErr.E("Clash")] };
    }
}
