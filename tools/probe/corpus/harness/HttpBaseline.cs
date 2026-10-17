// The baseline every http claim shares: under the row's options, a payload with the value object behaves like the same payload with the wrapped
// type (response bytes, request reading, parameter binding, OpenAPI schema), in the hosting models the row's http.host names.
// Oracle: differential (JSON/wire/schema parity), each comparison a check (Row.Check); a difference a document makes intended ends its check
// as that citation (Cite), and one no document makes intended is compared.
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Probes.Shapes;

public static class HttpBaseline
{
    static string Cut(string? s) => s is null ? "(null)" : s.Length > 220 ? s[..220] + "..." : s;

    /// True only where the shape's Create refuses exactly the wrapped type's limit (the validated byte refuses 255):
    /// anywhere else a refused limit is the value object's to explain.
    static bool RefusesItsLimit(Entry e)
    {
        var refused = e.RefusedFn();   // the plain value the shape's Create refuses
        if (refused is null) return false;
        var t = Make.Held(e.Plain);
        return new[] { "MaxValue", "MinValue" }.Any(f => t.GetField(f)?.GetValue(null) is { } limit && limit.Equals(refused));
    }

    /// <summary>
    /// One wire comparison with the twin, inside its check <paramref name="c"/>: the wrapped type's answer <paramref name="p"/> and the
    /// value object's <paramref name="v"/>. A difference a document makes intended ends the check as that document's citation; BaseProbe
    /// calibrates the host on C07's rows, whose claim promises nothing of the wire, so these cite the sentence itself, not a not-promised
    /// entry of C07.
    /// </summary>
    static string? Same(Check c, Row r, Entry e, string what, Hx.Wire p, Hx.Wire v, string input = "valid")
    {
        r.Log.AppendLine($"{what}: plain {(int)p.Status} {Cut(p.Body)} {p.Thrown} | vo {(int)v.Status} {Cut(v.Body)} {v.Thrown}");
        bool ok(Hx.Wire w) => (int)w.Status is >= 200 and < 300;
        if (!ok(p) && !ok(v)) { c.Note(NoteKeys.Compared, what + ": both refused"); return null; }
        // "Your own converter wins": the application's converter writes the value object instead of the generated one.
        if (r.Is("json.Converters", "own-for-value-object") && ok(v) && ok(p)) return c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), what);
        if (ok(p) && !ok(v))
        {
            if (ReadRefusedAsDocumented(c, r, e, input, what)) return null;
            // the same: the value object's array reads a JSON array of numbers, never the base64 string the twin's byte[] wrote and reads
            if (ByteArray(r, e)) return c.Inapplicable(Cite.Docs("docs/json.md#the-wrapped-types-json"), what);
            // Text-matching site, kept in this step: the depth refusal is told by the message's words. Baseline gap: no twin in the row
            // shows System.Text.Json charging a converter a level (the three-sided differential, next step), and no record cites it.
            if (r.Is("json.MaxDepth", "3") && (v.Thrown ?? v.Body).Contains("maximum allowed depth")) { r.Note(NoteKeys.StjMaxdepth, "value-object side"); return null; }
            // A closed set travels as its member's name; the wrapped type under the application's converter is no reference.
            if (e.Kind == "closed-set" && r.Is("json.Converters", "own-for-wrapped")) return c.Inapplicable(Cite.Docs("README.md#what-you-get"), what);
            // The validated shapes' Create refuses the wrapped type's limit on purpose, so the limit input is a refusal.
            if (e.Kind.StartsWith("validated") && input == "at-type-limit" && RefusesItsLimit(e)) return c.Refused(Cite.Docs("docs/json.md#when-a-value-is-rejected"), what);
            return (e.Wrapped == "NodaTime.Instant" && what.StartsWith("mvc bind") ? "[F1] " : "") + $"{what}: wrapped answered {(int)p.Status} {Cut(p.Body)}, value object answered {(int)v.Status} {Cut(v.Body)} {v.Thrown}";
        }
        if (!ok(p) && ok(v))
        {
            // docs/json.md "Null and missing": a struct value object rejects a JSON null; a sealed partial record reads it as null,
            // like any class, where the struct it wraps refuses it.
            if (input == "null" && !e.Vo.IsValueType && e.Plain.IsValueType) return c.Refused(Cite.Docs("docs/json.md#null-and-missing"), what);
            // Text-matching site, kept in this step: as the value-object side's above. Baseline gap: the same.
            if (r.Is("json.MaxDepth", "3") && (p.Thrown ?? p.Body).Contains("maximum allowed depth")) { r.Note(NoteKeys.StjMaxdepth, "wrapped side"); return null; }
        }
        if (!ok(p) && ok(v)) return $"{what}: value object answered {(int)v.Status} {Cut(v.Body)} where wrapped answered {(int)p.Status} {Cut(p.Body)} {p.Thrown}";
        if (p.Body != v.Body)
        {
            // A closed set travels as its member's name, never as the string or number standing in for it.
            if (e.Kind == "closed-set") return c.Inapplicable(Cite.Docs("README.md#what-you-get"), what);
            // docs/json.md "The wrapped type's JSON": an array of value objects over byte is a JSON array of numbers, where the twin, a byte[],
            // is one base64 string (KB-038).
            if (ByteArray(r, e)) return c.Inapplicable(Cite.Docs("docs/json.md#the-wrapped-types-json"), what);
            return $"{what}: bodies differ: wrapped {Cut(p.Body)} vs value object {Cut(v.Body)}";
        }
        c.Note(NoteKeys.Compared, what);
        return null;
    }

    /// <summary>An array of value objects over byte, whose twin is a byte[] (KB-038).</summary>
    static bool ByteArray(Row r, Entry e) => e.Wrapped == "byte" && r["shape.position"] == "array-element";

    /// <summary>What MVC writes for a type's default: null for no content (a reference, or a Nullable), else its JSON under MVC's options.</summary>
    static string? DefaultBody(WebApplication app, Type t) =>
        !t.IsValueType || Nullable.GetUnderlyingType(t) is not null ? null
        : JsonSerializer.Serialize(Activator.CreateInstance(t), t, app.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions);

    /// <summary>
    /// Whether an answer is the type's default as MVC writes it: 204 for null, else 200 with its JSON, compared as JSON, since MVC's
    /// formatter escapes with its own encoder where the options name none.
    /// </summary>
    static bool HoldsDefault(Hx.Wire w, string? body)
    {
        if (body is null) return w.Status == HttpStatusCode.NoContent;
        if (w.Status != HttpStatusCode.OK) return false;
        try { return JsonNode.DeepEquals(JsonNode.Parse(w.Body), JsonNode.Parse(body)); }
        catch (JsonException) { return false; }
    }

    /// <summary>A text the wrapped type does not read, for a twin that shows what MVC does with a binding failure; none where any text binds.</summary>
    static string? Unreadable(Entry e) => e.Wrapped is "string" or "Uri" or "own-record-class" ? null : "not-a-value";

    /// <summary>
    /// The value object refused a read the wrapped type answered, as a document says it does (JsonParity.ReadMayDiffer's reasons, the JSON
    /// family's, each cited by its sentence): the check ends as that refusal and true is returned.
    /// </summary>
    static bool ReadRefusedAsDocumented(Check c, Row r, Entry e, string input, string what)
    {
        if (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class") { c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), what); return true; }
        if (e.Kind.StartsWith("validated") && input == "refused-by-create" && r.Get("json.Converters") != "StoredJsonConverterFactory") { c.Refused(Cite.Docs("docs/json.md#when-a-value-is-rejected"), what); return true; }
        if (input == "null" && (e.Vo.IsValueType || e.Plain.IsValueType)) { c.Refused(Cite.Docs("docs/json.md#null-and-missing"), what); return true; }
        if (e.Kind == "closed-set" && input != "valid") { c.Refused(Cite.Docs("README.md#what-you-get"), what); return true; }
        return false;
    }

    /// <summary>Where two schemas first differ, and what each side has there: the comparison's 220 characters rarely reach it.</summary>
    static string FirstDifference(JsonNode? p, JsonNode? v, string path) =>
        Difference(p, v, path) is var (at, wp, wv) ? $"{at}: wrapped {Cut(wp?.ToJsonString() ?? "(absent)")} | value object {Cut(wv?.ToJsonString() ?? "(absent)")}" : "";

    /// <summary>The path where two schemas first differ, and each side's node there (null where it is absent).</summary>
    static (string, JsonNode?, JsonNode?) Difference(JsonNode? p, JsonNode? v, string path)
    {
        if (p is JsonObject po && v is JsonObject vo)
        {
            foreach (var k in po.Select(kv => kv.Key).Union(vo.Select(kv => kv.Key)).Order(StringComparer.Ordinal))
                if (!JsonNode.DeepEquals(po[k], vo[k])) return Difference(po[k], vo[k], path + "." + k);
        }
        else if (p is JsonArray pa && v is JsonArray va && pa.Count == va.Count)
        {
            for (var i = 0; i < pa.Count; i++)
                if (!JsonNode.DeepEquals(pa[i], va[i])) return Difference(pa[i], va[i], $"{path}[{i}]");
        }
        return (path, p, v);
    }

    /// <summary>
    /// BaseProbe's calibration of the shared host: every wire comparison a check of its own, named <paramref name="obligation"/> (BaseProbe
    /// runs on rows of C07, which promises nothing of the wire, so its checks are the implicit <c>claim</c>). Per side: the echo against the
    /// twin; the request read with the row's input.json, against the twin, or a closed set against its own response read back; the binding of
    /// the row's http.binding with a valid value against the twin, and with a value Create refuses against the documented 400. With
    /// <paramref name="schema"/>, the schema parity too (C21's check). Every check runs; the first failure is returned.
    /// </summary>
    public static async Task<string?> Run(Row r, Entry e, bool schema = true, string obligation = "claim")
    {
        WebApplication app;
        try { app = await Hx.Start(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }); }
        catch (Exception ex) { return "baseline host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
        await using var _ = app;
        var jopts = JsonRow.Apply(r, e).Options!;
        var position = r["shape.position"];
        var plainSample = e.Valid();
        var input = r.Get("input.json") ?? "valid";
        var ownVo = r.Is("json.Converters", "own-for-value-object");
        var bind = r["http.binding"];
        string? failed = null;
        var sides = new List<(string name, Func<string, string> path, string echo, string inn, string bindPre)>();
        if (Hx.HasMinimal(r)) sides.Add(("minimal", p => Hx.M(r, p), "/echo/", "/in/", "/bind/"));
        if (Hx.HasMvc(r)) sides.Add(("mvc", p => Hx.C(r, p), "/mvc/echo/", "/mvc/in/", "/mvc/bind/"));
        foreach (var (name, path, echo, inn, bindPre) in sides)
        {
            // 1. response side
            var ep = await Hx.Get(app, path(echo + "plain")); var ev = await Hx.Get(app, path(echo + "vo"));
            var f1 = r.Check(obligation, Baseline.Twin, c => Same(c, r, e, name + " echo", ep, ev)); failed ??= f1;
            // 2. request side, with the row's input.json, where the wrapped type's echo gave a body to change
            if ((int)ep.Status == 200)
            {
                string? f2;
                if (ownVo)
                    // "Your own converter wins": the application's converter reads the value object, so the twin's text is none of its;
                    // only that it reads its own response back is held, and the check earns nothing.
                    f2 = await r.Check(obligation, Baseline.RoundTrip, async c =>
                    {
                        var ip = await Hx.Send(app, HttpMethod.Post, path(inn + "plain"), ep.Body); var iv = await Hx.Send(app, HttpMethod.Post, path(inn + "vo"), ev.Body);
                        if ((int)ev.Status == 200 && (int)iv.Status != 200 && (int)ip.Status == 200) return $"{name} in: the value object cannot read its own response ({(int)iv.Status}): {Cut(iv.Body)}";
                        return c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), name + " in");
                    });
                else if (e.Kind == "closed-set")
                    // A closed set travels as its member's name (README "What you get"): the string standing in for it is no twin, so the
                    // value object reads its own response back.
                    f2 = await r.Check(obligation, Baseline.RoundTrip, async c =>
                    {
                        if ((int)ev.Status != 200) return c.Inconclusive($"{name} in: the value object's echo answered {(int)ev.Status}, no response of its own to read back");
                        var ip = await Hx.Send(app, HttpMethod.Post, path(inn + "plain"), ep.Body); var iv = await Hx.Send(app, HttpMethod.Post, path(inn + "vo"), ev.Body);
                        r.Log.AppendLine($"{name} in(own response): plain {(int)ip.Status} {Cut(ip.Body)} {ip.Thrown} | vo {(int)iv.Status} {Cut(iv.Body)} {iv.Thrown}");
                        // The wrapped type does not read its own response back under the row's options either: nothing to tell.
                        if ((int)iv.Status != 200 && (int)ip.Status != 200) return c.Inconclusive($"{name} in: neither side reads its own response back ({(int)ip.Status}, {(int)iv.Status})");
                        if ((int)iv.Status != 200) return $"{name} in: the closed set cannot read its own response ({(int)iv.Status}), the wrapped type reads its own: {Cut(iv.Body)} {iv.Thrown}";
                        c.Note(NoteKeys.Compared, name + " in: its own response read back");
                        return null;
                    });
                else
                    f2 = await r.Check(obligation, Baseline.Twin, async c =>
                    {
                        var text = JsonParity.InputFor(input, e, jopts, ep.Body, position);
                        var ip = await Hx.Send(app, HttpMethod.Post, path(inn + "plain"), text); var iv = await Hx.Send(app, HttpMethod.Post, path(inn + "vo"), text);
                        return Same(c, r, e, name + " in(" + input + ")", ip, iv, input);
                    });
                failed ??= f2;
            }
            // 3. binding, the row's http.binding
            var unbindable = e.Wrapped == "own-record-class" && bind != "body" ? "a record class"
                           : e.Wrapped == "NodaTime.Instant" && name == "minimal" && bind != "body" ? "Instant in a minimal API" : null;
            if (unbindable is not null)
            {
                // No sentence cites it: the wrapped type is not bound from text either, so neither side has an endpoint and there is no twin.
                r.Check(obligation, Baseline.Twin, c => c.Inconclusive($"{name} bind {bind}: {unbindable} is not bound from text, the wrapped type neither"));
                continue;
            }
            if (bind == "body" && ownVo)
            {
                // "Your own converter wins": the application's converter reads the body, not the twin's JSON.
                r.Check(obligation, Baseline.Twin, c => c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), $"{name} bind body"));
                continue;
            }
            var valid = Hx.BindText(e, plainSample);
            var refused = e.Refused() is { } rf ? Hx.BindText(e, rf) : null;
            async Task<Hx.Wire> B(string which, string txt, string w)
            {
                var enc = Uri.EscapeDataString(txt);
                return bind switch
                {
                    "route" => await Hx.Get(app, path(bindPre + "route/" + w + "/" + enc)),
                    "query" => await Hx.Get(app, path(bindPre + "query/" + w + "?v=" + enc)),
                    "as-parameters" => await Hx.Get(app, path(bindPre + "params/" + w + "?v=" + enc)),
                    "header" => await Hx.Send(app, HttpMethod.Get, path(bindPre + "header/" + w), header: ("v", txt)),
                    "form" => await Hx.Send(app, HttpMethod.Post, path(bindPre + "form/" + w), "v=" + enc, "application/x-www-form-urlencoded"),
                    _ => await Hx.Send(app, HttpMethod.Post, path(bindPre + "body/" + w),
                        which == "valid" ? JsonSerializer.Serialize(plainSample, e.Plain, jopts) : JsonSerializer.Serialize(e.Refused(), e.Plain, jopts)),
                };
            }
            // Both sides are mapped for every binding they reach here: a 404 is the harness's routing, never a binding result. Counted as
            // "plain unbindable", a plain route mapped under another name went unnoticed.
            static string? Routed(string name, string bind, Hx.Wire bp, Hx.Wire bv) =>
                bp.Status == HttpStatusCode.NotFound || bv.Status == HttpStatusCode.NotFound
                    ? $"harness: {name} bind {bind} has no endpoint (plain {(int)bp.Status}, value object {(int)bv.Status})" : null;
            // A text a route segment cannot carry as it is ('/', ':', '?', '#'): escaped, it arrives as other text ('%2F' stays), so the value
            // bound is not the one sent. A limit of the harness, which no sentence cites: the check cannot tell.
            bool Unsafe(string txt) => bind == "route" && (txt.Contains('/') || txt.Contains(':') || txt.Contains('?') || txt.Contains('#'));
            string? Unplaceable(Check c, string txt) => c.Inconclusive($"{name} bind route: the text {txt} cannot be carried by a route segment as it is");
            var f3 = await r.Check(obligation, Baseline.Twin, async c =>
            {
                if (Unsafe(valid)) return Unplaceable(c, valid);
                var bp = await B("valid", valid, "plain"); var bv = await B("valid", valid, "vo");
                if (Routed(name, bind, bp, bv) is { } unrouted) return unrouted;
                r.Log.AppendLine($"{name} bind {bind} valid text={valid}: plain {(int)bp.Status} {Cut(bp.Body)} {bp.Thrown} | vo {(int)bv.Status} {Cut(bv.Body)} {bv.Thrown}");
                // The twin refuses the valid text too: no binding to compare, and no sentence cites it.
                if ((int)bp.Status is not (>= 200 and < 300) && (int)bv.Status is not (>= 200 and < 300))
                    return c.Inconclusive($"{name} bind {bind}: the wrapped type refuses the valid text too ({(int)bp.Status}), the value object answered {(int)bv.Status}");
                return Same(c, r, e, name + " bind " + bind, bp, bv);
            });
            failed ??= f3;
            if (refused is null) continue;
            // README "Binding" (AspNetCore): a value Create rejects gets a 400, the one set of rules.
            var f4 = await r.Check(obligation, Baseline.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#binding"), async c =>
            {
                if (Unsafe(refused)) return Unplaceable(c, refused);
                var bp = await B("refused", refused, "plain"); var bv = await B("refused", refused, "vo");
                if (Routed(name, bind, bp, bv) is { } unrouted) return unrouted;
                r.Log.AppendLine($"{name} bind {bind} refused text={refused}: plain {(int)bp.Status} {Cut(bp.Body)} {bp.Thrown} | vo {(int)bv.Status} {Cut(bv.Body)} {bv.Thrown}");
                if ((int)bp.Status is not (>= 200 and < 300)) return c.Inconclusive($"{name} bind {bind}: the wrapped type refuses the value Create refuses too ({(int)bp.Status})");
                if (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class") return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "bind");
                // README "What TypeKit guarantees": StoredJsonConverterFactory reads JSON the application stored itself, without validation.
                if (bind == "body" && r.Is("json.Converters", "StoredJsonConverterFactory")) return c.Inapplicable(Cite.Docs("README.md#what-typekit-guarantees"), "bind body");
                // KB-037: under MaxValidationDepth=1 MVC runs an action whose parameter it could not bind, with the parameter at its default. The
                // value object must hold its default then, never the refused value; the twin, sent a text it cannot read in the same host, shows
                // the framework doing the same.
                if (name == "mvc" && r.Is("mvc.MaxValidationDepth", "1") && (int)bv.Status is >= 200 and < 300)
                {
                    if (DefaultBody(app, e.Vo) is var voDefault && !HoldsDefault(bv, voDefault))
                        return $"{name} bind {bind}: validated value object bound the refused value (status {(int)bv.Status}) {Cut(bv.Body)}, not its default ({(voDefault is null ? "204" : "200 " + Cut(voDefault))})";
                    if (Unreadable(e) is not { } text)
                        return c.Inapplicable(Cite.Fact("KB-037"), $"{name} bind {bind}: the value object holds its default; any text binds the wrapped type, so no twin shows it");
                    var enc = Uri.EscapeDataString(text);
                    var bt = bind switch
                    {
                        "route" => await Hx.Get(app, path(bindPre + "route/plain/" + enc)),
                        "query" => await Hx.Get(app, path(bindPre + "query/plain?v=" + enc)),
                        "as-parameters" => await Hx.Get(app, path(bindPre + "params/plain?v=" + enc)),
                        "header" => await Hx.Send(app, HttpMethod.Get, path(bindPre + "header/plain"), header: ("v", text)),
                        "form" => await Hx.Send(app, HttpMethod.Post, path(bindPre + "form/plain"), "v=" + enc, "application/x-www-form-urlencoded"),
                        _ => await Hx.Send(app, HttpMethod.Post, path(bindPre + "body/plain"), JsonSerializer.Serialize(text)),
                    };
                    r.Log.AppendLine($"{name} bind {bind} unreadable text={text}: plain {(int)bt.Status} {Cut(bt.Body)} {bt.Thrown}");
                    if (HoldsDefault(bt, DefaultBody(app, e.Plain)))
                        return c.Inapplicable(Cite.Fact("KB-037"), $"{name} bind {bind}: both hold their default, the wrapped type for a text it cannot read");
                    if ((int)bt.Status is >= 400 and < 500)
                        return $"{name} bind {bind}: the value object answered {(int)bv.Status} holding its default, where the wrapped type rejects a text it cannot read ({(int)bt.Status})";
                    // KB-037's class from the query: the validator throws at the twin's property, and the value object, written by a
                    // converter of its own, is no class it walks
                    return c.Inconclusive($"{name} bind {bind}: the value object holds its default (KB-037); the wrapped type answered {(int)bt.Status} {Cut(bt.Body)} {bt.Thrown}");
                }
                // a refused value must not bind (the one set of rules), except where the application skips validation on purpose
                if ((int)bv.Status is >= 200 and < 300) return $"{name} bind {bind}: validated value object bound the refused value (status {(int)bv.Status}) {Cut(bv.Body)}";
                // Text-matching site, kept in this step: MVC writes its 400 as ValidationProblemDetails, which a source-generated context alone
                // does not list, so ASP.NET answers 500 for any refused binding under that configuration, the wrapped type's included. Baseline
                // gap: the message's words tell it, where a wrapped twin refused in the same row should (the three-sided differential).
                if ((int)bv.Status == 500 && name == "mvc" && r.Is("json.TypeInfoResolver", "source-generated-context")
                    && (bv.Thrown ?? bv.Body ?? "").Contains("ValidationProblemDetails")) { r.Note(NoteKeys.Mvc400UnwritableUnderSourceGen); return null; }
                if ((int)bv.Status != 400) return $"{name} bind {bind}: refused value answered {(int)bv.Status}, expected 400: {Cut(bv.Body)} {bv.Thrown}";
                c.Note(NoteKeys.Compared, $"{name} bind {bind}: the refused value answered 400");
                return null;
            });
            failed ??= f4;
        }
        // 4. schema parity
        if (schema) { var f5 = await r.Check(obligation, Baseline.Twin, c => SchemaParity(app, r, e, c)); failed ??= f5; }
        return failed;
    }

    /// <summary>
    /// The schema parity alone, on the same host as <see cref="Run"/>: claim C21's check (C21OpenapiSchemaParityProbe), which BaseProbe leaves
    /// out of its calibration, run once for each of the row's <paramref name="obligations"/> against the twin. Every check runs; the first
    /// failure is returned.
    /// </summary>
    public static async Task<string?> Schema(Row r, Entry e, IEnumerable<string> obligations)
    {
        WebApplication? app = null; string? down = null;
        try { app = await Hx.Start(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }); }
        catch (Exception ex) { down = "schema host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
        await using var _ = app;
        string? failed = null;
        foreach (var obligation in obligations)
        {
            var f = await r.Check(obligation, Baseline.Twin, c => down is not null ? Task.FromResult<string?>(down) : SchemaParity(app!, r, e, c));
            failed ??= f;
        }
        return failed;
    }

    /// <summary>
    /// Where either side is a null-or-reference (<c>oneOf [null, component]</c>), both sides there in one spelling:
    /// the null as <c>x-null</c> beside the schema, wherever the side spelled it (<c>type [null, X]</c>, <c>nullable</c>,
    /// <c>x-nullable</c>, a null in an enum, or the null-or itself). A shared component cannot hold the null of one
    /// position, so a nullable value object that is one is null around its reference, where ASP.NET writes the
    /// wrapped type's null inside its inline schema (decision 36: "as a container's element (`oneOf: [null,
    /// component]`, ASP.NET's own form)"; AspNetCore README: "A nullable element (`List&lt;Quantity?&gt;`,
    /// `Dictionary&lt;string, Quantity?&gt;`) is the component or null, as a nullable property is."). Whether a side
    /// admits null is kept; everywhere else, inlined schemas included, the comparison stays byte for byte (rows 170,
    /// 196 and 215 of C21). Returns both sides without the marks <see cref="Hx.Deep"/> put on resolved references.
    /// </summary>
    /// <summary>The node at a path <see cref="Difference"/> names (<c>$.parameters[0].schema</c>), or none.</summary>
    static JsonNode? At(JsonNode? node, string path)
    {
        foreach (System.Text.RegularExpressions.Match step in System.Text.RegularExpressions.Regex.Matches(path, @"\.([^.\[]+)|\[(\d+)\]"))
            node = step.Groups[1].Success ? (node as JsonObject)?[step.Groups[1].Value] : (node as JsonArray) is { } a && int.Parse(step.Groups[2].Value) is var i && i < a.Count ? a[i] : null;
        return node;
    }

    static (string, string, bool) NullAroundReferences(string sp, string sv)
    {
        var around = false;
        static bool IsNull(JsonNode? a) => a is JsonObject ao && (ao["type"]?.ToJsonString() == "\"null\"" || ao["enum"]?.ToJsonString() == "[null]");
        static bool NullOrReference(JsonNode? n) =>
            n is JsonObject o && o["oneOf"] is JsonArray alts && alts.Count == 2 && alts.Any(IsNull) && alts.Any(a => a is JsonObject ao && ao.ContainsKey(Hx.ResolvedReference));
        static JsonNode? Unmark(JsonNode? n) => n switch
        {
            JsonObject o => new JsonObject(o.Where(kv => kv.Key != Hx.ResolvedReference).Select(kv => KeyValuePair.Create(kv.Key, Unmark(kv.Value)))),
            JsonArray a => new JsonArray(a.Select(Unmark).ToArray()),
            _ => n?.DeepClone(),
        };
        static JsonNode? OneSpelling(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o when o["oneOf"] is JsonArray alts && alts.Count == 2 && alts.Any(IsNull) && alts.FirstOrDefault(a => !IsNull(a)) is JsonObject keep:
                {
                    var c = (JsonObject)OneSpelling(keep)!; c["x-null"] = true; return Sorted(c);
                }
                case JsonObject o:
                {
                    var c = new JsonObject();
                    foreach (var (k, v) in o)
                    {
                        if (k == Hx.ResolvedReference) continue;
                        if (k is "nullable" or "x-nullable") { if (v?.GetValue<bool>() == true) c["x-null"] = true; continue; }
                        if (k == "allOf" && v is JsonArray aa && aa.Count == 1 && aa[0] is JsonObject a0 && a0.Count == 1 && a0.ContainsKey("x-nullable")) { c["x-null"] = true; continue; }
                        if (k == "type" && v is JsonArray ta && ta.Any(x => x?.GetValue<string>() == "null"))
                        {
                            var f = ta.Where(x => x?.GetValue<string>() != "null").ToList();
                            c["type"] = f.Count == 1 ? f[0]!.DeepClone() : new JsonArray(f.Select(x => x!.DeepClone()).ToArray()); c["x-null"] = true; continue;
                        }
                        if (k == "type" && v?.ToJsonString() == "\"null\"") { c["x-null"] = true; continue; }
                        if (k == "enum" && v is JsonArray en && en.Any(x => x is null)) { c["enum"] = new JsonArray(en.Where(x => x is not null).Select(x => x!.DeepClone()).ToArray()); c["x-null"] = true; continue; }
                        if (k == "anyOf" && v is JsonArray any && any.Any(IsNull)) { c["anyOf"] = new JsonArray(any.Where(x => !IsNull(x)).Select(OneSpelling).ToArray()); c["x-null"] = true; continue; }
                        c[k] = OneSpelling(v);
                    }
                    return Sorted(c);
                }
                case JsonArray a: return new JsonArray(a.Select(OneSpelling).ToArray());
                default: return n?.DeepClone();
            }
        }
        static JsonObject Sorted(JsonObject o) => new(o.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())));
        (JsonNode?, JsonNode?) Walk(JsonNode? p, JsonNode? v)
        {
            if (NullOrReference(p) || NullOrReference(v)) { around = true; return (OneSpelling(p), OneSpelling(v)); }
            if (p is JsonObject po && v is JsonObject vo)
            {
                var cp = new JsonObject(); var cv = new JsonObject();
                foreach (var k in po.Select(kv => kv.Key).Union(vo.Select(kv => kv.Key)).Where(k => k != Hx.ResolvedReference).Order(StringComparer.Ordinal))
                {
                    if (!po.ContainsKey(k)) { cv[k] = Unmark(vo[k]); continue; }
                    if (!vo.ContainsKey(k)) { cp[k] = Unmark(po[k]); continue; }
                    var (a, b) = Walk(po[k], vo[k]); cp[k] = a; cv[k] = b;
                }
                return (cp, cv);
            }
            if (p is JsonArray pa && v is JsonArray va && pa.Count == va.Count)
            {
                var cp = new JsonArray(); var cv = new JsonArray();
                for (var i = 0; i < pa.Count; i++) { var (a, b) = Walk(pa[i], va[i]); cp.Add(a); cv.Add(b); }
                return (cp, cv);
            }
            return (Unmark(p), Unmark(v));
        }
        var (np, nv) = Walk(JsonNode.Parse(sp), JsonNode.Parse(sv));
        return (np?.ToJsonString() ?? "null", nv?.ToJsonString() ?? "null", around);
    }

    static async Task<string?> SchemaParity(WebApplication app, Row r, Entry e, Check c)
    {
        JsonNode doc;
        try { doc = (await Hx.Doc(app))!; }
        catch (Exception ex)
        {
            // The baseline is ASP.NET alone on the same host: a document it refuses too has no schema to compare.
            return await Hx.DocRefusedByAspNetAlone(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }, ex);
        }
        // docs/json.md "Describing what a converter writes": ASP.NET drops items/additionalProperties of a converter it cannot see into, the value
        // object's schema is the underlying type's; and the application's converter for the value object describes it as that converter says.
        // C21 declares both on every obligation (claims.json inapplicable), so Row.Check records them before this body runs and no row of
        // C21 is generated with them; these lines hold for the schema half run under another claim (ShrinkProbe's baseline).
        if (r.Is("json.Converters", "own-for-wrapped")) return c.Inapplicable(Cite.NotPromised("own-converter-for-the-wrapped-type"));
        if (r.Is("json.Converters", "own-for-value-object")) return c.Inapplicable(Cite.NotPromised("own-converter-for-the-value-object"));
        string? Op(string path, string method, out JsonNode? node)
        {
            node = doc["paths"]?[path]?[method]; return node is null ? "missing " + method + " " + path : null;
        }
        // only the schemas are compared (the claim is about the schema); whether a parameter or body is required follows the .NET type's nullability
        JsonNode? Schemas(JsonNode? n)
        {
            switch (n)
            {
                case JsonArray a: { var c = new JsonArray(); foreach (var x in a) c.Add(Schemas(x)); return c; }
                case JsonObject o:
                {
                    var c = new JsonObject();
                    foreach (var kv in o.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        if (kv.Key == "required" && kv.Value is JsonValue) continue;   // parameter / body / old-style flags
                        c[kv.Key] = Schemas(kv.Value);
                    }
                    return c;
                }
                default: return n?.DeepClone();
            }
        }
        string Sig(JsonNode op)
        {
            var o = new JsonObject();
            o["parameters"] = Schemas(Hx.Deep(doc, op["parameters"], mark: true));
            o["requestBody"] = Schemas(Hx.Deep(doc, op["requestBody"], mark: true));
            o["ok"] = Schemas(Hx.Deep(doc, op["responses"]?["200"], mark: true));
            return o.ToJsonString();
        }
        var checks = new List<(string, string)>();
        if (Hx.HasMinimal(r)) { checks.Add((Hx.M(r, "/schema/plain"), Hx.M(r, "/schema/vo"))); }
        if (Hx.HasMvc(r))
        {
            var bind = r["http.binding"] switch { "route" => "route", "query" => "query", "header" => "header", "form" => "form", "as-parameters" => "params", _ => "body" };
            var sfx = bind == "route" ? "/{v}" : "";
            checks.Add((Hx.C(r, $"/mvc/bind/{bind}/plain{sfx}"), Hx.C(r, $"/mvc/bind/{bind}/vo{sfx}")));
        }
        if (Hx.HasMinimal(r))
        {
            var bind = r["http.binding"] switch { "route" => "route", "query" => "query", "header" => "header", "form" => "form", "as-parameters" => "params", _ => "body" };
            var sfx = bind == "route" ? "/{v}" : "";
            // route: /bind/route/{which}/{v}
            checks.Add((bind == "route" ? Hx.M(r, "/bind/route/plain/{v}") : Hx.M(r, $"/bind/{bind}/plain"), bind == "route" ? Hx.M(r, "/bind/route/vo/{v}") : Hx.M(r, $"/bind/{bind}/vo")));
        }
        // The inputs of the two sides, apart from the type substituted: where each value is bound, under which
        // name, and whether it is required. They must agree before the schemas are compared; otherwise the
        // comparison measures the harness (G1: a nullable class value object against a required int).
        string Inputs(JsonNode op)
        {
            var parts = new List<string>();
            if (op["parameters"] is JsonArray ps)
                foreach (var prm in ps) { var q = Hx.Deep(doc, prm); parts.Add($"{q?["in"]}:{q?["name"]}:required={q?["required"]?.ToJsonString() ?? "false"}"); }
            if (Hx.Deep(doc, op["requestBody"]) is JsonObject rb) parts.Add($"body:required={rb["required"]?.ToJsonString() ?? "false"}");
            return string.Join(",", parts);
        }
        foreach (var (pp0, vp) in checks)
        {
            var method = pp0.Contains("/schema/") || pp0.Contains("/body/") || pp0.Contains("/form/") ? "post" : "get";
            // Like with like from the start: a class value object against the wrapped type at its nullability.
            // The schema endpoint has the twin at the root only, where its body is the entry's own type (row 384 of C07:
            // a record class over TimeOnly, optional, against a required TimeOnly).
            var twinned = pp0.Contains("/bind/") || pp0.Contains("/schema/") && r["shape.position"] == "root";
            var pp = e.Vo.IsValueType != e.Plain.IsValueType && Hx.PlainNull(r, e) is not null && twinned ? pp0.Replace("/plain", "/plainnull") : pp0;
            if (pp != pp0) c.Note(NoteKeys.SchemaComparedWithNullableWrapped);
            if (Op(pp, method, out var po) is { } m1)
            {
                // Mapped for bodies only (a record class, Instant in a minimal API): neither side is there.
                if (Op(vp, method, out _) is null) return $"harness: the value object's {method} {vp} is documented, its plain twin is not ({m1})";
                c.Note(NoteKeys.SchemaNeitherSideDocumented, m1); continue;
            }
            if (Op(vp, method, out var vo) is { } m2) return m2;
            if (Inputs(po!) is var ip && Inputs(vo!) is var iv && ip != iv)
            {
                // No twin of the wrapped type at the value object's nullability here (a struct value object over a class, or under a
                // source-generated context an enum or Instant, whose Nullable the probes' context does not list): the two sides are not
                // alike, and no sentence cites it, so the check cannot tell.
                if (e.Vo.IsValueType != e.Plain.IsValueType && Hx.PlainNull(r, e) is null)
                    c.Inconclusive($"{pp} vs {vp}: no twin of {e.Plain.Name} at the value object's nullability, the inputs differ: wrapped [{ip}] vs value object [{iv}]");
                else r.Outcome("harness-error", $"inputs not equivalent for {pp} vs {vp}: wrapped [{ip}] vs value object [{iv}]");
                continue;
            }
            var sp = Sig(po!); var sv = Sig(vo!); var written = sp;
            if (NullAroundReferences(sp, sv) is var (np, nv, around)) { sp = np; sv = nv; if (around) c.Note(NoteKeys.SchemaNullAroundReferenceNormalised); }
            if (sp != sv && pp != pp0)
            {
                // Both sides nullable alike; OpenAPI 3.x spells nullability two ways (oneOf [null, X] or type [null, X]).
                sp = Hx.NoNull(JsonNode.Parse(sp))!.ToJsonString(); sv = Hx.NoNull(JsonNode.Parse(sv))!.ToJsonString();
                c.Note(NoteKeys.SchemaNullableSpellingsNormalised);
            }
            if (sp != sv && e.Vo.IsValueType != e.Plain.IsValueType && pp == pp0)
            {
                // A class value object inside a declared shape is nullable by being a class (ADR 0036: a nullable
                // value object admits null in OpenAPI); the markers go, the rest of the schema must agree.
                sp = Hx.NoNull(JsonNode.Parse(sp))!.ToJsonString(); sv = Hx.NoNull(JsonNode.Parse(sv))!.ToJsonString();
                c.Note(NoteKeys.SchemaClassVoNullabilityNormalised);
            }
            if (sp != sv)
            {
                if (e.Kind == "closed-set") { c.Inapplicable(Cite.NotPromised("a-closed-set-is-a-string-enum"), "schema"); continue; }
                var (at, wp, wv) = Difference(JsonNode.Parse(sp), JsonNode.Parse(sv), "$");
                var openApi2 = r.Is("openapi.OpenApiVersion", "OpenApi2_0");
                // KB-026: OpenAPI 2.0 writes a nullable property of any component as allOf [{x-nullable: true}], without the reference, and a
                // value object keeps a component of its own (not-promised openapi-2-nullable-component-loses-its-type; row 38 of C21).
                // Text-matching site, kept in this step: the difference is told by its path's suffix and the value object's side spelled out.
                // Baseline gap: no plain nullable component in the same document shows ASP.NET losing its reference too (the three-sided
                // differential, next step).
                if (openApi2 && !r.Is("openapi.CreateSchemaReferenceId", "null-inlines-all") && at.EndsWith(".allOf", StringComparison.Ordinal)
                    && wp is null && wv?.ToJsonString() == """[{"x-nullable":true}]""")
                { c.Inapplicable(Cite.NotPromised("openapi-2-nullable-component-loses-its-type"), at); continue; }
                // KB-001: OpenAPI 2.0 writes a nullable body of the wrapped value type as allOf [{x-nullable: true}] alone, where a class value
                // object's body in a minimal API is not nullable and keeps its schema (not-promised openapi-2-nullable-body-keeps-only-its-null; row 257).
                // Text-matching site, kept in this step: the wrapped side's document is searched for the spelling. Baseline gap: the twin's
                // nullable body is not compared with a plain nullable body ASP.NET writes alone (the three-sided differential).
                if (openApi2 && !e.Vo.IsValueType && Nullable.GetUnderlyingType(e.Plain) is not null
                    && written.Contains("""
                    "schema":{"allOf":[{"x-nullable":true}]}
                    """, StringComparison.Ordinal))
                { c.Inapplicable(Cite.NotPromised("openapi-2-nullable-body-keeps-only-its-null"), pp); continue; }
                // KB-027: the component ASP.NET makes for an enum written by name is the schema of the first use it meets, so a wrapped E?
                // parameter lists null through E's component where E was first met nullable; the value object's parameter is the enum's
                // content without it wherever the enum is a component (not-promised enum-component-is-its-first-use; row 285 of C21).
                // Text-matching site, kept in this step: told by the path's prefix and suffix. Baseline gap: the enum's first use is not
                // replayed in a document of its own to show the component it makes (the three-sided differential).
                if (at.StartsWith("$.parameters", StringComparison.Ordinal) && at.EndsWith(".enum", StringComparison.Ordinal)
                    && wp is JsonArray we && wv is JsonArray ve && we.Count == ve.Count + 1 && we[^1] is null
                    && JsonNode.DeepEquals(new JsonArray(we.Take(ve.Count).Select(x => x?.DeepClone()).ToArray()), ve)
                    && At(JsonNode.Parse(written), at[..^".enum".Length]) is JsonObject held && held.ContainsKey(Hx.ResolvedReference))
                { c.Inapplicable(Cite.NotPromised("enum-component-is-its-first-use"), vp); continue; }
                return $"schema differs for {pp} vs {vp} at {FirstDifference(JsonNode.Parse(sp), JsonNode.Parse(sv), "$")}:\n  wrapped {Cut(sp)}\n  value object {Cut(sv)}";
            }
        }
        return null;
    }
}
