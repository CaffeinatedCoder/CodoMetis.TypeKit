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
// Each comparison is about one Aspect, and a claim's Spec.Of names the obligations it is checked for: the row runs one check (Row.Check) per
// obligation it reaches, against the README's rules (Spec.Against) or, for a refusal, the documented one (Spec.Refusals), every check on the
// one host and the answers fetched once.
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

/// <summary>What one comparison of <see cref="FaultClaim.Run"/> is about; a claim's <see cref="Spec.Of"/> names the obligations each is checked for.</summary>
public static class Aspect
{
    /// <summary>The host starts, or refuses to as the README says.</summary>
    public const string Start = "start";
    /// <summary>Reporting a member left out throws, naming the endpoint and the member.</summary>
    public const string LeftOut = "left-out";
    /// <summary>A member's status, on the wire and in the problem's status field.</summary>
    public const string Status = "status";
    /// <summary>The problem's fault, its type and its detail.</summary>
    public const string Fault = "fault", Type = "type", Detail = "detail";
    /// <summary>The operation's statuses (which), their order, the problem schema's fault and rule, and the problem types its description names.</summary>
    public const string DocListed = "doc-listed", DocOrder = "doc-order", DocDescribes = "doc-describes", DocTypes = "doc-types";
    public static readonly string[] Answer = [Status, Fault, Type, Detail];
    public static readonly string[] Doc = [DocListed, DocOrder, DocDescribes, DocTypes];
}

/// <summary>What one claim's row asks: see the header.</summary>
public sealed class Spec
{
    /// <summary>The obligations a comparison of an aspect is checked for, given the member it is about (none for the start and the document).</summary>
    public Func<string, Mem?, string[]> Of = (_, _) => ["claim"];
    /// <summary>What the README's rules are compared as: the sentence of the claim's source.</summary>
    public Baseline Against = Baseline.Model;
    /// <summary>The obligations whose check holds a refusal by its type and what it names.</summary>
    public string[] Refusals = [];
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

    public static async Task<string?> Run(Row r, Spec s)
    {
        var en = Registry.Of(r); Process(r, en);
        var resp = Resp.Of(r);
        var (eps, cfg) = Plan(r, s, resp);
        if (eps.Count == 0) return "harness: the row maps no endpoint of the claim (an excluded pair?)";
        WebApplication? app = null; Exception? down = null;
        try { app = await Hx.Start(r, en, cfg); }
        catch (Exception ex) when (ex is not Sides.InputsNotEquivalent) { down = ex; }
        await using var _ = app;
        Baseline Against(string o) => s.Refusals.Contains(o) ? Baseline.Refusal : s.Against;
        string? failed = null;

        // A host the README says must not start: that is the row's one comparison.
        if (s.Refused is not null || s.RefusedAs is not null)
        {
            foreach (var o in s.Of(Aspect.Start, null))
            {
                var f = r.Check(o, Against(o), _ => down is null
                    ? (s.RefusedAs is not null ? "host started, though the README says this set stops the start"
                                              : $"host started, though a status every endpoint of its scope leaves out must stop it ({string.Join(", ", s.Refused!)})")
                    : StartRefused(s, down));
                failed ??= f;
            }
            return failed;
        }

        var members = s.Errors.Select(e => FxErr.H[e - 1]).ToList();
        var obligations = Aspect.Answer.Append(Aspect.LeftOut).SelectMany(a => members.SelectMany(m => s.Of(a, m)))
            .Concat(Aspect.Doc.SelectMany(a => s.Of(a, null))).Distinct().ToList();
        if (down is not null)
        {
            foreach (var o in obligations) { var f = r.Check(o, Against(o), _ => "host did not start: " + down.GetType().Name + ": " + Cut(down.Message)); failed ??= f; }
            return failed;
        }

        var display = app!.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .GroupBy(e => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DisplayName ?? "", StringComparer.OrdinalIgnoreCase);
        JsonNode? doc = null; Exception? docFailed = null;
        try { doc = await Hx.Doc(app); }
        catch (Exception ex) { docFailed = ex; }
        var wires = new Dictionary<(string Url, int E), Hx.Wire>();
        foreach (var ep in eps)
            foreach (var e in s.Errors)
                wires[(ep.Url, e)] = await Hx.Get(app, $"{ep.Url}?e={e}");

        foreach (var o in obligations)
        {
            var f = await r.Check(o, Against(o), async c =>
            {
                bool want(string aspect, Mem? m) => s.Of(aspect, m).Contains(o);
                foreach (var ep in eps)
                    foreach (var e in s.Errors)
                        if (Wire(r, ep, FxErr.H[e - 1], wires[(ep.Url, e)], display, want) is { } fail) return fail;
                if (!Aspect.Doc.Any(a => want(a, null))) return null;
                // A document ASP.NET alone refuses too has nothing to check (noted there); any other failure is returned.
                if (docFailed is not null) return await DocRefusedByAspNetAlone(c, r, en, cfg, docFailed);
                foreach (var ep in eps)
                    if (DocOf(r, doc!, ep, want) is { } fail) return fail;
                return null;
            });
            failed ??= f;
        }
        return failed;
    }

    /// <summary>The host refused to start: as the README says, with an InvalidOperationException naming each of <see cref="Spec.Refused"/>, or as <see cref="Spec.RefusedAs"/> judges.</summary>
    static string? StartRefused(Spec s, Exception ex)
    {
        if (s.RefusedAs is { } judge) return judge(ex);
        var names = s.Refused!;
        var ioe = ex as InvalidOperationException ?? ex.InnerException as InvalidOperationException;
        if (ioe is null) return $"host refused to start, but with {ex.GetType().Name}, not InvalidOperationException: {Cut(ex.Message)}";
        foreach (var name in names)
            if (!ioe.Message.Contains(name, StringComparison.Ordinal)) return $"host refused to start, but not naming {name}: {Cut(ioe.Message)}";
        return null;
    }

    /// <summary>
    /// Hx.DocRefusedByAspNetAlone, with a baseline that has a problem response: the same host without AddTypeKit(), plus one endpoint (and one
    /// action, where the host has MVC) declaring ProducesProblem(404). AddTypeKit() adds its problem responses "as ProducesProblem would"
    /// (README "A Result as a response"), so ASP.NET alone writing a document of the same host without any problem response says nothing about
    /// them: under json.MaxDepth=3 System.Text.Json refuses the problem schema of ASP.NET's own ProducesProblem too (decision 99).
    /// </summary>
    static async Task<string?> DocRefusedByAspNetAlone(Check c, Row r, Entry en, HostCfg cfg, Exception withTypeKit)
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
        // the same refusal, at another byte of each document (Hx.Unplaced: the two differ in length by what AddTypeKit() wrote; rows 73 and
        // 157 of C06's regenerated rows, a char under WriteAsString)
        catch (Exception ex) when (ex.GetType() == withTypeKit.GetType() && Hx.Unplaced(ex.Message) == Hx.Unplaced(withTypeKit.Message))
        {
            r.Note(NoteKeys.DocRefusedByAspnetAlone, ex.GetType().Name);
            return null;
        }
        catch (Exception ex) when (r.Is("json.MaxDepth", "3") && Depth(ex) && Depth(withTypeKit))
        {
            // Both refuse the problem schema under MaxDepth=3, by System.Text.Json's own depth checks, but on different paths (TypeKit's
            // transformer through the schema exporter, ProducesProblem through the document writer): a refusal of STJ's own, nothing to check
            // (decision 99: under MaxDepth=3 System.Text.Json refuses the OpenAPI document of a problem response for plain ASP.NET too).
            return c.Refused(Cite.Record("0099"), $"{Say(withTypeKit)} / {Say(ex)}");
        }
        catch (Exception ex) { return $"document: AddTypeKit() fails ({Say(withTypeKit)}), ASP.NET alone with a ProducesProblem fails otherwise ({Say(ex)})"; }
        return $"document: AddTypeKit() fails ({Say(withTypeKit)}), ASP.NET alone with a ProducesProblem writes it";
    }

    static bool Depth(Exception x) => x is InvalidOperationException or System.Text.Json.JsonException;

    /// <summary>One endpoint's answer <paramref name="w"/> to reporting <paramref name="m"/>, judged on the aspects <paramref name="want"/> names.</summary>
    static string? Wire(Row r, Ep ep, Mem m, Hx.Wire w, Dictionary<string, string> display, Func<string, Mem?, bool> want)
    {
        var at = $"{ep.Url} reporting {m.Q}";
        if (ep.LeftOut(m))
        {
            if (!want(Aspect.LeftOut, m)) return null;
            if (w.Thrown is null) return $"{at}: left out of its document, but answered {(int)w.Status}: {Cut(w.Body)}";
            if (!w.Thrown.StartsWith(nameof(InvalidOperationException) + ":", StringComparison.Ordinal)) return $"{at}: left out, threw {Cut(w.Thrown)}, expected an InvalidOperationException";
            if (!display.TryGetValue(ep.Url, out var name) || name.Length == 0) return $"harness: no endpoint named at {ep.Url}";
            if (!w.Thrown.Contains(name, StringComparison.Ordinal) || !w.Thrown.Contains(m.Q, StringComparison.Ordinal))
                return $"{at}: the refusal does not name the endpoint ({name}) and the member ({m.Q}): {Cut(w.Thrown)}";
            return null;
        }
        if (!Aspect.Answer.Any(a => want(a, m))) return null;
        if (w.Thrown is not null) return $"{at}: request threw {Cut(w.Thrown)}";
        var status = ep.StatusOf(m);
        if (want(Aspect.Status, m) && (int)w.Status != status) return $"{at}: status {(int)w.Status}, expected {status}: {Cut(w.Body)}";
        JsonNode? body; try { body = JsonNode.Parse(w.Body); } catch { return $"{at}: body is not JSON: {Cut(w.Body)}"; }
        var fault = Un(r, body?["fault"]?.GetValue<string>());
        if (want(Aspect.Fault, m) && fault != m.Name) return $"{at}: fault {fault ?? "(none)"}, expected {m.Name}";
        if (want(Aspect.Status, m) && int.Parse(body?["status"]?.ToString() is { } st ? Un(r, st)!.Trim('"') : "0") != status) return $"{at}: problem status field {body?["status"]}, expected {status}";
        if (want(Aspect.Type, m) && ep.Map(m).Type is { } type && Un(r, body?["type"]?.GetValue<string>()) != type) return $"{at}: type {body?["type"]}, expected {type}";
        var detail = Un(r, body?["detail"]?.GetValue<string>());
        var wantDetail = m.Q == "HFault.Missing" ? "Missing." : null;
        if (want(Aspect.Detail, m) && detail != wantDetail) return $"{at}: detail {detail ?? "(none)"}, expected {wantDetail ?? "none (the member has no description)"}";
        return null;
    }

    /// <summary>One endpoint's operation in the document, judged on the aspects <paramref name="want"/> names.</summary>
    static string? DocOf(Row r, JsonNode doc, Ep ep, Func<string, Mem?, bool> want)
    {
        var op = doc["paths"]?[ep.Url]?["get"];
        if (op is null && ep.Mvc && !Hx.InDocument(r)) { r.Note(NoteKeys.DocControllerNotInApiExplorer); return null; }
        if (op is null) return $"document has no GET {ep.Url}";
        var responses = op["responses"]?.AsObject();
        if (responses is null) return $"{ep.Url}: operation has no responses";
        var nums = responses.Select(kv => kv.Key).Where(k => int.TryParse(k, out _)).Select(int.Parse).ToList();
        if (want(Aspect.DocOrder, null) && !nums.SequenceEqual(nums.Order())) return $"{ep.Url}: responses {string.Join(",", responses.Select(kv => kv.Key))} are not in ascending order";
        var got = nums.Where(n => n >= 400).ToList();
        var listed = ep.DocStatuses();
        // which statuses are listed, as a set: their order is DocOrder's, above
        if (want(Aspect.DocListed, null) && !got.Order().SequenceEqual(listed)) return $"{ep.Url}: document lists {string.Join(",", got)}, expected {string.Join(",", listed)} ({string.Join(" ", ep.Decls)})";
        foreach (var status in ep.FaultStatuses())
        {
            var resp = responses[status.ToString()];
            var txt = Hx.Deep(doc, resp)?.ToJsonString() ?? "";
            if (want(Aspect.DocDescribes, null) && !txt.Contains("\"fault\"")) return $"{ep.Url} {status}: response schema does not describe fault: {Cut(txt)}";
            if (want(Aspect.DocDescribes, null) && !txt.Contains("\"rule\"")) return $"{ep.Url} {status}: response schema does not describe rule: {Cut(txt)}";
            var desc = resp?["description"]?.GetValue<string>() ?? "";
            const string Lead = "Problem types: ";
            var i = desc.IndexOf(Lead, StringComparison.Ordinal);
            var types = i < 0 ? [] : desc[(i + Lead.Length)..].Split(", ").ToHashSet();
            var wantTypes = ep.TypesAt(status);
            if (want(Aspect.DocTypes, null) && !types.SetEquals(wantTypes))
                return $"{ep.Url} {status}: description \"{desc}\" names problem types {{{string.Join(", ", types)}}}, expected {{{string.Join(", ", wantTypes)}}}";
        }
        return null;
    }
}
