// Claim C08-same-name-warning (src/CodoMetis.TypeKit.AspNetCore/README.md#faults-of-several-types, Keep names distinct). The check is SchemaChecks.C08 in harness/SchemaClaim.cs.
using Probes.Schema;
using Probes.Shapes;
namespace Probes;
public sealed class C08SameNameWarningProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C08SameNameWarningProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, SchemaChecks.C08);
}
