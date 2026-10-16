// Claim C16-options-agreement (src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says, Register the same on both option sets). The check is SchemaChecks.C16 in harness/SchemaClaim.cs.
using Probes.Schema;
using Probes.Shapes;
namespace Probes;
public sealed class C16OptionsAgreementProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C16-options-agreement");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, SchemaChecks.C16);
}
