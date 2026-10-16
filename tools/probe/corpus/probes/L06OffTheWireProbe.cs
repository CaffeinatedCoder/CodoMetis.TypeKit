// Claim L06-off-the-wire (README.md#what-typekit-guarantees): System.Text.Json throws NotSupportedException for an Option or a Result, in
// both directions, in every position, unless the application registered its own converter, which takes precedence. The check is
// Probes.Json.OffTheWire.
using Probes.Json;
using Probes.Shapes;
namespace Probes;
public sealed class L06OffTheWireProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("L06OffTheWireProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, OffTheWire.Check);
}
