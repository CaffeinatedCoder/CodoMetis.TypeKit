// Calibration of the shared HTTP host (not a claim): runs the baseline's wire half over its own rows of claim C07 to find harness mistakes
// before the claims use it. The schema half is claim C21's (C21OpenapiSchemaParityProbe). Each comparison is a check of its own (HttpBaseline.Run),
// of the implicit obligation claim: C07 promises nothing of the wire, and its seed names no obligation.
using Probes.Shapes;
namespace Probes;
public sealed class BaseProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("BaseProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("BASE", idx, async r => await HttpBaseline.Run(r, Registry.Of(r), schema: false), probe);
}
