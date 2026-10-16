using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Prober;

/// <summary>
/// The part of JSON Schema the tracked files' schemas use, checked where a file is read, so a field nobody
/// reads is refused instead of kept (decision 102: three invented fields for an exclusion's reason sat in
/// claims.json unread). A keyword outside this part throws: a schema that says more than is checked would be
/// a check that silently does less than it reads.
/// </summary>
internal static class SchemaCheck
{
    private static readonly HashSet<string> Annotations = ["$schema", "$id", "title", "description"];

    /// <summary>The schema of <c>Schemas/{name}</c>, embedded in the dll, so it is the one the dll's fingerprint covers.</summary>
    public static JsonObject Load(string name)
    {
        using var stream = typeof(SchemaCheck).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"no embedded schema {name}");
        return JsonNode.Parse(stream) as JsonObject ?? throw new InvalidOperationException($"the schema {name} is not a JSON object");
    }

    /// <summary>What in <paramref name="node"/> the schema refuses, each at its path (<c>exclude[0].reason</c>).</summary>
    public static List<string> Problems(JsonNode? node, JsonObject schema, string path = "")
    {
        var problems = new List<string>();
        Check(node, schema, path, problems);
        return problems;
    }

    private static void Check(JsonNode? node, JsonObject schema, string path, List<string> problems)
    {
        var at = path.Length == 0 ? "the value" : path;
        foreach (var (keyword, _) in schema)
            if (!Annotations.Contains(keyword) && keyword is not ("type" or "const" or "required" or "properties" or "additionalProperties"
                    or "items" or "minItems" or "maxItems" or "minLength" or "pattern"))
                throw new InvalidOperationException($"a schema uses {keyword}, which SchemaCheck does not check");

        if (schema["type"] is JsonValue type && !IsType(node, Files.Str(type)))
        {
            problems.Add($"{at} is not {Article(Files.Str(type))}");
            return;
        }
        if (schema["const"] is { } constant && !JsonNode.DeepEquals(node, constant)) problems.Add($"{at} is not {constant.ToJsonString()}");

        if (node is JsonObject o)
        {
            foreach (var name in Files.Strings(schema["required"]))
                if (!o.ContainsKey(name)) problems.Add($"{Join(path, name)} is required");
            var properties = schema["properties"] as JsonObject ?? [];
            foreach (var (name, value) in o)
            {
                if (properties[name] is JsonObject property) Check(value, property, Join(path, name), problems);
                else if (schema["additionalProperties"] is JsonObject rest) Check(value, rest, Join(path, name), problems);
                else if (schema["additionalProperties"] is JsonValue allowed && allowed.TryGetValue<bool>(out var b) && !b)
                    problems.Add($"{Join(path, name)} is not a field the schema has");
            }
        }
        if (node is JsonArray a)
        {
            if (schema["minItems"] is { } min && a.Count < Files.Num(min)) problems.Add($"{at} has {a.Count} item(s), fewer than {Files.Num(min)}");
            if (schema["maxItems"] is { } max && a.Count > Files.Num(max)) problems.Add($"{at} has {a.Count} item(s), more than {Files.Num(max)}");
            if (schema["items"] is JsonObject items)
                for (var i = 0; i < a.Count; i++) Check(a[i], items, $"{path}[{i}]", problems);
        }
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            if (schema["minLength"] is { } length && s.Length < Files.Num(length)) problems.Add($"{at} is shorter than {Files.Num(length)}");
            if (schema["pattern"] is JsonValue pattern && !Regex.IsMatch(s, Files.Str(pattern), RegexOptions.CultureInvariant))
                problems.Add($"{at} does not match {Files.Str(pattern)}");
        }
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static string Article(string type) => type is "object" or "array" or "integer" ? $"an {type}" : $"a {type}";

    private static bool IsType(JsonNode? node, string type) => type switch
    {
        "object" => node is JsonObject,
        "array" => node is JsonArray,
        "string" => node is JsonValue v && v.TryGetValue<string>(out _),
        "integer" => node is JsonValue v && v.TryGetValue<long>(out _),
        "boolean" => node is JsonValue v && v.TryGetValue<bool>(out _),
        _ => throw new InvalidOperationException($"a schema names type {type}, which SchemaCheck does not check"),
    };
}
