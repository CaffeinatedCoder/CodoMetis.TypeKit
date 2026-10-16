// Calibration of the shared HTTP host (not a claim): runs the baseline over the rows of claim C07 to find harness mistakes before the claims use it.
using Probes.Shapes;
namespace Probes;
public sealed class BaseProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("BaseProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("BASE", idx, async r => await HttpBaseline.Run(r, Registry.Of(r)), probe);
}
