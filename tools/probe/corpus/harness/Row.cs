// Row plumbing shared by every probe: each probe's rows are its own file, written by `prober rows --probe P` (the corpus's
// rows/P.json.gz, unpacked by the replay to Probes/rows/P.json); each probe records one line per row to $PROBE_OUT/<label>.jsonl
// (idx, category, applied settings, reasons, findings, note), which the replay reads.
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
    public void Substituted(string axis, string actual, string reason) { lock (Ev) Ev.Applied[axis] = actual; Note(NoteKeys.Substituted, $"{axis}={actual}, {reason}"); }
    public void Outcome(string category, string reason) { lock (Ev) Ev.Raise(category, reason); }
    /// <summary>The settings that ran: the axes the harness read, at the value that applied.</summary>
    public Dictionary<string, string> AppliedSettings()
    {
        lock (Ev) return Ev.Read.Where(d.ContainsKey).ToDictionary(a => a, a => Ev.Applied.TryGetValue(a, out var v) ? v : d[a]);
    }
    public string Notes { get; private set; } = "";
    /// <summary>
    /// Records what a check saw, by a key of <see cref="NoteKeys"/>; the key alone decides the outcome, the
    /// detail only explains it.
    /// </summary>
    public void Note(NoteKeys key, string? detail = null)
    {
        var n = detail is null ? key.Key : $"{key.Key}({detail})";
        if (!Notes.Split(';').Contains(n)) Notes += (Notes.Length == 0 ? "" : ";") + n;
        if (key.Outcome != "pass") Outcome(key.Outcome, n);
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
/// Every note a check may make, by exact key, and the outcome it stands for: the catalogue, and each of its
/// members one key. A check writes <c>r.Note(NoteKeys.Compared, detail)</c>; the constructor is private, so a
/// key outside the catalogue, a misspelled one or one built from text does not compile (decision 103). A row
/// whose checks returned no failure is a pass only if none of its notes says a check was skipped
/// (inapplicable), ended in a refusal (expected refusal) or could not tell (inconclusive): a refusal is
/// evidence about refusing, not about serializing or binding successfully. "pass" keys only explain how a
/// check that ran was compared.
/// </summary>
public sealed class NoteKeys
{
    const string Pass = "pass", Inapplicable = "inapplicable", ExpectedRefusal = "expected-refusal", Inconclusive = "inconclusive";
    /// <summary>The key as a row's notes and reasons spell it.</summary>
    public string Key { get; }
    /// <summary>The outcome a note with this key stands for; a note never makes a violation or a harness error.</summary>
    public string Outcome { get; }
    NoteKeys(string key, string outcome) { Key = key; Outcome = outcome; }
    public override string ToString() => Key;

    // how a check that ran was compared
    public static readonly NoteKeys Compared = new("compared", Pass);
    public static readonly NoteKeys Substituted = new("substituted", Pass);
    public static readonly NoteKeys Dropped = new("dropped", Pass);
    public static readonly NoteKeys CtxStrict = new("ctx-strict", Pass);
    public static readonly NoteKeys CtxFallback = new("ctx-fallback", Pass);
    public static readonly NoteKeys BindingNullable = new("binding:nullable", Pass);
    public static readonly NoteKeys OwnVo = new("own-vo", Pass);
    public static readonly NoteKeys ConverterWrapsInner = new("converter-wraps-inner", Pass);
    public static readonly NoteKeys DocumentedCharGuidKeyNeverGeneratedChecked = new("documented-char-guid-key-never-generated-checked", Pass);
    public static readonly NoteKeys DocumentedCharGuidNotGeneratedChecked = new("documented-char-guid-not-generated-checked", Pass);
    public static readonly NoteKeys SchemaComparedWithNullableWrapped = new("schema:compared-with-nullable-wrapped", Pass);
    public static readonly NoteKeys SchemaNullableSpellingsNormalised = new("schema:nullable-spellings-normalised", Pass);
    public static readonly NoteKeys SchemaClassVoNullabilityNormalised = new("schema:class-vo-nullability-normalised", Pass);
    public static readonly NoteKeys SchemaNeitherSideDocumented = new("schema:neither-side-documented", Pass);
    // a nullable value object that is a component is null around its reference, the wrapped type null inside its schema (decision 36)
    public static readonly NoteKeys SchemaNullAroundReferenceNormalised = new("schema:null-around-reference-normalised", Pass);
    // EF: the claim's check ran, against the documented expectation where the wrapped type's own side cannot answer
    public static readonly NoteKeys RefusalNamesTheType = new("refusal-names-the-type", Pass);
    public static readonly NoteKeys StoredFactoryReadUnvalidated = new("stored-factory-read-unvalidated", Pass);
    public static readonly NoteKeys DocumentedCharGuidDefaultGenerationNormalized = new("documented-char-guid-default-generation-normalized", Pass);
    public static readonly NoteKeys PlainKeyThrowsOnSqliteVoCheckedAgainstExpected = new("plain-key-throws-on-sqlite-vo-checked-against-expected", Pass);
    public static readonly NoteKeys TpcSqliteNoSequenceAssignChecked = new("tpc-sqlite-no-sequence-assign-checked", Pass);
    // the document part has nothing to check: ASP.NET alone refuses the same document in the same row, or
    // leaves the controller out of it (docs/aspnet-core.md, "Bind value objects in MVC"), checked absent
    public static readonly NoteKeys DocRefusedByAspnetAlone = new("doc:refused-by-aspnet-alone", Pass);
    public static readonly NoteKeys DocControllerNotInApiExplorer = new("doc:controller-not-in-api-explorer", Pass);
    // L03's JSON half has nothing to compare: the wrapped type alone refuses under the row's options, the value object alike, in the same row
    public static readonly NoteKeys JsonHalfWrappedAloneRefuses = new("json-half:wrapped-alone-refuses", Pass);
    // JSON family (C01, C10, L06): the options leave the member out of one direction for the plain type without TypeKit too, shown by a
    // baseline in the same row; that direction has nothing to check, and the row is judged by the other one (both left out: inapplicable)
    public static readonly NoteKeys JsonMemberLeftOutByOptions = new("json:member-left-out-by-options", Pass);
    public static readonly NoteKeys JsonBothDirectionsLeftOutByOptions = new("json:both-directions-left-out-by-options", Inapplicable);
    // a read the wrapped type's shape refuses with the very same exception and message: the serializer refused the document before any
    // value object ran (the baseline in the same row), so the read has nothing to check; the row is judged by its write and other reads
    public static readonly NoteKeys JsonReadRefusedByStjAlone = new("json:read-refused-by-stj-alone", Pass);
    // ASP.NET's OpenAPI 2.0 writer drops the reference of a nullable struct component; a plain struct beside it in the same
    // schema, without a converter, is written the same, so that position has nothing to check (C12)
    public static readonly NoteKeys SchemaNullableReferenceLostForPlainToo = new("schema:nullable-reference-lost-for-plain-too", Pass);
    // a check that did not apply
    public static readonly NoteKeys OwnVoConverter = new("own-vo-converter", Inapplicable);
    public static readonly NoteKeys ClosedSetVsOwnWrappedConverter = new("closed-set-vs-own-wrapped-converter", Inapplicable);
    public static readonly NoteKeys ClosedSetDiffers = new("closed-set-differs", Inapplicable);
    public static readonly NoteKeys ByteArrayIsBase64 = new("byte-array-is-base64", Inapplicable);
    public static readonly NoteKeys OwnSpelling = new("own-spelling", Inapplicable);
    public static readonly NoteKeys NotBindableFromText = new("not-bindable-from-text", Inapplicable);
    public static readonly NoteKeys RouteTextUnsafe = new("route-text-unsafe", Inapplicable);
    public static readonly NoteKeys OwnVoBodyBindingSkipped = new("own-vo:body-binding-skipped", Inapplicable);
    public static readonly NoteKeys MvcMaxvalidationdepth1IgnoresBindingErrors = new("mvc-maxvalidationdepth1-ignores-binding-errors", Inapplicable);
    public static readonly NoteKeys StoredFactorySkipsValidation = new("stored-factory-skips-validation", Inapplicable);
    public static readonly NoteKeys SchemaConverterNotDescribed = new("schema:converter-not-described", Inapplicable);
    public static readonly NoteKeys SchemaNullabilityNotComparable = new("schema:nullability-not-comparable", Inapplicable);
    // C21's not-promised entries, made by ASP.NET's OpenAPI 2.0 writer: a nullable property of a component loses its reference (KB-026),
    // a nullable body of a value type keeps only its null (KB-001)
    public static readonly NoteKeys SchemaOpenApi2NullableComponentLosesType = new("schema:openapi2-nullable-component-loses-type", Inapplicable);
    public static readonly NoteKeys SchemaOpenApi2NullableBodyKeepsOnlyNull = new("schema:openapi2-nullable-body-keeps-only-null", Inapplicable);
    // C21's not-promised enum-component-is-its-first-use, made by ASP.NET: an enum's component is the schema of its first use (KB-027)
    public static readonly NoteKeys SchemaEnumComponentIsItsFirstUse = new("schema:enum-component-is-its-first-use", Inapplicable);
    public static readonly NoteKeys ClosedSetKeyUnpoliced = new("closed-set-key-unpoliced", Inapplicable);
    public static readonly NoteKeys PlainRefused = new("plain-refused", Inapplicable);
    public static readonly NoteKeys EntryNotApplicable = new("entry-not-applicable", Inapplicable);
    public static readonly NoteKeys NoText = new("no-text", Inapplicable);
    public static readonly NoteKeys TextWrappedRoundTripLossyToo = new("text:wrapped-round-trip-lossy-too", Inapplicable);
    public static readonly NoteKeys NoOperator = new("no-operator", Inapplicable);
    public static readonly NoteKeys RefusedInputNotPlaceable = new("refused-input-not-placeable", Inapplicable);
    // a refusal, intended or the framework's own
    public static readonly NoteKeys DocumentedDiff = new("documented-diff", ExpectedRefusal);
    public static readonly NoteKeys StjMaxdepth = new("stj-maxdepth", ExpectedRefusal);
    public static readonly NoteKeys ValidatedRefusesTypeLimit = new("validated-refuses-type-limit", ExpectedRefusal);
    public static readonly NoteKeys ClassVoTakesNull = new("class-vo-takes-null", ExpectedRefusal);
    public static readonly NoteKeys BindPlainUnbindable = new("bind:plain-unbindable", ExpectedRefusal);
    public static readonly NoteKeys PreserveDocumented = new("preserve-documented", ExpectedRefusal);
    public static readonly NoteKeys Mvc400UnwritableUnderSourceGen = new("mvc-400-unwritable-under-source-gen", ExpectedRefusal);
    public static readonly NoteKeys OwnVoReadBackRefused = new("own-vo:read-back-refused", ExpectedRefusal);
    public static readonly NoteKeys BothThrew = new("both-threw", ExpectedRefusal);
    public static readonly NoteKeys ReadBothThrew = new("read-both-threw", ExpectedRefusal);
    public static readonly NoteKeys SerBothThrew = new("ser-both-threw", ExpectedRefusal);
    public static readonly NoteKeys SerRefused = new("ser-refused", ExpectedRefusal);
    public static readonly NoteKeys ReadBackRefused = new("read-back-refused", ExpectedRefusal);
    public static readonly NoteKeys PlainReadBackFailsToo = new("plain-read-back-fails-too", ExpectedRefusal);
    public static readonly NoteKeys OwnVoConverterThrew = new("own-vo-converter-threw", ExpectedRefusal);
    public static readonly NoteKeys OwnConverterRefusedForWrappedToo = new("own-converter-refused-for-wrapped-too", ExpectedRefusal);
    // MVC's validator throws for a nested struct of a body (MaxValidationDepth) for a plain twin on the same side too (C13)
    public static readonly NoteKeys MvcPlainNestedBodyThrowsToo = new("mvc:plain-nested-body-throws-too", ExpectedRefusal);
    // both sides refused, but not alike
    public static readonly NoteKeys BothThrewDifferently = new("both-threw-differently", Inconclusive);
}

public static class Rows
{
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CodoMetis.TypeKit.Samples.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
    /// <summary>The rows of one probe, from its own file (rows/<paramref name="probe"/>.json); a sibling probe of the claim reads its own.</summary>
    public static IEnumerable<Row> Load(string probe)
    {
        var path = Path.Combine(Root(), "Probes", "rows", probe + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException($"no rows file for the probe {probe}: {path}", path);
        var doc = JsonNode.Parse(File.ReadAllText(path))!;
        var i = 0;
        foreach (var r in doc["rows"]!.AsArray())
            yield return new Row(i++, r!["row"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>()));
    }
    /// <summary>
    /// xunit MemberData: (probe, index), the probe naming its rows file, labelled with the row's settings so a test list or a
    /// failure names them. Optional filter by env PROBE_ROWS="0-20" for quick runs.
    /// </summary>
    public static TheoryData<string, int> Data(string probe)
    {
        var d = new TheoryData<string, int>();
        var range = Environment.GetEnvironmentVariable("PROBE_ROWS");
        (int lo, int hi) = range is null ? (0, int.MaxValue) : (int.Parse(range.Split('-')[0]), int.Parse(range.Split('-')[1]));
        var only = Environment.GetEnvironmentVariable("PROBE_ONLY"); // "axis=value" keeps rows with that value
        foreach (var r in Load(probe))
            if (r.Index >= lo && r.Index <= hi && (only is null || only.Split(',').All(c => c.Contains("!=") ? !r.Is(c.Split("!=")[0], c.Split("!=")[1]) : r.Is(c.Split('=')[0], c.Split('=')[1]))))
                d.Add(new TheoryDataRow<string, int>(probe, r.Index) { Label = $"#{r.Index} {r}" });
        return d;
    }
    static readonly Dictionary<string, List<Row>> Cache = new();
    public static Row Get(string probe, int index)
    {
        lock (Cache) { if (!Cache.TryGetValue(probe, out var l)) Cache[probe] = l = Load(probe).ToList(); return l[index]; }
    }
}

public static class Rec
{
    static readonly object Gate = new();
    /// <summary>One line per row to $PROBE_OUT/label.jsonl: how it ended and the settings that ran.</summary>
    public static void Write(string label, int idx, string category, string note, Row? row = null)
    {
        var dir = Environment.GetEnvironmentVariable("PROBE_OUT") ?? "/tmp/probe-out";
        Directory.CreateDirectory(dir);
        var applied = row?.AppliedSettings() ?? new Dictionary<string, string>();
        var reasons = row is null ? [] : row.Ev.Reasons.ToArray();
        var findings = row is null ? [] : row.Ev.Findings.ToArray();
        lock (Gate) File.AppendAllText(Path.Combine(dir, label + ".jsonl"),
            JsonSerializer.Serialize(new { idx, status = category == "pass" ? "pass" : "fail", category, completed = row is not null,
                                           applied, reasons, findings, note = note.Length > 600 ? note[..600] : note }) + "\n");
    }
    /// <summary>
    /// Runs the check on a fresh copy of the row (its own evidence); null = no failure, otherwise the failure text.
    /// An exception the check did not expect is a harness error until triaged, never a pass and never a
    /// violation: the row fails the test with the exception named, and no seed counts it caught. A violation or a
    /// harness error fails the test; any other outcome but a pass skips it with its reasons, so a test run
    /// counts what earned nothing. <paramref name="label"/> names the row in a failure and its evidence file, and is the probe
    /// whose rows file holds the row unless <paramref name="rowsOf"/> names it: a probe passes its theory's first argument,
    /// the probe's own name, as one or the other.
    /// </summary>
    public static Task Run(string label, int idx, Func<Row, Task<string?>> check, string? rowsOf = null) => Run(label, Rows.Get(rowsOf ?? label, idx), check);

    /// <summary>The same for a row in hand, without a rows file: how the prober's tests run the harness.</summary>
    public static async Task Run(string label, Row cached, Func<Row, Task<string?>> check)
    {
        var idx = cached.Index;
        var row = new Row(cached.Index, cached.All.ToDictionary(kv => kv.Key, kv => kv.Value));
        string? fail = null, unexpected = null;
        try { fail = await check(row); }
        catch (Exception ex) { unexpected = "UNEXPECTED " + ex.GetType().Name + ": " + ex.Message; row.Outcome("harness-error", unexpected); }
        // An exception is the harness breaking, not the claim's check firing: a harness error and never a
        // violation, so a seeded defect that only makes a check throw is no catch (decision 103).
        if (fail is not null) row.Outcome("violation", fail);
        Write(label, idx, row.Ev.Category, fail ?? unexpected ?? row.Notes, row);
        if (fail is not null) Assert.Fail($"[{label} #{idx}] {row}\n{fail}");
        if (row.Ev.Category == "harness-error") Assert.Fail($"[{label} #{idx}] {row}\nharness error: {string.Join("; ", row.Ev.Reasons)}");
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
