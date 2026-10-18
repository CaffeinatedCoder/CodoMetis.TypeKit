using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Prober;

/// <summary>The command line: a command, then <c>--name value</c> options and <c>--flag</c> switches.</summary>
internal sealed class Args
{
    private readonly Dictionary<string, List<string>> options = new(StringComparer.Ordinal);
    public List<string> Positional { get; } = [];

    public Args(IEnumerable<string> argv, IReadOnlySet<string> flags)
    {
        var list = argv.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) { Positional.Add(a); continue; }
            var name = a[2..];
            if (flags.Contains(name)) { Add(name, "true"); continue; }
            // A multi-value option takes values until the next option.
            var any = false;
            while (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal)) { Add(name, list[++i]); any = true; if (!Multi.Contains(name)) break; }
            if (!any) Add(name, "");
        }
    }

    private static readonly HashSet<string> Multi = new(StringComparer.Ordinal) { "cobertura", "include", "compile-time", "from", "seed-env" };
    private void Add(string name, string value) { if (!options.TryGetValue(name, out var l)) options[name] = l = []; l.Add(value); }
    public string? Get(string name) => options.TryGetValue(name, out var l) ? l[^1] : null;
    public string Need(string name) => Get(name) is { Length: > 0 } v ? v : throw new Refusal($"--{name} is required", 2);
    public bool Flag(string name) => options.ContainsKey(name);
    public List<string> All(string name) => options.TryGetValue(name, out var l) ? l : [];
}

internal static class Commands
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static int Rows(Ledger l, Args a)
    {
        if (a.Get("probe") is not { Length: > 0 } probe)
        {
            l.Ctx.Out.WriteLine(CoveringRows(l, a.Need("claim"), a.Flag("full"), a.Get("max"), a.Get("seed")).ToJsonString(Pretty));
            return 0;
        }
        if (!l.Registry().TryGetValue(probe, out var entry)) throw new Refusal($"no probe {probe} in the registry, tracked or staged; `prober new` stages one", 2);
        var claimId = Files.Str(entry["claim"]);
        if (a.Get("claim") is { Length: > 0 } named && named != claimId) throw new Refusal($"{probe} is a probe of {claimId}, not of {named}", 2);
        var (rows, uncovered) = StageRows(l, probe, claimId, a.Flag("full"), a.Get("max"), a.Get("seed"));
        l.Ctx.Out.WriteLine($"rows/{probe}.json: {rows} rows for claim {claimId} over its pairs{(a.Flag("full") ? "" : " its other probes have not covered")}" +
                            $"{(uncovered > 0 ? $", {uncovered} left uncovered" : "")}; staged in {l.StagingDir}, the file written whole. " +
                            "`prober publish` moves it into the corpus");
        return 0;
    }

    /// <summary>
    /// Writes a probe's rows file in the staging, whole, never appended: a covering array over its claim's pairs (all of
    /// them with <paramref name="full"/>) less those the claim's other probes have credited. The probe's own credit is
    /// not subtracted, since it came from the rows this file replaces and ends with them. The stored rows are
    /// definitions; the evidence is in the state.
    /// </summary>
    internal static (int Rows, int Uncovered) StageRows(Ledger l, string probe, string claimId, bool full, string? max = null, string? seed = null)
    {
        var covering = CoveringRows(l, claimId, full, max, seed, except: probe);
        var rows = new JsonArray([.. (covering["rows"] as JsonArray ?? []).Select(r => (JsonNode)new JsonObject { ["row"] = r?["row"]?.DeepClone() })]);
        Files.WriteJson(Path.Combine(l.StagingDir, "rows", probe + ".json"), new JsonObject { ["claim"] = claimId, ["rows"] = rows });
        return (rows.Count, (int)Files.Num(covering["uncovered_after"]));
    }

    /// <summary>
    /// What `rows` prints: a pairwise covering array over the claim's pairs not yet covered (all of them with
    /// <paramref name="full"/>), each row with the pairs it covers. Inside a capped round the claim counts toward
    /// the round's cap, and past the deadline or the cap this refuses.
    /// </summary>
    public static JsonObject CoveringRows(Ledger l, string claimId, bool full, string? max = null, string? seed = null, string? except = null)
    {
        var universe = Universe.Load(l.P("universe.json")) ?? throw new Refusal("no universe.json for this branch yet; run `universe` first");
        if (!universe.ById.TryGetValue(claimId, out var claim)) throw new Refusal($"no claim {claimId} in the universe");
        if (!full && ActiveRound(l) is { } round)
        {
            if (RoundLock.Now() > Files.Num(round["deadline"]))
                throw new Refusal("the round's time is spent; start no new work. Record what ran, hand the corpus off, and report.");
            var started = round["claims"] as JsonArray ?? [];
            if (!Files.Strings(started).Contains(claimId))
            {
                if (round["max_claims"] is JsonValue m && m.TryGetValue<int>(out var cap) && started.Count >= cap)
                    throw new Refusal($"the round's cap of {cap} claim(s) is reached ({string.Join(", ", Files.Strings(started))}); finish those and report.");
                started.Add(claimId);
                round["claims"] = started.DeepClone();
                Files.WriteJson(l.P("round.json"), round);
            }
        }
        var covered = full ? [] : l.Covered(universe, claimId, except);
        // The pairs some obligation wants: a pair declared inapplicable for every obligation is asked for by no row.
        var todo = claim.PairsWanted().Where(p => !covered.Contains((claimId, p))).ToHashSet();
        var rows = Covering.Rows(claim, todo, int.Parse(max ?? "2000", CultureInfo.InvariantCulture), int.Parse(seed ?? "1", CultureInfo.InvariantCulture));
        return new JsonObject
        {
            ["claim"] = claimId,
            ["rows"] = new JsonArray([.. rows.Select(r => (JsonNode)new JsonObject
            {
                ["row"] = new JsonObject(r.Row.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["pairs"] = new JsonArray([.. r.Pairs.Select(p => (JsonNode)new JsonArray(p.A, p.B))]),
            })]),
            ["uncovered_after"] = todo.Count - rows.Sum(r => r.Pairs.Count),
        };
    }

    public static int UniverseCommand(Ledger l, Args a)
    {
        var claims = a.Get("claims") is { } cp ? Ledger.ReadClaims(cp, cp) : null;
        var generated = a.Get("generated") is { } gp ? Files.ReadJson(gp) as JsonObject : null;
        var (universe, problems) = Axes.Build(l, a.Get("base") ?? "origin/main", claims, generated);
        var total = 0;
        foreach (var c in universe.Claims)
        {
            var n = c.Pairs().Count; total += n;
            l.Ctx.Out.WriteLine($"{c.Id}: {c.Axes.Count} axes, {n} pairs");
            // What the claim leaves out and why: a narrowing's quote is checked against its document, an exclusion's reason by a reviewer only.
            foreach (var (axis, quote) in c.Narrowed) l.Ctx.Out.WriteLine($"  narrow {axis}: \"{quote}\"");
            foreach (var e in c.Exclusions)
                l.Ctx.Out.WriteLine($"  exclude {e.Pairs.Count} pair(s), first {e.Pairs[0]}: {e.Reason}");
            if (Inapplicable(c) is { } line) l.Ctx.Out.WriteLine($"  {line}");
        }
        l.Ctx.Out.WriteLine($"universe: {universe.Claims.Count} claims, {total} pairs");
        foreach (var p in problems) l.Ctx.Out.WriteLine($"  {p}");
        return 0;
    }

    /// <summary>
    /// The pairs of a claim no obligation wants, as one line: their count, the first, and the citations of the
    /// declarations that leave them out; null where there are none.
    /// </summary>
    private static string? Inapplicable(Claim c)
    {
        var dead = c.DeclaredInapplicable();
        if (dead.Count == 0) return null;
        var first = dead.Keys.OrderBy(p => p.A, StringComparer.Ordinal).ThenBy(p => p.B, StringComparer.Ordinal).First();
        var cites = new SortedSet<string>(dead.Values.SelectMany(x => x), StringComparer.Ordinal);
        return $"inapplicable {dead.Count} pair(s), wanted by no obligation, first {first}: {string.Join(", ", cites)}";
    }

    public static JsonObject ComputeStatus(Ledger l)
    {
        var universe = Universe.Load(l.P("universe.json"));
        if (universe is null) return new JsonObject { ["branch"] = l.Branch, ["done"] = false, ["reason"] = "no universe.json: run `universe` (no round has run here)" };
        var credits = l.Credits(universe);
        var done = credits.Select(x => (x.Claim, x.Pair)).ToHashSet();
        // How the rows and the checks of each claim's probes ended at their last replay: only a passing check earns
        // pairs, so the rest is what the claim's coverage loses, and a skip for a finding is an open finding the corpus
        // records. A row without checks counts as its one check.
        var outcomes = new Dictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        var checkOutcomes = new Dictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        var passed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var skips = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (probe, entry) in l.Registry())
        {
            var evidence = Files.ReadJson(l.EvidencePath(probe)) as JsonObject;
            var claim = Files.Str(entry["claim"]);
            var sum = outcomes.TryGetValue(claim, out var o) ? o : outcomes[claim] = new(StringComparer.Ordinal);
            foreach (var (category, n) in Ledger.Outcomes(evidence)) sum[category] = sum.GetValueOrDefault(category) + n;
            var checkSum = checkOutcomes.TryGetValue(claim, out var co) ? co : checkOutcomes[claim] = new(StringComparer.Ordinal);
            if (evidence is not null) foreach (var (category, n) in Ledger.CheckOutcomes(evidence)) checkSum[category] = checkSum.GetValueOrDefault(category) + n;
            (passed.TryGetValue(claim, out var p) ? p : passed[claim] = new(StringComparer.Ordinal)).UnionWith(Ledger.PassedObligations(evidence));
            foreach (var (finding, n) in Ledger.FindingSkips(evidence)) skips[finding] = skips.GetValueOrDefault(finding) + n;
        }
        // Each claim's obligations (decision 104), the probes registered for the claim, and the probes whose seeds
        // break each obligation: the claim's promise is checked only where a probe can be shown to fail on it.
        var listed = l.Claims().ToDictionary(c => Files.Str(c["id"]), Claim.ObligationIds, StringComparer.Ordinal);
        var registry = l.Registry();
        var claims = new JsonObject(); var total = 0; var current = 0; var inapplicableTotal = 0; var wantedTotal = 0; var wantedCurrent = 0;
        foreach (var c in universe.Claims)
        {
            var pairs = c.Pairs(); var hit = pairs.Count(p => done.Contains((c.Id, p)));
            // The pairs no obligation wants are declared inapplicable: left out of what is still to cover, as the gate leaves them out.
            var wanted = c.PairsWanted(); var inapplicable = pairs.Count - wanted.Count;
            inapplicableTotal += inapplicable; wantedTotal += wanted.Count; wantedCurrent += wanted.Count(p => done.Contains((c.Id, p)));
            var probes = registry.Where(kv => Files.Str(kv.Value["claim"]) == c.Id).ToList();
            var ids = listed.GetValueOrDefault(c.Id) ?? [Claim.ImplicitObligation];
            var obligations = new JsonArray();
            foreach (var id in ids)
            {
                var seededBy = probes.Where(kv => Seeds.SeedsOf(kv.Value).Any(s => (s.Obligation ?? (ids is [Claim.ImplicitObligation] ? Claim.ImplicitObligation : null)) == id))
                                     .Select(kv => (JsonNode)kv.Key).ToList();
                // current: the claim's pairs credited by current checks naming this obligation; passed: whether any check
                // named it and passed at the last replay, current or not (status's "K uncredited" counts those that did not).
                obligations.Add(new JsonObject
                {
                    ["id"] = id, ["seeded_by"] = new JsonArray([.. seededBy]),
                    ["current"] = pairs.Count(p => credits.Contains((c.Id, id, p))),
                    ["passed"] = passed.GetValueOrDefault(c.Id)?.Contains(id) ?? false,
                });
            }
            claims[c.Id] = new JsonObject
            {
                ["pairs"] = pairs.Count, ["current"] = hit, ["inapplicable_pairs"] = inapplicable,
                ["outcomes"] = new JsonObject((outcomes.GetValueOrDefault(c.Id) ?? []).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["check_outcomes"] = new JsonObject((checkOutcomes.GetValueOrDefault(c.Id) ?? []).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["probes"] = new JsonArray([.. probes.Select(kv => (JsonNode)kv.Key)]), ["obligations"] = obligations,
            };
            total += pairs.Count; current += hit;
        }
        var open = l.OpenFindings();
        return new JsonObject
        {
            ["branch"] = l.Branch, ["head"] = l.Head, ["universe_commit"] = universe.Commit, ["pairs"] = total, ["current"] = current,
            ["inapplicable_pairs"] = inapplicableTotal,
            ["share"] = total > 0 ? Math.Round((double)current / total, 4) : 1.0,
            ["open_findings"] = new JsonArray([.. open.Select(f => (JsonNode)f)]),
            ["skipped_for_findings"] = new JsonObject(skips.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))), ["claims"] = claims,
            ["done"] = total > 0 && wantedCurrent == wantedTotal && open.Count == 0,
        };
    }

    public static int Status(Ledger l, Args a)
    {
        var s = ComputeStatus(l);
        if (a.Flag("json")) { l.Ctx.Out.WriteLine(s.ToJsonString(Pretty)); return 0; }
        if (s["reason"] is { } reason) { l.Ctx.Out.WriteLine($"{l.Branch}: not done ({Files.Str(reason)})"); return 0; }
        var open = Files.Strings(s["open_findings"]).ToList();
        l.Ctx.Out.WriteLine($"{l.Branch} at {Files.Str(s["head"])[..7]}: {Files.Num(s["current"])} of {Files.Num(s["pairs"])} pairs current " +
                            $"({(Files.Num(s["share"]) * 100).ToString("F1", CultureInfo.InvariantCulture)}%), {open.Count} open finding(s)" +
                            (open.Count > 0 ? ": " + string.Join(", ", open) : "") + $" -> {(s["done"]!.GetValue<bool>() ? "DONE" : "not done")}");
        foreach (var (finding, rows) in (s["skipped_for_findings"] as JsonObject)!)
            l.Ctx.Out.WriteLine($"  {rows} row(s) skip for finding {finding}");
        foreach (var (id, c) in (s["claims"] as JsonObject)!.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var rows = (c!["outcomes"] as JsonObject)!.Select(kv => $"{kv.Key} {Files.Num(kv.Value)}").ToList();
            var checks = (c["check_outcomes"] as JsonObject ?? []).Select(kv => $"{kv.Key} {Files.Num(kv.Value)}").ToList();
            var obligations = (c["obligations"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var unseeded = obligations.Where(o => (o["seeded_by"] as JsonArray ?? []).Count == 0).Select(o => Files.Str(o["id"])).ToList();
            // K uncredited: the obligations no passing check named at the last replay.
            var uncredited = obligations.Count(o => o["passed"] is not JsonValue v || !v.GetValue<bool>());
            var promise = obligations.Count == 0 ? "" : $"  obligations {obligations.Count}" + (unseeded.Count > 0 ? $" ({unseeded.Count} without a seed: {string.Join(", ", unseeded)})" : ", each seeded")
                                                        + $", {uncredited} uncredited";
            var inapplicable = Files.Num(c["inapplicable_pairs"]) is var n and > 0 ? $", {n} inapplicable" : "";
            l.Ctx.Out.WriteLine($"  {id}: {Files.Num(c["current"])}/{Files.Num(c["pairs"])}{inapplicable}" + (rows.Count > 0 ? $"  (rows: {string.Join(", ", rows)}; checks: {string.Join(", ", checks)})" : "") + promise);
        }
        return 0;
    }

    /// <summary>
    /// What the corpus leaves out, each with its recorded reason, from the tracked corpus and the generated axes
    /// alone: the settings no surface reaches and nothing ignores, the settings without values, the declarations
    /// the generators refuse, the values the frameworks refuse on their own, what the claims say they do not
    /// promise (their not-promised entries and narrowings), and the surfaces the axes file declares out of reach.
    /// A report for the release checklist and a feature-research round; it decides nothing (decision 104).
    /// </summary>
    public static int Gaps(Ledger l, Args a)
    {
        var (universe, problems) = Axes.Build(l, a.Get("base") ?? "origin/main");
        var block = l.AxesBlock();
        var generated = Files.ReadJson(Path.Combine(l.Memory, "axes-generated.json")) as JsonObject ?? [];
        var versions = string.Join(", ", (generated["versions"] as JsonObject ?? []).Select(kv => $"{kv.Key} {Files.Str(kv.Value)}"));
        l.Ctx.Out.WriteLine($"prober gaps: {l.Branch} at {l.Head[..7]}: what the corpus leaves out, each with its recorded reason; a report, no decision");
        Section("settings no surface reaches and nothing ignores" + (versions.Length > 0 ? $" (enumerated at {versions})" : ""),
                problems.Where(p => p.StartsWith("unassigned: ", StringComparison.Ordinal)).Select(p => p["unassigned: ".Length..]));
        Section("settings without values", problems.Where(p => p.StartsWith("needs values: ", StringComparison.Ordinal)).Select(p => p["needs values: ".Length..]));
        Section("declarations the generators refuse (shape.refused-kind)", Files.Strings(block["axes"]?["shape.refused-kind"]));
        Section("values the frameworks refuse on their own",
                (block["refused"] as JsonObject ?? []).SelectMany(axis => (axis.Value as JsonObject ?? []).Select(v => $"{axis.Key}={v.Key}: {Files.Str(v.Value)}")));
        Section("not promised, by what it concerns",
                universe.Claims.SelectMany(c => c.NotPromised.Select(n => (Key: Concerns(n.Axes), Line: $"{Concerns(n.Axes)}: {c.Id}/{n.Id} \"{n.Sentence}\" ({n.Source})"))
                                                 .Concat(c.Narrowed.Select(kv => (Key: kv.Key, Line: $"{kv.Key}: {c.Id} narrows it, \"{kv.Value}\" ({c.Source})"))))
                        .OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Line, StringComparer.Ordinal).Select(x => x.Line));
        Section("pairs no obligation wants (declared inapplicable)",
                universe.Claims.Select(c => Inapplicable(c) is { } line ? $"{c.Id}: {line}" : null).OfType<string>());
        Section("pairs no passing check read both axes of (the last replay's evidence)", Unread(l, universe));
        Section("out of reach", (block["out-of-reach"] as JsonObject ?? []).Select(kv => $"{kv.Key}: {Files.Str(kv.Value)}"));
        return 0;

        void Section(string title, IEnumerable<string> items)
        {
            var list = items.ToList();
            l.Ctx.Out.WriteLine($"{title}: {(list.Count == 0 ? "none" : list.Count.ToString(CultureInfo.InvariantCulture))}");
            foreach (var item in list) l.Ctx.Out.WriteLine($"  {item}");
        }

        static string Concerns(IReadOnlyDictionary<string, List<string>> axes) =>
            axes.Count == 0 ? "the whole claim" : string.Join(" ", axes.Select(kv => $"{kv.Key}={string.Join("|", kv.Value)}"));
    }

    /// <summary>
    /// The wanted pairs of each claim with evidence that no passing check could earn: no obligation that wants the pair
    /// has a passing check, in its probes' last replay, that read both of the pair's axes. More rows cannot cover such a
    /// pair; a check that reads both axes, an exclusion or a declaration can (#54's four C11 pairs, wanted by obligations
    /// whose checks never read shape.wrapped). A claim whose probes have no evidence is left out.
    /// </summary>
    private static IEnumerable<string> Unread(Ledger l, Universe universe)
    {
        var registry = l.Registry();
        foreach (var claim in universe.Claims.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            // The axes each obligation's passing checks read together, as "a\nb" with a before b.
            var read = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var any = false;
            foreach (var (probe, entry) in registry)
            {
                if (Files.Str(entry["claim"]) != claim.Id || Files.ReadJson(l.EvidencePath(probe)) is not JsonObject evidence) continue;
                any = true;
                foreach (var (_, row) in evidence["rows"] as JsonObject ?? [])
                    if (row is JsonObject r)
                        foreach (var check in Ledger.ChecksOf(r).Where(c => c.Category == "pass"))
                        {
                            if (!read.TryGetValue(check.Obligation, out var axes)) read[check.Obligation] = axes = new(StringComparer.Ordinal);
                            var names = check.Applied.Keys.Order(StringComparer.Ordinal).ToList();
                            for (var i = 0; i < names.Count; i++)
                                for (var j = i + 1; j < names.Count; j++) axes.Add(names[i] + "\n" + names[j]);
                        }
            }
            if (!any) continue;
            var wantedBy = claim.ObligationIdsBuilt.Concat(read.Keys).Distinct(StringComparer.Ordinal).ToDictionary(o => o, claim.PairsWantedBy, StringComparer.Ordinal);
            foreach (var pair in claim.PairsWanted().OrderBy(p => p.A, StringComparer.Ordinal).ThenBy(p => p.B, StringComparer.Ordinal))
            {
                var (a, b) = (Axis(pair.A), Axis(pair.B));
                var both = string.CompareOrdinal(a, b) <= 0 ? a + "\n" + b : b + "\n" + a;
                if (!wantedBy.Any(o => o.Value.Contains(pair) && read.TryGetValue(o.Key, out var axes) && axes.Contains(both)))
                    yield return $"{claim.Id}: {pair.A} {pair.B}";
            }
        }

        static string Axis(string setting) => setting[..setting.IndexOf('=', StringComparison.Ordinal)];
    }

    public static int Gate(Ledger l, Args a) => ReleaseGate.Run(l, a.Get("since"), a.Flag("json"));

    public static int Footprint(Ledger l, Args a)
    {
        var worktree = Path.GetFullPath(a.Need("root"));
        var include = a.All("include") is { Count: > 0 } inc ? inc : ["src"];
        var hit = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var report in a.All("cobertura"))
        {
            var doc = XDocument.Load(report);
            var sources = doc.Descendants("source").Select(s => Path.GetFullPath(s.Value)).ToList();
            foreach (var cls in doc.Descendants("class"))
            {
                if (!cls.Descendants("line").Any(line => int.TryParse((string?)line.Attribute("hits"), out var h) && h > 0)) continue;
                var name = (string?)cls.Attribute("filename") ?? "";
                var candidates = Path.IsPathRooted(name) ? [name] : sources.Count > 0 ? sources.Select(s => Path.Combine(s, name)).ToList() : [name];
                foreach (var candidate in candidates)
                {
                    var full = Path.GetFullPath(candidate);
                    if (!full.StartsWith(worktree + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                    var rel = Path.GetRelativePath(worktree, full).Replace('\\', '/');
                    if (include.Any(p => rel == p || rel.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal))) hit.Add(rel);
                    break;
                }
            }
        }
        // A probe whose own process runs nothing under the include paths (one that builds a consumer in a child process, as C22
        // does) is recorded by what its registry entry watches; with nothing watched either, the footprint would never go stale.
        var watchedPaths = l.Watched(a.Need("probe"));
        if (hit.Count == 0 && watchedPaths.Count == 0)
            throw new Refusal("the coverage report shows no executed file under " + string.Join(", ", include) +
                              " and the registry entry watches nothing; refusing to record an empty footprint (it would never go stale): name what the probe depends on in its entry's watched");
        var rev = Git.Out(worktree, "rev-parse", "HEAD");
        var footprint = new JsonObject(hit.Select(p => KeyValuePair.Create(p, (JsonNode?)Git.ObjectId(worktree, p))));
        var compileTime = new JsonObject(a.All("compile-time").Select(p => KeyValuePair.Create(p, (JsonNode?)Git.ObjectId(worktree, p))));
        // What the probe's registry entry watches beyond what ran: recorded with the ids it ran at, so a change stales it.
        var watched = new JsonObject(watchedPaths.Select(p => KeyValuePair.Create(p, (JsonNode?)Git.ObjectId(worktree, p))));
        Files.AppendLine(l.P("footprints.jsonl"), new JsonObject
        {
            ["schemaVersion"] = Files.SchemaVersion, ["probe"] = a.Need("probe"), ["commit"] = rev, ["footprint"] = footprint,
            ["compileTime"] = compileTime, ["watched"] = watched, ["toolchain"] = l.Toolchain().DeepClone(), ["date"] = a.Get("date") ?? Today,
        });
        l.Ctx.Out.WriteLine($"{a.Need("probe")}: {footprint.Count} src files, {compileTime.Count} compile-time paths" +
                            $"{(watched.Count > 0 ? $", {watched.Count} watched" : "")} at {rev[..Math.Min(7, rev.Length)]}");
        return 0;
    }

    public static int Stale(Ledger l, Args a)
    {
        var states = l.ProbeStates(a.Flag("include-retired"));
        if (a.Flag("json"))
        {
            l.Ctx.Out.WriteLine(new JsonArray([.. states.Select(s => (JsonNode)new JsonObject
            {
                ["probe"] = s.Probe, ["claim"] = s.Claim, ["filter"] = s.Filter, ["variants"] = s.Variants.DeepClone(), ["stale"] = s.Stale,
                ["retired"] = s.Retired, ["passes"] = s.Passes, ["replay"] = s.Replay,
            })]).ToJsonString(Pretty));
            return 0;
        }
        var todo = states.Where(s => s.Replay).ToList();
        l.Ctx.Out.WriteLine($"{todo.Count} of {states.Count} probes to replay{(a.Flag("include-retired") ? " (retired included)" : "")}");
        foreach (var s in todo) l.Ctx.Out.WriteLine($"  {s.Probe}  {s.Filter}");
        return 0;
    }

    public static int RecordRun(Ledger l, Args a)
    {
        var probe = a.Need("probe"); var result = a.Need("result");
        if (result is not ("pass" or "fail")) throw new Refusal("--result is pass or fail", 2);
        var rev = l.Head; var identity = l.Identity(probe);
        // A regression is a probe that passed with this very identity and fails now; after any change to its
        // corpus, axes, environment or this tool, a failure is the probe's to triage, not the product's.
        var passes = Files.ReadLines(l.P("runs.jsonl")).Where(x => Files.Str(x["probe"]) == probe && Files.Str(x["result"]) == "pass").ToList();
        var passedBefore = passes.Any(x => Files.Str(x["identity"]) == identity);
        Files.AppendLine(l.P("runs.jsonl"), new JsonObject
        {
            ["schemaVersion"] = Files.SchemaVersion, ["probe"] = probe, ["result"] = result, ["commit"] = rev, ["date"] = a.Get("date") ?? Today,
            ["seconds"] = a.Get("seconds") is { } s ? double.Parse(s, CultureInfo.InvariantCulture) : null, ["identity"] = identity,
        });
        if (a.Get("evidence") is { } evidencePath)
        {
            var evidence = Files.ReadJson(evidencePath) as JsonObject ?? [];
            evidence["schemaVersion"] = Files.EvidenceSchemaVersion; evidence["probe"] = probe; evidence["identity"] = identity;
            evidence["commit"] = rev; evidence["result"] = result; evidence["date"] = a.Get("date") ?? Today;
            Files.WriteJson(l.EvidencePath(probe), evidence);
        }
        if (result == "fail" && passes.Count > 0 && !passedBefore)
            l.Ctx.Out.WriteLine($"{probe}: fails after a change to its corpus, axes, environment or this tool; triage the probe, not the product");
        if (result == "fail" && passedBefore)
            Files.AppendLine(l.P("findings.jsonl"), new JsonObject
            {
                ["schemaVersion"] = Files.SchemaVersion, ["id"] = $"R-{probe}-{rev[..7]}", ["claim"] = Files.Str(l.Registry().GetValueOrDefault(probe)?["claim"]),
                ["status"] = "open", ["commit"] = rev, ["probe"] = probe, ["reason"] = "replay: a probe that passed with this identity fails at this commit",
            });
        return 0;
    }

    public static int IdentityCommand(Ledger l, Args a) { l.Ctx.Out.WriteLine(l.Identity(a.Need("probe"))); return 0; }

    public static int Register(Ledger l, Args a)
    {
        // A note or a skip built from text fails at run time after a build and a replay; the staged sources are read first.
        SourceLint.Refuse(l, "register");
        var path = Path.Combine(l.StagingDir, "registry.json");
        var staged = Files.ReadJson(path) as JsonObject ?? new JsonObject { ["probes"] = new JsonObject() };
        var probes = staged["probes"] as JsonObject ?? [];
        var entry = l.Registry().GetValueOrDefault(a.Need("probe")) ?? [];
        entry["claim"] = a.Need("claim"); entry["filter"] = a.Need("filter");
        if (a.Get("variants") is { Length: > 0 } v) entry["variants"] = JsonNode.Parse(v);
        // The defect `seeds` applies to prove the probe can fail (decision 100): a patch under the corpus, an
        // environment toggle, or both, and what it breaks.
        var seed = new JsonObject();
        if (a.Get("seed") is { Length: > 0 } patch) seed["patch"] = patch;
        if (a.All("seed-env") is { Count: > 0 } toggles) seed["env"] = SeedEnv(toggles);
        if (a.Get("obligation") is { } obligation)
        {
            // A seed per obligation (decision 104): into `seeds`, in place of any seed that names the same obligation.
            if (obligation.Length == 0) throw new Refusal("--obligation needs the id of one of the claim's obligations", 2);
            if (seed.Count == 0) throw new Refusal("--obligation names the obligation of a seed: give the seed with --seed PATCH or --seed-env NAME=VALUE", 2);
            var claim = l.Claims().FirstOrDefault(c => Files.Str(c["id"]) == a.Need("claim"))
                        ?? throw new Refusal($"no claim {a.Need("claim")} in claims.json (tracked or staged), so obligation {obligation} cannot be checked");
            var ids = Claim.ObligationIds(claim);
            if (!ids.Contains(obligation))
                throw new Refusal($"claim {a.Need("claim")} lists no obligation {obligation} (it lists {string.Join(", ", ids)})", 2);
            var named = new JsonObject { ["obligation"] = obligation };
            foreach (var (key, value) in seed) named[key] = value?.DeepClone();
            named["defect"] = a.Need("defect");
            var kept = (entry["seeds"] as JsonArray ?? []).Where(s => Files.Str(s?["obligation"]) != obligation).Select(s => s?.DeepClone()).ToList();
            var replaced = (entry["seeds"] as JsonArray ?? []).Count - kept.Count;
            entry["seeds"] = new JsonArray([.. kept, named]);
            if (entry["seed"] is JsonObject one && Files.Str(one["obligation"]) == obligation) { entry.Remove("seed"); replaced++; }
            l.Ctx.Out.WriteLine($"seed for obligation {obligation} of {a.Need("probe")}" + (replaced > 0 ? $", in place of the {replaced} it had" : ""));
        }
        else if (seed.Count > 0) { seed["defect"] = a.Need("defect"); entry["seed"] = seed; }
        probes[a.Need("probe")] = entry;
        staged["probes"] = probes.DeepClone();
        Files.WriteJson(path, staged);
        l.Ctx.Out.WriteLine($"registered {a.Need("probe")} (staged; `publish` moves it into the corpus)");
        return 0;
    }

    /// <summary>A seed's environment toggle from <c>--seed-env NAME=VALUE ...</c>; a toggle without a name or an <c>=</c> is refused.</summary>
    private static JsonObject SeedEnv(List<string> toggles)
    {
        var env = new JsonObject();
        foreach (var toggle in toggles)
        {
            var eq = toggle.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) throw new Refusal($"--seed-env {toggle}: a toggle is NAME=VALUE", 2);
            env[toggle[..eq]] = toggle[(eq + 1)..];
        }
        return env;
    }

    public static int Publish(Ledger l, Args a)
    {
        if (!Directory.Exists(l.StagingDir)) { l.Ctx.Out.WriteLine("nothing staged"); return 0; }
        SourceLint.Refuse(l, "publish");
        var moved = new List<string>();
        foreach (var src in Directory.EnumerateFiles(l.StagingDir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(l.StagingDir, src).Replace('\\', '/');
            var dst = Path.Combine(l.CorpusDir, rel);
            if (rel == "registry.json")
            {
                var tracked = Files.ReadJson(dst) as JsonObject ?? new JsonObject { ["probes"] = new JsonObject() };
                var probes = tracked["probes"] as JsonObject ?? [];
                foreach (var (name, e) in Files.ReadJson(src)?["probes"] as JsonObject ?? []) probes[name] = e?.DeepClone();
                tracked["probes"] = new JsonObject(probes.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())));
                Files.WriteJson(dst, tracked);
            }
            else if (rel == "claims.json")
            {
                var merged = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
                foreach (var c in Ledger.ReadClaims(dst, "the corpus's claims.json").Concat(Ledger.ReadClaims(src, "the staged claims.json")))
                    merged[Files.Str(c["id"])] = c.DeepClone();
                Files.WriteJson(dst, new JsonArray([.. merged.Values]));
            }
            else if (rel.StartsWith("rows/", StringComparison.Ordinal) && rel.EndsWith(".json", StringComparison.Ordinal))
            {
                // A probe's rows, by probe: the file keeps the claim it names, else its probe's registry entry's.
                var probe = Path.GetFileNameWithoutExtension(rel);
                var claim = Files.Str(Files.ReadJson(src)?["claim"]) is { Length: > 0 } named ? named
                            : l.Registry().GetValueOrDefault(probe)?["claim"] is { } registered ? Files.Str(registered)
                            : throw new Refusal($"the staged {rel} names no claim and {probe} is no registered probe; a rows file is a probe's, its claim inside");
                Files.WriteRowsGzipped(dst + ".gz", claim, Files.ReadRows(src).OfType<JsonObject>().Select(r => r["row"] as JsonObject ?? r));
                rel += ".gz";
            }
            else if (rel.EndsWith(".jsonl", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                Files.WriteAtomically(dst, (File.Exists(dst) ? SharedFile.ReadAllText(dst) : "") + SharedFile.ReadAllText(src));
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            moved.Add(rel);
        }
        Directory.Delete(l.StagingDir, recursive: true);
        l.Ctx.Out.WriteLine($"published {moved.Count} file(s) into {Path.GetRelativePath(l.Top, l.CorpusDir).Replace('\\', '/')}:");
        foreach (var rel in moved) l.Ctx.Out.WriteLine($"  {rel}");
        return 0;
    }

    public static int Mark(Ledger l, Args a)
    {
        if (a.Positional.FirstOrDefault() != "retired") throw new Refusal("mark takes `retired`", 2);
        var probe = a.Need("probe");
        if (!l.Registry().TryGetValue(probe, out var entry)) throw new Refusal($"no probe {probe} in the registry");
        var path = Path.Combine(l.StagingDir, "registry.json");
        var staged = Files.ReadJson(path) as JsonObject ?? new JsonObject { ["probes"] = new JsonObject() };
        entry["retired"] = true;
        (staged["probes"] as JsonObject)![probe] = entry;
        Files.WriteJson(path, staged);
        l.Ctx.Out.WriteLine($"{probe} retired (staged)");
        return 0;
    }

    public static int Dismiss(Ledger l, Args a)
    {
        Files.AppendLine(Path.Combine(l.StagingDir, "dismissed.jsonl"),
            new JsonObject { ["schemaVersion"] = Files.SchemaVersion, ["id"] = a.Need("id"), ["reason"] = a.Need("reason"), ["date"] = a.Get("date") ?? Today });
        l.Ctx.Out.WriteLine($"{a.Need("id")} dismissed (staged; `publish` puts it in the corpus)");
        return 0;
    }

    /// <summary>
    /// Records an open finding fixed at a commit: the fixed line of findings.jsonl (finding-line.schema.json), with the
    /// open line's claim and probe and the commit's full id. It refuses an id this ledger does not hold open and a
    /// commit the repository does not have; a commit outside HEAD's history is recorded and counts once it is in it.
    /// </summary>
    public static int Fix(Ledger l, Args a)
    {
        var id = a.Positional is [var only] ? only : throw new Refusal("fix takes one finding id: fix F<n> --commit <sha>", 2);
        var given = a.Need("commit");
        var resolved = Git.Run(l.Top, "rev-parse", "-q", "--verify", given + "^{commit}");
        if (resolved.ExitCode != 0) throw new Refusal($"--commit {given} is no commit of this repository");
        var commit = resolved.Stdout.Trim();
        if (!l.Findings().TryGetValue(id, out var open)) throw new Refusal($"{id} is no finding of this ledger");
        if (Files.Str(open["status"]) is var status and not "open") throw new Refusal($"{id} is {status}, not open");
        var line = new JsonObject
        {
            ["schemaVersion"] = Files.SchemaVersion, ["id"] = id, ["claim"] = open["claim"]?.DeepClone(), ["status"] = "fixed", ["commit"] = commit,
        };
        if (Files.Str(open["probe"]) is { Length: > 0 } probe) line["probe"] = probe;
        if (a.Get("reason") is { Length: > 0 } reason) line["reason"] = reason;
        Files.AppendLine(l.P("findings.jsonl"), line);
        var inHistory = Git.Run(l.Top, "merge-base", "--is-ancestor", commit, "HEAD").ExitCode == 0;
        l.Ctx.Out.WriteLine($"{id} fixed at {commit[..7]}" + (inHistory ? "" : "; it counts once that commit is in HEAD's history"));
        return 0;
    }

    public static int Lock(Ledger l, Args a)
    {
        var holder = a.Need("holder"); var mode = a.Need("mode");
        if (mode is not ("replay" or "explore" or "full")) throw new Refusal("--mode is replay, explore or full", 2);
        var pid = a.Get("pid") is { Length: > 0 } p ? long.Parse(p, CultureInfo.InvariantCulture) : (long?)null;
        var (taken, previous) = RoundLock.Take(l, holder, mode, pid);
        if (!taken)
        {
            var since = DateTimeOffset.FromUnixTimeMilliseconds((long)(Files.Num(previous!["since"]) * 1000)).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            l.Ctx.Error.WriteLine($"prober: locked by {Files.Str(previous["holder"])} ({Files.Str(previous["mode"])}) since {since}");
            return 1;
        }
        var over = previous is not null && Files.Str(previous["holder"]) != holder ? $" (took over from {Files.Str(previous["holder"])}, whose heartbeat stopped)" : "";
        l.Ctx.Out.WriteLine($"locked for {mode} by {holder}{over}");
        return 0;
    }

    public static int Unlock(Ledger l, Args a)
    {
        var owed = RoundLock.Release(l, a.Need("holder"));
        l.Ctx.Out.WriteLine(owed ? "unlocked; a replay was skipped while locked: run `prober replay` now" : "unlocked");
        return 0;
    }

    public static int HeartbeatCommand(Ledger l, Args a) { RoundLock.Beat(l, a.Need("holder")); return 0; }

    public static int OweReplay(Ledger l, Args a) { RoundLock.OweReplay(l); return 0; }

    public static JsonObject? ActiveRound(Ledger l)
    {
        var round = Files.ReadJson(l.P("round.json")) as JsonObject;
        var held = RoundLock.Holder(l);
        return round is not null && held is not null && Files.Str(held["holder"]) == Files.Str(round["holder"]) ? round : null;
    }

    public static int RoundStart(Ledger l, Args a)
    {
        var holder = a.Need("holder");
        if (RoundLock.Holder(l) is not { } held || Files.Str(held["holder"]) != holder)
            throw new Refusal("take the lock first; round-start records the caps of the lock's holder");
        var minutes = double.Parse(a.Need("minutes"), CultureInfo.InvariantCulture);
        var claims = a.Get("claims") is { Length: > 0 } c ? int.Parse(c, CultureInfo.InvariantCulture) : (int?)null;
        Files.WriteJson(l.P("round.json"), new JsonObject
        {
            ["schemaVersion"] = Files.SchemaVersion, ["holder"] = holder, ["started"] = RoundLock.Now(),
            ["deadline"] = RoundLock.Now() + minutes * 60, ["max_claims"] = claims, ["claims"] = new JsonArray(),
        });
        l.Ctx.Out.WriteLine($"round capped at {minutes.ToString(CultureInfo.InvariantCulture)} min" + (claims is { } n ? $" and {n} claim(s)" : ""));
        return 0;
    }

    public static int RoundEnd(Ledger l, Args a)
    {
        if (Files.ReadJson(l.P("round.json")) is JsonObject round && Files.Str(round["holder"]) == a.Need("holder"))
        {
            File.Delete(l.P("round.json"));
            l.Ctx.Out.WriteLine($"round ended: claims {(Files.Strings(round["claims"]).Any() ? string.Join(", ", Files.Strings(round["claims"])) : "none")}");
        }
        return 0;
    }

    public static int Repair(Ledger l, Args a)
    {
        var fixedCount = 0;
        if (Directory.Exists(l.Dir))
            foreach (var path in Directory.EnumerateFiles(l.Dir, "*.jsonl", SearchOption.AllDirectories))
            {
                var data = File.ReadAllText(path);
                if (data.Length > 0 && !data.EndsWith('\n'))
                {
                    Files.WriteAtomically(path, data[..(data.LastIndexOf('\n') + 1)]);
                    fixedCount++;
                    l.Ctx.Out.WriteLine($"  removed a partial last line from {Path.GetRelativePath(l.Dir, path)}");
                }
            }
        l.Ctx.Out.WriteLine($"repair: {fixedCount} file(s) fixed");
        return 0;
    }

    public static int Abandon(Ledger l, Args a)
    {
        Repair(l, a);
        if (Files.ReadJson(l.P("round.json")) is JsonObject round)
        {
            File.Delete(l.P("round.json"));
            l.Ctx.Out.WriteLine($"round caps cleared ({Files.Str(round["holder"])})");
        }
        var held = RoundLock.Abandon(l);
        l.Ctx.Out.WriteLine(held is null ? "no lock was held" : $"lock of {Files.Str(held["holder"])} ({Files.Str(held["mode"])}) released");
        return 0;
    }

    /// <summary>
    /// Joins the states of a replay split by family (<c>replay --family F</c>, one state directory per leg) into
    /// this branch's state: each leg's evidence files are copied in, its run, footprint, finding and ledger lines
    /// are added where this state lacks them (a line a leg carried over from a restored state is not added
    /// twice), and one replay-last.json sums the legs. It refuses while a probe the registry lists was neither
    /// replayed nor skipped by any leg, which is how a matrix that leaves a family out fails instead of passing
    /// on what it ran, and it fails (exit 1) when any leg failed, so the job that merges fails as the legs did.
    /// </summary>
    public static int Merge(Ledger l, Args a)
    {
        var sources = a.All("from").Where(x => x.Length > 0).ToList();
        if (sources.Count == 0) throw new Refusal("--from DIR... is required: the state directories of the legs to join into this one", 2);
        var branchDir = Path.GetFileName(l.Dir);
        Directory.CreateDirectory(Path.Combine(l.Dir, "evidence"));
        var legs = new List<JsonObject>();
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var from in sources)
        {
            var root = Path.GetFullPath(from);
            var dir = Path.Combine(root, "branches", branchDir);
            var last = Files.ReadJson(Path.Combine(dir, "replay-last.json")) as JsonObject
                       ?? throw new Refusal($"{from} holds no replay of {l.Branch}: no branches/{branchDir}/replay-last.json");
            if (legs.Count > 0 && Files.Str(last["commit"]) != Files.Str(legs[0]["commit"]))
                throw new Refusal($"{from} replayed {Files.Str(last["commit"])[..7]}, another leg {Files.Str(legs[0]["commit"])[..7]}; the legs of one replay share a commit");
            legs.Add(last);
            foreach (var name in new[] { "runs.jsonl", "footprints.jsonl", "findings.jsonl", "ledger.jsonl" }) UnionLines(Path.Combine(dir, name), l.P(name));
            foreach (var sub in new[] { "evidence", "logs" })
            {
                var source = Path.Combine(dir, sub);
                if (!Directory.Exists(source)) continue;
                Directory.CreateDirectory(l.P(sub));
                foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, l.P(Path.Combine(sub, Path.GetFileName(file))), overwrite: true);
            }
            foreach (var name in new[] { "universe.json" })
                if (File.Exists(Path.Combine(dir, name)) && !File.Exists(l.P(name))) File.Copy(Path.Combine(dir, name), l.P(name));
            var axes = Path.Combine(root, "axes-generated.json");
            if (File.Exists(axes) && !File.Exists(Path.Combine(l.Memory, "axes-generated.json"))) File.Copy(axes, Path.Combine(l.Memory, "axes-generated.json"));
            foreach (var probe in Files.Strings(last["selected"]).Concat(Files.Strings(last["skipped"]))) seen.Add(probe);
        }

        var all = legs.Any(x => x["all"] is JsonValue v && v.TryGetValue<bool>(out var b) && b);
        var selected = new SortedSet<string>(legs.SelectMany(x => Files.Strings(x["selected"])), StringComparer.Ordinal);
        var skipped = new SortedSet<string>(legs.SelectMany(x => Files.Strings(x["skipped"])).Where(p => !selected.Contains(p)), StringComparer.Ordinal);
        var failed = new SortedSet<string>(legs.SelectMany(x => Files.Strings(x["failed"])), StringComparer.Ordinal);
        static JsonArray Names(IEnumerable<string> names) => new([.. names.Select(x => (JsonNode)x)]);
        Files.WriteJson(l.P("replay-last.json"), new JsonObject
        {
            ["branch"] = l.Branch, ["commit"] = Files.Str(legs[0]["commit"]), ["replayed"] = selected.Count, ["failed"] = Names(failed),
            ["seconds"] = Math.Round(legs.Max(x => Files.Num(x["seconds"])), 1), ["build_seconds"] = Math.Round(legs.Max(x => Files.Num(x["build_seconds"])), 1),
            ["all"] = all, ["legs"] = Names(legs.Select(x => Files.Str(x["family"], "all"))), ["footprint"] = legs.All(x => x["footprint"] is not JsonValue f || !f.TryGetValue<bool>(out var ft) || ft),
            ["selected"] = Names(selected), ["skipped"] = Names(skipped),
        });
        l.Ctx.Out.WriteLine($"prober merge: {legs.Count} leg(s) into {l.Dir}: {selected.Count} replayed, {failed.Count} failed, {skipped.Count} current");

        var missing = l.Registry().Where(kv => all || kv.Value["retired"] is not JsonValue r || !r.TryGetValue<bool>(out var retired) || !retired)
                                  .Select(kv => kv.Key).Where(p => !seen.Contains(p)).ToList();
        if (missing.Count > 0)
            throw new Refusal($"no leg replayed or skipped {string.Join(", ", missing)}: every probe's family is a leg of the matrix, or the merged state is short");
        if (failed.Count > 0) { l.Ctx.Out.WriteLine($"  failed: {string.Join(", ", failed)}"); return 1; }
        return 0;
    }

    /// <summary>Adds to <paramref name="target"/> every line of <paramref name="source"/> it does not already hold, in order.</summary>
    private static void UnionLines(string source, string target)
    {
        if (!File.Exists(source)) return;
        var have = File.Exists(target) ? SharedFile.ReadAllLines(target).Where(x => x.Trim().Length > 0).ToList() : [];
        var known = new HashSet<string>(have, StringComparer.Ordinal);
        var added = SharedFile.ReadAllLines(source).Where(x => x.Trim().Length > 0 && known.Add(x)).ToList();
        if (added.Count == 0 && File.Exists(target)) return;
        Files.WriteAtomically(target, string.Join('\n', have.Concat(added)) + (have.Count + added.Count > 0 ? "\n" : ""));
    }
}
