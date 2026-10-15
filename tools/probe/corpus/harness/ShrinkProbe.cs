// Debug aid: PROBE_SHRINK="rowsfile:idx:check" shrinks a failing row by resetting axes; writes /tmp/shrink.txt. check = baseline.
using Probes.Shapes;
namespace Probes;
public sealed class ShrinkProbe
{
    [Fact]
    public async Task Shrink()
    {
        var spec = Environment.GetEnvironmentVariable("PROBE_SHRINK"); if (spec is null) return;
        var p = spec.Split(':');
        Func<Row, Task<string?>> check = p[2] switch
        {
            "baseline" => x => HttpBaseline.Run(x, Registry.Of(x)),
            _ => ShrinkChecks.For(p[2]) ?? throw new ArgumentException(p[2]),
        };
        var sb = new System.Text.StringBuilder();
        foreach (var idx in p[1].Split(','))
        {
            try
            {
                var (s, f) = await Shrinker.Shrink(Rows.Get(p[0], int.Parse(idx)), check);
                sb.AppendLine($"#{idx} SHRUNK: {s}\n      FAILURE: {f.Replace("\n", " | ")}");
            }
            catch (Exception ex) { sb.AppendLine($"#{idx} (no shrink: {ex.Message})"); }
        }
        File.WriteAllText("/tmp/shrink.txt", sb.ToString());
    }
}
public static class ShrinkChecks
{
    public static readonly Dictionary<string, Func<Row, Task<string?>>> All = new();
    public static Func<Row, Task<string?>>? For(string name) => All.TryGetValue(name, out var f) ? f : null;
}
