using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DocsQuery;

/// <summary>
/// What a command answers: a contract (<c>exact</c> or <c>ranked</c>), a one-line summary, and rows
/// of pointers. A row's first field is the pointer, <c>path:line</c> (in JSON, <c>path</c> and <c>line</c>). Text prints the fields
/// separated by two spaces; <c>--json</c> prints one JSON object per line, the summary first.
/// </summary>
internal sealed record Answer(string Contract, string Command, string Query, string Summary, IReadOnlyList<Row> Rows, int ExitCode)
{
    public const string Exact = "exact", Ranked = "ranked";

    public string Text()
    {
        var text = new StringBuilder();
        text.Append(Contract).Append(' ').Append(Command);
        if (Query.Length > 0) text.Append(' ').Append(Query);
        text.Append(": ").Append(Summary).Append('\n');

        foreach (var row in Rows) text.Append(string.Join("  ", row.Fields.Where(field => field.Value.Length > 0).Select(field => field.Name == "text" ? FirstLine(field.Value) : field.Value))).Append('\n');

        return text.ToString();
    }

    /// <summary>A multi-line field, in text, as its first line: a row stays one line. JSON carries it whole.</summary>
    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 100 ? line[..100] + "…" : text.Contains('\n') ? line + " …" : line;
    }

    public string Json()
    {
        var options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var lines   = new StringBuilder();

        void Line(Action<Utf8JsonWriter> write)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, options)) write(writer);
            lines.Append(Encoding.UTF8.GetString(buffer.ToArray())).Append('\n');
        }

        Line(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("contract", Contract);
            writer.WriteString("command", Command);
            writer.WriteString("query", Query);
            writer.WriteString("summary", Summary);
            writer.WriteNumber("count", Rows.Count);
            writer.WriteEndObject();
        });

        foreach (var row in Rows)
        {
            Line(writer =>
            {
                writer.WriteStartObject();
                foreach (var (name, value) in row.Fields)
                {
                    if (name == "at" && value.LastIndexOf(':') is var colon and > 0 && int.TryParse(value[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture, out var line))
                    {
                        writer.WriteString("path", value[..colon]);
                        writer.WriteNumber("line", line);
                    }
                    else if (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && name is "line" or "depth" or "rank" or "level")
                        writer.WriteNumber(name, number);
                    else
                        writer.WriteString(name, value);
                }
                writer.WriteEndObject();
            });
        }

        return lines.ToString();
    }
}

/// <summary>A row of an answer: named fields in print order.</summary>
internal sealed record Row(IReadOnlyList<(string Name, string Value)> Fields)
{
    public static Row Of(params (string Name, string? Value)[] fields) => new([.. fields.Select(field => (field.Name, field.Value ?? ""))]);

    public override string ToString() => string.Join("  ", Fields.Select(field => $"{field.Name}={field.Value}"));
}
