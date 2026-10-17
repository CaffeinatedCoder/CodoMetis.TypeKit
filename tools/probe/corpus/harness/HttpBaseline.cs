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
using CodoMetis.TypeKit.Facts;
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
    /// One wire comparison with the twin, inside its check <paramref name="c"/>: the wrapped type's answer <paramref name="p"/> (side (2)) and the
    /// value object's <paramref name="v"/> (side (3)); side (1) refusing alike was decided before the check (<see cref="Wire"/>). A difference a
    /// document makes intended ends the check as that document's citation; BaseProbe calibrates the host on C07's rows, whose claim promises
    /// nothing of the wire, so these cite the sentence itself, not a not-promised entry of C07.
    /// </summary>
    static string? Same(Check c, Row r, Entry e, string what, Hx.Wire p, Hx.Wire v, string input = "valid")
    {
        r.Log.AppendLine($"{what}: plain {(int)p.Status} {Cut(p.Body)} {p.Thrown} | vo {(int)v.Status} {Cut(v.Body)} {v.Thrown}");
        bool ok(Hx.Wire w) => (int)w.Status is >= 200 and < 300;
        // both refused, and not as the twin without TypeKit refuses (Sides.Alike): evidence about refusing, and nothing to compare
        if (!ok(p) && !ok(v)) return c.Inconclusive($"{what}: both refuse, not alike: wrapped {p.Side(what)}, value object {v.Side(what)}");
        // "Your own converter wins": the application's converter writes the value object instead of the generated one.
        if (r.Is("json.Converters", "own-for-value-object") && ok(v)) return c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), what);
        if (ok(p) && !ok(v))
        {
            if (ReadRefusedAsDocumented(c, r, e, input, what)) return null;
            // the same: the value object's array reads a JSON array of numbers, never the base64 string the twin's byte[] wrote and reads
            if (ByteArray(r, e)) return c.Inapplicable(Cite.Docs("docs/json.md#the-wrapped-types-json"), what);
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
            if (input == "null" && Make.AdmitsNull(e.Vo) && !Make.AdmitsNull(e.Plain)) return c.Refused(Cite.Docs("docs/json.md#null-and-missing"), what);
            return $"{what}: value object answered {(int)v.Status} {Cut(v.Body)} where wrapped answered {(int)p.Status} {Cut(p.Body)} {p.Thrown}";
        }
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

    /// <summary>
    /// One check of the wire, three-sided (Sides): side (2) <paramref name="p"/> and side (3) <paramref name="v"/> answered on the host with
    /// TypeKit; side (1) is asked of the host without it (<paramref name="one"/>), lazily, only where they disagree or refuse. Where (1)
    /// refuses alike with (3), the check is the framework's refusal against Baseline.Framework; where (1) answers and the value object's
    /// write was refused under a depth limit, side (1) is a converter's value there (KB-040, in process); otherwise <paramref name="body"/>
    /// compares, once (2) is held to (1).
    /// </summary>
    static async Task<string?> Wire(Row r, Entry e, string obligation, Baseline against, string what, Hx.Wire p, Hx.Wire v, Func<Task<Hx.Wire?>> one, Func<Check, Task<string?>> body)
    {
        var two = p.Side(what); var three = v.Side(what);
        Sides.Answer? first = null; Sides.Side? framework = null;
        if (two.Refused || three.Refused || p.Body != v.Body)
        {
            if (await one() is { } w) first = w.Side(what);
            if (three.Refused && first is { Refused: true } && Sides.Alike(first, three)) framework = Sides.Measured(first, "the twin on a host without TypeKit");
            else if (three.Refused && v.Ex is { } thrown && first is { Refused: false } && (Sides.Why = null) is null
                     && Sides.WrittenByAConverter(r, e, Twin(r, e), Sides.WithoutTypeKit(JsonRow.Apply(r, e).Options!), new("write", null, thrown)) is { } converter
                     && Sides.Alike(converter.Answer, new("write", null, thrown)))
                return Sides.Check(r, obligation, against, converter, new("write", null, thrown), null, null, _ => (string?)null);
        }
        var failed = await Sides.Check(r, obligation, against, framework, three, first, first is null ? null : two, body);
        // the failure names side (1), for the triage of the row
        return failed is null || first is null ? failed : failed + $" [side (1), the twin without TypeKit: {first}{(Sides.Why is { } why ? "; " + why : "")}]";
    }

    /// <summary>The wrapped twin's payload, as the echo writes it: the row's position and containing type around the wrapped value.</summary>
    static Held Twin(Row r, Entry e) => Pos.Build(r["shape.position"], r.Get("shape.containing") ?? "class", e, false, e.Valid());

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
    static string? Unreadable(Entry e) => e.Wrapped is "string" or "Uri" or "own-record-class" ? null : "abc";

    /// <summary>
    /// The value object refused a read the wrapped type answered, as a document says it does (the JSON family's documented differences, each
    /// cited by its sentence; JsonParity.ReadMayDiffer listed them uncited): side (1) answered and (3) refused, which the citation makes
    /// intended (Sides). The check ends as that refusal and true is returned.
    /// </summary>
    static bool ReadRefusedAsDocumented(Check c, Row r, Entry e, string input, string what)
    {
        if (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class") { c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), what); return true; }
        if (e.Kind.StartsWith("validated") && input == "refused-by-create" && r.Get("json.Converters") != "StoredJsonConverterFactory") { c.Refused(Cite.Docs("docs/json.md#when-a-value-is-rejected"), what); return true; }
        if (input == "null" && !Make.AdmitsNull(e.Vo)) { c.Refused(Cite.Docs("docs/json.md#null-and-missing"), what); return true; }
        if (e.Kind == "closed-set" && input != "valid") { c.Refused(Cite.Docs("README.md#what-you-get"), what); return true; }
        return false;
    }

    /// <summary>Where two schemas first differ, and what each side has there: the comparison's 220 characters rarely reach it.</summary>
    static string FirstDifference(JsonNode? p, JsonNode? v, string path) =>
        Difference(p, v, path) is var d ? $"{d.Path}: wrapped {Cut(d.P?.ToJsonString() ?? "(absent)")} | value object {Cut(d.V?.ToJsonString() ?? "(absent)")}" : "";

    /// <summary>
    /// Where two schemas first differ: the path as it is shown, the keys that lead there (a property's name, an element's index), each
    /// side's node there (null where it is absent) and the node holding it, so a difference is told by its structure, never by its path's text.
    /// </summary>
    sealed record Diff(string Path, IReadOnlyList<string> Keys, JsonNode? P, JsonNode? V, JsonNode? ParentP, JsonNode? ParentV);

    static Diff Difference(JsonNode? p, JsonNode? v, string path) => Difference(p, v, path, [], null, null);

    static Diff Difference(JsonNode? p, JsonNode? v, string path, List<string> keys, JsonNode? parentP, JsonNode? parentV)
    {
        if (p is JsonObject po && v is JsonObject vo)
        {
            foreach (var k in po.Select(kv => kv.Key).Union(vo.Select(kv => kv.Key)).Order(StringComparer.Ordinal))
                if (!JsonNode.DeepEquals(po[k], vo[k])) return Difference(po[k], vo[k], path + "." + k, [.. keys, k], p, v);
        }
        else if (p is JsonArray pa && v is JsonArray va && pa.Count == va.Count)
        {
            for (var i = 0; i < pa.Count; i++)
                if (!JsonNode.DeepEquals(pa[i], va[i])) return Difference(pa[i], va[i], $"{path}[{i}]", [.. keys, i.ToString(System.Globalization.CultureInfo.InvariantCulture)], p, v);
        }
        return new(path, keys, p, v, parentP, parentV);
    }

    /// <summary>The node the keys of a <see cref="Diff"/> lead to in another schema, or null where it has none.</summary>
    static JsonNode? At(JsonNode? node, IEnumerable<string> keys)
    {
        foreach (var k in keys)
            node = node switch { JsonObject o => o[k], JsonArray a when int.TryParse(k, out var i) && i < a.Count => a[i], _ => null };
        return node;
    }

    /// <summary>
    /// Side (1) of a row's wire and schema comparisons: the same host started without TypeKit (HostCfg.WithoutTypeKit), once, on the first
    /// comparison that asks for it; null where it does not start, which no comparison then excuses anything by. The twin's side (1) is a
    /// host with the twin's endpoints alone (<c>plain</c>): ASP.NET alone describing the value object's types beside them names components
    /// after them and writes the twin's own schemas otherwise. A host with both (<c>both</c>) answers for the value object's position.
    /// </summary>
    sealed class SideOne(Row r, Entry e, string only) : IAsyncDisposable
    {
        Task<WebApplication?>? started;
        public string? Down { get; private set; }
        public Task<WebApplication?> Host() => started ??= Start();
        async Task<WebApplication?> Start()
        {
            try { return await Hx.Start(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false, WithoutTypeKit = true, PayloadOnly = only }); }
            catch (Exception x) when (x is not Sides.InputsNotEquivalent) { Down = x.GetType().Name + ": " + Cut(x.Message); r.Log.AppendLine("side (1) host did not start: " + Down); return null; }
        }
        public async Task<Hx.Wire?> Ask(Func<WebApplication, Task<Hx.Wire>> ask) => await Host() is { } app ? await ask(app) : null;
        public async ValueTask DisposeAsync() { if (started is not null && await started is { } app) await app.DisposeAsync(); }
    }

    /// <summary>
    /// BaseProbe's calibration of the shared host: every wire comparison a check of its own, named <paramref name="obligation"/> (BaseProbe
    /// runs on rows of C07, which promises nothing of the wire, so its checks are the implicit <c>claim</c>). Per side: the echo against the
    /// twin; the request read with the row's input.json, against the twin, or a closed set against its own response read back; the binding of
    /// the row's http.binding with a valid value against the twin, and with a value Create refuses against the documented 400. Each is
    /// three-sided (Sides): side (1), the twin on the same host without TypeKit, is asked where the twin and the value object disagree or
    /// refuse. With <paramref name="schema"/>, the schema parity too (C21's check). Every check runs; the first failure is returned.
    /// </summary>
    public static async Task<string?> Run(Row r, Entry e, bool schema = true, string obligation = "claim")
    {
        WebApplication app;
        try { app = await Hx.Start(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }); }
        catch (Exception ex) when (ex is not Sides.InputsNotEquivalent) { return "baseline host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
        await using var _ = app;
        await using var one = new SideOne(r, e, "plain");
        await using var alone = new SideOne(r, e, "both");
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
            var f1 = await Wire(r, e, obligation, Baseline.Twin, name + " echo", ep, ev, () => one.Ask(h => Hx.Get(h, path(echo + "plain"))),
                c => Task.FromResult(Same(c, r, e, name + " echo", ep, ev)));
            failed ??= f1;
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
                {
                    var text = JsonParity.InputFor(input, e, jopts, ep.Body, position);
                    var ip = await Hx.Send(app, HttpMethod.Post, path(inn + "plain"), text); var iv = await Hx.Send(app, HttpMethod.Post, path(inn + "vo"), text);
                    var what = name + " in(" + input + ")";
                    f2 = await Wire(r, e, obligation, Baseline.Twin, what, ip, iv, () => one.Ask(h => Hx.Send(h, HttpMethod.Post, path(inn + "plain"), text)),
                        c => Task.FromResult(Same(c, r, e, what, ip, iv, input)));
                }
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
            async Task<Hx.Wire> B(WebApplication host, string which, string txt, string w)
            {
                var enc = Uri.EscapeDataString(txt);
                return bind switch
                {
                    "route" => await Hx.Get(host, path(bindPre + "route/" + w + "/" + enc)),
                    "query" => await Hx.Get(host, path(bindPre + "query/" + w + "?v=" + enc)),
                    "as-parameters" => await Hx.Get(host, path(bindPre + "params/" + w + "?v=" + enc)),
                    "header" => await Hx.Send(host, HttpMethod.Get, path(bindPre + "header/" + w), header: ("v", txt)),
                    "form" => await Hx.Send(host, HttpMethod.Post, path(bindPre + "form/" + w), "v=" + enc, "application/x-www-form-urlencoded"),
                    _ => await Hx.Send(host, HttpMethod.Post, path(bindPre + "body/" + w),
                        which == "valid" ? JsonSerializer.Serialize(plainSample, e.Plain, jopts) : which == "refused" ? JsonSerializer.Serialize(e.Refused(), e.Plain, jopts) : JsonSerializer.Serialize(txt)),
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
            string? f3;
            if (Unsafe(valid)) f3 = r.Check(obligation, Baseline.Twin, c => Unplaceable(c, valid));
            else
            {
                var bp = await B(app, "valid", valid, "plain"); var bv = await B(app, "valid", valid, "vo");
                r.Log.AppendLine($"{name} bind {bind} valid text={valid}: plain {(int)bp.Status} {Cut(bp.Body)} {bp.Thrown} | vo {(int)bv.Status} {Cut(bv.Body)} {bv.Thrown}");
                f3 = Routed(name, bind, bp, bv) is { } unrouted
                    ? r.Check(obligation, Baseline.Twin, _ => unrouted)
                    : await Wire(r, e, obligation, Baseline.Twin, name + " bind " + bind, bp, bv, () => one.Ask(h => B(h, "valid", valid, "plain")),
                        c => Task.FromResult(Same(c, r, e, name + " bind " + bind, bp, bv)));
            }
            failed ??= f3;
            if (refused is null) continue;
            // README "Binding" (AspNetCore): a value Create rejects gets a 400, the one set of rules.
            var against = Baseline.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#binding");
            if (Unsafe(refused)) { failed ??= r.Check(obligation, against, c => Unplaceable(c, refused)); continue; }
            var rp = await B(app, "refused", refused, "plain"); var rv = await B(app, "refused", refused, "vo");
            r.Log.AppendLine($"{name} bind {bind} refused text={refused}: plain {(int)rp.Status} {Cut(rp.Body)} {rp.Thrown} | vo {(int)rv.Status} {Cut(rv.Body)} {rv.Thrown}");
            if (Routed(name, bind, rp, rv) is { } unrouted2) { failed ??= r.Check(obligation, against, _ => unrouted2); continue; }
            // Side (1) of a refused binding: the twin on the host without TypeKit, refused a binding too, as MVC refuses a text the wrapped
            // type cannot read. Where the value object's refusal could not be answered (a 500, which MVC's 400 under a source-generated
            // context that does not list ValidationProblemDetails is, KB-041), the twin's refused binding is side (1): read from KB-041's
            // fixture where it covers the row, else asked of the host without TypeKit.
            Sides.Side? framework = null;
            if (rv.Ex is { } refusal && Unreadable(e) is { } unreadable)
            {
                var settings = Sides.Settings(r, ("endpoint", name == "mvc" ? "mvc-api-controller" : "minimal"), ("binding", "fails"));
                if (Sides.Covered(r, "KB-041", settings) is { Kind: FactAnswer.Exception } answer)
                    framework = new(new(name + " bind " + bind + " refused", null, Status: 500, FixtureRefusal: (string?)answer.Value["type"]), Baseline.Fact("KB-041"), $"KB-041 {answer.Id}: a binding MVC refuses, under a source-generated context");
                else if (await one.Ask(h => B(h, "unreadable", unreadable, "plain")) is { } twin)
                    framework = Sides.Measured(twin.Side(name + " bind " + bind + " refused"), $"the twin on a host without TypeKit, sent {unreadable}");
            }
            var f4 = await Sides.Check(r, obligation, against, framework, rv.Side(name + " bind " + bind + " refused"), null, null, async c =>
            {
                if ((int)rp.Status is not (>= 200 and < 300)) return c.Inconclusive($"{name} bind {bind}: the wrapped type refuses the value Create refuses too ({(int)rp.Status})");
                if (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class") return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "bind");
                // README "What TypeKit guarantees": StoredJsonConverterFactory reads JSON the application stored itself, without validation.
                if (bind == "body" && r.Is("json.Converters", "StoredJsonConverterFactory")) return c.Inapplicable(Cite.Docs("README.md#what-typekit-guarantees"), "bind body");
                // KB-037: under MaxValidationDepth=1 MVC runs an action whose parameter it could not bind, with the parameter at its default. The
                // value object must hold its default then, never the refused value; side (1), the twin sent a text it cannot read, shows the
                // framework doing the same: read from KB-037's fixture where it covers the row, else asked of the host without TypeKit.
                if (name == "mvc" && r.Is("mvc.MaxValidationDepth", "1") && (int)rv.Status is >= 200 and < 300)
                {
                    if (DefaultBody(app, e.Vo) is var voDefault && !HoldsDefault(rv, voDefault))
                        return $"{name} bind {bind}: validated value object bound the refused value (status {(int)rv.Status}) {Cut(rv.Body)}, not its default ({(voDefault is null ? "204" : "200 " + Cut(voDefault))})";
                    if (Unreadable(e) is not { } text)
                        return c.Inapplicable(Cite.Fact("KB-037"), $"{name} bind {bind}: the value object holds its default; any text binds the wrapped type, so no twin shows it");
                    Hx.Wire? bt;
                    if (Sides.Covered(r, "KB-037", Sides.Settings(r, ("text", text))) is { Kind: FactAnswer.Status } answer)
                        bt = new((HttpStatusCode)(int)answer.Value["status"]!, (string?)answer.Value["body"] ?? "");
                    else bt = await one.Ask(h => B(h, "unreadable", text, "plain"));
                    if (bt is null) return c.Inconclusive($"{name} bind {bind}: the value object holds its default (KB-037); no host without TypeKit to show the wrapped type's ({one.Down})");
                    r.Log.AppendLine($"{name} bind {bind} unreadable text={text}: plain {(int)bt.Status} {Cut(bt.Body)} {bt.Thrown}");
                    if (HoldsDefault(bt, DefaultBody(app, e.Plain)))
                        return c.Inapplicable(Cite.Fact("KB-037"), $"{name} bind {bind}: both hold their default, the wrapped type for a text it cannot read");
                    if ((int)bt.Status is >= 400 and < 500)
                        return $"{name} bind {bind}: the value object answered {(int)rv.Status} holding its default, where the wrapped type rejects a text it cannot read ({(int)bt.Status})";
                    // KB-037's class from the query: the validator throws at the twin's property, and the value object, written by a
                    // converter of its own, is no class it walks
                    return c.Inconclusive($"{name} bind {bind}: the value object holds its default (KB-037); the wrapped type answered {(int)bt.Status} {Cut(bt.Body)} {bt.Thrown}");
                }
                // a refused value must not bind (the one set of rules), except where the application skips validation on purpose
                if ((int)rv.Status is >= 200 and < 300) return $"{name} bind {bind}: validated value object bound the refused value (status {(int)rv.Status}) {Cut(rv.Body)}";
                if ((int)rv.Status != 400) return $"{name} bind {bind}: refused value answered {(int)rv.Status}, expected 400: {Cut(rv.Body)} {rv.Thrown}";
                c.Note(NoteKeys.Compared, $"{name} bind {bind}: the refused value answered 400");
                return null;
            });
            failed ??= f4;
        }
        // 4. schema parity
        if (schema) { var f5 = await r.Check(obligation, Baseline.Twin, async c => await SchemaParity(app, one, alone, r, e, c)); failed ??= f5; }
        return failed;
    }

    /// <summary>
    /// The schema parity alone, on the same host as <see cref="Run"/>: claim C21's check (C21OpenapiSchemaParityProbe), which BaseProbe leaves
    /// out of its calibration, run once for each of the row's <paramref name="obligations"/> against the twin. C21 promises the schema ASP.NET
    /// Core publishes for the underlying type, so side (1), the same host without TypeKit, is started on every row (Sides.Unconditional).
    /// Every check runs; the first failure is returned.
    /// </summary>
    public static async Task<string?> Schema(Row r, Entry e, IEnumerable<string> obligations)
    {
        WebApplication? app = null; string? down = null;
        try { app = await Hx.Start(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }); }
        catch (Exception ex) when (ex is not Sides.InputsNotEquivalent) { down = "schema host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
        await using var _ = app;
        await using var one = new SideOne(r, e, "plain");
        await using var alone = new SideOne(r, e, "both");
        var list = obligations.ToList();
        if (down is null && list.Any(o => Sides.Unconditional(r, o))) await one.Host();
        string? failed = null;
        foreach (var obligation in list)
        {
            var f = await r.Check(obligation, Baseline.Twin, c => down is not null ? Task.FromResult<string?>(down) : SchemaParity(app!, one, alone, r, e, c));
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

    /// <summary>
    /// The component two operations of one document both refer to at the same place (a parameter's or the body's schema), where one
    /// substitutes the value object for the twin: two types under one name, told by the references' structure.
    /// </summary>
    static string? SharedComponent(JsonNode twin, JsonNode valueObject)
    {
        static IEnumerable<(string, string)> References(JsonNode op)
        {
            if (op["parameters"] is JsonArray ps)
                for (var i = 0; i < ps.Count; i++)
                    if (ps[i]?["schema"]?["$ref"]?.GetValue<string>() is { } p) yield return ($"parameter {i}", p);
            if (op["requestBody"]?["content"] is JsonObject content)
                foreach (var (media, body) in content)
                    if (body?["schema"]?["$ref"]?.GetValue<string>() is { } b) yield return ($"body {media}", b);
        }
        var twins = References(twin).ToDictionary(x => x.Item1, x => x.Item2);
        return References(valueObject).FirstOrDefault(x => twins.TryGetValue(x.Item1, out var t) && t == x.Item2).Item2;
    }

    /// <summary>A body whose schema is its null alone, <c>{"schema": {"allOf": [{"x-nullable": true}]}}</c>, anywhere in an operation: told by its structure.</summary>
    /// <summary>A schema that is the OpenAPI 2.0 null flag and nothing else: <c>{"allOf":[{"x-nullable":true}]}</c>.</summary>
    static bool OnlyTheNullFlag(JsonObject o) =>
        o.Count == 1 && o["allOf"] is JsonArray { Count: 1 } all && all[0] is JsonObject { Count: 1 } flag && flag["x-nullable"]?.GetValueKind() == JsonValueKind.True;

    static bool OnlyItsNull(JsonNode? n) => n switch
    {
        JsonObject o => o["schema"] is JsonObject { Count: 1 } schema && schema["allOf"] is JsonArray { Count: 1 } all && all[0] is JsonObject { Count: 1 } flag
                        && flag["x-nullable"]?.GetValueKind() == JsonValueKind.True
                        || o.Any(kv => OnlyItsNull(kv.Value)),
        JsonArray a => a.Any(OnlyItsNull),
        _ => false,
    };

    /// <summary>
    /// The value object's schema (3) against the wrapped twin's (2) at each endpoint pair, three-sided (Sides): the twin's schema is held to
    /// the one ASP.NET publishes on the same host without TypeKit (1), where that host writes a document; and where the value object's schema
    /// differs from the twin's, side (1)'s document at the same path tells whether ASP.NET alone writes that difference.
    /// </summary>
    static async Task<string?> SchemaParity(WebApplication app, SideOne one, SideOne alone, Row r, Entry e, Check c)
    {
        JsonNode doc;
        try { doc = (await Hx.Doc(app))!; }
        catch (Exception ex)
        {
            // The baseline is ASP.NET alone on the same host: a document it refuses too has no schema to compare (decision 99).
            return await Hx.DocRefusedByAspNetAlone(r, e, new HostCfg { FaultControllers = false, MapBlanketMapping = false }, ex);
        }
        // docs/json.md "Describing what a converter writes": ASP.NET drops items/additionalProperties of a converter it cannot see into, the value
        // object's schema is the underlying type's; and the application's converter for the value object describes it as that converter says.
        // C21 declares both on every obligation (claims.json inapplicable), so Row.Check records them before this body runs and no row of
        // C21 is generated with them; these lines hold for the schema half run under another claim (ShrinkProbe's baseline).
        if (r.Is("json.Converters", "own-for-wrapped")) return c.Inapplicable(Cite.NotPromised("own-converter-for-the-wrapped-type"));
        if (r.Is("json.Converters", "own-for-value-object")) return c.Inapplicable(Cite.NotPromised("own-converter-for-the-value-object"));
        // side (1): the document ASP.NET writes for the twin's endpoints without TypeKit, where it writes one; and, asked only where the value
        // object's schema differs from the twin's, the one it writes for both sides' endpoints, for the value object's position
        async Task<JsonNode?> Document(SideOne host)
        {
            if (await host.Host() is not { } h) return null;
            try { return await Hx.Doc(h); }
            catch (Exception x) { r.Log.AppendLine("side (1) document refused: " + x.GetType().Name + ": " + Cut(x.Message)); return null; }
        }
        var doc1 = await Document(one);
        JsonNode? Op(JsonNode d, string path, string method) => d["paths"]?[path]?[method];
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
        string Sig(JsonNode d, JsonNode op)
        {
            var o = new JsonObject();
            o["parameters"] = Schemas(Hx.Deep(d, op["parameters"], mark: true));
            o["requestBody"] = Schemas(Hx.Deep(d, op["requestBody"], mark: true));
            o["ok"] = Schemas(Hx.Deep(d, op["responses"]?["200"], mark: true));
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
            // Like with like from the start: the twin is the wrapped type at the value object's nullability (shapes.cs), so a class
            // value object over a struct faces the struct made nullable, at every endpoint (row 384 of C07: a record class over TimeOnly,
            // optional, against TimeOnly?).
            var pp = pp0;
            var po = Op(doc, pp, method); var vo = Op(doc, vp, method);
            if (po is null)
            {
                // Mapped for bodies only (a record class, Instant in a minimal API): neither side is there.
                if (vo is not null) return $"harness: the value object's {method} {vp} is documented, its plain twin is not (missing {method} {pp})";
                c.Note(NoteKeys.SchemaNeitherSideDocumented, $"missing {method} {pp}"); continue;
            }
            if (vo is null) return $"missing {method} {vp}";
            // The twin's and the value object's shapes are two .NET types; one component name for both (an id the row's CreateSchemaReferenceId
            // makes alike for both, such as an array of a generic type) holds one schema for the two, so the document cannot tell them apart.
            if (SharedComponent(po, vo) is { } shared)
            { c.Inconclusive($"{pp} vs {vp}: the twin's and the value object's shapes are one component, {shared}, in the document: nothing to compare"); continue; }
            // (2) held to (1): the twin's schema is the one ASP.NET publishes on the same host without TypeKit, but where the twin itself
            // holds one of TypeKit's types (a Patch), which no host without TypeKit describes
            var po1 = doc1 is null ? null : Op(doc1, pp, method);
            var twinHoldsTypeKit = pp.Contains("/schema/") && Sides.HoldsTypeKit(Twin(r, e).Declared);
            if (po1 is not null && !twinHoldsTypeKit && Sig(doc1!, po1) is var s1 && Sig(doc, po) is var s2 && s1 != s2)
                return $"schema of the wrapped type at {pp}: TypeKit changed the wrapped type's schema, at {FirstDifference(JsonNode.Parse(s1), JsonNode.Parse(s2), "$")} (without TypeKit, then with it)";
            if (Inputs(po) is var ip && Inputs(vo) is var iv && ip != iv)
            {
                // No twin of the wrapped type at the value object's nullability here (a struct value object over a class, which no plain
                // type mirrors), or a wrapped record class, which ASP.NET binds by its properties where the value object is one value: the
                // two sides are not alike, and no sentence cites it, so the check cannot tell.
                if (Make.AdmitsNull(e.Vo) != Make.AdmitsNull(e.Plain))
                    c.Inconclusive($"{pp} vs {vp}: no twin of {e.Plain.Name} at the value object's nullability, the inputs differ: wrapped [{ip}] vs value object [{iv}]");
                else if (e.Wrapped == "own-record-class")
                    c.Inconclusive($"{pp} vs {vp}: the wrapped record class is bound by its properties, the value object as one value, the inputs differ: wrapped [{ip}] vs value object [{iv}]");
                else r.Outcome("harness-error", $"inputs not equivalent for {pp} vs {vp}: wrapped [{ip}] vs value object [{iv}]");
                continue;
            }
            var sp = Sig(doc, po); var sv = Sig(doc, vo);
            if (NullAroundReferences(sp, sv) is var (np, nv, around)) { sp = np; sv = nv; if (around) c.Note(NoteKeys.SchemaNullAroundReferenceNormalised); }
            if (sp != sv && Make.AdmitsNull(e.Vo) && Make.AdmitsNull(e.Plain) && e.Vo.IsValueType != e.Plain.IsValueType)
            {
                // Both sides nullable alike, one a class and the other a struct made nullable (a class value object and its nullable twin, or
                // a nullable struct value object and a wrapped class); OpenAPI 3.x spells nullability two ways (oneOf [null, X] or type [null, X]).
                sp = Hx.NoNull(JsonNode.Parse(sp))!.ToJsonString(); sv = Hx.NoNull(JsonNode.Parse(sv))!.ToJsonString();
                c.Note(NoteKeys.SchemaNullableSpellingsNormalised);
            }
            if (sp != sv && Make.AdmitsNull(e.Vo) != Make.AdmitsNull(e.Plain))
            {
                // A struct value object over a class, whose twin is nullable by being a class where the value object is not (no plain
                // type mirrors it); the markers go, the rest of the schema must agree.
                sp = Hx.NoNull(JsonNode.Parse(sp))!.ToJsonString(); sv = Hx.NoNull(JsonNode.Parse(sv))!.ToJsonString();
                c.Note(NoteKeys.SchemaClassVoNullabilityNormalised);
            }
            if (sp != sv)
            {
                if (e.Kind == "closed-set") { c.Inapplicable(Cite.NotPromised("a-closed-set-is-a-string-enum"), "schema"); continue; }
                var d = Difference(JsonNode.Parse(sp), JsonNode.Parse(sv), "$");
                var openApi2 = r.Is("openapi.OpenApiVersion", "OpenApi2_0");
                // Side (1) at the same path: the twin's and the value object's operations in the document ASP.NET writes without TypeKit.
                var twin1 = po1 is null ? null : JsonNode.Parse(Sig(doc1!, po1));
                var both1 = openApi2 && d.P is null && d.V is not null ? await Document(alone) : null;
                var vo1 = both1 is null ? null : Op(both1, vp, method) is { } o1 ? JsonNode.Parse(Sig(both1, o1)) : null;
                var vo3 = JsonNode.Parse(Sig(doc, vo));
                // KB-026: OpenAPI 2.0 writes a nullable property of any component as allOf [{x-nullable: true}], without the reference, and a
                // value object keeps a component of its own (not-promised openapi-2-nullable-component-loses-its-type; row 38 of C21). Side (1)
                // shows it: without TypeKit, ASP.NET writes the value object's position at the same path the same, where the twin has nothing.
                if (openApi2 && d.P is null && d.V is not null && At(vo1, d.Keys) is { } same1 && JsonNode.DeepEquals(same1, At(vo3, d.Keys)))
                { c.Inapplicable(Cite.NotPromised("openapi-2-nullable-component-loses-its-type"), d.Path); continue; }
                // KB-001: OpenAPI 2.0 writes a nullable body of the wrapped value type as allOf [{x-nullable: true}] alone, where a class value
                // object's body in a minimal API is not nullable and keeps its schema (not-promised openapi-2-nullable-body-keeps-only-its-null;
                // row 257). Side (1) shows it: without TypeKit, the twin's schema there is that null alone.
                // AspNetCore README "What the document says" (KB-026), seen from the twin: OpenAPI 2.0 writes a nullable property of any component,
                // the twin's nullable enum among them, as allOf [{x-nullable: true}] without the reference, where the value object keeps its type.
                // Side (1) shows it: without TypeKit, the twin's property there is that null alone.
                if (openApi2 && d.P is null && d.V is not null && Make.AdmitsNull(e.Plain) && At(twin1, d.Keys.SkipLast(1)) is JsonObject t1 && OnlyTheNullFlag(t1))
                { c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), d.Path); continue; }
                if (openApi2 && !e.Vo.IsValueType && Nullable.GetUnderlyingType(e.Plain) is not null && OnlyItsNull(twin1) && OnlyItsNull(JsonNode.Parse(Sig(doc, po))))
                { c.Inapplicable(Cite.NotPromised("openapi-2-nullable-body-keeps-only-its-null"), pp); continue; }
                // KB-027: the component ASP.NET makes for an enum written by name is the schema of the first use it meets, so a wrapped E?
                // parameter lists null through E's component where E was first met nullable; the value object's parameter is the enum's
                // content without it wherever the enum is a component (not-promised enum-component-is-its-first-use; row 285 of C21). Side (1)
                // shows it: without TypeKit, the twin's parameter lists the same null, through the same resolved component.
                if (d.Keys is ["parameters", .., "enum"] && d.P is JsonArray we && d.V is JsonArray ve && we.Count == ve.Count + 1 && we[^1] is null
                    && JsonNode.DeepEquals(new JsonArray(we.Take(ve.Count).Select(x => x?.DeepClone()).ToArray()), ve)
                    && At(JsonNode.Parse(Sig(doc, po)), d.Keys.SkipLast(1)) is JsonObject held && held.ContainsKey(Hx.ResolvedReference)
                    && At(twin1, d.Keys) is { } listed1 && JsonNode.DeepEquals(listed1, d.P))
                { c.Inapplicable(Cite.NotPromised("enum-component-is-its-first-use"), vp); continue; }
                return $"schema differs for {pp} vs {vp} at {FirstDifference(JsonNode.Parse(sp), JsonNode.Parse(sv), "$")}:\n  wrapped {Cut(sp)}\n  value object {Cut(sv)}";
            }
        }
        return null;
    }
}
