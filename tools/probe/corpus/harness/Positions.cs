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
            case "nullable-property": m = t.IsValueType ? typeof(Nullable<>).MakeGenericType(t) : t; member = sample; break;
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

    /// <summary>The oracle "JSON parity" (differential): a shape with the value object and the same shape with the wrapped type give the same
    /// bytes under the same options and read each other's JSON. Returns null for a pass, otherwise the failure.</summary>
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
        var ownVo = r.Get("json.Converters") == "own-for-value-object";
        var preserveOwn = r.Get("json.ReferenceHandler") == "Preserve";
        if (ownVo)
        {
            if (ev is not null && ep is null) return $"own VO converter row: VO serialization threw {ev.GetType().Name}: {ev.Message}";
            r.Note("own-vo");
            if (jv is not null)
            {
                var (back, eb) = De(jv, hv, o);
                if (eb is not null) { if (r.Get("json.UnmappedMemberHandling") == "Disallow" || input != "valid") { r.Note("own-vo:read-back-refused"); return null; } return "own VO converter: read-back threw " + eb.GetType().Name + ": " + eb.Message; }
                if (back != jv) return $"own VO converter round trip differs: {jv} vs {back}";
            }
            return null;
        }
        if (e.Wrapped == "byte" && position == "array-element" && jp != jv) { r.Note("byte[]-is-base64-inherent"); return null; } // STJ writes byte[] as base64; an array of byte value objects is a JSON array
        if (ep is not null && ev is not null)
        {
            // Both refused: evidence about refusing, not about serializing. Refusing differently is no agreement at all.
            if (ep.GetType() != ev.GetType()) { r.Note("both-threw-differently:" + ep.GetType().Name + "/" + ev.GetType().Name); return null; }
            r.Note("both-threw:" + ep.GetType().Name + ":" + ev.Message[..Math.Min(70, ev.Message.Length)]); return null;
        }
        if (ep is null && ev is not null)
        {
            if (r.Is("json.MaxDepth", "3") && ev is JsonException && ev.Message.Contains("maximum allowed depth")) { r.Note("stj-maxdepth-charges-custom-converters"); return null; } // inherent to STJ: any custom converter costs a level (design note D1)
            if (preserveOwn && e.Wrapped == "own-record-class" && ev is NotSupportedException) { r.Note("preserve-documented"); return null; } // documented (Generators README#json)
            return $"wrapped serialized ({jp}) but the value object threw {ev.GetType().Name}: {ev.Message}";
        }
        if (ep is not null && ev is null) return $"value object serialized ({jv}) where the wrapped type threw {ep.GetType().Name}: {ep.Message}";
        if (jp != jv && e.Kind == "closed-set" && position == "dictionary-key" && r.Get("json.DictionaryKeyPolicy") != "default") { r.Note("closed-set-key-unpoliced"); return null; } // a closed set travels as its name
        if (jp != jv)
        {
            // intended: a struct VO over... none. DateTime is written as UTC; sample is UTC so equal.
            return $"JSON differs: wrapped {Cut(jp)} vs value object {Cut(jv)}";
        }
        // reading
        var text = Input(input, e, oPlain, jp!, position);
        var (dp, xp) = De(text, hp, oPlain); var (dv, xv) = De(text, hv, o);
        if (xp is not null && xv is not null)
        {
            if (xp.GetType() != xv.GetType() && !(xp is JsonException && xv is JsonException)) { r.Note("both-threw-differently(read):" + xp.GetType().Name + "/" + xv.GetType().Name); return null; }
            r.Note("read-both-threw:" + xp.GetType().Name); return null;
        }
        if (xp is null && xv is not null)
        {
            if (preserveOwn && e.Wrapped == "own-record-class" && xv is NotSupportedException) return null;
            if (Validated(e) && input == "refused-by-create" && r.Get("json.Converters") != "StoredJsonConverterFactory") return xv is JsonException ? null : $"refusal is {xv.GetType().Name}, not JsonException: {xv.Message}";
            if (input == "null" && e.Vo.IsValueType) return null; // intended: a struct value object refuses JSON null
            if (e.Kind == "closed-set" && input != "valid") return xv is JsonException ? null : $"closed set refusal is {xv.GetType().Name}"; // a closed set has the members it has
            return $"input {input}: wrapped read it, value object threw {xv.GetType().Name}: {Cut(xv.Message)}\n  text={Cut(text)}";
        }
        if (xp is not null && xv is null && input == "null" && !e.Vo.IsValueType && e.Plain.IsValueType) return null; // a reference-type value object takes JSON null as null, as any class does (README: "a JSON null for a struct value object")
        if (xp is not null && xv is null) return $"input {input}: value object read what the wrapped type refused ({xp.GetType().Name}: {Cut(xp.Message)})\n  text={Cut(text)}";
        if (Validated(e) && input == "refused-by-create" && text != jp && r.Get("json.Converters") != "StoredJsonConverterFactory" && position != "dictionary-key")
            return $"validated value object accepted the refused value: {Cut(text)}";
        if (dp != dv) return $"input {input}: read-back differs: wrapped {Cut(dp)} vs value object {Cut(dv)}";
        r.Note("compared"); return null;
    }
    static string Cut(string? s) => s is null ? "(null)" : s.Length > 200 ? s[..200] + "..." : s;
}

public static class EntryExt
{
    public static object? Refused(this Entry e) => e.Kind.StartsWith("validated") ? e.RefusedFn() : null;
}
