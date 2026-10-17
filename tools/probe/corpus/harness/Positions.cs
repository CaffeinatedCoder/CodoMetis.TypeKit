// Builds the value a probe serializes for a row's shape.position x shape.containing (VO side and plain side), and the JSON parity oracle.
using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CodoMetis.TypeKit;
namespace Probes.Shapes;

public sealed record Held(Type Declared, object Value);

public static class Pos
{
    static Type Family(string containing, string style, Type m)
    {
        var n = containing.Replace('-', '_');
        var arity = containing == "generic-type" ? 2 : 1;
        var open = typeof(PSet).Assembly.GetType($"Probes.Shapes.Cn_{n}_{style}`{arity}") ?? throw new InvalidOperationException($"no container Cn_{n}_{style}");
        return arity == 2 ? open.MakeGenericType(m, typeof(int)) : open.MakeGenericType(m);
    }

    public static Held Build(string position, string containing, Entry e, bool vo, object sample)
    {
        var t = vo ? e.Vo : e.Plain;
        if (position == "root") return new(t, sample);
        Type m; object member; string style = "P";
        switch (position)
        {
            case "property": case "required-init": case "field": case "record-ctor-parameter":
                m = t; member = sample;
                style = position switch { "field" => "F", "record-ctor-parameter" => "C", "required-init" => "R", _ => "P" };
                break;
            case "nullable-property": m = t.IsValueType && Nullable.GetUnderlyingType(t) is null ? typeof(Nullable<>).MakeGenericType(t) : t; member = sample; break;
            case "list-element": case "read-only-list-element":
            {
                var l = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(t))!; l.Add(sample);
                member = l; m = position == "list-element" ? l.GetType() : typeof(IReadOnlyList<>).MakeGenericType(t); break;
            }
            case "array-element": { var a = Array.CreateInstance(t, 1); a.SetValue(sample, 0); member = a; m = a.GetType(); break; }
            case "immutable-array-element":
            {
                var l = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(t))!; l.Add(sample);
                var create = typeof(ImmutableArray).GetMethods().First(x => x.Name == "CreateRange" && x.GetParameters().Length == 1).MakeGenericMethod(t);
                member = create.Invoke(null, [l])!; m = member.GetType(); break;
            }
            case "dictionary-key": { var d = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(t, typeof(int)))!; d.Add(sample, 1); member = d; m = d.GetType(); break; }
            case "dictionary-value": { var d = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), t))!; d.Add("k", sample); member = d; m = d.GetType(); break; }
            case "in-patch":
                member = typeof(Patch).GetMethod("Value")!.MakeGenericMethod(t).Invoke(null, [sample])!; m = member.GetType(); break;
            case "polymorphic-derived-property":
            {
                var derived = vo ? e.PolyDerived : e.PolyDerivedPlain;
                var inst = Activator.CreateInstance(derived)!; derived.GetProperty("Value")!.SetValue(inst, sample);
                member = inst; m = vo ? e.PolyBase : e.PolyBasePlain; break;
            }
            default: throw new ArgumentException("unknown position " + position);
        }
        var ct = Family(containing, style, m);
        object c;
        if (style == "C") c = Activator.CreateInstance(ct, member)!;
        else
        {
            c = Activator.CreateInstance(ct)!;
            if (style == "F") ct.GetField("F")!.SetValue(c, member); else ct.GetProperty("P")!.SetValue(c, member);
        }
        return new(ct, c);
    }
}

/// <summary>
/// Input equivalence for the differential oracles: the wrapped side and the value-object side must be the same
/// shape under the same options, differing only where the value object stands in for its wrapped type. A
/// mismatch is a harness error, never a pass or a product violation.
/// </summary>
public static class Equivalence
{
    public static string? Shapes(Type plain, Type vo, Entry e)
    {
        if (plain == vo) return null;
        if (vo == e.Vo) return plain == e.Plain || Nullable.GetUnderlyingType(plain) == e.Plain ? null : $"{Name(plain)} stands where {Name(vo)} should face {Name(e.Plain)}";
        // A nullable struct value object faces its wrapped type made nullable, or the wrapped class itself (already nullable).
        if (Nullable.GetUnderlyingType(vo) == e.Vo) return Nullable.GetUnderlyingType(plain) == e.Plain || (plain == e.Plain && !plain.IsValueType) ? null : $"{Name(plain)} against {Name(vo)}";
        if (plain.IsArray && vo.IsArray) return Shapes(plain.GetElementType()!, vo.GetElementType()!, e);
        if (plain.IsGenericType && vo.IsGenericType && plain.GetGenericTypeDefinition() == vo.GetGenericTypeDefinition())
        {
            var pa = plain.GetGenericArguments(); var va = vo.GetGenericArguments();
            for (var i = 0; i < pa.Length; i++) if (Shapes(pa[i], va[i], e) is { } why) return why;
            return null;
        }
        // The generated stand-ins (the polymorphic bases and derived types): one per side by design, equal in
        // structure: the same properties by name, each of an equivalent type, and the same polymorphism.
        if (plain.IsClass && vo.IsClass && !plain.IsGenericType && !vo.IsGenericType)
        {
            var pp = plain.GetProperties().ToDictionary(x => x.Name); var vp = vo.GetProperties().ToDictionary(x => x.Name);
            if (!pp.Keys.Order().SequenceEqual(vp.Keys.Order())) return $"{Name(plain)} and {Name(vo)} have different properties";
            foreach (var (name, prop) in pp) if (Shapes(prop.PropertyType, vp[name].PropertyType, e) is { } why) return $"{name}: {why}";
            var pd = plain.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), false).Length;
            var vd = vo.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), false).Length;
            if (pd != vd) return $"{Name(plain)} and {Name(vo)} declare {pd} and {vd} derived types";
            if ((plain.BaseType is { } pb && pb != typeof(object)) || (vo.BaseType is { } vb && vb != typeof(object)))
                return Shapes(plain.BaseType!, vo.BaseType!, e);
            return null;
        }
        return $"{Name(plain)} and {Name(vo)} are different shapes";
    }

    public static string? Options(JsonSerializerOptions plain, JsonSerializerOptions vo, bool allowConverters)
    {
        if (ReferenceEquals(plain, vo)) return null;
        foreach (var p in typeof(JsonSerializerOptions).GetProperties())
        {
            if (p.GetIndexParameters().Length > 0 || (allowConverters && p.Name == "Converters")) continue;
            var a = Print(p.GetValue(plain)); var b = Print(p.GetValue(vo));
            if (a != b) return $"options differ in {p.Name}: {a} vs {b}";
        }
        return null;
    }

    static string Print(object? v) => v switch
    {
        null => "null",
        string or bool or char or int or Enum => v.ToString()!,
        System.Collections.IEnumerable list => "[" + string.Join(",", list.Cast<object?>().Select(x => x?.GetType().FullName)) + "]",
        _ => v.GetType().FullName!,
    };

    static string Name(Type t) => t.IsGenericType ? t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">" : t.Name;
}

public static class JsonParity
{
    static (string? Json, Exception? Ex) Ser(Held h, JsonSerializerOptions o)
    { try { return (JsonSerializer.Serialize(h.Value, h.Declared, o), null); } catch (Exception ex) { return (null, ex); } }
    static (string? Json, Exception? Ex) De(string text, Held h, JsonSerializerOptions o)
    {
        try { var v = JsonSerializer.Deserialize(text, h.Declared, o); return (v is null ? "null" : JsonSerializer.Serialize(v, h.Declared, o), null); }
        catch (Exception ex) { return (null, ex); }
    }

    static string Limit(Entry e) => e.Wrapped switch
    {
        "int" => "2147483647", "long" => "9223372036854775807", "byte" => "255", "ulong" => "18446744073709551615",
        "decimal" => "79228162514264337593543950335", "double" => "1.7976931348623157E+308", "bool" => "true",
        "string" => "\"" + new string('x', 70000) + "\"", "char" => "\"\\uffff\"", "enum" => "2147483647", "flags-enum" => "7",
        "DateTime" => "\"9999-12-31T23:59:59.9999999Z\"", "DateTimeOffset" => "\"9999-12-31T23:59:59.9999999+00:00\"",
        "DateOnly" => "\"9999-12-31\"", "TimeOnly" => "\"23:59:59.9999999\"", "TimeSpan" => "\"10675199.02:48:05.4775807\"",
        "Uri" => "\"https://example.com/" + new string('p', 3000) + "\"", "NodaTime.Instant" => "\"9999-12-31T23:59:59.999999999Z\"",
        "own-record-class" => "{\"A\":\"x\",\"B\":2147483647}", "Guid" => "\"ffffffff-ffff-ffff-ffff-ffffffffffff\"",
        _ => "\"Beta\""
    };

    /// <summary>Text a position's JSON takes for an input.json value, built from the plain side's JSON by replacing the sample's own text.</summary>
    public static string InputFor(string input, Entry e, JsonSerializerOptions o, string jp, string position) => Input(input, e, o, jp, position);
    static string Input(string input, Entry e, JsonSerializerOptions o, string jp, string position)
    {
        string sample;
        try { sample = JsonSerializer.Serialize(e.Valid(), e.Plain, o); } catch { return jp; }
        var at = jp.IndexOf(sample, StringComparison.Ordinal);
        string Swap(string with) => at < 0 ? jp : jp[..at] + with + jp[(at + sample.Length)..];
        switch (input)
        {
            case "valid": return jp;
            case "refused-by-create":
                if (e.Refused() is not { } rf) return jp;
                try { return Swap(JsonSerializer.Serialize(rf, e.Plain, o)); } catch { return jp; }
            case "wrong-token": return Swap(sample.StartsWith('"') || sample.StartsWith('{') ? "[]" : "\"zz\"");
            case "null": return Swap("null");
            case "empty-string": return Swap("\"\"");
            case "at-type-limit": return Swap(Limit(e));
            case "quoted-number": return Swap(sample.StartsWith('"') ? sample : "\"" + sample + "\"");
            case "escape-sequence":
                return sample.StartsWith('"') && sample.Length > 3 ? Swap(sample[..1] + "\\u00" + ((int)sample[1]).ToString("x2") + sample[2..]) : jp;
            case "duplicate-property": return jp.StartsWith('{') && jp.Length > 2 ? "{" + jp[1..^1] + "," + jp[1..^1] + "}" : jp;
            case "metadata-property": return jp.StartsWith('{') ? (jp.Length > 2 ? "{\"$id\":\"1\"," + jp[1..] : "{\"$id\":\"1\"}") : jp;
            default: throw new ArgumentException("input " + input);
        }
    }

    /// <summary>True when the value object may legitimately refuse (or differ on) an input the wrapped type reads: the documented differences.</summary>
    public static bool ReadMayDiffer(Row r, Entry e, string input, Exception? xv)
    {
        if (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class" && xv is NotSupportedException) return true;
        if (Validated(e) && input == "refused-by-create" && r.Get("json.Converters") != "StoredJsonConverterFactory") return true;
        if (input == "null" && e.Vo.IsValueType) return true;
        if (input == "null" && !e.Vo.IsValueType && e.Plain.IsValueType) return true;
        if (e.Kind == "closed-set" && input != "valid") return true;
        return false;
    }
    static bool Validated(Entry e) => e.Kind.StartsWith("validated");

    /// <summary>
    /// The oracle "JSON parity" (differential) as L01's checks: the value object's write against the wrapped twin in the same shape
    /// under the same options (the-underlying-types-json, or as-a-dictionary-key-too where the value object is a dictionary key), and
    /// the read of the twin's document with the row's input.json against the twin's read (read-as-the-underlying-type-reads). Under an
    /// application's converter for the value object, which writes it instead of the generated one, the read is that converter's own
    /// document read back. Every check runs; returns the first failure.
    /// </summary>
    public static string? Check(Row r, Entry e, JsonSerializerOptions o, string position, string containing, string input, JsonSerializerOptions? oPlain = null)
    {
        oPlain ??= o;
        var plain = e.Valid();
        var voV = Make.Vo(e, plain);
        var hp = Pos.Build(position, containing, e, false, plain);
        var hv = Pos.Build(position, containing, e, true, voV);
        // Input equivalence: the two sides may differ only by the substituted type, not in shape or options.
        if (Equivalence.Shapes(hp.Declared, hv.Declared, e) is { } whyShape) { r.Outcome("harness-error", "inputs not equivalent: " + whyShape); return null; }
        if (Equivalence.Options(oPlain, o, allowConverters: e.Kind == "closed-set") is { } whyOptions) { r.Outcome("harness-error", "inputs not equivalent: " + whyOptions); return null; }
        var (jp, ep) = Ser(hp, oPlain); var (jv, ev) = Ser(hv, o);
        var write = position == "dictionary-key" ? "as-a-dictionary-key-too" : "the-underlying-types-json";
        const string read = "read-as-the-underlying-type-reads";
        string? fw, fr;
        if (r.Get("json.Converters") == "own-for-value-object")
        {
            // "Your own converter wins" (AspNetCore README#what-the-document-says): the application's converter writes the value object
            // instead of the generated one, so the twin's bytes are none of its. claims.json declares the write inapplicable there.
            fw = r.Check(write, Baseline.Twin, c => c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), "write"));
            fr = r.Check(read, Baseline.RoundTrip, c => OwnVoRead(c, e, hp, hv, oPlain, o, jv, ev, ep));
            return fw ?? fr;
        }
        fw = r.Check(write, Baseline.Twin, c => Write(c, e, position, jp, ep, jv, ev));
        fr = r.Check(read, Baseline.Twin, c => Read(c, e, position, input, hp, hv, oPlain, o, jp));
        return fw ?? fr;
    }

    /// <summary>The value object's document is the wrapped twin's, byte for byte, in the same shape under the same options.</summary>
    static string? Write(Check c, Entry e, string position, string? jp, Exception? ep, string? jv, Exception? ev)
    {
        // System.Text.Json writes byte[] as base64 and an array of value objects over byte as a JSON array (KB-038); claims.json declares it
        if (e.Wrapped == "byte" && position == "array-element" && jp != jv) return c.Inapplicable(Cite.NotPromised("a-byte-array-is-base64"), "write");
        if (ep is not null && ev is not null)
            // Both refused: evidence about refusing, not about serializing, and nothing to compare.
            return c.Inconclusive(ep.GetType() != ev.GetType()
                ? $"write: both refuse, differently: the wrapped type {ep.GetType().Name}, the value object {ev.GetType().Name}"
                : $"write: the options refuse the shape for the wrapped type too: {ep.GetType().Name}: {Cut(ep.Message)}");
        if (ep is null && ev is not null)
        {
            // Text-matching site, kept in this step (WS2 step 4): the depth refusal is told by the message's words. Baseline gap: no twin in
            // the row shows System.Text.Json charging a converter a level, and no record or fact cites it (the three-sided differential).
            if (c.Is("json.MaxDepth", "3") && ev is JsonException && ev.Message.Contains("maximum allowed depth")) { c.Note(NoteKeys.StjMaxdepth, "charges custom converters"); return null; } // inherent to STJ: any custom converter costs a level (design note D1)
            if (c.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class" && ev is NotSupportedException) return c.Refused(Cite.NotPromised("preserved-references-over-a-walked-class"), "write");
            return $"wrapped serialized ({jp}) but the value object threw {ev.GetType().Name}: {ev.Message}";
        }
        if (ep is not null && ev is null) return $"value object serialized ({jv}) where the wrapped type threw {ep.GetType().Name}: {ep.Message}";
        // a closed set travels as its name, which no key policy renames; claims.json declares it
        if (jp != jv && e.Kind == "closed-set" && position == "dictionary-key" && c.Get("json.DictionaryKeyPolicy") is not (null or "default")) return c.Inapplicable(Cite.NotPromised("a-closed-set-key-is-its-name"), "write");
        if (jp != jv) return $"JSON differs: wrapped {Cut(jp)} vs value object {Cut(jv)}";
        c.Note(NoteKeys.Compared, "write");
        return null;
    }

    /// <summary>The wrapped twin's document, with the row's input.json, read into both shapes: each reads it as the other does, or refuses as a document says.</summary>
    static string? Read(Check c, Entry e, string position, string input, Held hp, Held hv, JsonSerializerOptions oPlain, JsonSerializerOptions o, string? jp)
    {
        if (e.Wrapped == "byte" && position == "array-element") return c.Inapplicable(Cite.NotPromised("a-byte-array-is-base64"), "read");
        if (e.Kind == "closed-set" && position == "dictionary-key" && c.Get("json.DictionaryKeyPolicy") is not (null or "default")) return c.Inapplicable(Cite.NotPromised("a-closed-set-key-is-its-name"), "read");
        if (jp is null) return c.Inconclusive("read: the wrapped type is not written under these options, so there is no document to read");
        var text = Input(input, e, oPlain, jp, position);
        var (dp, xp) = De(text, hp, oPlain); var (dv, xv) = De(text, hv, o);
        var stored = c.Get("json.Converters") == "StoredJsonConverterFactory";
        if (xp is not null && xv is not null)
            return c.Inconclusive(xp.GetType() != xv.GetType() && !(xp is JsonException && xv is JsonException)
                ? $"read {input}: both refuse, differently: the wrapped type {xp.GetType().Name}, the value object {xv.GetType().Name}"
                : $"read {input}: both refuse: {xp.GetType().Name}");
        if (xp is null && xv is not null)
        {
            if (c.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class" && xv is NotSupportedException) return c.Refused(Cite.NotPromised("preserved-references-over-a-walked-class"), "read");
            // The text-matching site of the write, at the read: the depth refusal told by its words, no twin in the row showing it.
            if (c.Is("json.MaxDepth", "3") && xv is JsonException && xv.Message.Contains("maximum allowed depth")) { c.Note(NoteKeys.StjMaxdepth, "read: charges custom converters"); return null; }
            // The documented refusals, each the JsonException the generated converter throws.
            if (!(xv is JsonException) && ((Validated(e) && input == "refused-by-create" && !stored) || (input == "null" && e.Vo.IsValueType) || (e.Kind == "closed-set" && input != "valid")))
                return $"input {input}: the refusal is {xv.GetType().Name}, not JsonException: {Cut(xv.Message)}";
            if (Validated(e) && input == "refused-by-create" && !stored) return c.Refused(Cite.NotPromised("a-validated-value-object-refuses-what-create-refuses"), $"read {input}");
            if (input == "null" && e.Vo.IsValueType) return c.Refused(Cite.NotPromised("a-struct-refuses-a-json-null"), $"read {input}");
            if (e.Kind == "closed-set" && input != "valid") return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#matching-parsing-and-binding"), $"read {input}");   // "Unknown names are rejected."
            return $"input {input}: wrapped read it, value object threw {xv.GetType().Name}: {Cut(xv.Message)}\n  text={Cut(text)}";
        }
        // a reference-type value object takes JSON null as null, as any class does
        if (xp is not null && xv is null && input == "null" && !e.Vo.IsValueType && e.Plain.IsValueType) return c.Inapplicable(Cite.Docs("docs/json.md#null-and-missing"), "read null");
        if (xp is not null && xv is null) return $"input {input}: value object read what the wrapped type refused ({xp.GetType().Name}: {Cut(xp.Message)})\n  text={Cut(text)}";
        if (Validated(e) && input == "refused-by-create" && text != jp && !stored)
            return $"validated value object accepted the refused value{(position == "dictionary-key" ? " as a dictionary key" : "")}: {Cut(text)}";
        if (dp != dv) return $"input {input}: read-back differs: wrapped {Cut(dp)} vs value object {Cut(dv)}";
        c.Note(NoteKeys.Compared, "read " + input);
        return null;
    }

    /// <summary>
    /// Under an application's converter for the value object, the generated converter does not run: the value object's document is that
    /// converter's, and it reads back as written under the row's options. Where the value object's document cannot be written, a converter
    /// the application registers for the wrapped type at the same position is the baseline.
    /// </summary>
    static string? OwnVoRead(Check c, Entry e, Held hp, Held hv, JsonSerializerOptions oPlain, JsonSerializerOptions o, string? jv, Exception? ev, Exception? ep)
    {
        var oOwn = new JsonSerializerOptions(oPlain);
        oOwn.Converters.Insert(0, (System.Text.Json.Serialization.JsonConverter)Activator.CreateInstance(typeof(OwnWrappedConverter<>).MakeGenericType(Make.Held(e.Plain)), JsonRow.Inner(e))!);
        var (jo, eo) = Ser(hp, oOwn);
        if (ev is not null)
        {
            // System.Text.Json refuses any custom converter there (MaxDepth counts its value one level deeper, design note D1), the wrapped
            // type's under a converter of the application's alike
            if (ep is not null || (eo is not null && eo.GetType() == ev.GetType())) return c.Inconclusive($"the application's converter cannot write here, for the wrapped type too: {ev.GetType().Name}");
            return $"own VO converter row: VO serialization threw {ev.GetType().Name}: {ev.Message}";
        }
        c.Note(NoteKeys.OwnVo);
        var (back, eb) = De(jv!, hv, o);
        if (eb is not null)
        {
            // the same converter for the wrapped type, at the same position: its own document does not read back under these options either
            var ebOwn = jo is null ? eo : De(jo, hp, oOwn).Ex;
            if (ebOwn is not null && ebOwn.GetType() == eb.GetType()) return c.Inconclusive($"the application's converter's document does not read back under these options, for the wrapped type too: {eb.GetType().Name}");
            return "own VO converter: read-back threw " + eb.GetType().Name + ": " + eb.Message;
        }
        if (back != jv) return $"own VO converter round trip differs: {jv} vs {back}";
        return null;
    }
    static string Cut(string? s) => s is null ? "(null)" : s.Length > 200 ? s[..200] + "..." : s;
}

public static class EntryExt
{
    public static object? Refused(this Entry e) => e.Kind.StartsWith("validated") ? e.RefusedFn() : null;
}
