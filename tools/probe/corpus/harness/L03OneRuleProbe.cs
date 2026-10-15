// Claim L03-one-rule (README#what-typekit-guarantees; src/CodoMetis.TypeKit.Generators/README.md#parsing-and-formatting). L03 (claim): every entry runs Create and names its fault.
// Expected: no failure for any row; the documented differences (lossy default text of TimeOnly/DateTime/DateTimeOffset, a registered converter, StoredJsonConverterFactory) are passes.
using Probes.Shapes;
namespace Probes;
public sealed class L03OneRuleProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("L03-one-rule");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, r => Task.FromResult(TextClaims.OneRule(r, Registry.Of(r))));
}
