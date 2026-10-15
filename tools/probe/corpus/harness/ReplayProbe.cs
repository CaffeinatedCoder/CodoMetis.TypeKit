// Debug aid (not a claim): PROBE_REPLAY="rowsfile:idx" re-runs the baseline for one row and writes its exchange log to /tmp/replay.txt.
using Probes.Shapes;
namespace Probes;
public sealed class ReplayProbe
{
    [Fact]
    public async Task Replay()
    {
        var spec = Environment.GetEnvironmentVariable("PROBE_REPLAY"); if (spec is null) return;
        var parts = spec.Split(':'); var r = Rows.Get(parts[0], int.Parse(parts[1]));
        var f = await HttpBaseline.Run(r, Registry.Of(r));
        File.WriteAllText("/tmp/replay.txt", r.ToString() + "\nRESULT: " + (f ?? "pass") + "\nNOTES: " + r.Notes + "\n" + r.Log);
    }
}
