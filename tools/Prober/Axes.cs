using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Prober;

/// <summary>Two settings of one row, "axis=value" each, held in ordinal order so a pair has one form.</summary>
internal readonly record struct Pair
{
    public string A { get; }
    public string B { get; }

    public Pair(string x, string y)
    {
        if (string.CompareOrdinal(x, y) <= 0) { A = x; B = y; } else { A = y; B = x; }
    }

    public override string ToString() => $"{A} {B}";
}

/// <summary>One claim of the universe: its axes and values, its watched paths, and the pairs it excludes.</summary>
internal sealed class Claim
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<string> Surfaces { get; init; }
    public required IReadOnlyList<string> Watched { get; init; }
    public required SortedDictionary<string, List<string>> Axes { get; init; }
    public required HashSet<Pair> Excluded { get; init; }

    public HashSet<Pair> Pairs()
    {
        var result = new HashSet<Pair>();
        var names = Axes.Keys.ToList();
        for (var i = 0; i < names.Count; i++)
            for (var j = i + 1; j < names.Count; j++)
                foreach (var va in Axes[names[i]])
                    foreach (var vb in Axes[names[j]])
                    {
                        var pair = new Pair($"{names[i]}={va}", $"{names[j]}={vb}");
                        if (!Excluded.Contains(pair)) result.Add(pair);
                    }
        return result;
    }

    /// <summary>The pairs one row sets, restricted to the claim's axes and values (an applied value the claim does not list earns nothing).</summary>
    public HashSet<Pair> PairsOfRow(IReadOnlyDictionary<string, string> row)
    {
        var items = row.Where(kv => Axes.TryGetValue(kv.Key, out var values) && values.Contains(kv.Value))
                       .OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var result = new HashSet<Pair>();
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
                result.Add(new Pair($"{items[i].Key}={items[i].Value}", $"{items[j].Key}={items[j].Value}"));
        return result;
    }

    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["id"] = Id, ["source"] = Source, ["surfaces"] = new JsonArray([.. Surfaces.Select(s => (JsonNode)s)]),
            ["watched"] = new JsonArray([.. Watched.Select(s => (JsonNode)s)]),
            ["axes"] = new JsonObject(Axes.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)new JsonArray([.. kv.Value.Select(v => (JsonNode)v)])))),
        };
        if (Excluded.Count > 0) o["exclude"] = new JsonArray([.. Excluded.Select(p => (JsonNode)new JsonArray(p.A, p.B))]);
        return o;
    }

    public static Claim FromJson(JsonObject o) => new()
    {
        Id = Files.Str(o["id"]), Source = Files.Str(o["source"]), Surfaces = [.. Files.Strings(o["surfaces"])],
        Watched = [.. Files.Strings(o["watched"])],
        Axes = new((o["axes"] as JsonObject ?? []).ToDictionary(kv => kv.Key, kv => Files.Strings(kv.Value).ToList(), StringComparer.Ordinal), StringComparer.Ordinal),
        Excluded = [.. (o["exclude"] as JsonArray ?? []).OfType<JsonArray>().Where(p => p.Count == 2).Select(p => new Pair(Files.Str(p[0]), Files.Str(p[1])))],
    };
}

internal sealed class Universe
{
    public required string Base { get; init; }
    public required string Commit { get; init; }
    public required JsonObject GeneratedVersions { get; init; }
    public required List<Claim> Claims { get; init; }
    public Dictionary<string, Claim> ById => field ??= Claims.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public JsonObject ToJson() => new()
    {
        ["schemaVersion"] = Files.SchemaVersion, ["base"] = Base, ["commit"] = Commit,
        ["generated_versions"] = GeneratedVersions.DeepClone(), ["claims"] = new JsonArray([.. Claims.Select(c => (JsonNode)c.ToJson())]),
    };

    public static Universe? Load(string path) => Files.ReadJson(path) is JsonObject o ? new Universe
    {
        Base = Files.Str(o["base"]), Commit = Files.Str(o["commit"]), GeneratedVersions = o["generated_versions"] as JsonObject is { } g ? (JsonObject)g.DeepClone() : [],
        Claims = [.. (o["claims"] as JsonArray ?? []).OfType<JsonObject>().Select(Claim.FromJson)],
    } : null;
}

internal static class Axes
{
    /// <summary>The fenced <c>```json probe-axes</c> block of the axes file: hand, ignored, refused, axes, surfaces, replay.</summary>
    public static JsonObject Block(string path)
    {
        if (!File.Exists(path)) throw new Refusal($"no axes file at {path}");
        var text = File.ReadAllText(path);
        const string marker = "```json probe-axes";
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) throw new Refusal($"{path} has no {marker} block");
        var body = text[(at + marker.Length)..];
        body = body[..body.IndexOf("```", StringComparison.Ordinal)];
        return JsonNode.Parse(body) as JsonObject ?? throw new Refusal($"the {marker} block of {path} is not a JSON object");
    }

    /// <summary>fnmatch's "*" (any run of characters), the only wildcard the axes file uses.</summary>
    public static bool Matches(string pattern, string name) =>
        Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$", RegexOptions.CultureInvariant);

    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ");

    /// <summary>Builds the universe: each claim takes every axis of its surfaces, minus quoted narrowings, plus its own axes.</summary>
    public static (Universe Universe, List<string> Problems) Build(Ledger ledger, string baseRef, List<JsonObject>? claimsOverride = null, JsonObject? generatedOverride = null)
    {
        var block = ledger.AxesBlock();
        var generated = generatedOverride ?? Files.ReadJson(Path.Combine(ledger.Memory, "axes-generated.json")) as JsonObject
                        ?? throw new Refusal("no axes-generated.json; run `prober replay --all`, which derives the axes");
        var claims = claimsOverride ?? ledger.Claims();
        if (claims.Count == 0) throw new Refusal("no claims (tools/probe/corpus/claims.json, or the branch's staging)");

        var ignored = block["ignored"] as JsonObject ?? [];
        var hand = block["hand"] as JsonObject ?? [];
        var axes = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, values) in generated["axes"] as JsonObject ?? [])
            if (!ignored.ContainsKey(name)) axes[name] = [.. Files.Strings(values)];
        var problems = new List<string>();
        foreach (var name in Files.Strings(generated["needsValues"]))
        {
            if (ignored.ContainsKey(name)) continue;
            if (hand[name] is JsonArray h) axes[name] = ["default", .. Files.Strings(h)];
            else problems.Add($"needs values: {name} (no hand values and not ignored)");
        }
        foreach (var (name, values) in hand)
            if (!axes.ContainsKey(name) && !ignored.ContainsKey(name)) axes[name] = ["default", .. Files.Strings(values)];
        foreach (var (name, values) in block["axes"] as JsonObject ?? []) axes[name] = [.. Files.Strings(values)];
        foreach (var (name, refused) in block["refused"] as JsonObject ?? [])
            if (axes.TryGetValue(name, out var list) && refused is JsonObject r) axes[name] = [.. list.Where(v => !r.ContainsKey(v))];

        var surfaces = block["surfaces"] as JsonObject ?? throw new Refusal("the axes block has no surfaces");
        var reached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, surface) in surfaces)
            foreach (var pattern in Files.Strings(surface?["axes"]))
                reached.UnionWith(axes.Keys.Where(k => Matches(pattern, k)));
        var allGenerated = (generated["axes"] as JsonObject ?? []).Select(kv => kv.Key).Concat(Files.Strings(generated["needsValues"]));
        foreach (var name in allGenerated.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            if (!ignored.ContainsKey(name) && axes.ContainsKey(name) && !reached.Contains(name))
                problems.Add($"unassigned: {name} (no surface reaches it and it is not ignored)");

        var built = new List<Claim>();
        foreach (var c in claims)
        {
            var id = Files.Str(c["id"]); var source = Files.Str(c["source"]); var sourceFile = source.Split('#')[0];
            var mine = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            var watched = new List<string>();
            foreach (var surfaceName in Files.Strings(c["surfaces"]))
            {
                if (surfaces[surfaceName] is not JsonObject surface) throw new Refusal($"claim {id} names unknown surface {surfaceName}");
                foreach (var pattern in Files.Strings(surface["axes"]))
                    foreach (var name in axes.Keys.Where(k => Matches(pattern, k))) mine[name] = axes[name];
                watched.AddRange(Files.Strings(surface["watched"]));
            }
            var sourcePath = Path.Combine(ledger.Top, sourceFile);
            var sourceText = File.Exists(sourcePath) ? Collapse(File.ReadAllText(sourcePath)) : "";
            foreach (var (name, quote) in c["narrow"] as JsonObject ?? [])
            {
                if (!sourceText.Contains(Collapse(Files.Str(quote)), StringComparison.Ordinal))
                    throw new Refusal($"claim {id} narrows {name} by a quote not found in {sourceFile}");
                mine.Remove(name);
            }
            foreach (var (name, values) in c["axes"] as JsonObject ?? []) mine[name] = [.. Files.Strings(values)];
            built.Add(new Claim
            {
                Id = id, Source = source, Surfaces = [.. Files.Strings(c["surfaces"])],
                Watched = [.. watched.Append(sourceFile).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], Axes = mine,
                Excluded = [.. (c["exclude"] as JsonArray ?? []).OfType<JsonArray>().Select(p => new Pair(Files.Str(p[0]), Files.Str(p[1])))],
            });
        }
        var universe = new Universe
        {
            Base = Git.Out(ledger.Top, "merge-base", "HEAD", baseRef), Commit = ledger.Head,
            GeneratedVersions = generated["versions"] as JsonObject is { } v ? (JsonObject)v.DeepClone() : [], Claims = built,
        };
        Files.WriteJson(ledger.P("universe.json"), universe.ToJson());
        return (universe, problems);
    }
}
