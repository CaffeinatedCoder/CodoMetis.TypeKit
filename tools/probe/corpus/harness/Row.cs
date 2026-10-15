// Row plumbing shared by every probe: rows come from `prober rows --claim X` (JSON, copied to Probes/rows/X.json);
// each probe records one line per row to $PROBE_OUT/<claim>.jsonl (idx, status pass|fail, note) so the ledger is written by script.
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Probes.Shapes;

/// <summary>
/// What a row actually did, shared by the row and every copy made of it (With, Fresh): the axes the harness
/// read, the settings it substituted, and how the row ended. The ledger credits only the axes read, at the
/// value that applied, and only for a row that ended as a pass.
/// </summary>
public sealed class Evidence
{
    public readonly HashSet<string> Read = new();
    public readonly Dictionary<string, string> Applied = new();
    public string Category = "pass";
    public readonly List<string> Reasons = new();
    static readonly string[] Order = ["pass", "inapplicable", "expected-refusal", "inconclusive", "harness-error", "violation"];
    public void Raise(string category, string reason)
    {
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
    public void Substituted(string axis, string actual, string reason) { lock (Ev) Ev.Applied[axis] = actual; Note($"substituted:{axis}={actual}({reason})"); }
    public void Outcome(string category, string reason) { lock (Ev) Ev.Raise(category, reason); }
    /// <summary>The settings that ran: the axes the harness read, at the value that applied.</summary>
    public Dictionary<string, string> AppliedSettings()
    {
        lock (Ev) return Ev.Read.Where(d.ContainsKey).ToDictionary(a => a, a => Ev.Applied.TryGetValue(a, out var v) ? v : d[a]);
    }
    public string Notes { get; private set; } = "";
    public void Note(string n)
    {
        if (!Notes.Split(';').Contains(n)) Notes += (Notes.Length == 0 ? "" : ";") + n;
        if (NoteCategories.Of(n) is { } c) Outcome(c, n);
    }
    public System.Text.StringBuilder Log { get; } = new();
    public Row Fresh() => new Row(Index, new Dictionary<string, string>(d), Ev);
    public Row With(string axis, string value) { var c = new Dictionary<string, string>(d) { [axis] = value }; return new Row(Index, c, Ev); }
    public override string ToString() => string.Join(" ", d.Where(kv => kv.Value != "default").OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
}

/// <summary>
/// The outcome a harness note stands for. A row whose checks returned no failure is a pass only if none of
/// its notes says a check was skipped (inapplicable) or ended in a refusal (expected refusal): a refusal is
/// evidence about refusing, not about serializing or binding successfully.
/// </summary>
public static class NoteCategories
{
    static readonly string[] Refusal =
    [
        "both-threw", "read-both-threw", "ser-both-threw", "ser-refused", "read-back-refused", "refused-for-wrapped-too",
        "preserve-refused-by-stj", "aspnet-refuses", "preserve-documented", "stj-maxdepth", "mvc-400-unwritable",
        "validated-refuses-type-limit", "plain-read-back-fails-too", "own-vo-converter-threw", "plain-unbindable",
        "documented-diff", "class-vo-takes-null", "own-vo:read-back-refused",
    ];
    static readonly string[] Inapplicable =
    [
        "-NA", "no-text:", "cannot-bind", "not-bindable-from-text", "not-in-api-explorer", "body-binding-skipped",
        "converter-not-described", "closed-set-differs", "closed-set-vs-own-wrapped-converter", "stored-factory-skips-validation",
        "maxvalidationdepth1", "route-text-unsafe", "doc:F2", "wrapped-round-trip-lossy-too", "base64", "own-spelling",
        "reads-its-own-spelling", "closed-set-key-unpoliced", "nullability-not-comparable",
    ];
    public static string? Of(string note)
    {
        if (note.StartsWith("substituted:")) return null;
        if (note.StartsWith("both-threw-differently")) return "inconclusive";
        if (Refusal.Any(note.Contains)) return "expected-refusal";
        if (Inapplicable.Any(note.Contains) || note == "own-vo-converter") return "inapplicable";
        return null;
    }
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
    /// <summary>xunit MemberData: (claim, index). Optional filter by env PROBE_ROWS="0-20" for quick runs.</summary>
    public static TheoryData<string, int> Data(string claim)
    {
        var d = new TheoryData<string, int>();
        var range = Environment.GetEnvironmentVariable("PROBE_ROWS");
        (int lo, int hi) = range is null ? (0, int.MaxValue) : (int.Parse(range.Split('-')[0]), int.Parse(range.Split('-')[1]));
        var only = Environment.GetEnvironmentVariable("PROBE_ONLY"); // "axis=value" keeps rows with that value
        foreach (var r in Load(claim)) if (r.Index >= lo && r.Index <= hi && (only is null || only.Split(',').All(c => c.Contains("!=") ? !r.Is(c.Split("!=")[0], c.Split("!=")[1]) : r.Is(c.Split('=')[0], c.Split('=')[1])))) d.Add(claim, r.Index);
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
        lock (Gate) File.AppendAllText(Path.Combine(dir, claim + ".jsonl"),
            JsonSerializer.Serialize(new { idx, status = category == "pass" ? "pass" : "fail", category, completed = row is not null,
                                           applied, reasons, note = note.Length > 600 ? note[..600] : note }) + "\n");
    }
    /// <summary>
    /// Runs the check on a fresh copy of the row (its own evidence); null = no failure, otherwise the failure text.
    /// An exception the check did not expect is a harness error until triaged, never a pass.
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
        Assert.True(fail is null, $"[{claim} #{idx}] {row}\n{fail}");
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
