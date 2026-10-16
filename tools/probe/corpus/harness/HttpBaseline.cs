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

    /// <summary>
    /// The schema parity alone, on the same host as <see cref="Run"/>: claim C21's check (C21OpenapiSchemaParityProbe),
    /// which BaseProbe leaves out of its calibration.
    /// </summary>
    public static async Task<string?> Schema(Row r, Entry e)
    {
        WebApplication app;
        try { app = await Hx.Start(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }); }
        catch (Exception ex) { return "schema host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
        await using var _ = app;
        return await SchemaParity(app, r, e);
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
            var sp = Sig(po!); var sv = Sig(vo!); var written = sp;
            if (NullAroundReferences(sp, sv) is var (np, nv, around)) { sp = np; sv = nv; if (around) r.Note(NoteKeys.SchemaNullAroundReferenceNormalised); }
            if (sp != sv && pp != pp0)
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
                var (at, wp, wv) = Difference(JsonNode.Parse(sp), JsonNode.Parse(sv), "$");
                var openApi2 = r.Is("openapi.OpenApiVersion", "OpenApi2_0");
                // KB-026: OpenAPI 2.0 writes a nullable property of any component as allOf [{x-nullable: true}], without the reference, and a
                // value object keeps a component of its own (not-promised openapi-2-nullable-component-loses-its-type; row 38 of C21).
                if (openApi2 && !r.Is("openapi.CreateSchemaReferenceId", "null-inlines-all") && at.EndsWith(".allOf", StringComparison.Ordinal)
                    && wp is null && wv?.ToJsonString() == """[{"x-nullable":true}]""")
                { r.Note(NoteKeys.SchemaOpenApi2NullableComponentLosesType, at); continue; }
                // KB-001: OpenAPI 2.0 writes a nullable body of the wrapped value type as allOf [{x-nullable: true}] alone, where a class value
                // object's body in a minimal API is not nullable and keeps its schema (not-promised openapi-2-nullable-body-keeps-only-its-null; row 257).
                if (openApi2 && !e.Vo.IsValueType && Nullable.GetUnderlyingType(e.Plain) is not null
                    && written.Contains("""
                    "schema":{"allOf":[{"x-nullable":true}]}
                    """, StringComparison.Ordinal))
                { r.Note(NoteKeys.SchemaOpenApi2NullableBodyKeepsOnlyNull, pp); continue; }
                // KB-027: the component ASP.NET makes for an enum written by name is the schema of the first use it meets, so a wrapped E?
                // parameter lists null through E's component where E was first met nullable; the value object's parameter is the enum's
                // content without it wherever the enum is a component (not-promised enum-component-is-its-first-use; row 285 of C21).
                if (at.StartsWith("$.parameters", StringComparison.Ordinal) && at.EndsWith(".enum", StringComparison.Ordinal)
                    && wp is JsonArray we && wv is JsonArray ve && we.Count == ve.Count + 1 && we[^1] is null
                    && JsonNode.DeepEquals(new JsonArray(we.Take(ve.Count).Select(x => x?.DeepClone()).ToArray()), ve)
                    && At(JsonNode.Parse(written), at[..^".enum".Length]) is JsonObject held && held.ContainsKey(Hx.ResolvedReference))
                { r.Note(NoteKeys.SchemaEnumComponentIsItsFirstUse, vp); continue; }
                return $"schema differs for {pp} vs {vp} at {FirstDifference(JsonNode.Parse(sp), JsonNode.Parse(sv), "$")}:\n  wrapped {Cut(sp)}\n  value object {Cut(sv)}";
            }
        }
        return null;
    }
}
