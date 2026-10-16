// The baseline every http claim shares: under the row's options, a payload with the value object behaves like the same payload with the wrapped
// type (response bytes, request reading, parameter binding, OpenAPI schema), in the hosting models the row's http.host names.
// Oracle: differential (JSON/wire/schema parity). Documented differences are in JsonParity.ReadMayDiffer.
using System.Net;
using Microsoft.AspNetCore.Builder;
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

    static string? Same(Row r, Entry e, string what, Hx.Wire p, Hx.Wire v, string input = "valid")
    {
        r.Log.AppendLine($"{what}: plain {(int)p.Status} {Cut(p.Body)} {p.Thrown} | vo {(int)v.Status} {Cut(v.Body)} {v.Thrown}");
        bool ok(Hx.Wire w) => (int)w.Status is >= 200 and < 300;
        if (!ok(p) && !ok(v)) return null;
        if (r.Is("json.Converters", "own-for-value-object") && ok(v) && ok(p)) { r.Note(NoteKeys.OwnVoConverter); return null; }
        if (ok(p) && !ok(v))
        {
            if (JsonParity.ReadMayDiffer(r, e, input, null) || (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class")) { r.Note(NoteKeys.DocumentedDiff, what); return null; }
            if (r.Is("json.MaxDepth", "3") && (v.Thrown ?? v.Body).Contains("maximum allowed depth")) { r.Note(NoteKeys.StjMaxdepth, "value-object side"); return null; }
            // A closed set travels as its member's name; the wrapped type under the application's converter is no reference.
            if (e.Kind == "closed-set" && r.Is("json.Converters", "own-for-wrapped")) { r.Note(NoteKeys.ClosedSetVsOwnWrappedConverter, what); return null; }
            // The validated shapes' Create refuses the wrapped type's limit on purpose, so the limit input is a refusal.
            if (e.Kind.StartsWith("validated") && input == "at-type-limit" && RefusesItsLimit(e)) { r.Note(NoteKeys.ValidatedRefusesTypeLimit, what); return null; }
            return (e.Wrapped == "NodaTime.Instant" && what.StartsWith("mvc bind") ? "[F1] " : "") + $"{what}: wrapped answered {(int)p.Status} {Cut(p.Body)}, value object answered {(int)v.Status} {Cut(v.Body)} {v.Thrown}";
        }
        if (!ok(p) && ok(v))
        {
            // README "What TypeKit guarantees": a JSON null is refused by a struct value object; a class value object
            // takes it as a reference does, where the struct it wraps refuses it.
            if (input == "null" && !e.Vo.IsValueType && e.Plain.IsValueType) { r.Note(NoteKeys.ClassVoTakesNull, what); return null; }
            if (r.Is("json.MaxDepth", "3") && (p.Thrown ?? p.Body).Contains("maximum allowed depth")) { r.Note(NoteKeys.StjMaxdepth, "wrapped side"); return null; }
        }
        if (!ok(p) && ok(v)) return $"{what}: value object answered {(int)v.Status} {Cut(v.Body)} where wrapped answered {(int)p.Status} {Cut(p.Body)} {p.Thrown}";
        if (p.Body != v.Body)
        {
            if (e.Kind == "closed-set") { r.Note(NoteKeys.ClosedSetDiffers, what); return null; }
            if (e.Wrapped == "byte" && r["shape.position"] == "array-element") { r.Note(NoteKeys.ByteArrayIsBase64, "echo"); return null; }
            return $"{what}: bodies differ: wrapped {Cut(p.Body)} vs value object {Cut(v.Body)}";
        }
        return null;
    }

    public static async Task<string?> Run(Row r, Entry e, bool schema = true)
    {
        WebApplication app;
        try { app = await Hx.Start(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }); }
        catch (Exception ex) { return "baseline host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
        await using var _ = app;
        var jopts = JsonRow.Apply(r, e).Options!;
        var position = r["shape.position"];
        var plainSample = e.Valid();
        var sides = new List<(string name, Func<string, string> path, string echo, string inn, string bindPre)>();
        if (Hx.HasMinimal(r)) sides.Add(("minimal", p => Hx.M(r, p), "/echo/", "/in/", "/bind/"));
        if (Hx.HasMvc(r)) sides.Add(("mvc", p => Hx.C(r, p), "/mvc/echo/", "/mvc/in/", "/mvc/bind/"));
        foreach (var (name, path, echo, inn, bindPre) in sides)
        {
            // 1. response side
            var ep = await Hx.Get(app, path(echo + "plain")); var ev = await Hx.Get(app, path(echo + "vo"));
            if (Same(r, e, name + " echo", ep, ev) is { } f1) return f1;
            // 2. request side, with the row's input.json
            if (e.Wrapped == "byte" && position == "array-element") r.Note(NoteKeys.ByteArrayIsBase64, "in");
            else if ((int)ep.Status == 200)
            {
                var input = (r.Get("input.json") ?? "valid");
                var text = JsonParity.InputFor(input, e, jopts, ep.Body, position);
                var ownText = r.Is("json.Converters", "own-for-value-object") || e.Kind == "closed-set";   // a closed set travels as its name: it reads its own spelling, the string is only a stand-in
                var textVo = text;
                if (ownText) { r.Note(NoteKeys.OwnSpelling, "own converter: the value object reads its own spelling"); text = ep.Body; textVo = ev.Body; }
                var ip = await Hx.Send(app, HttpMethod.Post, path(inn + "plain"), text); var iv = await Hx.Send(app, HttpMethod.Post, path(inn + "vo"), textVo);
                if (ownText) { if ((int)ev.Status == 200 && (int)iv.Status != 200 && (int)ip.Status == 200) return $"{name} in: the value object cannot read its own response ({(int)iv.Status}): {Cut(iv.Body)}"; r.Note(NoteKeys.OwnSpelling, "in"); }
                else
                if (Same(r, e, name + " in(" + input + ")", ip, iv, input) is { } f2) return f2;
            }
            // 3. binding, the row's http.binding
            var bind = r["http.binding"];
            if (e.Wrapped == "own-record-class" && bind != "body") { r.Note(NoteKeys.NotBindableFromText, "record class"); bind = "none"; }
            if (e.Wrapped == "NodaTime.Instant" && name == "minimal" && bind != "body") { r.Note(NoteKeys.NotBindableFromText, "minimal API, Instant"); bind = "none"; }
            var valid = Hx.BindText(e, plainSample);
            var refused = e.Refused() is { } rf ? Hx.BindText(e, rf) : null;
            foreach (var (which, txt) in new[] { ("valid", valid), ("refused", refused) })
            {
                if (txt is null || bind == "none") continue;
                if (bind == "route" && (txt.Contains('/') || txt.Contains(':') || txt.Contains('?') || txt.Contains('#'))) { r.Note(NoteKeys.RouteTextUnsafe); continue; }
                async Task<Hx.Wire> B(string w)
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
                if (bind == "body" && r.Is("json.Converters", "own-for-value-object")) { r.Note(NoteKeys.OwnVoBodyBindingSkipped); continue; }
                var bp = await B("plain"); var bv = await B("vo");
                // Both sides are mapped for every binding they reach here: a 404 is the harness's routing, never a binding
                // result. Counted as "plain unbindable", a plain route mapped under another name went unnoticed.
                if (bp.Status == HttpStatusCode.NotFound || bv.Status == HttpStatusCode.NotFound)
                    return $"harness: {name} bind {bind} has no endpoint (plain {(int)bp.Status}, value object {(int)bv.Status})";
                var plainOk = (int)bp.Status is >= 200 and < 300;
                r.Log.AppendLine($"{name} bind {bind} {which} text={txt}: plain {(int)bp.Status} {Cut(bp.Body)} {bp.Thrown} | vo {(int)bv.Status} {Cut(bv.Body)} {bv.Thrown}");
                if (which == "valid")
                {
                    if (!plainOk) { r.Note(NoteKeys.BindPlainUnbindable, name); continue; }
                    if (Same(r, e, name + " bind " + bind, bp, bv) is { } f3) return f3;
                }
                else
                {
                    if (!plainOk) continue;
                    if (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class") { r.Note(NoteKeys.PreserveDocumented, "bind"); continue; }
                    if (name == "mvc" && r.Is("mvc.MaxValidationDepth", "1")) { r.Note(NoteKeys.MvcMaxvalidationdepth1IgnoresBindingErrors); continue; } // MVC itself: any type's binding error is ignored (D2)
                    if (bind == "body" && r.Is("json.Converters", "StoredJsonConverterFactory")) { r.Note(NoteKeys.StoredFactorySkipsValidation); continue; } // intended
                    // a refused value must not bind (the one set of rules), except where the application skips validation on purpose
                    if ((int)bv.Status is >= 200 and < 300) return $"{name} bind {bind}: validated value object bound the refused value (status {(int)bv.Status}) {Cut(bv.Body)}";
                    // MVC writes its 400 as ValidationProblemDetails, which a source-generated context alone does not list:
                    // ASP.NET's 500 for any refused binding under that configuration, the wrapped type's included.
                    if ((int)bv.Status == 500 && name == "mvc" && r.Is("json.TypeInfoResolver", "source-generated-context")
                        && (bv.Thrown ?? bv.Body ?? "").Contains("ValidationProblemDetails")) { r.Note(NoteKeys.Mvc400UnwritableUnderSourceGen); continue; }
                    if ((int)bv.Status != 400) return $"{name} bind {bind}: refused value answered {(int)bv.Status}, expected 400: {Cut(bv.Body)} {bv.Thrown}";
                }
            }
        }
        // 4. schema parity
        if (schema) { if (await SchemaParity(app, r, e) is { } f4) return f4; }
        return null;
    }

    static async Task<string?> SchemaParity(WebApplication app, Row r, Entry e)
    {
        JsonNode doc;
        try { doc = (await Hx.Doc(app))!; }
        catch (Exception ex)
        {
            // The baseline is ASP.NET alone on the same host: a document it refuses too has no schema to compare.
            return await Hx.DocRefusedByAspNetAlone(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }, ex);
        }
        if (r.Is("json.Converters", "own-for-wrapped")) { r.Note(NoteKeys.SchemaConverterNotDescribed, "own converter for the wrapped type"); return null; }   // docs/json.md "Describing what a converter writes": ASP.NET drops items/additionalProperties of a converter it cannot see into, the value object's schema is the underlying type's
        if (r.Is("json.Converters", "own-for-value-object")) { r.Note(NoteKeys.SchemaConverterNotDescribed, "own converter for the value object"); return null; }   // docs/json.md "Describing what a converter writes": the exporter loses track of a converter it cannot see into
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
            o["parameters"] = Schemas(Hx.Deep(doc, op["parameters"]));
            o["requestBody"] = Schemas(Hx.Deep(doc, op["requestBody"]));
            o["ok"] = Schemas(Hx.Deep(doc, op["responses"]?["200"]));
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
            var pp = e.Vo.IsValueType != e.Plain.IsValueType && Hx.PlainNull(r, e) is not null && pp0.Contains("/bind/") ? pp0.Replace("/plain", "/plainnull") : pp0;
            if (pp != pp0) r.Note(NoteKeys.SchemaComparedWithNullableWrapped);
            if (Op(pp, method, out var po) is { } m1)
            {
                // Mapped for bodies only (a record class, Instant in a minimal API): neither side is there.
                if (Op(vp, method, out _) is null) return $"harness: the value object's {method} {vp} is documented, its plain twin is not ({m1})";
                r.Note(NoteKeys.SchemaNeitherSideDocumented, m1); continue;
            }
            if (Op(vp, method, out var vo) is { } m2) return m2;
            if (Inputs(po!) is var ip && Inputs(vo!) is var iv && ip != iv)
            {
                if (e.Vo.IsValueType != e.Plain.IsValueType && Hx.PlainNull(r, e) is null)
                    r.Note(NoteKeys.SchemaNullabilityNotComparable);   // no nullable wrapped endpoint here (an enum or Instant under a source-generated context)
                else r.Outcome("harness-error", $"inputs not equivalent for {pp} vs {vp}: wrapped [{ip}] vs value object [{iv}]");
                continue;
            }
            var sp = Sig(po!); var sv = Sig(vo!);
            if (sp != sv && (r["shape.position"] == "nullable-property" || pp != pp0))
            {
                // Both sides nullable alike; OpenAPI 3.x spells nullability two ways (oneOf [null, X] or type [null, X]).
                sp = Hx.NoNull(JsonNode.Parse(sp))!.ToJsonString(); sv = Hx.NoNull(JsonNode.Parse(sv))!.ToJsonString();
                r.Note(NoteKeys.SchemaNullableSpellingsNormalised);
            }
            if (sp != sv && e.Vo.IsValueType != e.Plain.IsValueType && pp == pp0)
            {
                // A class value object inside a declared shape is nullable by being a class (ADR 0036: a nullable
                // value object admits null in OpenAPI); the markers go, the rest of the schema must agree.
                sp = Hx.NoNull(JsonNode.Parse(sp))!.ToJsonString(); sv = Hx.NoNull(JsonNode.Parse(sv))!.ToJsonString();
                r.Note(NoteKeys.SchemaClassVoNullabilityNormalised);
            }
            if (sp != sv)
            {
                if (e.Kind == "closed-set") { r.Note(NoteKeys.ClosedSetDiffers, "schema"); continue; }
                return $"schema differs for {pp} vs {vp}:\n  wrapped {Cut(sp)}\n  value object {Cut(sv)}";
            }
        }
        return null;
    }
}
