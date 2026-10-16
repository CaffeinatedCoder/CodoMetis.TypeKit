// Claim C10-preserve-guard (src/CodoMetis.TypeKit.Generators/README.md#json): under ReferenceHandler.Preserve a value object over a type the
// serializer walks as an object throws NotSupportedException in both directions, never writing an $id twice; everything else is unaffected.
// The check is Probes.Json.PreserveGuard.
using Probes.Json;
using Probes.Shapes;
namespace Probes;
public sealed class C10PreserveGuardProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C10PreserveGuardProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, r => Task.FromResult(PreserveGuard.Check(r)));
}
