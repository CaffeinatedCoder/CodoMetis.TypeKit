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
    public bool Linked { get; }          // a linked worktree (git worktree add), not the main checkout
    public string Branch { get; }
    public string Memory { get; }        // PROBE_STATE_DIR, else the main checkout's prober memory
    public string Dir { get; }           // the branch's directory under Memory
    public string CorpusDir => Path.Combine(Top, "tools", "probe", "corpus");
    public string AxesFile => Path.Combine(Top, ".claude", "probe-axes.md");
    public string StagingDir => Path.Combine(Dir, "staging");

    public Ledger(Context ctx, string? branch)
    {
        Ctx = ctx;
        var dirs = Git.Run(ctx.WorkingDirectory, "rev-parse", "--path-format=absolute", "--git-common-dir", "--git-dir");
        if (dirs.ExitCode != 0) throw new Refusal("not inside a git repository");
        var lines = dirs.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => d.TrimEnd('/', '\\')).ToArray();
        if (lines is not [var common, var own]) throw new Refusal("git rev-parse named no git directory");
        Root = Path.GetDirectoryName(common)!;
        Linked = own != common;
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
            foreach (var c in ReadClaims(path, Path.GetRelativePath(Top, path).Replace('\\', '/')))
                merged[Files.Str(c["id"])] = (JsonObject)c.DeepClone();
        return [.. merged.Values];
    }

    /// <summary>
    /// A claims file, refused unless it matches Schemas/claims.schema.json, each problem named by its claim and
    /// field (decision 102). No file is no claims.
    /// </summary>
    public static List<JsonObject> ReadClaims(string path, string shown)
    {
        if (!File.Exists(path)) return [];
        if (Files.ReadJson(path) is not JsonArray claims) throw new Refusal($"{shown} is not a JSON list of claims");
        var schema = SchemaCheck.Load("claims.schema.json")["items"] as JsonObject ?? throw new InvalidOperationException("claims.schema.json has no items");
        var problems = new List<string>();
        for (var i = 0; i < claims.Count; i++)
        {
            var id = claims[i] is JsonObject o && Files.Str(o["id"]) is { Length: > 0 } s ? s : $"#{i}";
            problems.AddRange(SchemaCheck.Problems(claims[i], schema).Select(p => $"claim {id}: {p}"));
        }
        if (problems.Count > 0)
            throw new Refusal($"{shown} does not match tools/Prober/Schemas/claims.schema.json:\n  " + string.Join("\n  ", problems));
        return [.. claims.OfType<JsonObject>()];
    }

    /// <summary>
    /// A probe's own rows (rows/P.json, staged, else the corpus's rows/P.json.gz). Each probe has a file of its own, its claim
    /// inside, so the rows of one probe of a claim never move another's identity.
    /// </summary>
    public JsonArray RowsOf(string probe)
    {
        var relative = Path.Combine("rows", probe + ".json");
        var staged = Path.Combine(StagingDir, relative);
        return File.Exists(staged) ? Files.ReadRows(staged) : Files.ReadRows(Path.Combine(CorpusDir, relative));
    }

    /// <summary>
    /// The paths a probe's registry entry watches: what its result depends on that the footprint cannot see, such as a
    /// variant catalogue under test/ compiled into the probes or a build input a child process reads. They stale its
    /// footprint like the paths it ran.
    /// </summary>
    public IReadOnlyList<string> Watched(string probe) => [.. Files.Strings(Registry().GetValueOrDefault(probe)?["watched"])];

    public JsonObject AxesBlock() => Axes.Block(AxesFile);

    // ---- identity --------------------------------------------------------------------------------------

    /// <summary>
    /// What a probe's result depends on besides the product footprint: its registry entry (not its seed), every source
    /// compiled with it (the harness and all probes), its own rows (not a sibling's), the axes file, the build environment
    /// (SDK, central package versions, build props, global.json), this tool's version and the schema version, and
    /// where its claim declares where an obligation does not apply, those declarations, which the harness reads.
    /// A result counts only while this is unchanged.
    /// </summary>
    public string Identity(string probe)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string s) => sha.AppendData(Encoding.UTF8.GetBytes(s + "\0"));
        var entry = Registry().GetValueOrDefault(probe) ?? [];
        // The seeds prove the probe; a result of the probe depends on none of them (decisions 100 and 104).
        var registered = (JsonObject)entry.DeepClone();
        registered.Remove("seed");
        registered.Remove("seeds");
        Add(Files.Canonical(registered));
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var baseDir in new[] { CorpusDir, StagingDir })
            foreach (var sub in new[] { "harness", "probes" })
            {
                var folder = Path.Combine(baseDir, sub);
                if (Directory.Exists(folder))
                    foreach (var file in Directory.EnumerateFiles(folder, "*.cs")) sources[Path.GetFileName(file)] = file;
            }
        foreach (var (name, file) in sources) { Add(name); sha.AppendData(File.ReadAllBytes(file)); }
        Add(Files.Canonical(RowsOf(probe)));
        // The declarations Row.Check applies before a check's body runs: a change to one changes what the probe's rows
        // check. Added only where the claim declares something, so a claim without declarations leaves the identity as it was.
        if (Declarations(Files.Str(entry["claim"])) is { } declared) Add("inapplicable:" + Files.Canonical(declared));
        try { Add(Files.Canonical(AxesBlock())); } catch (Refusal) { Add("no axes file"); }
        foreach (var path in new[] { "Directory.Packages.props", "Directory.Build.props", "global.json" })
            Add(path + (Git.ObjectId(Top, path) ?? "-"));
        Add(Files.Canonical(Toolchain()));
        Add("tool:" + SourceFingerprint.ToolVersion[..16]);
        Add("schema:" + Files.SchemaVersion);
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }

    private Dictionary<string, JsonArray>? declarations;

    /// <summary>A claim's declarations of where its obligations do not apply, as claims.json holds them, by obligation; null where it declares none.</summary>
    private JsonArray? Declarations(string claim)
    {
        declarations ??= Claims().ToDictionary(c => Files.Str(c["id"]), c => new JsonArray([.. (c["obligations"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(o => o["inapplicable"] is JsonArray { Count: > 0 })
            .Select(o => (JsonNode)new JsonObject { ["obligation"] = Files.Str(o["id"]), ["inapplicable"] = o["inapplicable"]!.DeepClone() })]), StringComparer.Ordinal);
        return declarations.TryGetValue(claim, out var declared) && declared.Count > 0 ? declared : null;
    }

    private JsonObject? toolchain;

    /// <summary>
    /// What no file in the repository records: the SDK a global.json roll-forward picked, say. A command that cannot
    /// say it (it does not start, exits non-zero or prints nothing) is a refusal: its error text in every identity
    /// would read as a toolchain change, and every result as stale, without a word why.
    /// </summary>
    public JsonObject Toolchain()
    {
        if (toolchain is not null) return toolchain;
        JsonObject? commands;
        try { commands = AxesBlock()["replay"]?["toolchain"] as JsonObject; } catch (Refusal) { commands = null; }
        var result = new JsonObject();
        foreach (var (name, argv) in (commands ?? []).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var parts = Files.Strings(argv).ToArray();
            var shown = $"the toolchain command {name} (`{string.Join(' ', parts)}`)";
            if (parts.Length == 0) throw new Refusal($"{shown} is empty");
            Ran ran;
            try { ran = Proc.Run(parts[0], parts[1..], Top); }
            catch (System.ComponentModel.Win32Exception e) { throw new Refusal($"{shown} did not start: {e.Message}"); }
            static string FirstLine(string s) => s.Trim().Split('\n')[0].Trim();
            // A failing `dotnet --version` lists the installed SDKs on stdout ("10.0.401 [path]", a version to look at)
            // and says why on stderr.
            if (ran.ExitCode != 0)
            {
                var why = FirstLine(ran.Stderr) is { Length: > 0 } err ? err : FirstLine(ran.Stdout);
                throw new Refusal($"{shown} exited {ran.ExitCode}: {(why.Length > 0 ? why : "no output")}. A result is bound to what it prints, " +
                                  "so none could be current; put the toolchain global.json asks for first on PATH, or install it where " +
                                  $"the SDK installer puts it ({Dotnet.InstallerDirectory}), which `dotnet` falls back to");
            }
            var first = FirstLine(ran.Stdout.Trim().Length > 0 ? ran.Stdout : ran.Stderr);
            if (first.Length == 0) throw new Refusal($"{shown} printed nothing, so a result could be bound to no toolchain");
            result[name] = first;
        }
        return toolchain = result;
    }

    // ---- footprints, evidence, coverage ------------------------------------------------------------------

    public Dictionary<string, JsonObject> Footprints() =>
        Files.ReadLines(P("footprints.jsonl")).GroupBy(l => Files.Str(l["probe"]), StringComparer.Ordinal)
             .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    private readonly Dictionary<string, bool> currentCache = new(StringComparer.Ordinal);

    /// <summary>
    /// A probe's footprint is current when every path it ran, every compile-time path, every path its registry entry
    /// watches and the toolchain are unchanged at HEAD. A watched path the footprint did not record (one added to the
    /// entry since) is not current.
    /// </summary>
    public bool FootprintCurrent(string probe, Dictionary<string, JsonObject> footprints)
    {
        if (currentCache.TryGetValue(probe, out var cached)) return cached;
        var ok = false;
        if (footprints.TryGetValue(probe, out var r) && r["footprint"] is JsonObject fp && fp.Count > 0)
        {
            var watched = r["watched"] as JsonObject ?? [];
            ok = Watched(probe).All(watched.ContainsKey)
                 && fp.Concat(r["compileTime"] as JsonObject ?? []).Concat(watched).All(kv => Git.ObjectId(Top, kv.Key) == Files.Str(kv.Value, null!));
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
        var paths = (r["footprint"] as JsonObject ?? []).Select(kv => kv.Key).Concat((r["compileTime"] as JsonObject ?? []).Select(kv => kv.Key))
                                                         .Concat((r["watched"] as JsonObject ?? []).Select(kv => kv.Key)).Concat(Watched(probe))
                                                         .Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0) return true;
        return Git.Run(Top, ["diff", "--quiet", since, "HEAD", "--", .. paths]).ExitCode != 0;
    }

    public string EvidencePath(string probe) => P(Path.Combine("evidence", probe + ".json"));

    /// <summary>A check that earned credit: the obligation it named and the settings it ran under.</summary>
    public sealed record Credit(string Obligation, Dictionary<string, string> Applied);

    /// <summary>
    /// The checks of one probe's evidence that earn credit, each with its obligation and settings: only while the
    /// probe's identity and product footprint are current, and only checks that ended as a pass in rows that were
    /// expected and executed. A check's settings are the row's (read outside any check, so every check ran under them)
    /// with its own laid over them; a row without checks is one check of the obligation <c>claim</c>, credited when
    /// the row passed.
    /// </summary>
    public List<Credit> CreditedChecks(string probe, JsonObject? evidence, Dictionary<string, JsonObject>? footprints)
    {
        var result = new List<Credit>();
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
            if (row is not JsonObject r || !expected.Contains(i) || !executed.Contains(i)) continue;
            foreach (var check in ChecksOf(r))
                if (check.Category == "pass" && check.Applied.Count > 0) result.Add(new Credit(check.Obligation, check.Applied));
        }
        return result;
    }

    /// <summary>One check of an evidence row as the ledger reads it.</summary>
    public sealed record CheckRead(string Obligation, string Category, Dictionary<string, string> Applied, string? Baseline);

    /// <summary>
    /// The checks of one evidence row: each with its obligation, category and settings (the row's, the check's laid
    /// over them). A row without checks, every row of a version-1 file among them, is one check of the implicit
    /// obligation <c>claim</c> with the row's category and settings and no baseline.
    /// </summary>
    public static List<CheckRead> ChecksOf(JsonObject row)
    {
        var applied = row["applied"] is JsonObject a ? Files.StringMap(a) : new Dictionary<string, string>(StringComparer.Ordinal);
        if (row["checks"] is not JsonArray { Count: > 0 } checks)
            return [new CheckRead(Claim.ImplicitObligation, Files.Str(row["category"], "violation"), applied, null)];
        var result = new List<CheckRead>();
        foreach (var c in checks.OfType<JsonObject>())
        {
            var own = new Dictionary<string, string>(applied, StringComparer.Ordinal);
            if (c["applied"] is JsonObject ca) foreach (var (axis, value) in Files.StringMap(ca)) own[axis] = value;
            result.Add(new CheckRead(Files.Str(c["obligation"]), Files.Str(c["category"], "violation"), own, c["baseline"] is JsonValue b ? Files.Str(b) : null));
        }
        return result;
    }

    /// <summary>
    /// (claim, obligation, pair) for every pair a current check earned, leaving out the probe <paramref name="except"/>
    /// names. Each check's settings alone make its pairs: a pair is never put together from two checks' settings.
    /// </summary>
    public HashSet<(string Claim, string Obligation, Pair Pair)> Credits(Universe universe, string? claimId = null, string? except = null)
    {
        var footprints = Footprints();
        var done = new HashSet<(string, string, Pair)>();
        foreach (var (name, entry) in Registry())
        {
            var cid = Files.Str(entry["claim"]);
            if (name == except || (claimId is not null && cid != claimId) || !universe.ById.TryGetValue(cid, out var claim)) continue;
            var evidence = Files.ReadJson(EvidencePath(name)) as JsonObject;
            foreach (var credit in CreditedChecks(name, evidence, footprints))
                foreach (var pair in claim.PairsOfRow(credit.Applied)) done.Add((cid, credit.Obligation, pair));
        }
        return done;
    }

    /// <summary>
    /// (claim, pair) for every pair current evidence earned, leaving out the probe <paramref name="except"/> names. A round's
    /// own ledger lines earn nothing.
    /// </summary>
    public HashSet<(string Claim, Pair Pair)> Covered(Universe universe, string? claimId = null, string? except = null) =>
        [.. Credits(universe, claimId, except).Select(x => (x.Claim, x.Pair))];

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

    /// <summary>How the checks of one probe's evidence ended, by category; a row without checks counts as its one check <c>claim</c>.</summary>
    public static SortedDictionary<string, int> CheckOutcomes(JsonObject? evidence)
    {
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, row) in evidence?["rows"] as JsonObject ?? [])
            if (row is JsonObject r)
                foreach (var check in ChecksOf(r)) outcomes[check.Category] = outcomes.GetValueOrDefault(check.Category) + 1;
        return outcomes;
    }

    /// <summary>The obligations a check of one probe's evidence passed for at its last replay, current or not.</summary>
    public static HashSet<string> PassedObligations(JsonObject? evidence)
    {
        var passed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, row) in evidence?["rows"] as JsonObject ?? [])
            if (row is JsonObject r)
                foreach (var check in ChecksOf(r).Where(c => c.Category == "pass")) passed.Add(check.Obligation);
        return passed;
    }

    public List<string> OpenFindings() => [.. Findings().Where(kv => Files.Str(kv.Value["status"]) == "open").Select(kv => kv.Key).Order(StringComparer.Ordinal)];

    public sealed record ProbeState(string Probe, string Claim, string Filter, JsonArray Variants, bool Stale, bool Retired, int Passes, bool Replay);

    /// <summary>A registry entry's process variants: its own, or one that sets nothing.</summary>
    public static JsonArray VariantsOf(JsonObject entry) => entry["variants"] is JsonArray va && va.Count > 0 ? (JsonArray)va.DeepClone() : [new JsonObject()];

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
            var variants = VariantsOf(r);
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
