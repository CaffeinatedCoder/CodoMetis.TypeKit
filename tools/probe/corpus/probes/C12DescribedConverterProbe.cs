// Claim C12-described-converter (docs/json.md#describing-what-a-converter-writes and the AspNetCore README's A described type). The check is SchemaChecks.C12 in harness/SchemaClaim.cs.
using Probes.Schema;
using Probes.Shapes;
namespace Probes;
public sealed class C12DescribedConverterProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C12-described-converter");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, SchemaChecks.C12);
}
