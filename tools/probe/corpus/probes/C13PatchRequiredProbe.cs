// Claim C13-patch-required (docs/merge-patches.md#required-constructor-parameters). The check is SchemaChecks.C13 in harness/SchemaClaim.cs.
using Probes.Schema;
using Probes.Shapes;
namespace Probes;
public sealed class C13PatchRequiredProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C13PatchRequiredProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, SchemaChecks.C13);
}
