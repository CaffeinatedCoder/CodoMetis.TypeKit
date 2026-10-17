using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// A fact's measured answers as data (design/adr/0107): <c>KBNNN.fixture.json</c> beside the fact's test <c>KBNNN.cs</c>,
/// named by the fact's <c>fixture:</c> line. The test measures what the framework does and asserts it is still each
/// answer of the fixture; the probe harness reads the same answers as a baseline, <c>Baseline.Fact("KB-NNN")</c>, in
/// place of starting a host for the framework's side.
///
/// <para>
/// The rule the harness follows when it reads a fixture as <c>Baseline.Fact</c>:
/// </para>
/// <list type="number">
/// <item>It reads only a fixture that a fact's <c>fixture:</c> line names, which <c>docs-query check</c> allows only for
/// a fact with a test (<c>fact-fixture</c>, <c>fact-not-executable-cited</c>), and only while the check is clean, so a
/// fact whose pins fell behind what is installed (<c>fact-behind</c>) is no baseline until its test passed again.</item>
/// <item>It takes the one answer that <see cref="FactAnswer.Covers"/> the row: every setting the answer's <c>when</c>
/// names is the row's, axis for axis (<c>openapi.OpenApiVersion</c>, <c>http.binding</c>, ...). A setting the row does
/// not have is not covered. No answer covers it: the fact says nothing about this row, and the harness measures the
/// framework's side itself. More than one: the fixture is ambiguous, and the row ends as a harness error.</item>
/// <item>It compares by the answer's kind: a <c>document</c> at its <c>at</c> (a JSON pointer) byte for byte in its
/// compact form, a <c>status</c> and, where the answer has one, its body, or an <c>exception</c> by its type and a part
/// of its message, at the operation and stage the answer names (<see cref="FactAnswer.Mismatch(JsonNode)"/> and its
/// overloads). A mismatch is the product's difference from the framework, as a twin's answer would be.</item>
/// <item>It never writes a fixture. An answer is changed by changing the fact: its test, its fixture and its entry, in
/// one commit.</item>
/// </list>
///
/// BCL only and nothing of the test framework, so that the harness can compile this file as it is.
/// </summary>
public sealed class FactFixture
{
    public const string Suffix = ".fixture.json";

    private FactFixture(string fact, IReadOnlyList<FactAnswer> answers) => (Fact, Answers) = (fact, answers);

    /// <summary>The fact's id, <c>KB-001</c>.</summary>
    public string Fact { get; }

    public IReadOnlyList<FactAnswer> Answers { get; }

    /// <summary>The answer of that id; an id the fixture does not have throws.</summary>
    public FactAnswer this[string id] =>
        Answers.FirstOrDefault(answer => answer.Id == id) ?? throw new KeyNotFoundException($"{Fact}'s fixture has no answer '{id}'");

    /// <summary>The file of a fact's fixture: <c>KB001.fixture.json</c> for <c>KB-001</c>.</summary>
    public static string FileName(string fact) => fact.Replace("-", "", StringComparison.Ordinal) + Suffix;

    /// <summary>The fixture as the facts project's build copies it beside its tests.</summary>
    public static FactFixture Beside(string fact) => Load(Path.Combine(AppContext.BaseDirectory, FileName(fact)));

    /// <summary>The one answer that covers a row's settings, or null where none does; more than one throws.</summary>
    public FactAnswer? For(IReadOnlyDictionary<string, string> settings)
    {
        var covering = Answers.Where(answer => answer.Covers(settings)).ToList();
        return covering.Count <= 1 ? covering.SingleOrDefault()
            : throw new InvalidOperationException($"{Fact}'s fixture is ambiguous for these settings: {string.Join(", ", covering.Select(answer => answer.Id))} each cover them");
    }

    /// <summary>Reads and checks a fixture: its fact is the one its file is named for, and each answer has an id of its own and the fields its kind needs.</summary>
    public static FactFixture Load(string path)
    {
        var name = Path.GetFileName(path);
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) throw Invalid(name, "is not a JSON object");

        var fact = (string?)root["fact"] ?? throw Invalid(name, "names no \"fact\"");
        if (FileName(fact) != name) throw Invalid(name, $"names the fact {fact}, whose fixture is {FileName(fact)}");

        var answers = new List<FactAnswer>();
        foreach (var node in root["answers"] as JsonArray ?? throw Invalid(name, "has no \"answers\" array"))
        {
            if (node is not JsonObject entry) throw Invalid(name, "has an answer that is not an object");

            var id   = (string?)entry["id"] ?? throw Invalid(name, "has an answer without an \"id\"");
            var kind = (string?)entry["kind"] ?? throw Invalid(name, $"answer {id} has no \"kind\"");
            var when = (entry["when"] as JsonObject ?? []).ToDictionary(kv => kv.Key, kv => (string?)kv.Value ?? throw Invalid(name, $"answer {id}'s when {kv.Key} is no string"), StringComparer.Ordinal);
            var at   = (string?)entry["at"];
            var value = entry["value"] ?? throw Invalid(name, $"answer {id} has no \"value\"");

            if (answers.Any(answer => answer.Id == id)) throw Invalid(name, $"lists the answer {id} twice");
            switch (kind)
            {
                case FactAnswer.Document when at is null || (at.Length > 0 && at[0] != '/'):
                    throw Invalid(name, $"answer {id} is a document and needs \"at\", a JSON pointer such as /paths/~1a/get");
                case FactAnswer.Status when value["status"] is not JsonValue status || !status.TryGetValue<int>(out _):
                    throw Invalid(name, $"answer {id} is a status and needs a \"status\" number");
                case FactAnswer.Exception when (string?)value["type"] is null:
                    throw Invalid(name, $"answer {id} is an exception and needs its \"type\"");
                case FactAnswer.Document or FactAnswer.Status or FactAnswer.Exception:
                    break;
                default:
                    throw Invalid(name, $"answer {id}'s kind '{kind}' is none of document, status and exception");
            }

            answers.Add(new FactAnswer(id, kind, when, at, value.DeepClone(), (string?)entry["note"]));
        }

        return new FactFixture(fact, answers);
    }

    private static InvalidDataException Invalid(string name, string why) => new($"{name} {why}");
}

/// <summary>
/// One measured answer of a fact: what the framework gave (<see cref="Value"/>) for the settings in <see cref="When"/>,
/// of one kind, <c>document</c>, <c>status</c> or <c>exception</c>, and a note saying what to read in it.
/// </summary>
public sealed class FactAnswer
{
    public const string Document = "document", Status = "status", Exception = "exception";

    internal FactAnswer(string id, string kind, IReadOnlyDictionary<string, string> when, string? at, JsonNode value, string? note) =>
        (Id, Kind, When, At, Value, Note) = (id, kind, when, at, value, note);

    public string Id { get; }

    public string Kind { get; }

    /// <summary>The settings the answer was measured under, axis to value, as a row names them.</summary>
    public IReadOnlyDictionary<string, string> When { get; }

    /// <summary>For a document, the JSON pointer of the part the answer is (RFC 6901: <c>~1</c> for <c>/</c>, <c>~0</c> for <c>~</c>).</summary>
    public string? At { get; }

    public JsonNode Value { get; }

    public string? Note { get; }

    /// <summary>Every setting the answer names is the row's.</summary>
    public bool Covers(IReadOnlyDictionary<string, string> settings) =>
        When.All(setting => settings.TryGetValue(setting.Key, out var value) && value == setting.Value);

    /// <summary>A document's difference from the answer at its pointer, or null where it is the same, byte for byte compact.</summary>
    public string? Mismatch(JsonNode document)
    {
        Expect(Document);
        var part     = Resolve(document, At!);
        var measured = part?.ToJsonString() ?? "(nothing)";
        var expected = Value.ToJsonString();
        return measured == expected ? null : $"{Id} at {At}: measured {measured}, the fixture says {expected}{Noted}";
    }

    /// <summary>A response's difference from the answer, or null: its status, and its body where the answer has one.</summary>
    public string? Mismatch(int status, string body)
    {
        Expect(Status);
        var expected = (int)Value["status"]!;
        if (status != expected) return $"{Id}: measured status {status}, the fixture says {expected}{Noted}";
        return (string?)Value["body"] is { } text && text != body ? $"{Id}: measured body {body}, the fixture says {text}{Noted}" : null;
    }

    /// <summary>A thrown exception's difference from the answer, or null: its type's full name, and a part of its message where the answer has one.</summary>
    public string? Mismatch(System.Exception? thrown)
    {
        Expect(Exception);
        var type = (string)Value["type"]!;
        if (thrown is null) return $"{Id}: nothing was thrown, the fixture says {type} at {Value["operation"]}, {Value["stage"]}{Noted}";
        if (thrown.GetType().FullName != type) return $"{Id}: measured {thrown.GetType().FullName}, the fixture says {type}{Noted}";
        return (string?)Value["message"] is { } part && !thrown.Message.Contains(part, StringComparison.Ordinal)
            ? $"{Id}: the message '{thrown.Message}' does not say '{part}'{Noted}"
            : null;
    }

    /// <summary>The node a JSON pointer names, or null where the document has none.</summary>
    public static JsonNode? Resolve(JsonNode document, string pointer)
    {
        var node = (JsonNode?)document;
        foreach (var token in pointer.Length == 0 ? [] : pointer[1..].Split('/'))
        {
            var key = token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            node = node switch
            {
                JsonObject o => o[key],
                JsonArray a when int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) && i < a.Count => a[i],
                _ => null,
            };
            if (node is null) return null;
        }

        return node;
    }

    private string Noted => Note is null ? "" : $" ({Note})";

    private void Expect(string kind)
    {
        if (Kind != kind) throw new InvalidOperationException($"{Id} is a {Kind}, not a {kind}");
    }
}
