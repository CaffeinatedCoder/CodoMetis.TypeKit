// Claim C14-declared-problem (CHANGELOG 2.1.0 Fixed): "OpenAPI: a problem response an endpoint declares itself (ProducesProblem,
// [ProducesResponseType]) describes fault, and names its problem types." and "an operation with fault responses lists every status ascending,
// the ones its endpoint declares itself among them". declared: the endpoint declares a 404 problem itself, a status HFault.Missing answers:
// minimal .ProducesProblem(404) / MVC [ProducesResponseType<ProblemDetails>(404, "application/problem+json")] (what ProducesProblem declares),
// [ProducesResponseType<ProblemDetails>(404)] on the lambda or the action, or nothing (the control). The mapping gives a problem type per kind.
// Oracle: FaultClaim: the 404's schema describes fault and rule, its description names https://p.example/NotFound, the statuses ascending.
// A check per obligation: declared-describes-fault (the problem schemas' fault and rule), declared-names-types (the problem types the
// descriptions name, and what each member answers), ascending-with-declared (which statuses each operation lists, and their order), and in
// the rows of [ProducesResponseType<ProblemDetails>(404)] application-json-declared (its schema and its problem types). declared=none is the
// control: the same checks, where the endpoint declares nothing. Not checked here: that application/json keeps its media type.
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
        var json = r["declared"] == "ProducesResponseType";
        return new Spec
        {
            Minimal = new MGrp("/c14", [], [], [ep]), Controllers = [ctl], Errors = [FxErr.E("Missing"), FxErr.E("Clash")],
            Against = Baseline.Docs("CHANGELOG.md#2.1.0-fixed"),
            Of = (aspect, _) => aspect switch
            {
                Aspect.DocDescribes => json ? ["declared-describes-fault", "application-json-declared"] : ["declared-describes-fault"],
                Aspect.DocTypes => json ? ["declared-names-types", "application-json-declared"] : ["declared-names-types"],
                Aspect.DocListed or Aspect.DocOrder => ["ascending-with-declared"],
                _ => ["declared-names-types"],
            },
        };
    }
}
