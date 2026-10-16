// Claim L05-equality (README.md#what-typekit-guarantees; Generators README "Equality" and "Comparison"; oracle "Equality (metamorphic)"):
// equal value objects hash alike, CompareTo is zero exactly when Equals is true, both agree with the wrapped type's, through every base and
// wherever the value is held. The check is Probes.Json.Equality; the shapes are harness/L05Shapes.g.cs.
using Probes.Json;
using Probes.Shapes;
namespace Probes;
public sealed class L05EqualityProbe
{
    const string Claim = "L05-equality";
    public static TheoryData<string, int> Data() => Rows.Data(Claim);
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, r => Task.FromResult(Equality.Check(r)));
}
