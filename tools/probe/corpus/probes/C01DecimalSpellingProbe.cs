// Claim C01-decimal-spelling (docs/json.md#decimals-in-one-spelling): both decimal converters write one spelling per value, the smallest
// scale, as a number or a string, for every decimal: nullable ones, dictionary keys and every value object over a decimal; both read a
// number or a string holding one and keep its scale. The check is Probes.Json.DecimalSpelling.
using Probes.Json;
using Probes.Shapes;
namespace Probes;
public sealed class C01DecimalSpellingProbe
{
    const string Claim = "C01-decimal-spelling";
    public static TheoryData<string, int> Data() => Rows.Data(Claim);
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, r => Task.FromResult(DecimalSpelling.Check(r)));
}
