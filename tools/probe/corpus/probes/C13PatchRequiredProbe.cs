// Claim C13-patch-required (docs/merge-patches.md#required-constructor-parameters). The check is SchemaChecks.C13 in harness/SchemaClaim.cs.
using Probes.Schema;
using Probes.Shapes;
namespace Probes;
public sealed class C13PatchRequiredProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C13-patch-required");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, SchemaChecks.C13);
}
