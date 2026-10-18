using System.Text.Json;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// The release gate. It needs nothing but the tracked corpus and a replay: no model, no login, no local
/// memory of a developer's machine. It refuses while the staging is unpublished, a finding is open or a row
/// skips for one, a probe has not passed at its current identity and footprint, an expected row did not run, or a claim touched
/// since the release has pairs no probe covers and no reviewed waiver. Touched is chosen conservatively: the
/// claim's source document, its surfaces' source areas, the paths its probes watch or the build inputs changed; footprints only add.
/// </summary>
internal static class ReleaseGate
{
    private static readonly string[] BuildInputs = ["Directory.Packages.props", "Directory.Build.props", "global.json"];

    public static int Run(Ledger l, string? sinceArg, bool json)
    {
        var since = sinceArg ?? Git.Out(l.Top, "describe", "--tags", "--abbrev=0");
        var (universe, _) = Axes.Build(l, since);
        var footprints = l.Footprints();
        var reasons = new List<string>();
        if (Directory.Exists(l.StagingDir) && Directory.EnumerateFiles(l.StagingDir, "*", SearchOption.AllDirectories).Any())
            reasons.Add("the staging corpus is not published (run `publish` and commit tools/probe/corpus)");
        var open = l.OpenFindings();
        if (open.Count > 0) reasons.Add($"open findings: {string.Join(", ", open)}");

        var runs = Files.ReadLines(l.P("runs.jsonl")).GroupBy(r => Files.Str(r["probe"]), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        foreach (var s in l.ProbeStates(includeRetired: true))
        {
            if (s.Stale) { reasons.Add($"{s.Probe}: not replayed at its current identity and footprint (run `prober replay --all`)"); continue; }
            if (Files.Str(runs.GetValueOrDefault(s.Probe)?["result"]) != "pass") { reasons.Add($"{s.Probe}: its last replay failed"); continue; }
            var evidence = Files.ReadJson(l.EvidencePath(s.Probe)) as JsonObject;
            var expected = new HashSet<int>(); var executed = new HashSet<int>();
            foreach (var v in (evidence?["variants"] as JsonArray ?? []).OfType<JsonObject>())
            {
                foreach (var i in v["expected"] as JsonArray ?? []) expected.Add((int)Files.Num(i));
                foreach (var i in v["executed"] as JsonArray ?? []) executed.Add((int)Files.Num(i));
            }
            expected.ExceptWith(executed);
            if (expected.Count > 0) reasons.Add($"{s.Probe}: {expected.Count} expected row(s) did not run");
            // A skip is an open finding the corpus itself records, so it binds where the ledger has none (CI).
            foreach (var (finding, rows) in Ledger.FindingSkips(evidence))
                reasons.Add($"{s.Probe}: {rows} row(s) skip for finding {finding}; fix it and remove the skip, or dismiss it");
        }

        var done = l.Covered(universe);
        var byClaim = l.Registry().GroupBy(kv => Files.Str(kv.Value["claim"]), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList(), StringComparer.Ordinal);
        var changed = Git.Out(l.Top, "diff", "--name-only", since, "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        bool Under(IEnumerable<string> paths) => paths.Any(w => changed.Any(f => f == w || f.StartsWith(w.TrimEnd('/') + "/", StringComparison.Ordinal)));

        // A reviewed waiver meets an obligation without a probe; one without a reason or a reviewer meets nothing.
        var waivers = (Files.ReadJson(l.CorpusPath("waivers.json")) as JsonArray ?? []).OfType<JsonObject>()
            .Where(w => Files.Str(w["claim"]).Length > 0 && Files.Str(w["reason"]).Length > 0 && Files.Str(w["reviewed_by"]).Length > 0)
            .Select(w => Files.Str(w["claim"])).ToHashSet(StringComparer.Ordinal);

        var required = new List<string>(); var waived = new List<string>(); var missedByFootprint = new List<string>(); var footprintOnly = new List<string>();
        var missingTotal = 0; var inapplicableTotal = 0;
        foreach (var claim in universe.Claims)
        {
            var probes = byClaim.GetValueOrDefault(claim.Id) ?? [];
            // What the claim's surfaces watch, and what its probes' registry entries watch beyond their footprints.
            var conservative = Under([claim.Source.Split('#')[0]]) || Under(claim.Watched) || Under(probes.SelectMany(l.Watched)) || Under(BuildInputs);
            var footprint = probes.Count > 0 && probes.Any(p => l.FootprintChangedSince(p, since, footprints));
            if (probes.Count > 0 && conservative && !footprint) missedByFootprint.Add(claim.Id);
            if (footprint && !conservative) footprintOnly.Add(claim.Id);
            if (!(conservative || footprint)) continue;
            if (waivers.Contains(claim.Id)) { waived.Add(claim.Id); continue; }
            // The pairs some obligation wants; one no obligation wants is declared inapplicable and left out, as an exclusion is.
            var pairs = claim.PairsWanted();
            inapplicableTotal += claim.Pairs().Count - pairs.Count;
            var missing = pairs.Count(p => !done.Contains((claim.Id, p)));
            required.Add(claim.Id); missingTotal += missing;
            if (missing > 0) reasons.Add($"{claim.Id}: {missing} of {pairs.Count} pairs uncovered (touched since {since})");
        }

        if (json)
        {
            l.Ctx.Out.WriteLine(new JsonObject
            {
                ["since"] = since, ["touched"] = new JsonArray([.. required.Select(x => (JsonNode)x)]), ["uncovered_pairs"] = missingTotal, ["inapplicable_pairs"] = inapplicableTotal,
                ["reasons"] = new JsonArray([.. reasons.Select(x => (JsonNode)x)]),
                ["selected_by_footprint_only"] = new JsonArray([.. footprintOnly.Select(x => (JsonNode)x)]),
                ["waived"] = new JsonArray([.. waived.Select(x => (JsonNode)x)]),
                ["missed_by_footprint_alone"] = new JsonArray([.. missedByFootprint.Select(x => (JsonNode)x)]), ["passes"] = reasons.Count == 0,
            }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return reasons.Count == 0 ? 0 : 1;
        }
        if (reasons.Count > 0)
        {
            l.Ctx.Error.WriteLine($"probe gate: refused for {l.Branch} since {since} ({required.Count} claim(s) touched, {missingTotal} pair(s) uncovered):");
            foreach (var r in reasons) l.Ctx.Error.WriteLine($"  {r}");
            return 1;
        }
        l.Ctx.Out.WriteLine($"probe gate: {l.Branch} passes since {since}: {required.Count} claim(s) touched and fully covered" +
                            (waived.Count > 0 ? $", {waived.Count} waived ({string.Join(", ", waived)})" : "") +
                            ", every probe replayed and passing, no open finding.");
        return 0;
    }
}
