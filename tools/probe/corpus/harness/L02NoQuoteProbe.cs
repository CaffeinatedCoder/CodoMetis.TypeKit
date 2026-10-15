// Claim L02-no-quote (README#what-typekit-guarantees; src/CodoMetis.TypeKit.Generators/README.md#parsing-and-formatting). L02 (exception): a rejection never repeats the input.
// Expected: no failure for any row; the documented differences (lossy default text of TimeOnly/DateTime/DateTimeOffset, a registered converter, StoredJsonConverterFactory) are passes.
using Probes.Shapes;
namespace Probes;
public sealed class L02NoQuoteProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("L02-no-quote");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, r => Task.FromResult(TextClaims.NoQuote(r, Registry.Of(r))));
}
