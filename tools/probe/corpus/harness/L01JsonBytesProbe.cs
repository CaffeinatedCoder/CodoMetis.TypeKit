// Claim L01-json-bytes (README#what-typekit-guarantees "Swapping a primitive for a value object keeps the JSON"; docs/json.md#the-wrapped-types-json):
// the value object's JSON is the wrapped type's, byte for byte, under whatever options, as value and as key, and each reads the other's JSON.
// Oracle: differential (JsonParity). Expected: no failure for any row; STJ refusing its own option combination, and the documented
// Preserve refusal, are passes.
using Probes.Shapes;
using System.Text.Json;
namespace Probes;
public sealed class L01JsonBytesProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("L01JsonBytesProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, async r =>
    {
        var e = Registry.Of(r);
        var a = JsonRow.Apply(r, e);
        JsonSerializerOptions? plainOpts = null;
        if (e.Kind == "closed-set" && r.Is("json.Converters", "own-for-wrapped")) plainOpts = JsonRow.Apply(r.With("json.Converters", "default"), e).Options; // a closed set travels as its name, not as a string
        await Task.CompletedTask;
        return JsonParity.Check(r, e, a.Options!, r["shape.position"], (r.Get("shape.containing") ?? "class"), r["input.json"], plainOpts);
    });
}
