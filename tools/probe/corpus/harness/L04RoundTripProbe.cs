// Claim L04-round-trip (README#what-typekit-guarantees; src/CodoMetis.TypeKit.Generators/README.md#parsing-and-formatting). L04 round trip (metamorphic): VO text/JSON written by the project reads back as the same value.
// Expected: no failure for any row; the documented differences (lossy default text of TimeOnly/DateTime/DateTimeOffset, a registered converter, StoredJsonConverterFactory) are passes.
using Probes.Shapes;
namespace Probes;
public sealed class L04RoundTripProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("L04-round-trip");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, r => Task.FromResult(TextClaims.RoundTrip(r, Registry.Of(r))));
}
