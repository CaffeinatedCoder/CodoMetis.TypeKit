using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Prober;

/// <summary>
/// <c>new</c>: the mechanical half of writing a probe, done the same way every time, into the branch's staging.
/// It writes the probe's skeleton in the harness's form (<c>probes/P.cs</c>), headed by the claim's source and
/// every obligation's id and sentence, with one check per obligation that throws until it is written; stages the
/// probe's own rows (a covering array over its claim's pairs the claim's other probes have not covered, as
/// <c>rows --probe P</c> writes);
/// and registers the probe with an empty <c>seeds</c>, so <c>seeds</c> and <c>Seeds.Problems</c> name each
/// obligation's seed as owed. The skeleton builds, so the other probes keep building beside it, and every row of
/// it ends as a harness error, which fails the row, earns nothing and is never a seed's catch (decision 103).
/// It refuses, writing nothing, a claim that is not in the claims, a name a probe has already, and a claim
/// whose first surface is in no family of the replay configuration (or not in the one <c>--family</c> names).
/// </summary>
internal static partial class NewProbe
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*Probe$")]
    private static partial Regex ProbeName();

    [GeneratedRegex("^([A-Za-z]+[0-9]+)(?:-(.*))?$")]
    private static partial Regex ClaimPrefix();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NotAlphanumeric();

    public static int Run(Ledger l, Args a)
    {
        var claimId = a.Need("claim");
        var claim = l.Claims().FirstOrDefault(c => Files.Str(c["id"]) == claimId)
                    ?? throw new Refusal($"no claim {claimId} in claims.json (tracked or staged)");
        var probe = a.Get("probe") is { Length: > 0 } named ? named : DefaultName(claimId);
        if (!ProbeName().IsMatch(probe)) throw new Refusal($"{probe} is not a probe's name: a C# class name ending in Probe", 2);
        if (Taken(l, probe) is { } where) throw new Refusal($"a probe {probe} exists already ({where}); name another with --probe");
        var family = Family(l, claim, a.Get("family"));
        var obligations = Obligations(claim);

        var source = Path.Combine(l.StagingDir, "probes", probe + ".cs");
        Files.WriteAtomically(source, Skeleton(claim, probe, family, obligations));
        // The probe's own rows, its claim's other probes' untouched: a covering array over the pairs they have not covered.
        var (rows, _) = Commands.StageRows(l, probe, claimId, full: false);
        var registry = Path.Combine(l.StagingDir, "registry.json");
        var staged = Files.ReadJson(registry) as JsonObject ?? new JsonObject { ["probes"] = new JsonObject() };
        var probes = staged["probes"] as JsonObject ?? [];
        probes[probe] = new JsonObject { ["claim"] = claimId, ["filter"] = "*" + probe, ["seeds"] = new JsonArray() };
        staged["probes"] = probes.DeepClone();
        Files.WriteJson(registry, staged);

        var o = l.Ctx.Out;
        o.WriteLine($"prober new: {probe} for claim {claimId}{(family is null ? "" : $" (family {family})")}, staged in {l.StagingDir}");
        o.WriteLine($"  probes/{probe}.cs: the skeleton; it builds, and every row ends as a harness error until each obligation's check is written");
        o.WriteLine($"  rows/{probe}.json: {rows} rows over the claim's pairs its other probes have not covered" +
                    (rows == 0 ? "; with no row the probe has no test to run" : ""));
        o.WriteLine($"  registry.json: {probe}, claim {claimId}, filter *{probe}, no seed yet");
        o.WriteLine("still owed, per obligation:");
        foreach (var (id, sentence, method) in obligations)
            o.WriteLine($"  {id}: the check {method} ({(sentence is null ? "the claim as a whole" : $"\"{sentence}\"")}), and a seed that breaks it " +
                        (id == Claim.ImplicitObligation && sentence is null
                             ? $"(`prober register --probe {probe} --claim {claimId} --filter \"*{probe}\" --seed seeds/{claimId}.patch --defect \"...\"`)"
                             : $"(`prober register --probe {probe} --claim {claimId} --filter \"*{probe}\" --obligation {id} --seed seeds/{claimId}-{id}.patch --defect \"...\"`)"));
        o.WriteLine($"then: `prober try --probe {probe}` until each row ends as it should, `prober seeds --probe {probe}`, `prober register` (it lints the staged sources), `prober publish`");
        return 0;
    }

    /// <summary>The claim id's letter-digit prefix, then its words run together, then <c>Probe</c>: C20-testing-shouldhold is C20TestingShouldholdProbe.</summary>
    internal static string DefaultName(string claimId)
    {
        var m = ClaimPrefix().Match(claimId);
        var (prefix, rest) = m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : ("", claimId);
        return Pascal(prefix) + Pascal(rest) + "Probe";
    }

    private static string Pascal(string text) =>
        string.Concat(NotAlphanumeric().Split(text).Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>Where a probe of that name is already: the registry, a source file of that name, or a class of that name in any corpus source.</summary>
    private static string? Taken(Ledger l, string probe)
    {
        if (l.Registry().ContainsKey(probe)) return "in the registry";
        var declares = new Regex($@"\b(class|record|struct)\s+{probe}\b");
        foreach (var dir in new[] { l.CorpusDir, l.StagingDir })
            foreach (var sub in new[] { "harness", "probes" })
            {
                var folder = Path.Combine(dir, sub);
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
                    if (Path.GetFileNameWithoutExtension(file) == probe || declares.IsMatch(File.ReadAllText(file))) return file;
            }
        return null;
    }

    /// <summary>
    /// The family the replay runs the probe in, its claim's first surface's (decision 101), checked against
    /// <c>--family</c>. Null where the replay configuration names no families and none was asked for.
    /// </summary>
    private static string? Family(Ledger l, JsonObject claim, string? asked)
    {
        var families = (l.AxesBlock()["replay"] as JsonObject)?["families"] as JsonObject;
        if (families is null)
            return asked is null ? null : throw new Refusal($"--family {asked}: the replay configuration names no families", 2);
        var surface = Files.Strings(claim["surfaces"]).FirstOrDefault() ?? "";
        var family = families.FirstOrDefault(kv => Files.Strings(kv.Value).Contains(surface)).Key
                     ?? throw new Refusal($"the first surface of claim {Files.Str(claim["id"])} ({surface}) is in no family of the replay configuration, so a replay by family would never run its probe");
        if (asked is not null && asked != family)
            throw new Refusal($"claim {Files.Str(claim["id"])} is of family {family} (its first surface, {surface}), not {asked}", 2);
        return family;
    }

    /// <summary>Each obligation of the claim with its sentence and the check method the skeleton gives it; a claim without obligations has one, the claim itself, with no sentence.</summary>
    internal static List<(string Id, string? Sentence, string Method)> Obligations(JsonObject claim)
    {
        var listed = (claim["obligations"] as JsonArray ?? []).OfType<JsonObject>()
                                                              .Select(o => (Id: Files.Str(o["id"]), Sentence: (string?)OneLine(Files.Str(o["sentence"])))).ToList();
        if (listed.Count == 0) listed = [(Claim.ImplicitObligation, null)];
        var methods = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string, string?, string)>();
        foreach (var (id, sentence) in listed)
        {
            var method = "Check" + Pascal(id);
            for (var n = 2; !methods.Add(method); n++) method = "Check" + Pascal(id) + n;
            result.Add((id, sentence, method));
        }
        return result;
    }

    /// <summary>The probe's source: the form of the corpus's probes (<c>Rows.Data</c>, <c>[Theory, MemberData(nameof(Data))]</c>, <c>Rec.Run</c>), one throwing check per obligation.</summary>
    internal static string Skeleton(JsonObject claim, string probe, string? family, List<(string Id, string? Sentence, string Method)> obligations)
    {
        var id = Files.Str(claim["id"]);
        var literal = JsonSerializer.Serialize(probe);
        var b = new StringBuilder();
        void Line(string text = "") => b.Append(text).Append('\n');
        Line($"// Claim {id} ({Files.Str(claim["source"])}), surfaces {string.Join(", ", Files.Strings(claim["surfaces"]))}{(family is null ? "" : $", family {family}")}.");
        Line("// Obligations (claims.json), each with a check below and a seed of its own in the registry entry (decision 104):");
        foreach (var (oid, sentence, _) in obligations)
            Line($"//   {oid}: {(sentence is null ? "the claim as a whole (claims.json lists no obligations for it)" : $"\"{sentence}\"")}");
        Line("// Oracle: TODO: what each row is compared against, and the result it expects.");
        Line("// Written by `prober new`: it builds, and every row ends as a harness error until each obligation's check is written.");
        Line("using Probes.Shapes;");
        Line("namespace Probes;");
        Line($"public sealed class {probe}");
        Line("{");
        Line($"    public static TheoryData<string, int> Data() => Rows.Data({literal});");
        Line("    [Theory, MemberData(nameof(Data))]");
        Line("    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);");
        Line();
        Line("    static async Task<string?> Check(Row r) =>");
        for (var i = 0; i < obligations.Count; i++)
            Line($"        {(i == 0 ? "" : "?? ")}await {obligations[i].Method}(r){(i == obligations.Count - 1 ? ";" : "")}");
        foreach (var (oid, sentence, method) in obligations)
        {
            Line();
            Line($"    // TODO {oid}: {(sentence is null ? "the claim as a whole" : $"\"{sentence}\"")}");
            Line("    // Return the failure text, or null where the row holds it; say how it was compared with r.Note(NoteKeys.Compared, detail).");
            Line($"    static Task<string?> {method}(Row r) => throw new NotImplementedException({JsonSerializer.Serialize($"{id}/{oid}: its check is not written yet")});");
        }
        Line("}");
        return b.ToString();
    }

    private static string OneLine(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
