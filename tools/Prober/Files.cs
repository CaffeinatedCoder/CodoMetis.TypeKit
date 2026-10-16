using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// Reading and writing the ledger's files. Every write goes to a temp file renamed over the target, so a
/// reader sees the file before or after a change, never half of it, on every OS; reads and the rename go
/// through SharedFile, which Windows needs while another process reads the target. A partial or unreadable
/// line in a JSON-lines file is skipped, never fatal (a writer killed mid-write in an older version).
/// </summary>
internal static class Files
{
    /// <summary>The schema version this tool writes. A file or line without one is version 1.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Indented, and with only what JSON itself requires escaped: the tracked corpus is read in a diff, where
    /// the default encoder's six-character escapes for <c>+</c>, a backtick and <c>&lt;</c> made every publish a
    /// rewrite (decision 102). No reader here or in the harness parses these files as
    /// anything but JSON, and an identity hashes <see cref="Canonical"/>, not the file's text.
    /// </summary>
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonNode? ReadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonNode.Parse(SharedFile.ReadAllText(path)); }
        catch (JsonException) { return null; }   // a file another process is creating this instant: not there yet
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }   // Windows: a file being replaced this instant
    }

    public static void WriteJson(string path, JsonNode value) => WriteAtomically(path, Format(value));

    /// <summary>The text <see cref="WriteJson"/> writes: a tracked file in this form is unchanged by a publish that changes nothing in it.</summary>
    public static string Format(JsonNode value) => value.ToJsonString(Indented) + "\n";

    public static List<JsonObject> ReadLines(string path)
    {
        var result = new List<JsonObject>();
        if (!File.Exists(path)) return result;
        foreach (var line in SharedFile.ReadAllLines(path))
        {
            if (line.Trim().Length == 0) continue;
            try { if (JsonNode.Parse(line) is JsonObject o) result.Add(o); }
            catch (JsonException) { }
        }
        return result;
    }

    /// <summary>Appends one line by writing the whole file anew and renaming it over the old one.</summary>
    public static void AppendLine(string path, JsonObject value)
    {
        var old = File.Exists(path) ? SharedFile.ReadAllText(path) : "";
        if (old.Length > 0 && !old.EndsWith('\n')) old = old[..(old.LastIndexOf('\n') + 1)];
        WriteAtomically(path, old + value.ToJsonString() + "\n");
    }

    public static void WriteAtomically(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        SharedFile.Replace(tmp, path);
    }

    /// <summary>A rows file, plain (staging) or gzipped (the corpus).</summary>
    public static JsonArray ReadRows(string path)
    {
        if (File.Exists(path)) return ReadJson(path)?["rows"] as JsonArray ?? [];
        if (File.Exists(path + ".gz"))
        {
            using var gz = new GZipStream(SharedFile.OpenRead(path + ".gz"), CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            return JsonNode.Parse(reader.ReadToEnd())?["rows"] as JsonArray ?? [];
        }
        return [];
    }

    public static byte[] Gunzip(string path)
    {
        using var gz = new GZipStream(SharedFile.OpenRead(path), CompressionMode.Decompress);
        using var memory = new MemoryStream();
        gz.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Writes rows gzipped and reproducibly: one row per line, keys sorted, no timestamp in the header.</summary>
    public static void WriteRowsGzipped(string path, string claim, IEnumerable<JsonObject> rows)
    {
        var body = new StringBuilder();
        body.Append("{\"claim\": ").Append(JsonSerializer.Serialize(claim)).Append(", \"rows\": [\n");
        body.Append(string.Join(",\n", rows.Select(r => "{\"row\":" + Canonical(r) + "}")));
        body.Append("\n]}\n");
        var tmp = $"{path}.{Environment.ProcessId}.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = File.Create(tmp))
        using (var gz = new GZipStream(file, CompressionLevel.SmallestSize))
            gz.Write(Encoding.UTF8.GetBytes(body.ToString()));
        SharedFile.Replace(tmp, path);
    }

    /// <summary>A JSON object with its keys in ordinal order, recursively: the form hashed for an identity.</summary>
    public static string Canonical(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                               .Select(kv => JsonSerializer.Serialize(kv.Key) + ":" + Canonical(kv.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    public static string Str(JsonNode? node, string fallback = "") => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    /// <summary>A number, whether parsed from text or built in memory from any numeric type.</summary>
    public static double Num(JsonNode? node, double fallback = 0)
    {
        if (node is not JsonValue v) return fallback;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<decimal>(out var m)) return (double)m;
        return double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : fallback;
    }

    public static IEnumerable<string> Strings(JsonNode? node) => node is JsonArray a ? a.Select(x => Str(x)) : [];

    public static Dictionary<string, string> StringMap(JsonNode? node) =>
        node is JsonObject o ? o.Where(kv => kv.Value is JsonValue).ToDictionary(kv => kv.Key, kv => Str(kv.Value, kv.Value!.ToJsonString()), StringComparer.Ordinal)
                             : new(StringComparer.Ordinal);
}
