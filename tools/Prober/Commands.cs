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

    private static readonly HashSet<string> Multi = new(StringComparer.Ordinal) { "cobertura", "include", "compile-time" };
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
        var universe = Universe.Load(l.P("universe.json")) ?? throw new Refusal("no universe.json for this branch yet; run `universe` first");
        var claimId = a.Need("claim");
        if (!universe.ById.TryGetValue(claimId, out var claim)) throw new Refusal($"no claim {claimId} in the universe");
        var full = a.Flag("full");
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
        var covered = full ? [] : l.Covered(universe, claimId);
        var todo = claim.Pairs().Where(p => !covered.Contains((claimId, p))).ToHashSet();
        var max = int.Parse(a.Get("max") ?? "2000", CultureInfo.InvariantCulture);
        var rows = Covering.Rows(claim, todo, max, int.Parse(a.Get("seed") ?? "1", CultureInfo.InvariantCulture));
        var output = new JsonObject
        {
            ["claim"] = claimId,
            ["rows"] = new JsonArray([.. rows.Select(r => (JsonNode)new JsonObject
            {
                ["row"] = new JsonObject(r.Row.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["pairs"] = new JsonArray([.. r.Pairs.Select(p => (JsonNode)new JsonArray(p.A, p.B))]),
            })]),
            ["uncovered_after"] = todo.Count - rows.Sum(r => r.Pairs.Count),
        };
        l.Ctx.Out.WriteLine(output.ToJsonString(Pretty));
        return 0;
    }

    public static int UniverseCommand(Ledger l, Args a)
    {
        var claims = a.Get("claims") is { } cp ? (Files.ReadJson(cp) as JsonArray ?? []).OfType<JsonObject>().ToList() : null;
        var generated = a.Get("generated") is { } gp ? Files.ReadJson(gp) as JsonObject : null;
        var (universe, problems) = Axes.Build(l, a.Get("base") ?? "origin/main", claims, generated);
        var total = 0;
        foreach (var c in universe.Claims)
        {
            var n = c.Pairs().Count; total += n;
            l.Ctx.Out.WriteLine($"{c.Id}: {c.Axes.Count} axes, {n} pairs");
        }
        l.Ctx.Out.WriteLine($"universe: {universe.Claims.Count} claims, {total} pairs");
        foreach (var p in problems) l.Ctx.Out.WriteLine($"  {p}");
        return 0;
    }

    public static JsonObject ComputeStatus(Ledger l)
    {
        var universe = Universe.Load(l.P("universe.json"));
        if (universe is null) return new JsonObject { ["branch"] = l.Branch, ["done"] = false, ["reason"] = "no universe.json: run `universe` (no round has run here)" };
        var done = l.Covered(universe);
        var claims = new JsonObject(); var total = 0; var current = 0;
        foreach (var c in universe.Claims)
        {
            var pairs = c.Pairs(); var hit = pairs.Count(p => done.Contains((c.Id, p)));
            claims[c.Id] = new JsonObject { ["pairs"] = pairs.Count, ["current"] = hit };
            total += pairs.Count; current += hit;
        }
        var open = l.OpenFindings();
        return new JsonObject
        {
            ["branch"] = l.Branch, ["head"] = l.Head, ["universe_commit"] = universe.Commit, ["pairs"] = total, ["current"] = current,
            ["share"] = total > 0 ? Math.Round((double)current / total, 4) : 1.0,
            ["open_findings"] = new JsonArray([.. open.Select(f => (JsonNode)f)]), ["claims"] = claims,
            ["done"] = total > 0 && current == total && open.Count == 0,
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
        foreach (var (id, c) in (s["claims"] as JsonObject)!.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            l.Ctx.Out.WriteLine($"  {id}: {Files.Num(c!["current"])}/{Files.Num(c["pairs"])}");
        return 0;
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
        if (hit.Count == 0)
            throw new Refusal("the coverage report shows no executed file under " + string.Join(", ", include) + "; refusing to record an empty footprint (it would never go stale)");
        var rev = Git.Out(worktree, "rev-parse", "HEAD");
        var footprint = new JsonObject(hit.Select(p => KeyValuePair.Create(p, (JsonNode?)Git.ObjectId(worktree, p))));
        var compileTime = new JsonObject(a.All("compile-time").Select(p => KeyValuePair.Create(p, (JsonNode?)Git.ObjectId(worktree, p))));
        Files.AppendLine(l.P("footprints.jsonl"), new JsonObject
        {
            ["schemaVersion"] = Files.SchemaVersion, ["probe"] = a.Need("probe"), ["commit"] = rev, ["footprint"] = footprint,
            ["compileTime"] = compileTime, ["toolchain"] = l.Toolchain().DeepClone(), ["date"] = a.Get("date") ?? Today,
        });
        l.Ctx.Out.WriteLine($"{a.Need("probe")}: {footprint.Count} src files, {compileTime.Count} compile-time paths at {rev[..Math.Min(7, rev.Length)]}");
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
            evidence["schemaVersion"] = Files.SchemaVersion; evidence["probe"] = probe; evidence["identity"] = identity;
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
        var path = Path.Combine(l.StagingDir, "registry.json");
        var staged = Files.ReadJson(path) as JsonObject ?? new JsonObject { ["probes"] = new JsonObject() };
        var probes = staged["probes"] as JsonObject ?? [];
        var entry = l.Registry().GetValueOrDefault(a.Need("probe")) ?? [];
        entry["claim"] = a.Need("claim"); entry["filter"] = a.Need("filter");
        if (a.Get("variants") is { Length: > 0 } v) entry["variants"] = JsonNode.Parse(v);
        probes[a.Need("probe")] = entry;
        staged["probes"] = probes.DeepClone();
        Files.WriteJson(path, staged);
        l.Ctx.Out.WriteLine($"registered {a.Need("probe")} (staged; `publish` moves it into the corpus)");
        return 0;
    }

    public static int Publish(Ledger l, Args a)
    {
        if (!Directory.Exists(l.StagingDir)) { l.Ctx.Out.WriteLine("nothing staged"); return 0; }
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
                foreach (var c in (Files.ReadJson(dst) as JsonArray ?? []).Concat(Files.ReadJson(src) as JsonArray ?? []).OfType<JsonObject>())
                    merged[Files.Str(c["id"])] = c.DeepClone();
                Files.WriteJson(dst, new JsonArray([.. merged.Values]));
            }
            else if (rel.StartsWith("rows/", StringComparison.Ordinal) && rel.EndsWith(".json", StringComparison.Ordinal))
            {
                var claim = Path.GetFileNameWithoutExtension(rel);
                Files.WriteRowsGzipped(dst + ".gz", claim, Files.ReadRows(src).OfType<JsonObject>().Select(r => r["row"] as JsonObject ?? r));
                rel += ".gz";
            }
            else if (rel.EndsWith(".jsonl", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                Files.WriteAtomically(dst, (File.Exists(dst) ? File.ReadAllText(dst) : "") + File.ReadAllText(src));
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
}
