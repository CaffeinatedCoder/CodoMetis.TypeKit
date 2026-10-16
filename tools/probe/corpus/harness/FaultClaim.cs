// The shared runner of the fault claims C04-without-faults, C06-fault-kinds, C14-declared-problem and C15-mvc-fault-status-doc
// (AspNetCore README "A closed set as the fault type", "A status per endpoint", "Leaving out what a route cannot answer"; CHANGELOG 2.1.0 Fixed).
// A claim is a Spec: its minimal endpoints as a tree of groups, the MVC controllers of FxCtl (FaultClaimControllers.g.cs), what
// MapControllers()'s builder adds, and the errors each endpoint reports. Each declaration is listed where it is made, so an endpoint's
// declarations come in ASP.NET's metadata order: outer group, inner group, endpoint; or controller, action, then MapControllers()'s builder.
// The oracle is the README's rules applied to those declarations, never the product's own reading of them:
//  - a member is answered with the last status its endpoint's metadata sets for it, else the mapped one ("When several statuses name one
//    member, the last in ASP.NET's metadata order wins"); the mapping is by kind (README table), or its own with a problem type per kind;
//  - a member left out anywhere in that metadata is listed nowhere in the operation, and reporting it throws an InvalidOperationException
//    naming the endpoint and the member ("Leave out only what the route can't report");
//  - the operation lists every status of every remaining member of every answered type, the endpoint's own declared ones among them, all its
//    responses ascending; each fault status's problem schema describes fault and rule, and its description names exactly the problem types
//    of the members answered with it ("names its problem types", CHANGELOG 2.1.0);
//  - a status that every endpoint of its scope leaves out stops the host, naming the member and the status.
// http.response picks what every endpoint of the row answers (one fault type, a composed fault, two types writing one name) and what the
// outermost scope adds (a status for another member, a member left out, a problem the endpoint declares itself).
using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Probes.Shapes;
namespace Probes.Faults;

/// <summary>One declaration about one member: a status (WithFaultStatus, [FaultStatus]) or, with status 0, the member left out.</summary>
public sealed record Decl(string Type, string Member, int Status)
{
    public bool Out => Status == 0;
    public bool Names(Mem m) => Type == m.Type && Member == m.Name;
    public override string ToString() => Out ? $"WithoutFaults({Type}.{Member})" : $"WithFaultStatus({Type}.{Member}, {Status})";
}
/// <summary>An MVC action of FxCtl: its route (before the result-type segment), what it declares, the problem statuses it declares itself.</summary>
public sealed record Act(string Route, Decl[] Decls, int[] Declared);
/// <summary>An MVC controller of FxCtl: the [ApiController] and the plain copy, what the class declares, its actions.</summary>
public sealed record Ctl(Type Api, Type Plain, Decl[] Decls, Act[] Actions);
public static partial class FxCtl
{
    public static readonly Dictionary<string, Ctl> All = Build();
    static Dictionary<string, Ctl> Build() { var d = new Dictionary<string, Ctl>(StringComparer.Ordinal); Fill(d); return d; }
    static partial void Fill(Dictionary<string, Ctl> d);
}

/// <summary>A minimal endpoint, relative to its group; How: "pp" = ProducesProblem(each Declared), "prt" = [ProducesResponseType&lt;ProblemDetails&gt;(404)].</summary>
public sealed record MEp(string Route, Decl[] Decls, int[]? Declared = null, string? How = null);
/// <summary>A route group: its prefix (never empty), what it declares, its inner groups and endpoints.</summary>
public sealed record MGrp(string Prefix, Decl[] Decls, MGrp[] Groups, MEp[] Eps);

/// <summary>A member of a fault type the endpoints answer with, its kind and the status its kind maps to (README table).</summary>
public sealed record Mem(string Type, string Name, string Kind, int Status) { public string Q => Type + "." + Name; }

public static class FxErr
{
    public static readonly Mem[] H =
    [
        new("HFault", "Missing", "NotFound", 404), new("HFault", "Clash", "Conflict", 409), new("HFault", "Bad", "Invalid", 422),
        new("HFault", "Denied", "Forbidden", 403), new("HFault", "Down", "Unavailable", 503), new("HFault", "Stale", "PreconditionFailed", 412),
        new("HFault", "Garbled", "Malformed", 400),
    ];
    static readonly Mem[] G = [new("GFault", "Gone", "NotFound", 404), new("GFault", "Raced", "Conflict", 409), new("GFault", "Old", "PreconditionFailed", 412)];
    static readonly Mem[] K = [new("KFault", "Clash", "Conflict", 409), new("KFault", "Lost", "NotFound", 404)];
    /// <summary>Every member of every type an endpoint answering <paramref name="t"/> answers with.</summary>
    public static Mem[] Answered(string t) => t == "two" ? [.. H, .. G] : t == "dup" ? [.. H, .. K] : H;
    /// <summary>e: 1-based index into H; 0 is a success.</summary>
    static HFault Of(int e) => (HFault)typeof(HFault).GetField(H[e - 1].Name)!.GetValue(null)!;
    public static Result<Fault<HFault>> One(int e) { if (e == 0) return Result<Fault<HFault>>.Success(); return Fault.Of(Of(e)); }
    public static Result<Fault<HFault, GFault>> Two(int e) => e == 0 ? Result<Fault<HFault, GFault>>.Success() : Fault<HFault, GFault>.Of(Of(e));
    public static Result<Fault<HFault, KFault>> Dup(int e) => e == 0 ? Result<Fault<HFault, KFault>>.Success() : Fault<HFault, KFault>.Of(Of(e));
    public static int E(string member) => Array.FindIndex(H, m => m.Name == member) + 1;
}

/// <summary>One endpoint as the oracle sees it: its URL in this host, the result it answers, its declarations in metadata order.</summary>
public sealed record Ep(bool Mvc, string Url, string T, Decl[] Decls, int[] Declared, Func<Mem, (int Status, string? Type)> Map)
{
    public bool LeftOut(Mem m) => Decls.Any(d => d.Out && d.Names(m));
    public int StatusOf(Mem m) => Decls.LastOrDefault(d => !d.Out && d.Names(m))?.Status ?? Map(m).Status;
    public SortedSet<int> FaultStatuses() => new(FxErr.Answered(T).Where(m => !LeftOut(m)).Select(StatusOf));
    public SortedSet<int> DocStatuses() { var s = FaultStatuses(); foreach (var d in Declared) s.Add(d); return s; }
    public HashSet<string> TypesAt(int status) =>
        [.. FxErr.Answered(T).Where(m => !LeftOut(m) && StatusOf(m) == status).Select(m => Map(m).Type).OfType<string>()];
}

/// <summary>What one claim's row asks: see the header.</summary>
public sealed class Spec
{
    public MGrp? Minimal;                  // under the outer group /m, where the host has minimal APIs
    public string[] Controllers = [];      // FxCtl keys, where it has MVC
    public Decl[] Builder = [];            // added to MapControllers()'s builder, after the response's own
    public int[] Errors = [];              // the HFault members each endpoint reports (FxErr.E)
    public bool Typed = true;              // the host's mapping gives a problem type per kind; else AddFaultResponses<TSet>() by kind
    public string[]? Refused;              // the host must not start, with an InvalidOperationException naming each of these
    public Func<Exception, string?>? RefusedAs;   // or: the host must not start, and this judges the exception (null: as expected)
    public Func<Mem, (int Status, string? Type)>? Map;   // how the host maps each member; default by kind, with a type per kind where Typed
    public string? Mapping;                // HostCfg.Mapping in place of Typed's
    public Action<IServiceCollection>? Services;
    public Action<IEndpointRouteBuilder>? Extra;   // more minimal endpoints, at the root
    public Type[] ExtraControllers = [];
}

/// <summary>What http.response makes every endpoint of the row answer, and what the outermost scope adds.</summary>
public sealed record Resp(string T, Decl[] Outer, int[] Declared)
{
    public static Resp Of(Row r) => r["http.response"] switch
    {
        "composed-fault" => new("two", [], []),
        "two-faults-same-name" => new("dup", [], []),
        "group-with-fault-status" => new("one", [new("HFault", "Clash", 422)], []),
        "endpoint-without-faults" => new("one", [new("HFault", "Missing", 0)], []),
        "declared-produces-problem" => new("decl", [], [429]),
        _ => new("one", [], []),
    };
}

/// <summary>The baseline's problem response in MVC (see FaultClaim.DocRefusedByAspNetAlone).</summary>
[ApiController]
internal sealed class FxProblemBaseline : ControllerBase
{
    [HttpGet("baseline-problem/c"), ProducesResponseType<ProblemDetails>(404, "application/problem+json")] public IActionResult Get() => NoContent();
}

public static class FaultClaim
{
    public static string TypeOf(string kind) => "https://p.example/" + kind;
    static string? Un(Row r, string? s) => r.Is("json.Converters", "own-for-wrapped") && s is not null && s.StartsWith("own:") ? s[4..].Trim('"') : s;
    static string Cut(string? s) => s is null ? "(null)" : s.Length > 300 ? s[..300] + "..." : s;
    static void Process(Row r, Entry en) { if (r.Is("process.converter-lookup", "wrapped-type-converter-looked-up-first")) _ = System.ComponentModel.TypeDescriptor.GetConverter(Make.Held(en.Plain)); }

    static T Member<T>(string name) => (T)typeof(T).GetField(name)!.GetValue(null)!;
    static TB Apply<TB>(TB b, Decl d) where TB : IEndpointConventionBuilder => (d.Type, d.Out) switch
    {
        ("HFault", true) => b.WithoutFaults(Member<HFault>(d.Member)),
        ("HFault", false) => b.WithFaultStatus(Member<HFault>(d.Member), d.Status),
        ("GFault", true) => b.WithoutFaults(Member<GFault>(d.Member)),
        ("GFault", false) => b.WithFaultStatus(Member<GFault>(d.Member), d.Status),
        ("KFault", true) => b.WithoutFaults(Member<KFault>(d.Member)),
        _ => b.WithFaultStatus(Member<KFault>(d.Member), d.Status),
    };

    static RouteHandlerBuilder MapT(IEndpointRouteBuilder g, string path, string t, string? how) => (t, how) switch
    {
        ("two", "prt") => g.MapGet(path, [ProducesResponseType<ProblemDetails>(404)] (int e) => FxErr.Two(e).ToHttpResult()),
        ("dup", "prt") => g.MapGet(path, [ProducesResponseType<ProblemDetails>(404)] (int e) => FxErr.Dup(e).ToHttpResult()),
        (_, "prt") => g.MapGet(path, [ProducesResponseType<ProblemDetails>(404)] (int e) => FxErr.One(e).ToHttpResult()),
        ("two", _) => g.MapGet(path, (int e) => FxErr.Two(e).ToHttpResult()),
        ("dup", _) => g.MapGet(path, (int e) => FxErr.Dup(e).ToHttpResult()),
        _ => g.MapGet(path, (int e) => FxErr.One(e).ToHttpResult()),
    };

    /// <summary>Walks a minimal tree: maps it where <paramref name="b"/> is given, and lists its endpoints with their declarations in metadata order.</summary>
    static void Walk(IEndpointRouteBuilder? b, MGrp g, Decl[] above, string prefix, Resp resp, List<(string Path, Decl[] Decls, int[] Declared)> eps)
    {
        RouteGroupBuilder? grp = null;
        if (b is not null) { grp = b.MapGroup(g.Prefix); foreach (var d in g.Decls) Apply(grp, d); }
        Decl[] decls = [.. above, .. g.Decls];
        var pre = prefix + g.Prefix;
        foreach (var sub in g.Groups) Walk(grp, sub, decls, pre, resp, eps);
        foreach (var ep in g.Eps)
        {
            var path = "/" + ep.Route + "/" + resp.T;
            if (grp is not null)
            {
                var rb = MapT(grp, path, resp.T, ep.How);
                foreach (var d in ep.Decls) Apply(rb, d);
                if (ep.How == "pp") foreach (var s in ep.Declared ?? []) rb.ProducesProblem(s);
                if (resp.T == "decl") rb.ProducesProblem(429);
            }
            eps.Add((pre + path, [.. decls, .. ep.Decls], [.. ep.Declared ?? [], .. resp.Declared]));
        }
    }

    static (List<Ep> Eps, HostCfg Cfg) Plan(Row r, Spec s, Resp resp)
    {
        var eps = new List<Ep>();
        var map = s.Map ?? (m => (m.Status, s.Typed ? TypeOf(m.Kind) : (string?)null));
        MGrp? root = s.Minimal is null ? null : new MGrp("/m", resp.Outer, [s.Minimal], []);
        if (root is not null && Hx.HasMinimal(r))
        {
            var found = new List<(string Path, Decl[] Decls, int[] Declared)>();
            Walk(null, root, [], "", resp, found);
            eps.AddRange(found.Select(x => new Ep(false, Hx.M(r, x.Path), resp.T, x.Decls, x.Declared, map)));
        }
        var controllers = new List<Type>();
        Decl[] builder = [.. resp.Outer, .. s.Builder];
        if (Hx.HasMvc(r))
            foreach (var key in s.Controllers)
            {
                var c = FxCtl.All[key];
                controllers.Add(Hx.Plain(r) ? c.Plain : c.Api);
                foreach (var a in c.Actions)
                    eps.Add(new Ep(true, Hx.C(r, "/c/" + a.Route + "/" + resp.T), resp.T, [.. c.Decls, .. a.Decls, .. builder], [.. a.Declared, .. resp.Declared], map));
            }
        if (Hx.HasMvc(r)) controllers.AddRange(s.ExtraControllers);
        var cfg = new HostCfg
        {
            FaultControllers = true, Controllers = [.. controllers],
            Mapping = s.Mapping ?? (s.Typed ? "own-IFault-mapping" : "per-set"),
            Services = sv => { if (s.Mapping is null && !s.Typed) sv.AddFaultResponses<KFault>(); s.Services?.Invoke(sv); },
            Minimal = b => { if (root is not null) Walk(b, root, [], "", resp, []); s.Extra?.Invoke(b); },
            ControllerBuilder = cb => { foreach (var d in builder) Apply(cb, d); return cb; },
        };
        return (eps, cfg);
    }

    const string Listed = "the probes' source-generated context (ProbeContext) lists value objects at root and as a property of a class only";

    /// <summary>
    /// Under json.TypeInfoResolver=source-generated-context the payload's shape moves to one the context lists (as a property of a class),
    /// so the strict context applies and its pairs with the fault axes are earned; JsonRow would otherwise fall back to reflection for the
    /// whole row. NodaTime.Instant is listed nowhere and keeps JsonRow's fallback. The pairs of the strict context with the other positions
    /// and containers are excluded in claims.json for the same reason.
    /// </summary>
    static Row StrictContext(Row r)
    {
        if (!r.Is("json.TypeInfoResolver", "source-generated-context") || r["shape.wrapped"] == "NodaTime.Instant") return r;
        if (r["shape.position"] is not ("root" or "property")) { r = r.With("shape.position", "property"); r.Substituted("shape.position", "property", Listed); }
        if (r.Get("shape.containing") is not (null or "class")) { r = r.With("shape.containing", "class"); r.Substituted("shape.containing", "class", Listed); }
        return r;
    }

    public static async Task<string?> Run(Row r, Spec s)
    {
        r = StrictContext(r);
        var en = Registry.Of(r); Process(r, en);
        var resp = Resp.Of(r);
        var (eps, cfg) = Plan(r, s, resp);
        if (eps.Count == 0) return "harness: the row maps no endpoint of the claim (an excluded pair?)";
        WebApplication app;
        try { app = await Hx.Start(r, en, cfg); }
        catch (Exception ex)
        {
            if (s.RefusedAs is { } judge) return judge(ex);
            if (s.Refused is not { } names) return "host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message);
            var ioe = ex as InvalidOperationException ?? ex.InnerException as InvalidOperationException;
            if (ioe is null) return $"host refused to start, but with {ex.GetType().Name}, not InvalidOperationException: {Cut(ex.Message)}";
            var missing = names.Where(n => !ioe.Message.Contains(n, StringComparison.Ordinal)).ToList();
            return missing.Count == 0 ? null : $"host refused to start, but not naming {string.Join(", ", missing)}: {Cut(ioe.Message)}";
        }
        await using var _ = app;
        if (s.RefusedAs is not null) return "host started, though the README says this set stops the start";
        if (s.Refused is { } refused) return $"host started, though a status every endpoint of its scope leaves out must stop it ({string.Join(", ", refused)})";

        var display = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .GroupBy(e => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DisplayName ?? "", StringComparer.OrdinalIgnoreCase);

        JsonNode? doc = null; string? docFail = null;
        try { doc = await Hx.Doc(app); }
        catch (Exception ex) { docFail = await DocRefusedByAspNetAlone(r, en, cfg, ex); }
        if (docFail is not null) return docFail;

        foreach (var ep in eps)
        {
            foreach (var e in s.Errors)
            {
                var fail = await Wire(r, app, ep, FxErr.H[e - 1], e, display);
                if (fail is not null) return fail;
            }
            if (doc is null) continue;   // ASP.NET alone refuses this document too (noted)
            var f = DocOf(r, doc, ep);
            if (f is not null) return f;
        }
        return null;
    }

    /// <summary>
    /// Hx.DocRefusedByAspNetAlone, with a baseline that has a problem response: the same host without AddTypeKit(), plus one endpoint (and one
    /// action, where the host has MVC) declaring ProducesProblem(404). AddTypeKit() adds its problem responses "as ProducesProblem would"
    /// (README "A Result as a response"), so ASP.NET alone writing a document of the same host without any problem response says nothing about
    /// them: under json.MaxDepth=3 System.Text.Json refuses the problem schema of ASP.NET's own ProducesProblem too (decision 99).
    /// </summary>
    static async Task<string?> DocRefusedByAspNetAlone(Row r, Entry en, HostCfg cfg, Exception withTypeKit)
    {
        static string Say(Exception x) => x.GetType().Name + ": " + Cut(x.Message);
        var alone = cfg.Copy(); alone.TypeKitOpenApi = false;
        alone.Controllers = [.. cfg.Controllers ?? [], typeof(FxProblemBaseline)];
        alone.Minimal = b => { cfg.Minimal?.Invoke(b); b.MapGet("/baseline-problem", () => Results.NoContent()).ProducesProblem(404); };
        try
        {
            await using var app = await Hx.Start(r, en, alone);
            await Hx.Doc(app);
        }
        catch (Exception ex) when (ex.GetType() == withTypeKit.GetType() && ex.Message == withTypeKit.Message)
        {
            r.Note(NoteKeys.DocRefusedByAspnetAlone, ex.GetType().Name);
            return null;
        }
        catch (Exception ex) when (r.Is("json.MaxDepth", "3") && Depth(ex) && Depth(withTypeKit))
        {
            // Both refuse the problem schema under MaxDepth=3, by System.Text.Json's own depth checks, but on different paths (TypeKit's
            // transformer through the schema exporter, ProducesProblem through the document writer): a refusal of STJ's own, nothing to check.
            r.Note(NoteKeys.StjMaxdepth, $"{Say(withTypeKit)} / {Say(ex)}");
            return null;
        }
        catch (Exception ex) { return $"document: AddTypeKit() fails ({Say(withTypeKit)}), ASP.NET alone with a ProducesProblem fails otherwise ({Say(ex)})"; }
        return $"document: AddTypeKit() fails ({Say(withTypeKit)}), ASP.NET alone with a ProducesProblem writes it";
    }

    static bool Depth(Exception x) => x is InvalidOperationException or System.Text.Json.JsonException;

    static async Task<string?> Wire(Row r, WebApplication app, Ep ep, Mem m, int e, Dictionary<string, string> display)
    {
        var w = await Hx.Get(app, $"{ep.Url}?e={e}");
        var at = $"{ep.Url} reporting {m.Q}";
        if (ep.LeftOut(m))
        {
            if (w.Thrown is null) return $"{at}: left out of its document, but answered {(int)w.Status}: {Cut(w.Body)}";
            if (!w.Thrown.StartsWith(nameof(InvalidOperationException) + ":", StringComparison.Ordinal)) return $"{at}: left out, threw {Cut(w.Thrown)}, expected an InvalidOperationException";
            if (!display.TryGetValue(ep.Url, out var name) || name.Length == 0) return $"harness: no endpoint named at {ep.Url}";
            if (!w.Thrown.Contains(name, StringComparison.Ordinal) || !w.Thrown.Contains(m.Q, StringComparison.Ordinal))
                return $"{at}: the refusal does not name the endpoint ({name}) and the member ({m.Q}): {Cut(w.Thrown)}";
            return null;
        }
        if (w.Thrown is not null) return $"{at}: request threw {Cut(w.Thrown)}";
        var status = ep.StatusOf(m);
        if ((int)w.Status != status) return $"{at}: status {(int)w.Status}, expected {status}: {Cut(w.Body)}";
        JsonNode? body; try { body = JsonNode.Parse(w.Body); } catch { return $"{at}: body is not JSON: {Cut(w.Body)}"; }
        var fault = Un(r, body?["fault"]?.GetValue<string>());
        if (fault != m.Name) return $"{at}: fault {fault ?? "(none)"}, expected {m.Name}";
        if (int.Parse(body?["status"]?.ToString() is { } st ? Un(r, st)!.Trim('"') : "0") != status) return $"{at}: problem status field {body?["status"]}, expected {status}";
        if (ep.Map(m).Type is { } type && Un(r, body?["type"]?.GetValue<string>()) != type) return $"{at}: type {body?["type"]}, expected {type}";
        var detail = Un(r, body?["detail"]?.GetValue<string>());
        var wantDetail = m.Q == "HFault.Missing" ? "Missing." : null;
        if (detail != wantDetail) return $"{at}: detail {detail ?? "(none)"}, expected {wantDetail ?? "none (the member has no description)"}";
        return null;
    }

    static string? DocOf(Row r, JsonNode doc, Ep ep)
    {
        var op = doc["paths"]?[ep.Url]?["get"];
        if (op is null && ep.Mvc && !Hx.InDocument(r)) { r.Note(NoteKeys.DocControllerNotInApiExplorer); return null; }
        if (op is null) return $"document has no GET {ep.Url}";
        var responses = op["responses"]?.AsObject();
        if (responses is null) return $"{ep.Url}: operation has no responses";
        var nums = responses.Select(kv => kv.Key).Where(k => int.TryParse(k, out _)).Select(int.Parse).ToList();
        if (!nums.SequenceEqual(nums.Order())) return $"{ep.Url}: responses {string.Join(",", responses.Select(kv => kv.Key))} are not in ascending order";
        var got = nums.Where(n => n >= 400).ToList();
        var want = ep.DocStatuses();
        if (!got.SequenceEqual(want)) return $"{ep.Url}: document lists {string.Join(",", got)}, expected {string.Join(",", want)} ({string.Join(" ", ep.Decls)})";
        foreach (var status in ep.FaultStatuses())
        {
            var resp = responses[status.ToString()];
            var txt = Hx.Deep(doc, resp)?.ToJsonString() ?? "";
            if (!txt.Contains("\"fault\"")) return $"{ep.Url} {status}: response schema does not describe fault: {Cut(txt)}";
            if (!txt.Contains("\"rule\"")) return $"{ep.Url} {status}: response schema does not describe rule: {Cut(txt)}";
            var desc = resp?["description"]?.GetValue<string>() ?? "";
            const string Lead = "Problem types: ";
            var i = desc.IndexOf(Lead, StringComparison.Ordinal);
            var types = i < 0 ? [] : desc[(i + Lead.Length)..].Split(", ").ToHashSet();
            var wantTypes = ep.TypesAt(status);
            if (!types.SetEquals(wantTypes))
                return $"{ep.Url} {status}: description \"{desc}\" names problem types {{{string.Join(", ", types)}}}, expected {{{string.Join(", ", wantTypes)}}}";
        }
        return null;
    }
}
