// Row plumbing shared by every probe: rows come from `prober rows --claim X` (JSON, copied to Probes/rows/X.json);
// each probe records one line per row to $PROBE_OUT/<claim>.jsonl (idx, category, applied settings, reasons, findings, note), which the replay reads.
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Probes.Shapes;

/// <summary>
/// What a row actually did, shared by the row and every copy made of it (With, Fresh): the axes the harness
/// read, the settings it substituted, how the row ended, and the open findings it stepped around. The ledger
/// credits only the axes read, at the value that applied, and only for a row that ended as a pass.
/// </summary>
public sealed class Evidence
{
    public readonly HashSet<string> Read = new();
    public readonly Dictionary<string, string> Applied = new();
    public readonly SortedSet<string> Findings = new(StringComparer.Ordinal);
    public string Category = "pass";
    public readonly List<string> Reasons = new();
    static readonly string[] Order = ["pass", "inapplicable", "expected-refusal", "inconclusive", "known-finding", "harness-error", "violation"];
    public void Raise(string category, string reason)
    {
        if (Array.IndexOf(Order, category) < 0) throw new ArgumentException("no outcome category " + category, nameof(category));
        if (Array.IndexOf(Order, category) > Array.IndexOf(Order, Category)) Category = category;
        Reasons.Add(category + ":" + reason);
    }
}

public sealed class Row
{
    readonly Dictionary<string, string> d;
    public Evidence Ev { get; }
    public int Index { get; }
    public Row(int index, Dictionary<string, string> d) : this(index, d, new Evidence()) { }
    Row(int index, Dictionary<string, string> d, Evidence ev) { Index = index; this.d = d; Ev = ev; }
    public string this[string axis] => d.TryGetValue(axis, out var v) ? Mark(axis, v) : throw new KeyNotFoundException($"row has no axis {axis}");
    public string? Get(string axis) => d.TryGetValue(axis, out var v) ? Mark(axis, v) : null;
    public bool Is(string axis, string value) => d.TryGetValue(axis, out var v) && Mark(axis, v) == value;
    /// <summary>Every axis, each marked read: a caller that enumerates is applying them (the JSON options builder).</summary>
    public IEnumerable<KeyValuePair<string, string>> All { get { foreach (var kv in d) { Mark(kv.Key, kv.Value); yield return kv; } } }
    string Mark(string axis, string v) { lock (Ev) Ev.Read.Add(axis); return v; }
    /// <summary>The setting ran as <paramref name="actual"/>, not as the row asked: only the actual value earns credit.</summary>
    public void Substituted(string axis, string actual, string reason) { lock (Ev) Ev.Applied[axis] = actual; Note("substituted", $"{axis}={actual}, {reason}"); }
    public void Outcome(string category, string reason) { lock (Ev) Ev.Raise(category, reason); }
    /// <summary>The settings that ran: the axes the harness read, at the value that applied.</summary>
    public Dictionary<string, string> AppliedSettings()
    {
        lock (Ev) return Ev.Read.Where(d.ContainsKey).ToDictionary(a => a, a => Ev.Applied.TryGetValue(a, out var v) ? v : d[a]);
    }
    public string Notes { get; private set; } = "";
    /// <summary>
    /// Records what a check saw, by a key of <see cref="NoteKeys"/>; the key alone decides the outcome, the
    /// detail only explains it. A key the catalogue does not list is a harness error, never a pass.
    /// </summary>
    public void Note(string key, string? detail = null)
    {
        var n = detail is null ? key : $"{key}({detail})";
        if (!Notes.Split(';').Contains(n)) Notes += (Notes.Length == 0 ? "" : ";") + n;
        if (!NoteKeys.Category.TryGetValue(key, out var c)) Outcome("harness-error", $"note key {key} is not in NoteKeys: add it with its outcome");
        else if (c != "pass") Outcome(c, n);
    }
    /// <summary>
    /// Steps around an open finding: the row ends as known-finding and earns nothing. The replay fails a probe
    /// that skips for a finding the ledger has fixed or dismissed, and the release gate refuses while any skips.
    /// </summary>
    public void Skip(string finding, string why)
    {
        lock (Ev) Ev.Findings.Add(finding);
        Outcome("known-finding", $"{finding}: {why}");
    }
    public System.Text.StringBuilder Log { get; } = new();
    public Row Fresh() => new Row(Index, new Dictionary<string, string>(d), Ev);
    public Row With(string axis, string value) { var c = new Dictionary<string, string>(d) { [axis] = value }; return new Row(Index, c, Ev); }
    public override string ToString() => string.Join(" ", d.Where(kv => kv.Value != "default").OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
}

/// <summary>
/// Every note a check may make, by exact key, and the outcome it stands for. A row whose checks returned no
/// failure is a pass only if none of its notes says a check was skipped (inapplicable), ended in a refusal
/// (expected refusal) or could not tell (inconclusive): a refusal is evidence about refusing, not about
/// serializing or binding successfully. "pass" keys only explain how a check that ran was compared.
/// </summary>
public static class NoteKeys
{
    public static readonly IReadOnlyDictionary<string, string> Category = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // how a check that ran was compared
        ["compared"] = "pass", ["substituted"] = "pass", ["dropped"] = "pass", ["ctx-strict"] = "pass", ["ctx-fallback"] = "pass",
        ["binding:nullable"] = "pass", ["own-vo"] = "pass", ["converter-wraps-inner"] = "pass",
        ["documented-char-guid-key-never-generated-checked"] = "pass", ["documented-char-guid-not-generated-checked"] = "pass",
        ["schema:compared-with-nullable-wrapped"] = "pass", ["schema:nullable-spellings-normalised"] = "pass",
        ["schema:class-vo-nullability-normalised"] = "pass", ["schema:neither-side-documented"] = "pass",
        // EF: the claim's check ran, against the documented expectation where the wrapped type's own side cannot answer
        ["refusal-names-the-type"] = "pass", ["documented-char-guid-default-generation-normalized"] = "pass",
        ["plain-key-throws-on-sqlite-vo-checked-against-expected"] = "pass", ["tpc-sqlite-no-sequence-assign-checked"] = "pass",
        // the document part has nothing to check: ASP.NET alone refuses the same document in the same row, or
        // leaves the controller out of it (docs/aspnet-core.md, "Bind value objects in MVC"), checked absent
        ["doc:refused-by-aspnet-alone"] = "pass", ["doc:controller-not-in-api-explorer"] = "pass",
        // a check that did not apply
        ["own-vo-converter"] = "inapplicable", ["closed-set-vs-own-wrapped-converter"] = "inapplicable", ["closed-set-differs"] = "inapplicable",
        ["byte-array-is-base64"] = "inapplicable", ["own-spelling"] = "inapplicable", ["not-bindable-from-text"] = "inapplicable",
        ["route-text-unsafe"] = "inapplicable", ["own-vo:body-binding-skipped"] = "inapplicable",
        ["mvc-maxvalidationdepth1-ignores-binding-errors"] = "inapplicable", ["stored-factory-skips-validation"] = "inapplicable",
        ["schema:converter-not-described"] = "inapplicable", ["schema:nullability-not-comparable"] = "inapplicable",
        ["closed-set-key-unpoliced"] = "inapplicable", ["plain-refused"] = "inapplicable", ["entry-not-applicable"] = "inapplicable",
        ["no-text"] = "inapplicable", ["text:wrapped-round-trip-lossy-too"] = "inapplicable", ["no-operator"] = "inapplicable",
        // a refusal, intended or the framework's own
        ["documented-diff"] = "expected-refusal", ["stj-maxdepth"] = "expected-refusal", ["validated-refuses-type-limit"] = "expected-refusal",
        ["class-vo-takes-null"] = "expected-refusal", ["bind:plain-unbindable"] = "expected-refusal", ["preserve-documented"] = "expected-refusal",
        ["mvc-400-unwritable-under-source-gen"] = "expected-refusal", ["own-vo:read-back-refused"] = "expected-refusal",
        ["both-threw"] = "expected-refusal", ["read-both-threw"] = "expected-refusal", ["ser-both-threw"] = "expected-refusal",
        ["ser-refused"] = "expected-refusal", ["read-back-refused"] = "expected-refusal", ["plain-read-back-fails-too"] = "expected-refusal",
        ["own-vo-converter-threw"] = "expected-refusal",
        // both sides refused, but not alike
        ["both-threw-differently"] = "inconclusive",
    };
}

public static class Rows
{
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CodoMetis.TypeKit.Samples.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
    public static IEnumerable<Row> Load(string claim)
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "Probes", "rows", claim + ".json")))!;
        var i = 0;
        foreach (var r in doc["rows"]!.AsArray())
            yield return new Row(i++, r!["row"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>()));
    }
    /// <summary>
    /// xunit MemberData: (claim, index), labelled with the row's settings so a test list or a failure names
    /// them. Optional filter by env PROBE_ROWS="0-20" for quick runs.
    /// </summary>
    public static TheoryData<string, int> Data(string claim)
    {
        var d = new TheoryData<string, int>();
        var range = Environment.GetEnvironmentVariable("PROBE_ROWS");
        (int lo, int hi) = range is null ? (0, int.MaxValue) : (int.Parse(range.Split('-')[0]), int.Parse(range.Split('-')[1]));
        var only = Environment.GetEnvironmentVariable("PROBE_ONLY"); // "axis=value" keeps rows with that value
        foreach (var r in Load(claim))
            if (r.Index >= lo && r.Index <= hi && (only is null || only.Split(',').All(c => c.Contains("!=") ? !r.Is(c.Split("!=")[0], c.Split("!=")[1]) : r.Is(c.Split('=')[0], c.Split('=')[1]))))
                d.Add(new TheoryDataRow<string, int>(claim, r.Index) { Label = $"#{r.Index} {r}" });
        return d;
    }
    static readonly Dictionary<string, List<Row>> Cache = new();
    public static Row Get(string claim, int index)
    {
        lock (Cache) { if (!Cache.TryGetValue(claim, out var l)) Cache[claim] = l = Load(claim).ToList(); return l[index]; }
    }
}

public static class Rec
{
    static readonly object Gate = new();
    /// <summary>One line per row to $PROBE_OUT/claim.jsonl: how it ended and the settings that ran.</summary>
    public static void Write(string claim, int idx, string category, string note, Row? row = null)
    {
        var dir = Environment.GetEnvironmentVariable("PROBE_OUT") ?? "/tmp/probe-out";
        Directory.CreateDirectory(dir);
        var applied = row?.AppliedSettings() ?? new Dictionary<string, string>();
        var reasons = row is null ? [] : row.Ev.Reasons.ToArray();
        var findings = row is null ? [] : row.Ev.Findings.ToArray();
        lock (Gate) File.AppendAllText(Path.Combine(dir, claim + ".jsonl"),
            JsonSerializer.Serialize(new { idx, status = category == "pass" ? "pass" : "fail", category, completed = row is not null,
                                           applied, reasons, findings, note = note.Length > 600 ? note[..600] : note }) + "\n");
    }
    /// <summary>
    /// Runs the check on a fresh copy of the row (its own evidence); null = no failure, otherwise the failure text.
    /// An exception the check did not expect is a harness error until triaged, never a pass. A violation or a
    /// harness error fails the test; any other outcome but a pass skips it with its reasons, so a test run
    /// counts what earned nothing.
    /// </summary>
    public static async Task Run(string claim, int idx, Func<Row, Task<string?>> check, string? rowsOf = null)
    {
        var cached = Rows.Get(rowsOf ?? claim, idx);
        var row = new Row(cached.Index, cached.All.ToDictionary(kv => kv.Key, kv => kv.Value));
        string? fail;
        try { fail = await check(row); }
        catch (Exception ex) { fail = "UNEXPECTED " + ex.GetType().Name + ": " + ex.Message; row.Outcome("harness-error", fail); }
        if (fail is not null) row.Outcome("violation", fail);
        Write(claim, idx, row.Ev.Category, fail ?? row.Notes, row);
        if (fail is not null) Assert.Fail($"[{claim} #{idx}] {row}\n{fail}");
        if (row.Ev.Category == "harness-error") Assert.Fail($"[{claim} #{idx}] {row}\nharness error: {string.Join("; ", row.Ev.Reasons)}");
        if (row.Ev.Category != "pass") Assert.Skip($"{row.Ev.Category}: {string.Join("; ", row.Ev.Reasons.Where(x => !x.StartsWith("pass:")))}");
    }
}

public static class Shrinker
{
    /// <summary>Greedy shrink: reset each axis to its default (shape axes to a fixed base) while the failure (same first 50 characters) persists.</summary>
    public static async Task<(Row Row, string Failure)> Shrink(Row r, Func<Row, Task<string?>> check)
    {
        var baseShape = new Dictionary<string, string> { ["shape.kind"] = "record-struct", ["shape.wrapped"] = "int", ["shape.position"] = "property", ["shape.containing"] = "class", ["input.json"] = "valid" };
        async Task<string?> Safe(Row x) { try { return await check(x.Fresh()); } catch (Exception ex) { return "UNEXPECTED " + ex.GetType().Name + ": " + ex.Message; } }
        var fail = await Safe(r) ?? throw new InvalidOperationException("row does not fail");
        var key = fail[..Math.Min(50, fail.Length)];
        var cur = r;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var kv in cur.All.ToList())
            {
                var def = baseShape.TryGetValue(kv.Key, out var b) ? b : "default";
                if (kv.Value == def) continue;
                var cand = cur.With(kv.Key, def);
                var f = await Safe(cand);
                if (f is not null && f.StartsWith(key[..Math.Min(30, key.Length)])) { cur = cand; changed = true; fail = f; }
            }
        }
        return (cur, fail);
    }
}
