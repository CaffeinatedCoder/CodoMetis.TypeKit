// The three-sided differential: the rules every comparison of a value object with its wrapped twin follows, in one place. A comparison
// has three sides:
//   (1) the wrapped twin without TypeKit: for http a host without AddTypeKit() and without TypeKit's JSON converters
//       (HostCfg.WithoutTypeKit); in process, the twin under the application's options less TypeKit's converters (WithoutTypeKit);
//   (2) the wrapped twin with TypeKit, as the row configures it;
//   (3) the value object.
// Side (1) is read from a fact's fixture where one covers the row's configuration (Fact, FactFixture), and the check's baseline is then
// Baseline.Fact("KB-NNN"); otherwise it is measured, and the baseline is Baseline.Framework. A configuration measured here that no fact
// covers and that decides rows is pinned as a fact with its test and fixture (design/knowledge), never as a note key.
// The rules:
//   - (1) and (3) refuse alike, at the same operation and stage with the same exception type and the same HTTP status: the framework
//     refuses, and the check ends expected-refusal against side (1)'s baseline (NoteKeys.FrameworkRefuses). No message is compared: the
//     stage is what an exception's properties say (a JsonException's Path), never its text.
//   - (1) answers and (3) refuses, or (1) refuses and (3) answers: a violation, unless a sentence makes the difference intended, cited by
//     the check (a not-promised entry of the claim, or the docs section a calibration cites).
//   - (2) differs from (1): a violation, "TypeKit changed the wrapped type's behaviour" (Interferes). (2) and (3) agreeing on a wrong answer
//     is why side (1) runs on every row of a claim whose obligations promise non-interference (NonInterference).
//   - Elsewhere side (1) runs lazily, only where (2) and (3) disagree or refuse.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.Facts;
namespace Probes.Shapes;

public static class Sides
{
    /// <summary>
    /// One side's answer to one operation: the text it gave, or the exception it threw or the status it answered with. A side read from a
    /// fixture has no exception object, only the type the fixture names (<see cref="Refusal"/>).
    /// </summary>
    public sealed record Answer(string Operation, string? Value, Exception? Thrown = null, int? Status = null, string? FixtureRefusal = null)
    {
        public bool Refused => Thrown is not null || FixtureRefusal is not null || Status is { } s && s is not (>= 200 and < 300);
        /// <summary>The refusal's exception type, by its full name; null for an answer or a refusal by status alone.</summary>
        public string? Refusal => Thrown?.GetType().FullName ?? FixtureRefusal;
        /// <summary>
        /// Where the refusal was met, from the exception's properties and never from its message: the JSON path a JsonException names. A side
        /// read from a fixture has none: the settings its answer covers (the depth a value is written at) say where.
        /// </summary>
        public string? Stage => FixtureRefusal is not null ? null : Thrown is JsonException j ? j.Path ?? "" : "";
        public override string ToString() =>
            Refused ? $"{Operation} refused ({string.Join(" ", new[] { Thrown?.GetType().Name ?? FixtureRefusal, Status?.ToString() }.Where(x => x is not null))}{(Stage is { Length: > 0 } at ? " at " + at : "")})"
                    : $"{Operation} {Cut(Value)}";
    }

    /// <summary>(1) and (3) refuse alike: at the same operation and stage, with the same exception type and the same status. No message is compared.</summary>
    public static bool Alike(Answer one, Answer three) =>
        one.Refused && three.Refused && one.Operation == three.Operation && one.Refusal == three.Refusal && one.Status == three.Status
        && (one.Stage is null || SameStage(one.Stage, three.Stage!));

    /// <summary>
    /// Two refusals' stages are the same: the same JSON path, or, where one side writes or reads its value through a nested call of the
    /// serializer, whose path starts at that value (a value object over a class the serializer walks), the other's path ending in it.
    /// </summary>
    static bool SameStage(string a, string b) =>
        a == b || (b.Length > 1 && b[0] == '$' && a.EndsWith(b[1..], StringComparison.Ordinal)) || (a.Length > 1 && a[0] == '$' && b.EndsWith(a[1..], StringComparison.Ordinal));

    /// <summary>
    /// (2) is not (1): TypeKit changed what the wrapped type does. The failure, or null where the two are the same answer: the same text, or
    /// a refusal alike (a refusal's body, such as a problem's trace id, is not compared).
    /// </summary>
    public static string? Interferes(Answer one, Answer two) =>
        one.Refused == two.Refused && one.Refusal == two.Refusal && one.Status == two.Status && one.Stage == two.Stage && (one.Refused || one.Value == two.Value) ? null
        : $"{two.Operation}: TypeKit changed the wrapped type's behaviour: without TypeKit {one}, with it {two}";

    /// <summary>
    /// Ends a check whose sides (1) and (3) refuse alike: the framework's refusal, an expected refusal that earns nothing. The check runs
    /// against side (1)'s baseline, which its caller chose (<see cref="Side.Baseline"/>).
    /// </summary>
    public static string? Refuses(Check c, Side one, Answer three)
    {
        c.Note(NoteKeys.FrameworkRefuses, $"{three}, {one.Source}");
        return null;
    }

    /// <summary>
    /// One check of the differential. Where side (1) refuses alike with (3) (<paramref name="framework"/>), the check is the framework's
    /// refusal, against side (1)'s baseline; otherwise <paramref name="body"/> compares, against <paramref name="against"/>, once (2) is held
    /// to (1) where side (1) ran (<paramref name="one"/>, <paramref name="two"/>).
    /// </summary>
    public static string? Check(Row r, string obligation, Baseline against, Side? framework, Answer three, Answer? one, Answer? two, Func<Check, string?> body) =>
        framework is not null && Alike(framework.Answer, three)
            ? r.Check(obligation, framework.Baseline, c => Refuses(c, framework, three))
            : r.Check(obligation, against, c => (one is not null && two is not null ? Interferes(one, two) : null) ?? body(c));

    /// <summary>The asynchronous form of <see cref="Check(Row, string, Baseline, Side?, Answer, Answer?, Answer?, Func{Check, string?})"/>.</summary>
    public static Task<string?> Check(Row r, string obligation, Baseline against, Side? framework, Answer three, Answer? one, Answer? two, Func<Check, Task<string?>> body) =>
        framework is not null && Alike(framework.Answer, three)
            ? Task.FromResult(r.Check(obligation, framework.Baseline, c => Refuses(c, framework, three)))
            : r.Check(obligation, against, async c => (one is not null && two is not null ? Interferes(one, two) : null) ?? await body(c));

    /// <summary>
    /// Side (1) where the value object refused (<paramref name="three"/>), as the framework's: the twin without TypeKit refusing alike
    /// (<paramref name="one"/>), or, where the twin answered, a converter's value under the row's depth limit (<see cref="WrittenByAConverter"/>).
    /// Null where the framework does not refuse alike, and the check compares.
    /// </summary>
    public static Side? Framework(Row r, Entry e, Held twin, JsonSerializerOptions oneOptions, Answer one, Answer three)
    {
        if (!three.Refused) return null;
        if (one.Refused) return Alike(one, three) ? Measured(one, "the twin under the application's options, less TypeKit's converters") : null;
        return WrittenByAConverter(r, e, twin, oneOptions, three) is { } s && Alike(s.Answer, three) ? s : null;
    }

    /// <summary>Side (1) as a check uses it: its answer, the baseline it stands for, and where it came from.</summary>
    public sealed record Side(Answer Answer, Baseline Baseline, string Source);

    /// <summary>Side (1) measured: the framework without TypeKit, run in the same row.</summary>
    public static Side Measured(Answer a, string how) => new(a, Baseline.Framework, "measured: " + how);

    // ---- the twin

    /// <summary>
    /// The twin and the value object at the row's position in its containing type, each around its sample: the one place where both sides
    /// of a two-sided comparison are built, and where the precondition of every such comparison is held, that the twin and the value object
    /// hold the same shape (Equivalence.Shapes: the twin stands where the value object stands, at its nullability). A mismatch is a harness
    /// error, never a pass: it throws, and the check or row that asked ends as one.
    /// </summary>
    public static (Held Twin, Held Vo) Twins(string position, string containing, Entry e, object plain, object vo)
    {
        var hp = Pos.Build(position, containing, e, false, plain);
        var hv = Pos.Build(position, containing, e, true, vo);
        if (Equivalence.Shapes(hp.Declared, hv.Declared, e) is { } why) throw new InputsNotEquivalent(why);
        return (hp, hv);
    }

    /// <summary>The twin and the value object do not hold the same shape: the harness built a comparison that compares nothing.</summary>
    public sealed class InputsNotEquivalent(string why) : Exception("inputs not equivalent: " + why);

    // ---- which obligations promise non-interference

    /// <summary>
    /// The obligations whose sentence promises that the value object leaves the wrapped type as ASP.NET or System.Text.Json have it ("as the
    /// underlying type's", "the one ASP.NET Core publishes for the underlying type", "unaffected"), read from each claim's obligations in
    /// claims.json. Side (1) runs on every row that checks one of them. C12's parameter-keeps-aspnet and C01's covers-every-decimal already
    /// compare with the framework without TypeKit's converters on every row (SchemaClaim's Baseline.Framework, DecimalSpelling's serializer's
    /// own decimal); the text claims compare with the wrapped type's own text, which TypeKit adds nothing to.
    /// </summary>
    static readonly Dictionary<string, string[]> NonInterference = new()
    {
        ["C21-openapi-schema-parity"] = ["the-underlying-types-schema", "nullable-admits-null-as-the-wrapped-does", "container-elements", "dictionary-key", "every-document-version"],
        ["L01-json-bytes"] = ["the-underlying-types-json", "as-a-dictionary-key-too", "read-as-the-underlying-type-reads"],
        ["C10-preserve-guard"] = ["value-converters-unaffected", "own-converters-unaffected", "ignore-cycles-unaffected"],
    };

    /// <summary>Whether side (1) runs on every row for this obligation of the row's claim, not only where (2) and (3) disagree or refuse.</summary>
    public static bool Unconditional(Row r, string obligation) =>
        r.Claim is { } claim && NonInterference.TryGetValue(claim, out var obligations) && obligations.Contains(obligation);

    /// <summary>
    /// Whether a type is or holds one of TypeKit's own (a Patch, an Option), through its type arguments and element type: a twin that holds
    /// one has no side without TypeKit.
    /// </summary>
    public static bool HoldsTypeKit(Type t) =>
        t.Assembly == typeof(CodoMetis.TypeKit.ValueObjects.StoredJsonConverterFactory).Assembly
        || (t.IsArray && HoldsTypeKit(t.GetElementType()!))
        || (t.IsGenericType && t.GetGenericArguments().Any(HoldsTypeKit));

    // ---- side (1) in process: the twin under the application's options, less TypeKit's converters

    /// <summary>The application's options less what TypeKit adds to them: its converters (StoredJsonConverterFactory, the decimal converters), told by their assembly.</summary>
    public static JsonSerializerOptions WithoutTypeKit(JsonSerializerOptions o)
    {
        var one = new JsonSerializerOptions(o);
        DropTypeKit(one);
        return one;
    }

    /// <summary>Removes TypeKit's converters from options in place: a host's side (1) (HostCfg.WithoutTypeKit).</summary>
    public static void DropTypeKit(JsonSerializerOptions o)
    {
        var typeKit = typeof(CodoMetis.TypeKit.ValueObjects.StoredJsonConverterFactory).Assembly;
        foreach (var x in o.Converters.Where(x => x.GetType().Assembly == typeKit).ToList()) o.Converters.Remove(x);
    }

    /// <summary>A side's write of a held shape.</summary>
    public static Answer Write(Held h, JsonSerializerOptions o, string operation = "write")
    {
        try { return new(operation, JsonSerializer.Serialize(h.Value, h.Declared, o)); }
        catch (Exception x) { return new(operation, null, x); }
    }

    /// <summary>A side's read of a text into a held shape, and what it read written again: a refusal of the write back is the operation "write back".</summary>
    public static Answer Read(string text, Held h, JsonSerializerOptions o, string operation = "read")
    {
        object? v;
        try { v = JsonSerializer.Deserialize(text, h.Declared, o); }
        catch (Exception x) { return new(operation, null, x); }
        try { return new(operation, v is null ? "null" : JsonSerializer.Serialize(v, h.Declared, o)); }
        catch (Exception x) { return new("write back", null, x); }
    }

    // ---- side (1) of a value a converter writes, under a depth limit (KB-040)

    /// <summary>
    /// Side (1) where the value object, which its converter writes, refused a write that the twin, which the serializer's own converter writes,
    /// answered, under the row's json.MaxDepth: System.Text.Json charges a converter's value a level against the limit (KB-040), so the
    /// framework's side for a converter-written value is a converter of the application's for the wrapped type, without TypeKit. Read from
    /// KB-040's fixture where an answer covers the row's depth limit and the depth the value is written at, else measured with that
    /// converter in the same row. Null where the row sets no depth limit, (3) refused no write, or the depth cannot be read.
    /// </summary>
    public static Side? WrittenByAConverter(Row r, Entry e, Held twin, JsonSerializerOptions one, Answer three)
    {
        if (r.Get("json.MaxDepth") is null or "default" || three.Thrown is null || three.Operation is not ("write" or "write back")) return null;
        if (ConverterDepth(e, twin, one) is not { } depth) { r.Log.AppendLine(Why = $"side (1) of a converter's value: no depth read for {Make.Held(e.Plain).Name} ({Unread})"); return null; }
        var settings = Settings(r, ("written-by", "a-converter"), ("writer-depth", depth.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (Covered(r, "KB-040", settings) is { Kind: FactAnswer.Exception } answer)
            return new(new(three.Operation, null, FixtureRefusal: (string?)answer.Value["type"]), Baseline.Fact("KB-040"),
                       $"KB-040 {answer.Id}: a converter's value at depth {depth} under MaxDepth {r["json.MaxDepth"]}");
        var measured = ByAConverter(e, twin, one, three.Operation);
        Why = $"side (1) of a converter's value at depth {depth}: no KB-040 answer covers it, measured {measured?.ToString() ?? "nothing"}";
        return measured is null ? null : Measured(measured, $"the twin written by a converter of the application's at depth {depth}");
    }

    /// <summary>The writer's depth where the serializer hands the wrapped value to a converter, read by one that records it, without a limit.</summary>
    static int? ConverterDepth(Entry e, Held twin, JsonSerializerOptions one)
    {
        try
        {
            var open = new JsonSerializerOptions(one) { MaxDepth = 64 };
            var serializers = new JsonSerializerOptions(open);
            var converter = (IRecordsDepth)Activator.CreateInstance(typeof(PassThrough<>).MakeGenericType(Make.Held(e.Plain)), serializers)!;
            open.Converters.Insert(0, (JsonConverter)converter);
            JsonSerializer.Serialize(twin.Value, twin.Declared, open);
            Unread = converter.Depth >= 0 ? null : "no value handed to the converter";
            return converter.Depth >= 0 ? converter.Depth : null;
        }
        catch (Exception x) { Unread = x.GetType().Name + ": " + Cut(x.Message); return null; }   // the twin is not written with a converter of the application's here: no depth to read
    }
    [ThreadStatic] static string? Unread;
    /// <summary>Why the last <see cref="WrittenByAConverter"/> on this thread gave no fixture's side, for a failure to name.</summary>
    [ThreadStatic] public static string? Why;

    /// <summary>The twin written (and, for a write back, read and written again) by a converter of the application's that writes as the serializer does.</summary>
    static Answer? ByAConverter(Entry e, Held twin, JsonSerializerOptions one, string operation)
    {
        try
        {
            var with = new JsonSerializerOptions(one);
            with.Converters.Insert(0, (JsonConverter)Activator.CreateInstance(typeof(PassThrough<>).MakeGenericType(Make.Held(e.Plain)), new JsonSerializerOptions(one))!);
            return Write(twin, with, operation);
        }
        catch (Exception) { return null; }
    }

    interface IRecordsDepth { int Depth { get; } }

    /// <summary>A converter of the application's for the wrapped type, without TypeKit: it writes and reads through the serializer's own contract for it, and records the writer's depth.</summary>
    sealed class PassThrough<T>(JsonSerializerOptions serializers) : JsonConverter<T>, IRecordsDepth where T : notnull
    {
        // options a host or a row built without a resolver get the serializer's default one, as JsonSerializer gives them on first use
        readonly JsonTypeInfo<T> info = (JsonTypeInfo<T>)Resolving(serializers).GetTypeInfo(typeof(T));
        static JsonSerializerOptions Resolving(JsonSerializerOptions o) { o.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver(); return o; }
        public int Depth { get; private set; } = -1;
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonSerializer.Deserialize(ref reader, info);
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) { Depth = Math.Max(Depth, writer.CurrentDepth); JsonSerializer.Serialize(writer, value, info); }
        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ((JsonConverter<T>)info.Converter).ReadAsPropertyName(ref reader, typeToConvert, serializers);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        { Depth = Math.Max(Depth, writer.CurrentDepth); ((JsonConverter<T>)info.Converter).WriteAsPropertyName(writer, value, serializers); }
    }

    // ---- facts' fixtures

    static readonly Dictionary<string, FactFixture?> Fixtures = new();

    /// <summary>
    /// A fact's fixture as side (1), as FactFixture's rule says: only a fixture the fact's entry names on its <c>fixture:</c> line, read from the
    /// repository (the Facts project's test/CodoMetis.TypeKit.Facts, found by walking up to CodoMetis.TypeKit.slnx), never written. Null where
    /// the fact names none. Whether `docs-query check` is clean is CI's to hold: the harness does not run it.
    /// </summary>
    public static FactFixture? Fact(string id)
    {
        lock (Fixtures)
        {
            if (Fixtures.TryGetValue(id, out var known)) return known;
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "CodoMetis.TypeKit.slnx"))) root = root.Parent;
            FactFixture? fixture = null;
            if (root is not null)
            {
                var file = FactFixture.FileName(id);
                var entry = Directory.EnumerateFiles(Path.Combine(root.FullName, "design", "knowledge"), id + "-*.md").FirstOrDefault();
                // the entry's frontmatter, between its first two "---" lines, names the fixture
                var named = entry is not null && File.ReadLines(entry).Skip(1).TakeWhile(l => l != "---")
                    .Any(l => l.Trim() == $"fixture: test/CodoMetis.TypeKit.Facts/{file}");
                var path = Path.Combine(root.FullName, "test", "CodoMetis.TypeKit.Facts", file);
                if (named && File.Exists(path)) fixture = FactFixture.Load(path);
            }
            return Fixtures[id] = fixture;
        }
    }

    /// <summary>
    /// The row's settings as a fixture's answers name them: every axis at the value that applied (a substitution's, else the row's), unread,
    /// and the settings the harness measured (<paramref name="measured"/>), such as the depth a value is written at. The caller marks read
    /// the axes of the answer that covers them (<see cref="Covered"/>).
    /// </summary>
    public static Dictionary<string, string> Settings(Row r, params (string Key, string Value)[] measured)
    {
        var s = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string> applied;
        lock (r.Ev) applied = new(r.Ev.Applied);
        foreach (var (axis, value) in r.Values) s[axis] = applied.TryGetValue(axis, out var a) ? a : value;
        foreach (var (key, value) in measured) s[key] = value;
        return s;
    }

    /// <summary>The answer of a fact's fixture that covers the row's settings, its axes then read for the running check; null where none does.</summary>
    public static FactAnswer? Covered(Row r, string fact, Dictionary<string, string> settings)
    {
        if (Fact(fact)?.For(settings) is not { } answer) return null;
        foreach (var axis in answer.When.Keys) _ = r.Get(axis);
        return answer;
    }

    static string Cut(string? s) => s is null ? "(null)" : s.Length > 160 ? s[..160] + "..." : s;
}
