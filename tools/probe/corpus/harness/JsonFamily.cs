// Shared code of the JSON family's claim checks (C01 decimal spelling, C10 Preserve guard, L06 off the wire):
// serializing a held shape, reading its tokens, rewriting one token, and reading what a document holds back.
using System.Text;
using System.Text.Json;
using Probes.Shapes;
namespace Probes.Json;

/// <summary>One token of a JSON document: its kind, its raw text (a string's without quotes), its value as text, and its byte span.</summary>
public sealed record JTok(JsonTokenType Type, string Raw, string? Text, int Start, int End);

public static class Jx
{
    public static (string? Json, Exception? Ex) Ser(Held h, JsonSerializerOptions o)
    { try { return (JsonSerializer.Serialize(h.Value, h.Declared, o), null); } catch (Exception ex) { return (null, ex); } }

    public static (object? Value, Exception? Ex) De(string text, Type declared, JsonSerializerOptions o)
    { try { return (JsonSerializer.Deserialize(text, declared, o), null); } catch (Exception ex) { return (null, ex); } }

    /// <summary>Every token of a document, with the byte span it takes (a string's and a property name's quotes included).</summary>
    public static List<JTok> Tokens(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var rd = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var list = new List<JTok>();
        while (rd.Read())
        {
            var start = (int)rd.TokenStartIndex;
            switch (rd.TokenType)
            {
                case JsonTokenType.Number:
                    list.Add(new(rd.TokenType, Encoding.UTF8.GetString(rd.ValueSpan), Encoding.UTF8.GetString(rd.ValueSpan), start, start + rd.ValueSpan.Length)); break;
                case JsonTokenType.String or JsonTokenType.PropertyName:
                    list.Add(new(rd.TokenType, Encoding.UTF8.GetString(rd.ValueSpan), rd.GetString(), start, start + rd.ValueSpan.Length + 2)); break;
                default:
                    list.Add(new(rd.TokenType, "", null, start, start)); break;
            }
        }
        return list;
    }

    /// <summary>The document with the tokens <paramref name="at"/> replaced by <paramref name="with"/> (raw JSON text).</summary>
    public static string Rewrite(string json, IEnumerable<JTok> at, Func<JTok, string> with)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var sb = new StringBuilder(); var pos = 0;
        foreach (var t in at.OrderBy(t => t.Start))
        {
            sb.Append(Encoding.UTF8.GetString(bytes, pos, t.Start - pos)); sb.Append(with(t)); pos = t.End;
        }
        sb.Append(Encoding.UTF8.GetString(bytes, pos, bytes.Length - pos));
        return sb.ToString();
    }

    /// <summary>Options that write what a value holds as the serializer writes it by default (a decimal with its scale, every field).</summary>
    public static readonly JsonSerializerOptions Plain = new() { IncludeFields = true };

    public static string Cut(string? s) => s is null ? "(null)" : s.Length > 220 ? s[..220] + "..." : s;
    public static string Name(Type t) => t.IsGenericType ? t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">" : t.Name;
    /// <summary>A refusal's message with the type the other side holds named instead: the serializer names the shape it refused.</summary>
    public static string Swap(string message, Type from, Type to) => message.Replace(from.ToString(), to.ToString());
    /// <summary>What a containing type holds: its property P or its field F.</summary>
    public static object? Member(Type declared, object? holder) =>
        holder is null ? null : declared.GetProperty("P") is { } p ? p.GetValue(holder) : declared.GetField("F") is { } f ? f.GetValue(holder) : null;
    public static string Ex(Exception e) => e.GetType().Name + ": " + Cut(e.Message);

    /// <summary>A copy of the options that writes every member: the ignore conditions reset, so a read has the member to read.</summary>
    public static JsonSerializerOptions Full(JsonSerializerOptions o) => new(o)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never, IgnoreReadOnlyProperties = false, IgnoreReadOnlyFields = false,
        // A number written as a string reads back only with AllowReadingFromString; the document is input, so it says its other numbers plainly.
        NumberHandling = o.NumberHandling & ~System.Text.Json.Serialization.JsonNumberHandling.WriteAsString,
    };
}

/// <summary>
/// The row's json.* axes, applied by JsonRow; under json.TypeInfoResolver=source-generated-context the resolver is the claim's own
/// context (JsonFamilyShapes.g.cs), which lists every shape the claim's rows serialize there, so the setting runs as the row names it.
/// </summary>
public static class JCtx
{
    public static JsonSerializerOptions Apply(Row r, Entry e, System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver ctx, params Type[] shapes)
    {
        var src = r.Is("json.TypeInfoResolver", "source-generated-context");
        var o = JsonRow.Apply(src ? r.With("json.TypeInfoResolver", "default") : r, e).Options!;
        if (!src) return o;
        var probe = new JsonSerializerOptions();
        var missing = shapes.Where(t => ctx.GetTypeInfo(t, probe) is null).Select(Jx.Name).ToList();
        if (missing.Count > 0) throw new InvalidOperationException("not in the claim's context (run harness/gen_json_family.py): " + string.Join(", ", missing));
        if (r.Is("json.TypeInfoResolverChain", "context-then-default")) o.TypeInfoResolverChain.Insert(0, ctx);
        else o.TypeInfoResolver = ctx;
        return o;
    }
}
