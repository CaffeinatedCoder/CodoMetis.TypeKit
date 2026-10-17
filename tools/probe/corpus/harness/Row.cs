// Row plumbing shared by every probe: each probe's rows are its own file, written by `prober rows --probe P` (the corpus's
// rows/P.json.gz, unpacked by the replay to Probes/rows/P.json); each probe records one line per row to $PROBE_OUT/<label>.jsonl
// (idx, category, applied settings, reasons, findings, note, and the checks the row ran), which the replay reads.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Probes.Shapes;

/// <summary>
/// What a row actually did, shared by the row and every copy made of it (With, Fresh): the axes the harness
/// read, the settings it substituted, how the row ended, the open findings it stepped around, and the checks it
/// ran (<see cref="Row.Check(string, Baseline?, Func{Check, string?})"/>), each with evidence of its own. The ledger
/// credits only the axes read, at the value that applied, and only for a check that ended as a pass.
/// </summary>
public sealed class Evidence
{
    public readonly HashSet<string> Read = new();
    public readonly Dictionary<string, string> Applied = new();
    public readonly SortedSet<string> Findings = new(StringComparer.Ordinal);
    /// <summary>How the row ended: the highest of its own outcomes and its checks'.</summary>
    public string Category = "pass";
    /// <summary>How the row ended outside any check: its own notes, a failure it returned, an exception it threw.</summary>
    public string Own = "pass";
    public readonly List<string> Reasons = new();
    /// <summary>The checks the row ran, in the order they ended.</summary>
    public readonly List<Check> Checks = new();
    static readonly string[] Order = ["pass", "inapplicable", "expected-refusal", "inconclusive", "known-finding", "harness-error", "violation"];
    public static int Rank(string category) =>
        Array.IndexOf(Order, category) is var i and >= 0 ? i : throw new ArgumentException("no outcome category " + category, nameof(category));
    public void Raise(string category, string reason)
    {
        if (Rank(category) > Rank(Own)) Own = category;
        Lift(category);
        Reasons.Add(category + ":" + reason);
    }
    /// <summary>Raises the row's category to a check's, without a reason of the row's own: the check keeps its reasons.</summary>
    public void Lift(string category)
    {
        if (Rank(category) > Rank(Category)) Category = category;
    }
}

public sealed class Row
{
    readonly Dictionary<string, string> d;
    public Evidence Ev { get; }
    public int Index { get; }
    /// <summary>The claim whose rows file holds the row, whose declarations <see cref="Check(string, Baseline?, Func{Check, string?})"/> applies; null for a row in hand.</summary>
    public string? Claim { get; }
    public Row(int index, Dictionary<string, string> d, string? claim = null) : this(index, d, new Evidence(), claim) { }
    Row(int index, Dictionary<string, string> d, Evidence ev, string? claim) { Index = index; this.d = d; Ev = ev; Claim = claim; }
    public string this[string axis] => d.TryGetValue(axis, out var v) ? Mark(axis, v) : throw new KeyNotFoundException($"row has no axis {axis}");
    public string? Get(string axis) => d.TryGetValue(axis, out var v) ? Mark(axis, v) : null;
    public bool Is(string axis, string value) => d.TryGetValue(axis, out var v) && Mark(axis, v) == value;
    /// <summary>Every axis, each marked read: a caller that enumerates is applying them (the JSON options builder).</summary>
    public IEnumerable<KeyValuePair<string, string>> All { get { foreach (var kv in d) { Mark(kv.Key, kv.Value); yield return kv; } } }
    /// <summary>
    /// Marks an axis read: for the check of this row that is running, if one is (a helper handed the row inside a
    /// check's body reads for that check), else for the row, whose reads every check of it ran under.
    /// </summary>
    string Mark(string axis, string v)
    {
        if (Scope.Running(Ev) is { } c) c.MarkRead(axis);
        else lock (Ev) Ev.Read.Add(axis);
        return v;
    }
    /// <summary>The value the row holds for an axis, without marking it read: a check marks its own.</summary>
    internal string? Raw(string axis) => d.TryGetValue(axis, out var v) ? v : null;
    /// <summary>Every axis and its value, none marked read: what a fact's fixture is matched against (Sides.Settings), whose covering answer then reads its own.</summary>
    internal IReadOnlyDictionary<string, string> Values => d;
    /// <summary>The setting ran as <paramref name="actual"/>, not as the row asked: only the actual value earns credit.</summary>
    public void Substituted(string axis, string actual, string reason)
    {
        if (Scope.Running(Ev) is { } c) { c.Substituted(axis, actual, reason); return; }
        lock (Ev) Ev.Applied[axis] = actual;
        Note(NoteKeys.Substituted, $"{axis}={actual}, {reason}");
    }
    public void Outcome(string category, string reason)
    {
        if (Scope.Running(Ev) is { } c) c.Outcome(category, reason);
        else lock (Ev) Ev.Raise(category, reason);
    }
    /// <summary>The settings that ran: the axes the harness read outside any check, at the value that applied.</summary>
    public Dictionary<string, string> AppliedSettings()
    {
        lock (Ev) return Ev.Read.Where(d.ContainsKey).ToDictionary(a => a, a => Ev.Applied.TryGetValue(a, out var v) ? v : d[a]);
    }
    public string Notes { get; private set; } = "";
    /// <summary>
    /// Records what a check saw, by a key of <see cref="NoteKeys"/>; the key alone decides the outcome, the
    /// detail only explains it. Inside a check's body the note is the check's.
    /// </summary>
    public void Note(NoteKeys key, string? detail = null)
    {
        if (Scope.Running(Ev) is { } c) { c.Noted(key, detail); return; }
        var n = detail is null ? key.Key : $"{key.Key}({detail})";
        if (!Notes.Split(';').Contains(n)) Notes += (Notes.Length == 0 ? "" : ";") + n;
        if (key.Outcome != "pass") Outcome(key.Outcome, n);
    }
    /// <summary>
    /// Steps around an open finding: the row (or, inside a check's body, the check) ends as known-finding and earns
    /// nothing. The replay fails a probe that skips for a finding the ledger has fixed or dismissed, and the release
    /// gate refuses while any skips.
    /// </summary>
    public void Skip(string finding, string why)
    {
        lock (Ev) Ev.Findings.Add(finding);
        Outcome("known-finding", $"{finding}: {why}");
    }
    public System.Text.StringBuilder Log { get; } = new();
    public Row Fresh() => new Row(Index, new Dictionary<string, string>(d), Ev, Claim);
    public Row With(string axis, string value) { var c = new Dictionary<string, string>(d) { [axis] = value }; return new Row(Index, c, Ev, Claim); }
    public override string ToString() => string.Join(" ", d.Where(kv => kv.Value != "default").OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>The check running in this flow, so a Row whose method is named Check reaches the type's flow by another name.</summary>
    static class Scope
    {
        public static Check? Running(Evidence ev) => Probes.Shapes.Check.Running(ev);
        public static Check? Enter(Check c) => Probes.Shapes.Check.Enter(c);
        public static void Leave(Check? outer) => Probes.Shapes.Check.Leave(outer);
    }

    /// <summary>
    /// Runs one check of the row: one obligation of the claim, compared against one <paramref name="baseline"/>, with
    /// reads, substitutions, notes and an outcome of its own. A failure the body returns is a violation; an exception
    /// is a harness error, recorded and not rethrown, so the row's other checks still run (and null is returned, so
    /// no exception ever reads as a violation); otherwise the highest of its notes, else a pass, and a pass with no
    /// baseline is the harness error <c>pass-without-baseline</c>: a pass is a comparison that ran. The row's category
    /// is raised to the check's. Returns the failure, so a probe may stop the row at its first violation or run every
    /// check. A body that only returns null or throws names its form, <c>_ => (string?)null</c>, or the two overloads are ambiguous.
    /// Before the body, the claim's declarations of where the obligation does not apply are read (claims.json, staged
    /// beside the rows): where one matches the row's values, the check is inapplicable with the declaration's citation
    /// and the body does not run.
    /// </summary>
    public async Task<string?> Check(string obligation, Baseline? baseline, Func<Check, Task<string?>> body)
    {
        var c = new Check(this, obligation, baseline);
        var outer = Scope.Enter(c);
        string? fail = null; Exception? thrown = null;
        try { if (!Declared(c)) fail = await body(c); }
        catch (Exception ex) { thrown = ex; }
        finally { Scope.Leave(outer); }
        return Close(c, fail, thrown);
    }

    /// <summary>The synchronous form of <see cref="Check(string, Baseline?, Func{Check, Task{string?}})"/>.</summary>
    public string? Check(string obligation, Baseline? baseline, Func<Check, string?> body)
    {
        var c = new Check(this, obligation, baseline);
        var outer = Scope.Enter(c);
        string? fail = null; Exception? thrown = null;
        try { if (!Declared(c)) fail = body(c); }
        catch (Exception ex) { thrown = ex; }
        finally { Scope.Leave(outer); }
        return Close(c, fail, thrown);
    }

    /// <summary>
    /// Applies the first declaration of the check's obligation whose every axis the row holds at a value it lists: the
    /// check is inapplicable with the declaration's citation, those axes read for it. True when one matched.
    /// </summary>
    bool Declared(Check c)
    {
        foreach (var (axes, cite) in Rows.Declarations(Claim, c.Obligation))
            if (axes.All(kv => d.TryGetValue(kv.Key, out var v) && kv.Value.Contains(v)))
            {
                c.Declared(cite, string.Join(" ", axes.Keys.Order(StringComparer.Ordinal).Select(a => $"{a}={c[a]}")));
                return true;
            }
        return false;
    }

    string? Close(Check c, string? fail, Exception? thrown)
    {
        if (thrown is not null) c.Outcome("harness-error", "UNEXPECTED " + thrown.GetType().Name + ": " + thrown.Message);
        else if (fail is not null) { c.Failure = fail; c.Outcome("violation", fail); }
        if (c.Category == "pass" && c.Against is null) c.Outcome("harness-error", "pass-without-baseline");
        lock (Ev) { Ev.Checks.Add(c); Ev.Lift(c.Category); }
        return thrown is null ? fail : null;
    }
}

/// <summary>
/// One check of a row (<see cref="Row.Check(string, Baseline?, Func{Check, string?})"/>): the obligation it checks, the
/// baseline it compares against, the axes it read at the value that applied, its notes and how it ended. Its evidence
/// is its own; the row's reads, made outside any check, are what every check of the row ran under.
/// </summary>
public sealed class Check
{
    static readonly AsyncLocal<Check?> Current = new();
    static readonly Regex Id = new("^[a-z0-9][a-z0-9-]*$");
    readonly Row row;
    internal Check(Row row, string obligation, Baseline? against)
    {
        this.row = row; Obligation = obligation; Against = against;
        // An obligation is an id of the claim's obligations (claims.json), or "claim"; anything else is the harness's mistake.
        if (!Id.IsMatch(obligation ?? "")) Outcome("harness-error", $"obligation-not-an-id({obligation})");
    }
    /// <summary>The claim's obligation this check checks, as claims.json names it, or <c>claim</c> for a claim without obligations.</summary>
    public string Obligation { get; }
    /// <summary>What the check compared against; null where it compared nothing, which no pass may be.</summary>
    public Baseline? Against { get; }
    /// <summary>The evidence of this check alone: its reads, substitutions, reasons and category.</summary>
    public Evidence Ev { get; } = new();
    public string Category { get { lock (Ev) return Ev.Category; } }
    /// <summary>The failure the body returned, if it returned one.</summary>
    public string? Failure { get; internal set; }
    public string Notes { get; private set; } = "";

    internal static Check? Running(Evidence rowEvidence) => Current.Value is { } c && ReferenceEquals(c.row.Ev, rowEvidence) ? c : null;
    internal static Check? Enter(Check c) { var outer = Current.Value; Current.Value = c; return outer; }
    internal static void Leave(Check? outer) => Current.Value = outer;

    public string this[string axis] => row.Raw(axis) is { } v ? Mark(axis, v) : throw new KeyNotFoundException($"row has no axis {axis}");
    public string? Get(string axis) => row.Raw(axis) is { } v ? Mark(axis, v) : null;
    public bool Is(string axis, string value) => row.Raw(axis) is { } v && Mark(axis, v) == value;
    string Mark(string axis, string v) { MarkRead(axis); return v; }
    internal void MarkRead(string axis) { lock (Ev) Ev.Read.Add(axis); }
    /// <summary>The setting ran as <paramref name="actual"/> for this check, not as the row asked: only the actual value earns credit.</summary>
    public void Substituted(string axis, string actual, string reason)
    {
        lock (Ev) Ev.Applied[axis] = actual;
        Note(NoteKeys.Substituted, $"{axis}={actual}, {reason}");
    }
    /// <summary>How the check was compared, by a key of <see cref="NoteKeys"/>; a key of another outcome raises the check's.</summary>
    public void Note(NoteKeys key, string? detail = null) => Noted(key, detail);
    /// <summary>A note the row routes here from inside the check's body; its key is a member, written out where the row's note was made.</summary>
    internal void Noted(NoteKeys key, string? detail)
    {
        var n = detail is null ? key.Key : $"{key.Key}({detail})";
        Remember(n);
        if (key.Outcome != "pass") Outcome(key.Outcome, n);
    }
    /// <summary>The check does not apply to this row, for the reason <paramref name="cite"/> names; returns null, no failure.</summary>
    public string? Inapplicable(Cite cite, string? detail = null) => Cited("inapplicable", cite, detail);
    /// <summary>The check met the refusal <paramref name="cite"/> says is intended; returns null, no failure.</summary>
    public string? Refused(Cite cite, string? detail = null) => Cited("expected-refusal", cite, detail);
    /// <summary>The check does not apply where its claim declares so (claims.json): the declaration's cite, read from the file, not a literal.</summary>
    internal void Declared(Cite cite, string settings) => Cited("inapplicable", cite, "declared " + settings);
    /// <summary>The check could not tell; returns null, no failure.</summary>
    public string? Inconclusive(string reason) { Outcome("inconclusive", reason); return null; }
    public System.Text.StringBuilder Log => row.Log;

    string? Cited(string category, Cite cite, string? detail)
    {
        var n = detail is null ? cite.Spelling : $"{cite.Spelling}({detail})";
        Remember(n);
        Outcome(category, n);
        return null;
    }
    void Remember(string n) { lock (Ev) if (!Notes.Split(';').Contains(n)) Notes += (Notes.Length == 0 ? "" : ";") + n; }
    internal void Outcome(string category, string reason) { lock (Ev) Ev.Raise(category, reason); }

    /// <summary>The axes this check read, at the value that applied: its own substitution, else the row's, else the row's value.</summary>
    public Dictionary<string, string> AppliedSettings()
    {
        Dictionary<string, string> rowApplied;
        lock (row.Ev) rowApplied = new(row.Ev.Applied);
        lock (Ev)
            return Ev.Read.Where(a => row.Raw(a) is not null)
                     .ToDictionary(a => a, a => Ev.Applied.TryGetValue(a, out var v) ? v : rowApplied.TryGetValue(a, out var w) ? w : row.Raw(a)!);
    }
}

/// <summary>
/// What a check compared the value object against: a closed catalogue, as <see cref="NoteKeys"/> is, each member
/// spelled as the evidence line carries it. There is no text baseline: a check that matched a message names the
/// refusal, the fact or the sentence of the docs it held the message to, or it compared nothing.
/// </summary>
public sealed class Baseline
{
    /// <summary>The baseline as a check's evidence spells it.</summary>
    public string Spelling { get; }
    Baseline(string spelling) { Spelling = spelling; }
    public override string ToString() => Spelling;

    /// <summary>The wrapped twin in the same place, with TypeKit.</summary>
    public static readonly Baseline Twin = new("twin");
    /// <summary>The wrapped twin without TypeKit.</summary>
    public static readonly Baseline Framework = new("framework");
    /// <summary>The input itself, read back.</summary>
    public static readonly Baseline RoundTrip = new("round-trip");
    /// <summary>An in-memory model of the same operations.</summary>
    public static readonly Baseline Model = new("model");
    /// <summary>The documented refusal: its type, the name it gives, no inner exception, no quoted input.</summary>
    public static readonly Baseline Refusal = new("refusal");
    /// <summary>The wrapped type's path in the same process.</summary>
    public static readonly Baseline Cost = new("cost");
    /// <summary>A pinned fact's fixture, by its id under design/knowledge/ (<c>KB-031</c>).</summary>
    public static Baseline Fact(string id) => new("fact:" + Cite.FactForm(id));
    /// <summary>A sentence of the docs, by <c>file#anchor</c>, where the wrapped type's side cannot answer.</summary>
    public static Baseline Docs(string source) => new("docs:" + Cite.DocsForm(source));
}

/// <summary>
/// Why a check did not apply or met an intended refusal: a claim's not-promised entry, a section of the docs, a
/// pinned fact or a decision record. Each factory checks the form of what it is given; that it exists is held by
/// `prober register` and `publish` and by the conventions tests, which also require a string literal.
/// </summary>
public sealed class Cite
{
    static readonly Regex Id = new("^[a-z0-9][a-z0-9-]*$");
    static readonly Regex Source = new(@"^(?!/)(?!.*(^|/)\.\.?(/|#|$))[A-Za-z0-9_./-]+\.[A-Za-z0-9]+(#[A-Za-z0-9][A-Za-z0-9._-]*)?$");
    static readonly Regex FactId = new(@"^KB-\d{3}$");
    static readonly Regex RecordId = new(@"^\d{4}$");
    /// <summary>The citation as a reason spells it: <c>not-promised:id</c>, <c>docs:file#anchor</c>, <c>fact:KB-031</c>, <c>record:0094</c>.</summary>
    public string Spelling { get; }
    Cite(string spelling) { Spelling = spelling; }
    public override string ToString() => Spelling;

    /// <summary>An id of the claim's <c>not-promised</c> list.</summary>
    public static Cite NotPromised(string id) => new("not-promised:" + Form(id, Id, "a not-promised id", "closed-set-has-no-text"));
    /// <summary>A section of the docs, as a not-promised entry's <c>source</c> spells it: <c>file#anchor</c>, the file from the repository root.</summary>
    public static Cite Docs(string source) => new("docs:" + DocsForm(source));
    /// <summary>A knowledge entry under design/knowledge/.</summary>
    public static Cite Fact(string id) => new("fact:" + FactForm(id));
    /// <summary>A decision record under design/adr/.</summary>
    public static Cite Record(string id) => new("record:" + Form(id, RecordId, "a decision record's number", "0094"));

    /// <summary>A citation as claims.json spells it (<c>not-promised:id</c>, <c>docs:file#anchor</c>, <c>fact:KB-031</c>, <c>record:0094</c>), by its factory.</summary>
    internal static Cite Parse(string spelling)
    {
        var at = spelling.IndexOf(':');
        var value = at < 0 ? "" : spelling[(at + 1)..];
        return (at < 0 ? "" : spelling[..at]) switch
        {
            "not-promised" => NotPromised(value), "docs" => Docs(value), "fact" => Fact(value), "record" => Record(value),
            _ => throw new ArgumentException($"a citation is not-promised:id, docs:file#anchor, fact:KB-NNN or record:NNNN, not '{spelling}'", nameof(spelling)),
        };
    }

    internal static string DocsForm(string source) => Form(source, Source, "a docs source", "docs/json.md#decimals-in-one-spelling");
    internal static string FactForm(string id) => Form(id, FactId, "a knowledge entry's id", "KB-031");
    static string Form(string value, Regex form, string what, string example) =>
        value is not null && form.IsMatch(value) ? value : throw new ArgumentException($"{what} is spelled like {example}, not '{value}'", nameof(value));
}

/// <summary>
/// Every note a check may make, by exact key, and the outcome it stands for: the catalogue, and each of its
/// members one key. A check writes <c>r.Note(NoteKeys.Compared, detail)</c>; the constructor is private, so a
/// key outside the catalogue, a misspelled one or one built from text does not compile (decision 103). "pass"
/// keys only explain how a check that ran was compared. A check that did not apply, ended in a refusal or could
/// not tell says so through its scope, citing what makes it so (<c>c.Inapplicable(Cite…)</c>, <c>c.Refused(Cite…)</c>,
/// <c>c.Inconclusive(reason)</c>); the one other key is <see cref="FrameworkRefuses"/>, the three-sided
/// differential's expected refusal, against side (1)'s baseline. The outcome keys of the notes those replaced
/// were removed once no harness or probe source called them (WS2's close).
/// </summary>
public sealed class NoteKeys
{
    const string Pass = "pass", ExpectedRefusal = "expected-refusal";
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
    public static readonly NoteKeys CtxFallback = new("ctx-fallback", Pass);   // not called since the one ProbeContext lists every shape (shapes.cs): no fallback under the context
    public static readonly NoteKeys BindingNullable = new("binding:nullable", Pass);
    public static readonly NoteKeys OwnVo = new("own-vo", Pass);
    public static readonly NoteKeys ConverterWrapsInner = new("converter-wraps-inner", Pass);
    public static readonly NoteKeys DocumentedCharGuidKeyNeverGeneratedChecked = new("documented-char-guid-key-never-generated-checked", Pass);
    public static readonly NoteKeys DocumentedCharGuidNotGeneratedChecked = new("documented-char-guid-not-generated-checked", Pass);
    public static readonly NoteKeys SchemaComparedWithNullableWrapped = new("schema:compared-with-nullable-wrapped", Pass);   // not called since the twin is at the value object's nullability (shapes.cs)
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
    public static readonly NoteKeys JsonHalfWrappedAloneRefuses = new("json-half:wrapped-alone-refuses", Pass);   // not called since the three-sided differential (Sides): framework-refuses
    // JSON family (C01, C10, L06): the options leave the member out of one direction for the plain type without TypeKit too, shown by a
    // baseline in the same row; that direction has nothing to check, and the row is judged by the other one (both left out: each direction inconclusive)
    public static readonly NoteKeys JsonMemberLeftOutByOptions = new("json:member-left-out-by-options", Pass);
    // a read the wrapped type's shape refuses with the very same exception and message: the serializer refused the document before any
    // value object ran (the baseline in the same row), so the read has nothing to check; the row is judged by its write and other reads
    public static readonly NoteKeys JsonReadRefusedByStjAlone = new("json:read-refused-by-stj-alone", Pass);   // not called since the three-sided differential (Sides): framework-refuses
    // ASP.NET's OpenAPI 2.0 writer drops the reference of a nullable struct component; a plain struct beside it in the same
    // schema, without a converter, is written the same, so that position has nothing to check (C12)
    public static readonly NoteKeys SchemaNullableReferenceLostForPlainToo = new("schema:nullable-reference-lost-for-plain-too", Pass);
    // the three-sided differential (Sides): the wrapped twin without TypeKit and the value object refuse alike, at the same operation and
    // stage with the same exception type and status, so the framework refuses; no message is compared
    public static readonly NoteKeys FrameworkRefuses = new("framework-refuses", ExpectedRefusal);
}

public static class Rows
{
    public static string Root() => TryRoot() ?? throw new DirectoryNotFoundException("no CodoMetis.TypeKit.Samples.csproj above " + AppContext.BaseDirectory);
    static string? TryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CodoMetis.TypeKit.Samples.csproj"))) dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>
    /// The claims the replay stages beside the rows (Probes/claims.json); where none is staged, as for a probe the prober's
    /// own tests run in-process, no claim declares anything. The prober's tests point it at a file of their own.
    /// </summary>
    internal static string? ClaimsFile { get; set; } = TryRoot() is { } root ? Path.Combine(root, "Probes", "claims.json") : null;
    static readonly Dictionary<string, Dictionary<string, JsonObject>> Claims = new();

    /// <summary>A claim as the staged claims.json holds it, or null where the file or the claim is not there.</summary>
    public static JsonObject? Claim(string id)
    {
        if (ClaimsFile is not { } path) return null;
        lock (Claims)
        {
            if (!Claims.TryGetValue(path, out var claims))
                Claims[path] = claims = !File.Exists(path) ? new()
                    : JsonNode.Parse(File.ReadAllText(path))!.AsArray().OfType<JsonObject>().ToDictionary(c => c["id"]!.GetValue<string>());
            return claims.GetValueOrDefault(id);
        }
    }

    /// <summary>The declarations of where <paramref name="obligation"/> of <paramref name="claim"/> does not apply: each its axis values and its citation.</summary>
    public static IEnumerable<(Dictionary<string, string[]> Axes, Cite Cite)> Declarations(string? claim, string obligation)
    {
        var o = (claim is null ? null : Claim(claim))?["obligations"]?.AsArray().OfType<JsonObject>().FirstOrDefault(x => x["id"]?.GetValue<string>() == obligation);
        foreach (var declared in (o?["inapplicable"]?.AsArray() ?? []).OfType<JsonObject>())
            yield return (declared["axes"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.AsArray().Select(v => v!.GetValue<string>()).ToArray()),
                          Cite.Parse(declared["cite"]!.GetValue<string>()));
    }
    /// <summary>The rows of one probe, from its own file (rows/<paramref name="probe"/>.json); a sibling probe of the claim reads its own.</summary>
    public static IEnumerable<Row> Load(string probe)
    {
        var path = Path.Combine(Root(), "Probes", "rows", probe + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException($"no rows file for the probe {probe}: {path}", path);
        return Read(path);
    }
    /// <summary>The rows of a rows file, each holding the file's claim, whose declarations its checks apply.</summary>
    internal static IEnumerable<Row> Read(string path)
    {
        var doc = JsonNode.Parse(File.ReadAllText(path))!;
        var claim = doc["claim"]?.GetValue<string>();
        var i = 0;
        foreach (var r in doc["rows"]!.AsArray())
            yield return new Row(i++, r!["row"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>()), claim);
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
    /// <summary>
    /// One line per row to $PROBE_OUT/label.jsonl: how it ended and the settings that ran, and for a row that ran
    /// checks, each check's obligation, baseline, outcome, settings, reasons and notes. <paramref name="passed"/> is
    /// the row's test result where its category alone does not say it (a row with a passing check and another that
    /// did not apply passes).
    /// </summary>
    public static void Write(string label, int idx, string category, string note, Row? row = null, bool? passed = null)
    {
        var dir = Environment.GetEnvironmentVariable("PROBE_OUT") ?? "/tmp/probe-out";
        Directory.CreateDirectory(dir);
        var applied = row?.AppliedSettings() ?? new Dictionary<string, string>();
        var reasons = row is null ? [] : row.Ev.Reasons.ToArray();
        var findings = row is null ? [] : row.Ev.Findings.ToArray();
        var status = passed ?? category == "pass" ? "pass" : "fail";
        note = note.Length > 600 ? note[..600] : note;
        var checks = row is null ? [] : Checks(row);
        var line = checks.Count == 0
            ? JsonSerializer.Serialize(new { idx, status, category, completed = row is not null, applied, reasons, findings, note })
            : JsonSerializer.Serialize(new
            {
                idx, status, category, completed = row is not null, applied, reasons, findings, note,
                checks = checks.Select(c => new
                {
                    obligation = c.Obligation, baseline = c.Against?.Spelling, category = c.Category, applied = c.AppliedSettings(),
                    reasons = Reasons(c), notes = c.Notes,
                }),
            });
        lock (Gate) File.AppendAllText(Path.Combine(dir, label + ".jsonl"), line + "\n");
    }

    static List<Check> Checks(Row row) { lock (row.Ev) return [.. row.Ev.Checks]; }
    static string[] Reasons(Check c) { lock (c.Ev) return [.. c.Ev.Reasons]; }

    /// <summary>
    /// Runs the check on a fresh copy of the row (its own evidence); null = no failure, otherwise the failure text.
    /// An exception the check did not expect is a harness error until triaged, never a pass and never a
    /// violation: the row fails the test with the exception named, and no seed counts it caught. A violation or a
    /// harness error fails the test; any other outcome but a pass skips it with its reasons, so a test run
    /// counts what earned nothing. A row that ran checks (<see cref="Row.Check(string, Baseline?, Func{Check, string?})"/>)
    /// fails when the row itself or any check ended as a violation or a harness error, passes when at least one check
    /// passed, and otherwise skips, listing each check's outcome. <paramref name="label"/> names the row in a failure and
    /// its evidence file, and is the probe whose rows file holds the row unless <paramref name="rowsOf"/> names it: a
    /// probe passes its theory's first argument, the probe's own name, as one or the other.
    /// </summary>
    public static Task Run(string label, int idx, Func<Row, Task<string?>> check, string? rowsOf = null) => Run(label, Rows.Get(rowsOf ?? label, idx), check);

    /// <summary>The same for a row in hand, without a rows file: how the prober's tests run the harness.</summary>
    public static async Task Run(string label, Row cached, Func<Row, Task<string?>> check)
    {
        var idx = cached.Index;
        var row = new Row(cached.Index, cached.All.ToDictionary(kv => kv.Key, kv => kv.Value), cached.Claim);
        string? fail = null, unexpected = null;
        try { fail = await check(row); }
        catch (Exception ex) { unexpected = "UNEXPECTED " + ex.GetType().Name + ": " + ex.Message; row.Outcome("harness-error", unexpected); }
        var checks = Checks(row);
        if (checks.Count == 0)
        {
            // An exception is the harness breaking, not the claim's check firing: a harness error and never a
            // violation, so a seeded defect that only makes a check throw is no catch (decision 103).
            if (fail is not null) row.Outcome("violation", fail);
            Write(label, idx, row.Ev.Category, fail ?? unexpected ?? row.Notes, row);
            if (fail is not null) Assert.Fail($"[{label} #{idx}] {row}\n{fail}");
            if (row.Ev.Category == "harness-error") Assert.Fail($"[{label} #{idx}] {row}\nharness error: {string.Join("; ", row.Ev.Reasons)}");
            if (row.Ev.Category != "pass") Assert.Skip($"{row.Ev.Category}: {string.Join("; ", row.Ev.Reasons.Where(x => !x.StartsWith("pass:")))}");
            return;
        }
        // A failure the row returns from one of its checks is that check's violation, recorded there; any other is the row's own.
        if (fail is not null && !checks.Any(c => c.Failure == fail)) row.Outcome("violation", fail);
        var failing = checks.Where(c => c.Category is "violation" or "harness-error").ToList();
        var own = row.Ev.Reasons.Where(x => !x.StartsWith("pass:")).ToList();
        var ownFailed = row.Ev.Own is "violation" or "harness-error";
        var passed = !ownFailed && failing.Count == 0 && checks.Any(c => c.Category == "pass");
        Write(label, idx, row.Ev.Category, fail ?? unexpected ?? row.Notes, row, passed);
        if (ownFailed || failing.Count > 0)
            Assert.Fail($"[{label} #{idx}] {row}\n" + string.Join("\n", (ownFailed ? own : []).Concat(failing.Select(Shown))));
        if (!passed) Assert.Skip(string.Join("; ", own.Concat(checks.Select(Shown))));
    }

    /// <summary>A check as a failure or a skip lists it: <c>smallest-scale inapplicable: not-promised:closed-set-has-no-scale</c>.</summary>
    static string Shown(Check c)
    {
        var category = c.Category;
        var reasons = Reasons(c).Where(x => !x.StartsWith("pass:")).Select(x => x.StartsWith(category + ":") ? x[(category.Length + 1)..] : x);
        return $"{c.Obligation} {category}: {string.Join(", ", reasons)}";
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
