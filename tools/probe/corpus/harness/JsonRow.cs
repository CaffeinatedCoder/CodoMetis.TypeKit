// Applies a row's json.* axes to JsonSerializerOptions (reflection for every plain bool/enum setting, hand-listed values by name).
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.ValueObjects;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
namespace Probes.Shapes;

public sealed class UpperPolicy : JsonNamingPolicy { public override string ConvertName(string name) => name.ToUpperInvariant(); }

public static class Make
{
    /// <summary>
    /// The type a nullable one holds: under http.nullability=nullable an entry's Vo and Plain are <c>T?</c>, the types the
    /// endpoints declare, while the value object is still made by <c>From(T)</c> and read through its <c>Value</c>.
    /// </summary>
    public static Type Held(Type t) => Nullable.GetUnderlyingType(t) ?? t;

    /// <summary>Creates the value object over a plain sample (From / FromKnownGood), with optional parameters filled.</summary>
    public static object Vo(Entry e, object plain)
    {
        if (e.Kind == "closed-set") return PSet.All.First(x => x.Name == (string)plain);
        var m = Held(e.Vo).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(x => (x.Name is "FromKnownGood" or "From") && x.GetParameters().Length > 0 && x.GetParameters()[0].ParameterType == Held(e.Plain));
        var args = m.GetParameters().Select((p, i) => i == 0 ? plain : Type.Missing).ToArray();
        return m.Invoke(null, BindingFlags.OptionalParamBinding | BindingFlags.Default, null, args, null)!;
    }
    public static object Unwrap(Entry e, object vo) => e.Kind == "closed-set" ? ((PSet)vo).Name : Held(e.Vo).GetProperty("Value")!.GetValue(vo)!;
}

/// <summary>A converter an application registers for the wrapped type: writes "own:" + the default JSON.</summary>
public sealed class OwnWrappedConverter<T>(JsonSerializerOptions inner) : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? throw new JsonException("null");
        if (!s.StartsWith("own:")) throw new JsonException("not own");
        return JsonSerializer.Deserialize<T>(s[4..], inner);
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue("own:" + JsonSerializer.Serialize(value, inner));
    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Read(ref reader, typeToConvert, options)!;
    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WritePropertyName("own:" + JsonSerializer.Serialize(value, inner));
}

/// <summary>A converter an application registers for the value object itself: writes "vo:" + the wrapped value's default JSON.</summary>
public sealed class OwnVoFactory(Entry e, JsonSerializerOptions inner) : JsonConverterFactory
{
    public override bool CanConvert(Type t) => t == Make.Held(e.Vo);   // System.Text.Json asks for T, never for T?
    public override JsonConverter? CreateConverter(Type t, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OwnVo<>).MakeGenericType(t), e, inner)!;
}
public sealed class OwnVo<V>(Entry e, JsonSerializerOptions inner) : JsonConverter<V>
{
    public override V? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? throw new JsonException("null");
        if (!s.StartsWith("vo:")) throw new JsonException("not vo");
        return (V)Make.Vo(e, JsonSerializer.Deserialize(s[3..], Make.Held(e.Plain), inner)!);
    }
    public override void Write(Utf8JsonWriter writer, V value, JsonSerializerOptions options) =>
        writer.WriteStringValue("vo:" + JsonSerializer.Serialize(Make.Unwrap(e, value!), Make.Held(e.Plain), inner));
    public override V ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Read(ref reader, typeToConvert, options)!;
    public override void WriteAsPropertyName(Utf8JsonWriter writer, V value, JsonSerializerOptions options) =>
        writer.WritePropertyName("vo:" + JsonSerializer.Serialize(Make.Unwrap(e, value!), Make.Held(e.Plain), inner));
}

public sealed record Applied(JsonSerializerOptions? Options, string? Refused);

public static class JsonRow
{
    public static JsonSerializerOptions Inner(Entry e)
    {
        var o = new JsonSerializerOptions();
        if (e.Wrapped == "NodaTime.Instant") o.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        return o;
    }

    /// <summary>Applies every json.* axis of the row to <paramref name="into"/> (or a new instance). Refused = STJ rejected a combination with an exception of its own (a pass).</summary>
    public static Applied Apply(Row r, Entry e, JsonSerializerOptions? into = null)
    {
        var o = into ?? new JsonSerializerOptions();
        var dropped = new List<string>();
        if (e.Wrapped == "NodaTime.Instant") o.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        if (r.Get("json.PreferredObjectCreationHandling") == "Populate" && r.Get("json.ReferenceHandler") is not (null or "default"))
        {
            dropped.Add("json.PreferredObjectCreationHandling=Populate (STJ: incompatible with reference handling)");
            r.Substituted("json.PreferredObjectCreationHandling", "default", "STJ: incompatible with reference handling");
            r = r.With("json.PreferredObjectCreationHandling", "default");
        }
        // resolver before chain: a chain on top of a resolver is a combination STJ may refuse
        foreach (var kv in r.All.Where(kv => kv.Key.StartsWith("json.") && kv.Value != "default").OrderBy(kv => kv.Key == "json.TypeInfoResolverChain" ? 1 : 0))
        {
            try { Set(r, o, kv.Key[5..], kv.Value, e); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                // STJ refuses this setting with an exception of its own: a pass for the pairs of that value; the rest of the row is still probed
                dropped.Add($"{kv.Key}={kv.Value} ({ex.GetType().Name})");
                r.Substituted(kv.Key, "default", "STJ refused it: " + ex.GetType().Name);
            }
        }
        r.Note("dropped", string.Join(",", dropped));
        return new(o, dropped.Count == 0 ? null : string.Join(",", dropped));
    }

    static JsonNamingPolicy? Policy(string v) => v switch
    {
        "CamelCase" => JsonNamingPolicy.CamelCase, "SnakeCaseLower" => JsonNamingPolicy.SnakeCaseLower, "SnakeCaseUpper" => JsonNamingPolicy.SnakeCaseUpper,
        "KebabCaseLower" => JsonNamingPolicy.KebabCaseLower, "KebabCaseUpper" => JsonNamingPolicy.KebabCaseUpper, "own-upper-case" => new UpperPolicy(),
        _ => throw new ArgumentException("unknown policy " + v)
    };

    static bool ListedInContext(Row r, Entry e)
    {
        try
        {
            var plain = e.Valid(); var vo = Make.Vo(e, plain);
            var pos = r["shape.position"]; var con = (r.Get("shape.containing") ?? "class");
            var hp = Pos.Build(pos, con, e, false, plain); var hv = Pos.Build(pos, con, e, true, vo);
            var ok = ProbeContext.Default.GetTypeInfo(hp.Declared) is not null && ProbeContext.Default.GetTypeInfo(hv.Declared) is not null;
            r.Note(ok ? "ctx-strict" : "ctx-fallback");
            if (!ok) r.Substituted("json.TypeInfoResolver", "source-generated-context+reflection-fallback", "the shape is not listed in the context");
            return ok;
        }
        catch { return false; }
    }

    static void Set(Row row, JsonSerializerOptions o, string name, string v, Entry e)
    {
        switch (name)
        {
            case "PropertyNamingPolicy": o.PropertyNamingPolicy = Policy(v); return;
            case "DictionaryKeyPolicy": o.DictionaryKeyPolicy = Policy(v); return;
            case "ReferenceHandler": o.ReferenceHandler = v == "Preserve" ? ReferenceHandler.Preserve : ReferenceHandler.IgnoreCycles; return;
            case "Encoder": o.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; return;
            case "MaxDepth": o.MaxDepth = int.Parse(v); return;
            case "Converters":
                switch (v)
                {
                    case "own-for-wrapped": o.Converters.Insert(0, (JsonConverter)Activator.CreateInstance(typeof(OwnWrappedConverter<>).MakeGenericType(Make.Held(e.Plain)), Inner(e))!); break;
                    case "own-for-value-object": o.Converters.Insert(0, new OwnVoFactory(e, Inner(e))); break;
                    case "JsonStringEnumConverter": o.Converters.Add(new JsonStringEnumConverter()); break;
                    case "StoredJsonConverterFactory": o.Converters.Add(new StoredJsonConverterFactory()); break;
                    default: throw new ArgumentException("unknown converter " + v);
                }
                return;
            case "TypeInfoResolver":
                if (v == "source-generated-context") o.TypeInfoResolver = ListedInContext(row, e) ? ProbeContext.Default : JsonTypeInfoResolver.Combine(ProbeContext.Default, new DefaultJsonTypeInfoResolver());
                else o.TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ti => { foreach (var p in ti.Properties) p.Name += "_x"; } } };
                return;
            case "TypeInfoResolverChain":
                o.TypeInfoResolverChain.Add(ProbeContext.Default); o.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver()); return;
        }
        var prop = typeof(JsonSerializerOptions).GetProperty(name) ?? throw new ArgumentException("no json option " + name);
        object value = prop.PropertyType == typeof(bool) ? bool.Parse(v) : prop.PropertyType.IsEnum ? Enum.Parse(prop.PropertyType, v) : throw new ArgumentException($"no hand value for {name}");
        try { prop.SetValue(o, value); }
        catch (TargetInvocationException ex) { throw ex.InnerException!; }
    }
}
