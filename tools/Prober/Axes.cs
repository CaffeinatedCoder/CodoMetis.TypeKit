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

/// <summary>Pairs a claim excludes, and the one sentence that says why no row can hold them or the documents do not promise them.</summary>
internal sealed record Exclusion(string Reason, IReadOnlyList<Pair> Pairs);

/// <summary>One thing a claim promises, checkable on its own: a sentence quoted from the claim's source, under an id a seed names (decision 104).</summary>
internal sealed record Obligation(string Id, string Sentence, IReadOnlyDictionary<string, List<string>> Axes)
{
    /// <summary>Where the obligation does not apply, each declaration with its citation; the harness's <c>Row.Check</c> reads the same ones.</summary>
    public IReadOnlyList<Inapplicability> Inapplicable { get; init; } = [];
}

/// <summary>
/// Where an obligation does not apply: every axis of the declaration at one of the values it lists, and the citation
/// that says why (<c>not-promised:id</c>, <c>docs:file#anchor</c>, <c>fact:KB-NNN</c>, <c>record:NNNN</c>). A row
/// matches when it holds each axis at a listed value; a pair matches when every row holding it does, so when each
/// axis of the declaration is one of the pair's two at a listed value.
/// </summary>
internal sealed record Inapplicability(IReadOnlyDictionary<string, List<string>> Axes, string Cite)
{
    public bool Matches(Pair pair) => Axes.All(axis => Holds(pair.A, axis) || Holds(pair.B, axis));

    private static bool Holds(string setting, KeyValuePair<string, List<string>> axis)
    {
        var at = setting.IndexOf('=', StringComparison.Ordinal);
        return at > 0 && string.Equals(setting[..at], axis.Key, StringComparison.Ordinal) && axis.Value.Contains(setting[(at + 1)..]);
    }

    public JsonObject ToJson() => new()
    {
        ["axes"] = new JsonObject(Axes.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)new JsonArray([.. kv.Value.Select(v => (JsonNode)v)])))),
        ["cite"] = Cite,
    };
}

/// <summary>One thing a claim does not promise: the sentence of the document or record that makes the difference intended, and what it concerns.</summary>
internal sealed record NotPromised(string Id, string Source, string Sentence, IReadOnlyDictionary<string, List<string>> Axes);

/// <summary>One claim of the universe: its axes and values, its watched paths, and the pairs it excludes.</summary>
internal sealed class Claim
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<string> Surfaces { get; init; }
    public required IReadOnlyList<string> Watched { get; init; }
    public required SortedDictionary<string, List<string>> Axes { get; init; }
    public required HashSet<Pair> Excluded { get; init; }

    /// <summary>The claim's exclusions with their reasons, as claims.json holds them; universe.json keeps only <see cref="Excluded"/>.</summary>
    public IReadOnlyList<Exclusion> Exclusions { get; init; } = [];

    /// <summary>Each axis the claim drops, with the document sentence that limits it; universe.json does not keep these.</summary>
    public IReadOnlyDictionary<string, string> Narrowed { get; init; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

    /// <summary>What the claim promises, each checkable on its own, as claims.json lists them (empty: one implicit obligation, the claim); universe.json does not keep these.</summary>
    public IReadOnlyList<Obligation> Obligations { get; init; } = [];

    /// <summary>What the claim does not promise, each with the sentence that makes it intended; universe.json does not keep these.</summary>
    public IReadOnlyList<NotPromised> NotPromised { get; init; } = [];

    /// <summary>The id of a claim's one obligation where claims.json lists none: the claim itself.</summary>
    public const string ImplicitObligation = "claim";

    /// <summary>The obligation ids of a claim as claims.json holds it: the listed ones, or the implicit one.</summary>
    public static List<string> ObligationIds(JsonObject claim)
    {
        var ids = (claim["obligations"] as JsonArray ?? []).OfType<JsonObject>().Select(o => Files.Str(o["id"])).Where(id => id.Length > 0).ToList();
        return ids.Count > 0 ? ids : [ImplicitObligation];
    }

    /// <summary>The ids of the claim's obligations as built: the listed ones, or the implicit one.</summary>
    public IEnumerable<string> ObligationIdsBuilt => Obligations.Count > 0 ? Obligations.Select(o => o.Id) : [ImplicitObligation];

    /// <summary>
    /// The pairs <paramref name="obligation"/> wants: the claim's pairs less those a declaration of that obligation
    /// matches on either side. An obligation that declares nothing, the implicit one among them, wants them all.
    /// </summary>
    public HashSet<Pair> PairsWantedBy(string obligation)
    {
        var declared = Obligations.FirstOrDefault(o => o.Id == obligation)?.Inapplicable ?? [];
        var result = Pairs();
        if (declared.Count > 0) result.RemoveWhere(p => declared.Any(d => d.Matches(p)));
        return result;
    }

    /// <summary>The pairs at least one obligation wants: the union of <see cref="PairsWantedBy"/>, each pair once.</summary>
    public HashSet<Pair> PairsWanted()
    {
        if (!Obligations.Any(o => o.Inapplicable.Count > 0)) return Pairs();
        var result = new HashSet<Pair>();
        foreach (var id in ObligationIdsBuilt) result.UnionWith(PairsWantedBy(id));
        return result;
    }

    /// <summary>
    /// The pairs no obligation wants, each with the citations of the declarations that match it: declared
    /// inapplicable, so no row is generated for them and the gate leaves them out as it leaves out an exclusion.
    /// </summary>
    public Dictionary<Pair, SortedSet<string>> DeclaredInapplicable()
    {
        var result = new Dictionary<Pair, SortedSet<string>>();
        var wanted = PairsWanted();
        foreach (var pair in Pairs())
            if (!wanted.Contains(pair))
                result[pair] = new SortedSet<string>(Obligations.SelectMany(o => o.Inapplicable).Where(d => d.Matches(pair)).Select(d => d.Cite), StringComparer.Ordinal);
        return result;
    }

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
        // Every obligation's id with its declarations, written only where one declares something, so the covering
        // generator and the gate, which read universe.json, know which pairs each wants.
        if (Obligations.Any(ob => ob.Inapplicable.Count > 0))
            o["obligations"] = new JsonArray([.. Obligations.Select(ob =>
            {
                var item = new JsonObject { ["id"] = ob.Id };
                if (ob.Inapplicable.Count > 0) item["inapplicable"] = new JsonArray([.. ob.Inapplicable.Select(d => (JsonNode)d.ToJson())]);
                return (JsonNode)item;
            })]);
        return o;
    }

    public static Claim FromJson(JsonObject o) => new()
    {
        Id = Files.Str(o["id"]), Source = Files.Str(o["source"]), Surfaces = [.. Files.Strings(o["surfaces"])],
        Watched = [.. Files.Strings(o["watched"])],
        Axes = new((o["axes"] as JsonObject ?? []).ToDictionary(kv => kv.Key, kv => Files.Strings(kv.Value).ToList(), StringComparer.Ordinal), StringComparer.Ordinal),
        Excluded = [.. (o["exclude"] as JsonArray ?? []).OfType<JsonArray>().Where(p => p.Count == 2).Select(p => new Pair(Files.Str(p[0]), Files.Str(p[1])))],
        Obligations = [.. (o["obligations"] as JsonArray ?? []).OfType<JsonObject>().Select(ob => new Obligation(Files.Str(ob["id"]), "", new SortedDictionary<string, List<string>>(StringComparer.Ordinal))
        {
            Inapplicable = [.. (ob["inapplicable"] as JsonArray ?? []).OfType<JsonObject>().Select(Prober.Axes.Declaration)],
        })],
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

    /// <summary>An axis-to-values object of claims.json (an obligation's or a not-promised entry's `axes`), or none.</summary>
    private static SortedDictionary<string, List<string>> AxesOf(JsonNode? node)
    {
        var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, values) in node as JsonObject ?? []) result[name] = [.. Files.Strings(values)];
        return result;
    }

    /// <summary>A declaration of where an obligation does not apply, as claims.json and universe.json hold it.</summary>
    internal static Inapplicability Declaration(JsonObject d) => new(AxesOf(d["axes"]), Files.Str(d["cite"]));

    private static readonly Regex CiteId = new("^[a-z0-9][a-z0-9-]*$");
    private static readonly Regex CiteSource = new(@"^(?!/)(?!.*(^|/)\.\.?(/|#|$))[A-Za-z0-9_./-]+\.[A-Za-z0-9]+(#[A-Za-z0-9][A-Za-z0-9._-]*)?$");
    private static readonly Regex CiteFact = new(@"^KB-\d{3}$");
    private static readonly Regex CiteRecord = new(@"^\d{4}$");

    /// <summary>
    /// What is wrong with a citation's form, or null: the forms the harness's <c>Cite</c> (harness/Row.cs) accepts, so
    /// every declaration the universe reads, the harness can cite (<c>CiteFormTests</c> holds the two alike).
    /// </summary>
    internal static string? CiteProblem(string cite)
    {
        var at = cite.IndexOf(':', StringComparison.Ordinal);
        var (kind, value) = at < 0 ? ("", cite) : (cite[..at], cite[(at + 1)..]);
        return kind switch
        {
            "not-promised" => CiteId.IsMatch(value) ? null : "is not a not-promised id (not-promised:closed-set-has-no-text)",
            "docs" => CiteSource.IsMatch(value) ? null : "is not a docs source (docs:docs/json.md#decimals-in-one-spelling)",
            "fact" => CiteFact.IsMatch(value) ? null : "is not a knowledge entry's id (fact:KB-031)",
            "record" => CiteRecord.IsMatch(value) ? null : "is not a decision record's number (record:0094)",
            _ => "is not a citation (not-promised:id, docs:file#anchor, fact:KB-NNN or record:NNNN)",
        };
    }

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
            var narrowed = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, quote) in c["narrow"] as JsonObject ?? [])
            {
                if (!sourceText.Contains(Collapse(Files.Str(quote)), StringComparison.Ordinal))
                    throw new Refusal($"claim {id} narrows {name} by a quote not found in {sourceFile}");
                mine.Remove(name);
                narrowed[name] = Files.Str(quote);
            }
            foreach (var (name, values) in c["axes"] as JsonObject ?? []) mine[name] = [.. Files.Strings(values)];
            // What the claim promises and what it does not, each a sentence quoted from its document, checked as a
            // narrowing is: a sentence the document no longer holds is a refusal, never a stale promise (decision 104).
            var obligations = new List<Obligation>();
            var notPromisedIds = (c["not-promised"] as JsonArray ?? []).OfType<JsonObject>().Select(n => Files.Str(n["id"])).ToHashSet(StringComparer.Ordinal);
            foreach (var o in (c["obligations"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var oid = Files.Str(o["id"]);
                if (obligations.Any(x => x.Id == oid)) throw new Refusal($"claim {id}: obligation {oid} is listed twice");
                if (!sourceText.Contains(Collapse(Files.Str(o["sentence"])), StringComparison.Ordinal))
                    throw new Refusal($"claim {id}: obligation {oid} quotes a sentence not found in {sourceFile}");
                // Where the obligation does not apply, said by the claim's own axes and values and cited: the covering
                // generator, the gate and the harness read the same declarations, so they never disagree.
                var declared = new List<Inapplicability>();
                foreach (var (d, i) in (o["inapplicable"] as JsonArray ?? []).OfType<JsonObject>().Select((d, i) => (d, i)))
                {
                    var at = $"claim {id}: obligation {oid}: inapplicable[{i}]";
                    var declaration = Declaration(d);
                    if (declaration.Axes.Count == 0) throw new Refusal($"{at} names no axis");
                    foreach (var (name, values) in declaration.Axes)
                    {
                        if (!mine.TryGetValue(name, out var listed)) throw new Refusal($"{at} names axis {name}, which the claim does not have");
                        if (values.Count == 0) throw new Refusal($"{at} names no value of {name}");
                        foreach (var value in values.Where(v => !listed.Contains(v)))
                            throw new Refusal($"{at} names {name}={value}, a value the claim does not list for {name}");
                    }
                    if (CiteProblem(declaration.Cite) is { } problem) throw new Refusal($"{at}: cite {declaration.Cite} {problem}");
                    if (declaration.Cite.StartsWith("not-promised:", StringComparison.Ordinal) && !notPromisedIds.Contains(declaration.Cite["not-promised:".Length..]))
                        throw new Refusal($"{at} cites {declaration.Cite}, which the claim's not-promised list does not hold");
                    declared.Add(declaration);
                }
                obligations.Add(new Obligation(oid, Files.Str(o["sentence"]), AxesOf(o["axes"])) { Inapplicable = declared });
            }
            var notPromised = new List<NotPromised>();
            foreach (var n in (c["not-promised"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var nid = Files.Str(n["id"]); var nsource = Files.Str(n["source"]); var nfile = nsource.Split('#')[0];
                if (notPromised.Any(x => x.Id == nid)) throw new Refusal($"claim {id}: not-promised {nid} is listed twice");
                var npath = Path.Combine(ledger.Top, nfile);
                var ntext = File.Exists(npath) ? Collapse(File.ReadAllText(npath)) : "";
                if (!ntext.Contains(Collapse(Files.Str(n["sentence"])), StringComparison.Ordinal))
                    throw new Refusal($"claim {id}: not-promised {nid} quotes a sentence not found in {nfile}");
                notPromised.Add(new NotPromised(nid, nsource, Files.Str(n["sentence"]), AxesOf(n["axes"])));
            }
            // The claims file's schema holds the form (Ledger.ReadClaims): each entry a reason and its pairs.
            List<Exclusion> exclusions = [.. (c["exclude"] as JsonArray ?? []).OfType<JsonObject>().Select(e => new Exclusion(Files.Str(e["reason"]),
                [.. (e["pairs"] as JsonArray ?? []).OfType<JsonArray>().Select(p => new Pair(Files.Str(p[0]), Files.Str(p[1])))]))];
            built.Add(new Claim
            {
                Id = id, Source = source, Surfaces = [.. Files.Strings(c["surfaces"])],
                Watched = [.. watched.Append(sourceFile).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], Axes = mine,
                Excluded = [.. exclusions.SelectMany(e => e.Pairs)], Exclusions = exclusions, Narrowed = narrowed,
                Obligations = obligations, NotPromised = notPromised,
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
