using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// One branch's ledger. Tracked, in the repository: the corpus under tools/probe/corpus (claims, registry,
/// harness and probe sources, rows, waivers, dismissed findings) and the axes file. Local, under the state
/// directory: the universe, evidence, footprints, runs, findings, the round's caps and the lock. A pair is
/// covered only by evidence a replay recorded under an identity that still holds.
/// </summary>
internal sealed class Ledger
{
    public Context Ctx { get; }
    public string Root { get; }          // the main checkout, also from a linked worktree
    public string Top { get; }           // this checkout
    public string Branch { get; }
    public string Memory { get; }        // PROBE_STATE_DIR, else the main checkout's prober memory
    public string Dir { get; }           // the branch's directory under Memory
    public string CorpusDir => Path.Combine(Top, "tools", "probe", "corpus");
    public string AxesFile => Path.Combine(Top, ".claude", "probe-axes.md");
    public string StagingDir => Path.Combine(Dir, "staging");

    public Ledger(Context ctx, string? branch)
    {
        Ctx = ctx;
        var common = Git.Run(ctx.WorkingDirectory, "rev-parse", "--path-format=absolute", "--git-common-dir");
        if (common.ExitCode != 0) throw new Refusal("not inside a git repository");
        Root = Path.GetDirectoryName(common.Stdout.Trim().TrimEnd('/', '\\'))!;
        Top = Git.Out(ctx.WorkingDirectory, "rev-parse", "--show-toplevel");
        Branch = branch ?? Git.Out(ctx.WorkingDirectory, "rev-parse", "--abbrev-ref", "HEAD");
        Memory = ctx.Env("PROBE_STATE_DIR") ?? Path.Combine(Root, ".claude", "agent-memory-local", "prober");
        Dir = Path.Combine(Memory, "branches", Branch.Replace('/', '-'));
    }

    public string Head => Git.Out(Top, "rev-parse", "HEAD");
    public string P(string name) => Path.Combine(Dir, name);

    // ---- the corpus, tracked, overlaid by the branch's unpublished staging ------------------------------

    public string CorpusPath(string relative)
    {
        var staged = Path.Combine(StagingDir, relative);
        return File.Exists(staged) ? staged : Path.Combine(CorpusDir, relative);
    }

    public SortedDictionary<string, JsonObject> Registry()
    {
        var merged = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var path in new[] { Path.Combine(CorpusDir, "registry.json"), Path.Combine(StagingDir, "registry.json") })
            if (Files.ReadJson(path)?["probes"] is JsonObject probes)
                foreach (var (name, entry) in probes)
                    if (entry is JsonObject e) merged[name] = (JsonObject)e.DeepClone();
        return merged;
    }

    public List<JsonObject> Claims()
    {
        var merged = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var path in new[] { Path.Combine(CorpusDir, "claims.json"), Path.Combine(StagingDir, "claims.json") })
            if (Files.ReadJson(path) is JsonArray claims)
                foreach (var c in claims.OfType<JsonObject>())
                    merged[Files.Str(c["id"])] = (JsonObject)c.DeepClone();
        return [.. merged.Values];
    }

    public JsonArray RowsOf(string claim)
    {
        var relative = Path.Combine("rows", claim + ".json");
        var staged = Path.Combine(StagingDir, relative);
        return File.Exists(staged) ? Files.ReadRows(staged) : Files.ReadRows(Path.Combine(CorpusDir, relative));
    }

    public JsonObject AxesBlock() => Axes.Block(AxesFile);

    // ---- identity --------------------------------------------------------------------------------------

    /// <summary>
    /// What a probe's result depends on besides the product footprint: its registry entry, every source
    /// compiled with it (the harness and all probes), its claim's rows, the axes file, the build environment
    /// (SDK, central package versions, build props, global.json), this tool's version and the schema version.
    /// A result counts only while this is unchanged.
    /// </summary>
    public string Identity(string probe)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string s) => sha.AppendData(Encoding.UTF8.GetBytes(s + "\0"));
        var entry = Registry().GetValueOrDefault(probe) ?? [];
        Add(Files.Canonical(entry));
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var baseDir in new[] { CorpusDir, StagingDir })
            foreach (var sub in new[] { "harness", "probes" })
            {
                var folder = Path.Combine(baseDir, sub);
                if (Directory.Exists(folder))
                    foreach (var file in Directory.EnumerateFiles(folder, "*.cs")) sources[Path.GetFileName(file)] = file;
            }
        foreach (var (name, file) in sources) { Add(name); sha.AppendData(File.ReadAllBytes(file)); }
        Add(Files.Canonical(RowsOf(Files.Str(entry["claim"]))));
        try { Add(Files.Canonical(AxesBlock())); } catch (Refusal) { Add("no axes file"); }
        foreach (var path in new[] { "Directory.Packages.props", "Directory.Build.props", "global.json" })
            Add(path + (Git.ObjectId(Top, path) ?? "-"));
        Add(Files.Canonical(Toolchain()));
        Add("tool:" + SourceFingerprint.ToolVersion[..16]);
        Add("schema:" + Files.SchemaVersion);
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }

    private JsonObject? toolchain;

    /// <summary>What no file in the repository records: the SDK a global.json roll-forward picked, say.</summary>
    public JsonObject Toolchain()
    {
        if (toolchain is not null) return toolchain;
        var result = new JsonObject();
        try
        {
            if (AxesBlock()["replay"]?["toolchain"] is JsonObject commands)
                foreach (var (name, argv) in commands.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    var parts = Files.Strings(argv).ToArray();
                    var ran = Proc.Run(parts[0], parts[1..], Top);
                    var text = (ran.Stdout.Trim().Length > 0 ? ran.Stdout : ran.Stderr).Trim();
                    result[name] = text.Split('\n')[0].Trim();
                }
        }
        catch (Exception e) when (e is Refusal or System.ComponentModel.Win32Exception) { }
        return toolchain = result;
    }

    // ---- footprints, evidence, coverage ------------------------------------------------------------------

    public Dictionary<string, JsonObject> Footprints() =>
        Files.ReadLines(P("footprints.jsonl")).GroupBy(l => Files.Str(l["probe"]), StringComparer.Ordinal)
             .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    private readonly Dictionary<string, bool> currentCache = new(StringComparer.Ordinal);

    /// <summary>A probe's footprint is current when every path it ran, every compile-time path and the toolchain are unchanged at HEAD.</summary>
    public bool FootprintCurrent(string probe, Dictionary<string, JsonObject> footprints)
    {
        if (currentCache.TryGetValue(probe, out var cached)) return cached;
        var ok = false;
        if (footprints.TryGetValue(probe, out var r) && r["footprint"] is JsonObject fp && fp.Count > 0)
        {
            ok = fp.Concat(r["compileTime"] as JsonObject ?? []).All(kv => Git.ObjectId(Top, kv.Key) == Files.Str(kv.Value, null!));
            if (ok)
            {
                var now = Toolchain();
                ok = (r["toolchain"] as JsonObject ?? []).All(kv => Files.Str(now[kv.Key]) == Files.Str(kv.Value));
            }
        }
        return currentCache[probe] = ok;
    }

    public bool FootprintChangedSince(string probe, string since, Dictionary<string, JsonObject> footprints)
    {
        if (!footprints.TryGetValue(probe, out var r)) return true;
        var paths = (r["footprint"] as JsonObject ?? []).Select(kv => kv.Key).Concat((r["compileTime"] as JsonObject ?? []).Select(kv => kv.Key)).ToArray();
        if (paths.Length == 0) return true;
        return Git.Run(Top, ["diff", "--quiet", since, "HEAD", "--", .. paths]).ExitCode != 0;
    }

    public string EvidencePath(string probe) => P(Path.Combine("evidence", probe + ".json"));

    /// <summary>
    /// The rows of one probe's evidence that earn credit, as applied settings: only while its identity and
    /// product footprint are current, and only rows that were expected, executed and ended as a pass.
    /// </summary>
    public List<Dictionary<string, string>> CreditedRows(string probe, JsonObject? evidence, Dictionary<string, JsonObject>? footprints)
    {
        var result = new List<Dictionary<string, string>>();
        if (evidence is null || Files.Str(evidence["identity"]) != Identity(probe)) return result;
        if (footprints is not null && !FootprintCurrent(probe, footprints)) return result;
        var expected = new HashSet<int>(); var executed = new HashSet<int>();
        foreach (var v in (evidence["variants"] as JsonArray ?? []).OfType<JsonObject>())
        {
            foreach (var i in v["expected"] as JsonArray ?? []) expected.Add((int)Files.Num(i));
            foreach (var i in v["executed"] as JsonArray ?? []) executed.Add((int)Files.Num(i));
        }
        foreach (var (key, row) in evidence["rows"] as JsonObject ?? [])
        {
            var i = int.Parse(key, System.Globalization.CultureInfo.InvariantCulture);
            if (row is JsonObject r && expected.Contains(i) && executed.Contains(i) && Files.Str(r["category"]) == "pass"
                && r["applied"] is JsonObject applied && applied.Count > 0)
                result.Add(Files.StringMap(applied));
        }
        return result;
    }

    /// <summary>(claim, pair) for every pair current evidence earned. A round's own ledger lines earn nothing.</summary>
    public HashSet<(string Claim, Pair Pair)> Covered(Universe universe, string? claimId = null)
    {
        var footprints = Footprints();
        var done = new HashSet<(string, Pair)>();
        foreach (var (name, entry) in Registry())
        {
            var cid = Files.Str(entry["claim"]);
            if ((claimId is not null && cid != claimId) || !universe.ById.TryGetValue(cid, out var claim)) continue;
            var evidence = Files.ReadJson(EvidencePath(name)) as JsonObject;
            foreach (var applied in CreditedRows(name, evidence, footprints))
                foreach (var pair in claim.PairsOfRow(applied)) done.Add((cid, pair));
        }
        return done;
    }

    /// <summary>The last event per finding; a fixed line counts once its commit is in HEAD's history, a dismissal in the corpus everywhere.</summary>
    public Dictionary<string, JsonObject> Findings()
    {
        var last = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var f in Files.ReadLines(P("findings.jsonl")))
        {
            if (Files.Str(f["status"]) == "fixed" && Files.Str(f["commit"]) is { Length: > 0 } c
                && Git.Run(Top, "merge-base", "--is-ancestor", c, "HEAD").ExitCode != 0) continue;
            last[Files.Str(f["id"])] = f;
        }
        foreach (var f in Files.ReadLines(CorpusPath("dismissed.jsonl")))
        {
            var d = (JsonObject)f.DeepClone(); d["status"] = "dismissed";
            last[Files.Str(f["id"])] = d;
        }
        return last;
    }

    /// <summary>The findings one probe's evidence steps around, with the number of rows that skip for each.</summary>
    public static SortedDictionary<string, int> FindingSkips(JsonObject? evidence)
    {
        var skips = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, row) in evidence?["rows"] as JsonObject ?? [])
            foreach (var f in Files.Strings(row?["findings"])) skips[f] = skips.GetValueOrDefault(f) + 1;
        return skips;
    }

    /// <summary>
    /// The skips of one probe's evidence that no longer stand: a finding this ledger has fixed (in HEAD's history)
    /// or the corpus has dismissed. Such a skip would hide the defect coming back, so the replay fails the probe.
    /// A finding this ledger has never seen (a fresh state, as in CI) is left to the gate, which refuses any skip.
    /// </summary>
    public List<string> StaleSkips(JsonObject? evidence)
    {
        var findings = Findings();
        var stale = new List<string>();
        foreach (var (id, rows) in FindingSkips(evidence))
        {
            if (!findings.TryGetValue(id, out var f)) continue;
            var status = Files.Str(f["status"]);
            if (status == "fixed" && Files.Str(f["commit"]) is { Length: >= 7 } commit) status += " at " + commit[..7];
            if (status != "open") stale.Add($"{rows} row(s) skip for {id}, which is {status}: remove the skip");
        }
        return stale;
    }

    /// <summary>How the rows of one probe's evidence ended, by category.</summary>
    public static SortedDictionary<string, int> Outcomes(JsonObject? evidence)
    {
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, row) in evidence?["rows"] as JsonObject ?? [])
        {
            var category = Files.Str(row?["category"], "violation");
            outcomes[category] = outcomes.GetValueOrDefault(category) + 1;
        }
        return outcomes;
    }

    public List<string> OpenFindings() => [.. Findings().Where(kv => Files.Str(kv.Value["status"]) == "open").Select(kv => kv.Key).Order(StringComparer.Ordinal)];

    public sealed record ProbeState(string Probe, string Claim, string Filter, JsonArray Variants, bool Stale, bool Retired, int Passes, bool Replay);

    public List<ProbeState> ProbeStates(bool includeRetired)
    {
        var footprints = Footprints();
        var runs = Files.ReadLines(P("runs.jsonl"));
        var states = new List<ProbeState>();
        foreach (var (probe, r) in Registry())
        {
            var passes = runs.Count(x => Files.Str(x["probe"]) == probe && Files.Str(x["result"]) == "pass");
            var retired = r["retired"] is JsonValue rv && rv.TryGetValue<bool>(out var b) && b;
            var evidence = Files.ReadJson(EvidencePath(probe));
            var stale = !FootprintCurrent(probe, footprints) || evidence is null || Files.Str(evidence["identity"]) != Identity(probe);
            var variants = r["variants"] is JsonArray va && va.Count > 0 ? (JsonArray)va.DeepClone() : [new JsonObject()];
            states.Add(new ProbeState(probe, Files.Str(r["claim"]), Files.Str(r["filter"]), variants, stale, retired, passes, stale && (includeRetired || !retired)));
        }
        return states;
    }
}

/// <summary>A refusal the user acts on: printed as "prober: ...", exit 1. Never a stack trace.</summary>
internal sealed class Refusal(string message, int exitCode = 1) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
