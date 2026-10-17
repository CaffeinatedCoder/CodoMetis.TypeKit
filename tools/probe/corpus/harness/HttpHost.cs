// The shared HTTP host: applies a row's json.*, openapi.*, mvc.* and http.* axes and the shape axes, then runs the baseline checks
// (payload echo/in/binding parity, schema parity) that every http claim shares. Claim-specific scenarios plug in through HostCfg.
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;
namespace Probes.Shapes;

public sealed record Payload(Held Vo, Held Plain);

public sealed class NullBinderProvider : IModelBinderProvider { public IModelBinder? GetBinder(ModelBinderProviderContext context) => null; }
public sealed class PrefixConvention(string prefix) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var c in application.Controllers)
            foreach (var s in c.Selectors.Concat(c.Actions.SelectMany(a => a.Selectors)))
                if (s.AttributeRouteModel is { } m && !(m.Template?.StartsWith('/') ?? false))
                    s.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(new AttributeRouteModel(new RouteAttribute(prefix)), m);
    }
}

/// <summary>
/// http.host=controller-without-ApiController-visible: what ASP.NET documents as the switch beside [ApiController] and
/// [ApiExplorerSettings], a convention that sets ApiExplorer.IsVisible, so the document checks reach those controllers.
/// </summary>
public sealed class VisibleToApiExplorer : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var c in application.Controllers) c.ApiExplorer.IsVisible = true;
    }
}

/// <summary>Closed generic controllers: one over the value object, one over the wrapped type; routes are set by BindConvention.</summary>
[ApiController]
public sealed class MvcBind<T> : ControllerBase
{
    public sealed class Q { public T V { get; set; } = default!; }
    [ProducesResponseType(200), Route("_")]
    public IActionResult RouteA([FromRoute(Name = "v")] T v) => new ObjectResult(v) { DeclaredType = typeof(T) };
    [ProducesResponseType(200), Route("_")]
    public IActionResult QueryA([FromQuery(Name = "v")] T v) => new ObjectResult(v) { DeclaredType = typeof(T) };
    [ProducesResponseType(200), Route("_")]
    public IActionResult HeaderA([FromHeader(Name = "v")] T v) => new ObjectResult(v) { DeclaredType = typeof(T) };
    [ProducesResponseType(200), Route("_")]
    public IActionResult BodyA([FromBody] T v) => new ObjectResult(v) { DeclaredType = typeof(T) };
    [ProducesResponseType(200), Route("_")]
    public IActionResult FormA([FromForm(Name = "v")] T v) => new ObjectResult(v) { DeclaredType = typeof(T) };
    [ProducesResponseType(200), Route("_")]
    public IActionResult ParamsA([FromQuery] Q q) => new ObjectResult(q.V) { DeclaredType = typeof(T) };
}
public sealed class BindConvention(Type vo, Type plain, string prefix) : IControllerModelConvention
{
    public void Apply(ControllerModel c)
    {
        if (!c.ControllerType.IsGenericType || c.ControllerType.GetGenericTypeDefinition() != typeof(MvcBind<>)) return;
        var arg = c.ControllerType.GenericTypeArguments[0];
        // By the entry's own types: the plain side is the twin, at the value object's nullability.
        var which = arg == vo ? "vo" : arg == plain ? "plain" : throw new InvalidOperationException($"MvcBind<{arg.Name}> is neither side's");
        foreach (var a in c.Actions)
        {
            var (kind, post) = a.ActionName switch { "RouteA" => ("route", false), "QueryA" => ("query", false), "HeaderA" => ("header", false), "BodyA" => ("body", true), "FormA" => ("form", true), _ => ("params", false) };
            a.Selectors.Clear();
            a.Selectors.Add(new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel(new RouteAttribute($"{prefix}mvc/bind/{kind}/{which}" + (kind == "route" ? "/{v}" : ""))),
                ActionConstraints = { new Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint([post ? "POST" : "GET"]) },
                EndpointMetadata = { new Microsoft.AspNetCore.Routing.HttpMethodMetadata([post ? "POST" : "GET"]) },
            });
        }
    }
}
[ApiController]
public sealed class MvcPayloadController(Payload payload) : ControllerBase
{
    [HttpGet("mvc/echo/{which}")]
    public IActionResult Echo(string which) { var h = which == "vo" ? payload.Vo : payload.Plain; return new ObjectResult(h.Value) { DeclaredType = h.Declared }; }

    [HttpPost("mvc/in/{which}")]
    public async Task<IActionResult> In(string which)
    {
        var h = which == "vo" ? payload.Vo : payload.Plain;
        var f = HttpContext.RequestServices.GetRequiredService<IOptions<MvcOptions>>().Value.InputFormatters.OfType<SystemTextJsonInputFormatter>().First();
        var meta = HttpContext.RequestServices.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(h.Declared);
        var factory = HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IHttpRequestStreamReaderFactory>();
        var ctx = new InputFormatterContext(HttpContext, "model", ModelState, meta, factory.CreateReader);
        var result = await f.ReadAsync(ctx);
        if (result.HasError) return StatusCode(400);
        return new ObjectResult(result.Model) { DeclaredType = h.Declared };
    }
}

public sealed class HostCfg
{
    public Action<IEndpointRouteBuilder>? Minimal;
    public Func<IEndpointConventionBuilder, IEndpointConventionBuilder>? ControllerBuilder;
    public Action<IServiceCollection>? Services;
    public string Mapping = "by-kind";
    public bool Qualified;
    public ListedLogs? Logs;
    public Type[]? Controllers;          // null: the fault controllers of the host
    public bool FaultControllers = true;
    public bool Payload = true;          // payload echo / in / binding / schema endpoints
    public string PayloadOnly = "both";  // "both", "vo" or "plain": the endpoints of one side only, minimal and MVC (side (1)'s twin alone, a document refusal isolated)
    public bool MapBlanketMapping = true;
    public bool TypeKitOpenApi = true;   // false: the document as ASP.NET alone writes it, the baseline of a refused one
    /// <summary>
    /// The three-sided differential's side (1) (Sides): no AddTypeKit(), none of TypeKit's JSON converters on either set of options, and no
    /// fault responses; the wrapped twin's endpoints answer as ASP.NET and System.Text.Json have them without TypeKit.
    /// </summary>
    public bool WithoutTypeKit;
    public HostCfg Copy() => (HostCfg)MemberwiseClone();
}

public static class Hx
{
    public static bool HasMinimal(Row r) => r["http.host"] is "minimal" or "both" or "route-group";
    public static bool HasMvc(Row r) => r["http.host"] is "mvc" or "both" or "controller-without-ApiController" or "controller-without-ApiController-visible";
    /// <summary>The fault controllers have no [ApiController]: MVC infers no [FromBody] and answers no automatic 400.</summary>
    public static bool Plain(Row r) => r["http.host"] is "controller-without-ApiController" or "controller-without-ApiController-visible";
    /// <summary>A controller without [ApiController] is in the document only when something makes it visible to the API explorer.</summary>
    public static bool InDocument(Row r) => r["http.host"] != "controller-without-ApiController";
    /// <summary>Path of a minimal endpoint (route-group hosts map everything under /rg).</summary>
    public static string M(Row r, string path) => (r["http.host"] == "route-group" ? "/rg" : "") + path;
    /// <summary>Path of a controller endpoint (own-route-prefix convention puts /px before relative routes).</summary>
    public static string C(Row r, string path) => (r.Is("mvc.Conventions", "own-route-prefix") ? "/px" : "") + path;

    public static Type[] FaultControllers(Row r) =>
        Plain(r) ? [typeof(PlainAController), typeof(PlainBController)] : [typeof(MvcAController), typeof(MvcBController)];

    static void ApplyMvc(MvcOptions o, Row r)
    {
        foreach (var kv in r.All.Where(kv => kv.Key.StartsWith("mvc.") && kv.Value != "default"))
        {
            var n = kv.Key[4..];
            switch (n)
            {
                case "InputFormatters": o.InputFormatters.Add(new XmlSerializerInputFormatter(o)); continue;
                case "OutputFormatters": o.OutputFormatters.Add(new XmlSerializerOutputFormatter()); continue;
                case "ModelBinderProviders": o.ModelBinderProviders.Insert(0, new NullBinderProvider()); continue;
                case "Conventions": o.Conventions.Add(new PrefixConvention("px")); continue;
            }
            var p = typeof(MvcOptions).GetProperty(n) ?? throw new ArgumentException("no mvc option " + n);
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object v = t == typeof(bool) ? bool.Parse(kv.Value) : t == typeof(int) ? int.Parse(kv.Value) : t.IsEnum ? Enum.Parse(t, kv.Value) : throw new ArgumentException($"no hand value for mvc.{n}");
            p.SetValue(o, v);
        }
    }

    static void ApplyOpenApi(OpenApiOptions o, Row r)
    {
        var v = r.Get("openapi.OpenApiVersion");
        if (v is not (null or "default")) o.OpenApiVersion = Enum.Parse<OpenApiSpecVersion>(v);
        switch (r.Get("openapi.CreateSchemaReferenceId"))
        {
            case "own-prefixing": o.CreateSchemaReferenceId = ti => OpenApiOptions.CreateDefaultSchemaReferenceId(ti) is { } id ? "X" + id : null; break;
            case "null-inlines-all": o.CreateSchemaReferenceId = _ => null; break;
        }
        switch (r.Get("openapi.transformers"))
        {
            case "own-schema-transformer": o.AddSchemaTransformer((s, c, ct) => Task.CompletedTask); break;
            case "own-operation-transformer": o.AddOperationTransformer((op, c, ct) => Task.CompletedTask); break;
            case "own-document-transformer": o.AddDocumentTransformer((d, c, ct) => Task.CompletedTask); break;
        }
    }

    public static async Task<WebApplication> Start(Row r, Entry e, HostCfg c)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (c.Logs is not null) builder.Logging.AddProvider(c.Logs);
        var plain = e.Valid(); var vo = Make.Vo(e, plain);
        var (twin, held) = Sides.Twins(r["shape.position"], r.Get("shape.containing") ?? "class", e, plain, vo);
        var payload = new Payload(held, twin);
        builder.Services.AddSingleton(payload);
        builder.Services.ConfigureHttpJsonOptions(o => { JsonRow.Apply(r, e, o.SerializerOptions); if (c.WithoutTypeKit) Sides.DropTypeKit(o.SerializerOptions); });
        builder.Services.AddOpenApi(x => { ApplyOpenApi(x, r); if (c.TypeKitOpenApi && !c.WithoutTypeKit) x.AddTypeKit(); });
        if (!c.WithoutTypeKit) builder.Services.Configure<FaultResponseOptions>(x => x.QualifiedFaultNames = c.Qualified);
        c.Services?.Invoke(builder.Services);
        if (c.MapBlanketMapping && !c.WithoutTypeKit)
        {
            builder.Services.AddFaultResponses<SFault>(f => f switch { SFault.First => 409, SFault.Second => 410, _ => throw new ArgumentOutOfRangeException(nameof(f)) });
            if (c.Mapping == "by-kind") builder.Services.AddFaultResponses();
            else if (c.Mapping == "own-IFault-mapping") builder.Services.AddFaultResponses(f => FaultResponse.For(f.Kind).WithType(new Uri($"https://p.example/{f.Kind}")));
            else if (c.Mapping == "per-set") { builder.Services.AddFaultResponses<HFault>(); builder.Services.AddFaultResponses<GFault>(); }
        }
        var mvc = HasMvc(r);
        if (mvc)
        {
            var controllers = new List<Type>();
            if (c.FaultControllers) controllers.AddRange(c.Controllers ?? FaultControllers(r));
            if (c.Payload)
            {
                controllers.Add(typeof(MvcPayloadController));
                if (c.PayloadOnly != "plain") controllers.Add(typeof(MvcBind<>).MakeGenericType(e.Vo));
                if (c.PayloadOnly != "vo") controllers.Add(typeof(MvcBind<>).MakeGenericType(e.Plain));
            }
            var pfx = "";
            builder.Services.AddControllers(o =>
            {
                o.Conventions.Add(new BindConvention(e.Vo, e.Plain, pfx));   // before the prefix convention, so that it prefixes these routes too
                if (r["http.host"] == "controller-without-ApiController-visible") o.Conventions.Add(new VisibleToApiExplorer());
                ApplyMvc(o, r);
            })
            .AddJsonOptions(o => { JsonRow.Apply(r, e, o.JsonSerializerOptions); if (c.WithoutTypeKit) Sides.DropTypeKit(o.JsonSerializerOptions); })
            .ConfigureApplicationPartManager(p => { p.ApplicationParts.Clear(); p.FeatureProviders.Add(new OnlyControllers([.. controllers])); });
        }
        var app = builder.Build();
        app.MapOpenApi();
        if (HasMinimal(r))
        {
            IEndpointRouteBuilder root = r["http.host"] == "route-group" ? app.MapGroup("/rg") : app;
            c.Minimal?.Invoke(root);
            if (c.Payload) MapPayload(root, payload, r, e, c.PayloadOnly);
        }
        if (mvc) { var cb = app.MapControllers(); c.ControllerBuilder?.Invoke(cb); }
        try { await app.StartAsync(); } catch { await app.DisposeAsync(); throw; }
        return app;
    }

    // ---- minimal payload endpoints
    static void MapPayload(IEndpointRouteBuilder b, Payload p, Row r, Entry e, string only)
    {
        foreach (var which in new[] { "vo", "plain" })
        {
            if (only != "both" && only != which) continue;
            var h = which == "vo" ? p.Vo : p.Plain;
            b.MapGet($"/echo/{which}", async (HttpContext ctx) => { await ctx.Response.WriteAsJsonAsync(h.Value, h.Declared, options: null, contentType: null, ctx.RequestAborted); });
            b.MapPost($"/in/{which}", async (HttpContext ctx) =>
            {
                object? v;
                try { v = await ctx.Request.ReadFromJsonAsync(h.Declared, ctx.RequestAborted); }
                catch (JsonException) { ctx.Response.StatusCode = 400; return; }
                catch (BadHttpRequestException) { ctx.Response.StatusCode = 400; return; }
                await ctx.Response.WriteAsJsonAsync(v, h.Declared, options: null, contentType: null, ctx.RequestAborted);
            });
            typeof(Hx).GetMethod(nameof(MapBind), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(which == "vo" ? e.Vo : e.Plain)
                .Invoke(null, [b, which, e.Wrapped is "own-record-class" or "NodaTime.Instant"]);
            typeof(Hx).GetMethod(nameof(MapSchema), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(h.Declared).Invoke(null, [b, which]);
        }
    }

    sealed record AsP<T>([FromQuery(Name = "v")] T V);
    static void MapBind<T>(IEndpointRouteBuilder b, string which, bool bodyOnly)
    {
        static IResult Out(HttpContext ctx, T v) => Results.Json(v, ctx.RequestServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions, null, null);
        b.MapPost($"/bind/body/{which}", (HttpContext ctx, [FromBody] T v) => Out(ctx, v));
        if (bodyOnly) return;   // a record class cannot be bound from a route, query, header or form (the wrapped type neither)
        b.MapGet($"/bind/route/{which}/{{v}}", (HttpContext ctx, T v) => Out(ctx, v));
        b.MapGet($"/bind/query/{which}", (HttpContext ctx, [FromQuery(Name = "v")] T v) => Out(ctx, v));
        b.MapGet($"/bind/header/{which}", (HttpContext ctx, [FromHeader(Name = "v")] T v) => Out(ctx, v));
        b.MapPost($"/bind/form/{which}", (HttpContext ctx, [FromForm(Name = "v")] T v) => Out(ctx, v)).DisableAntiforgery();
        b.MapGet($"/bind/params/{which}", (HttpContext ctx, [AsParameters] AsP<T> p) => Out(ctx, p.V));
    }
    static void MapSchema<T>(IEndpointRouteBuilder b, string which) =>
        b.MapPost($"/schema/{which}", ([FromBody] T body) => Results.Ok(body));

    // ---- client side
    /// <summary>An answer on the wire: its status and body, or the exception the server threw (<see cref="Ex"/>, a 500), named in <see cref="Thrown"/>.</summary>
    public sealed record Wire(HttpStatusCode Status, string Body, string? Thrown = null, Exception? Ex = null)
    {
        /// <summary>The answer as the three-sided differential compares it (Sides): the status, the body, and the exception's type and stage.</summary>
        public Sides.Answer Side(string operation) => new(operation, Ex is null ? Body : null, Ex, (int)Status);
    }
    public static async Task<Wire> Send(WebApplication app, HttpMethod m, string path, string? body = null, string? contentType = null, (string, string)? header = null)
    {
        var req = new HttpRequestMessage(m, path);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/json");
        if (header is { } h) req.Headers.TryAddWithoutValidation(h.Item1, h.Item2);
        try
        {
            var resp = await app.GetTestClient().SendAsync(req);
            return new(resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { return new(HttpStatusCode.InternalServerError, "", ex.GetType().Name + ": " + ex.Message, ex); }
    }
    public static Task<Wire> Get(WebApplication app, string path) => Send(app, HttpMethod.Get, path);
    public static async Task<JsonNode?> Doc(WebApplication app) => JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json"));

    /// <summary>
    /// The document failed with AddTypeKit(). The same host without it is side (1) of the three (Sides): where ASP.NET alone refuses the
    /// document alike, by exception type and stage and never by message (<see cref="DocAlike"/>), the document part has nothing to check
    /// (<see cref="DocRefused"/>); where it writes the document, or refuses it otherwise, the failure is returned (F2 hid behind a baseline
    /// that kept AddTypeKit() on both sides). <paramref name="comparedBefore"/> says whether the check compared an answer before the document.
    /// </summary>
    public static async Task<string?> DocRefusedByAspNetAlone(Row r, Entry e, HostCfg c, Exception withTypeKit, bool comparedBefore = false)
    {
        static string Say(Exception x) => x.GetType().Name + ": " + (x.Message.Length > 200 ? x.Message[..200] + "..." : x.Message);
        var alone = c.Copy(); alone.TypeKitOpenApi = false;
        try
        {
            await using var app = await Start(r, e, alone);
            await Doc(app);
        }
        catch (Exception ex) when (DocAlike(ex, withTypeKit))
        {
            DocRefused(r, ex, comparedBefore);
            return null;
        }
        catch (Exception ex) { return $"document: AddTypeKit() fails ({Say(withTypeKit)}), ASP.NET alone fails otherwise ({Say(ex)})"; }
        return $"document: AddTypeKit() fails ({Say(withTypeKit)}), ASP.NET alone writes it";
    }

    /// <summary>
    /// ASP.NET alone (side (1)) and AddTypeKit() (side (3)) refuse the document alike: the same exception type at the same stage (a
    /// JsonException's Path), as <see cref="Sides.Alike"/> compares two refusals. No message is compared: the two documents differ in length
    /// by what AddTypeKit() wrote, so one refusal is at another line and byte of each (rows 31 and 140 of C21), and a refusal's words decide
    /// no row (decision 99, WS2's three-sided differential).
    /// </summary>
    public static bool DocAlike(Exception one, Exception three) =>
        Sides.Alike(new Sides.Answer("document", null, one), new Sides.Answer("document", null, three));

    /// <summary>
    /// The document refused alike by ASP.NET alone: its part has nothing to check. A check that compared nothing before it is the framework's
    /// refusal, an expected refusal that earns nothing (framework-refuses); one that compared an answer on the wire first is judged by that
    /// answer (doc:refused-by-aspnet-alone, a pass: decision 99's "judged by its other checks").
    /// </summary>
    public static void DocRefused(Row r, Exception one, bool comparedBefore)
    {
        if (comparedBefore) r.Note(NoteKeys.DocRefusedByAspnetAlone, one.GetType().Name);
        else r.Note(NoteKeys.FrameworkRefuses, $"document refused by ASP.NET alone too ({one.GetType().Name}), side (1) measured on the same host without AddTypeKit()");
    }

    public static string BindText(Entry e, object plain) => e.Wrapped switch
    {
        "DateTime" => ((DateTime)plain).ToString("O"), "DateTimeOffset" => ((DateTimeOffset)plain).ToString("O"),
        "DateOnly" => ((DateOnly)plain).ToString("yyyy-MM-dd"), "TimeOnly" => ((TimeOnly)plain).ToString("HH:mm:ss"),
        "TimeSpan" => ((TimeSpan)plain).ToString("c"), "bool" => (bool)plain ? "true" : "false", "enum" or "flags-enum" => plain.ToString()!,
        _ => Convert.ToString(plain, System.Globalization.CultureInfo.InvariantCulture)!
    };

    // ---- schema comparison
    /// <summary>Drops nullability markers (a class value object over a value type is nullable by being a class; not the value object's doing).</summary>
    public static JsonNode? NoNull(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                static bool IsNull(JsonNode? a) => a is JsonObject ao && (ao["type"]?.ToJsonString() == "\"null\"" || ao["enum"]?.ToJsonString() == "[null]");
                if (o["oneOf"] is JsonArray alts && alts.Count == 2 && alts.Any(IsNull) && alts.FirstOrDefault(a => !IsNull(a)) is { } keep)
                    return NoNull(keep.DeepClone());
                var c = new JsonObject();
                foreach (var kv in o)
                {
                    if (kv.Key is "nullable" or "x-nullable" or "x-null" or "required") continue;
                    if (kv.Key == "type" && kv.Value is JsonArray ta) { var f = ta.Where(x => x?.GetValue<string>() != "null").ToList(); c["type"] = f.Count == 1 ? f[0]!.DeepClone() : new JsonArray(f.Select(x => x!.DeepClone()).ToArray()); continue; }
                    if (kv.Key == "allOf" && kv.Value is JsonArray aa && aa.Count == 1 && aa[0] is JsonObject a0 && a0.Count == 1 && a0.ContainsKey("x-nullable")) continue;
                    // OpenAPI 3.0 writes a type of several with null as anyOf [{type: integer}, {type: string}, {enum: [null]}] (row 260 of C21)
                    if (kv.Key == "anyOf" && kv.Value is JsonArray any && any.Any(IsNull)) { c["anyOf"] = new JsonArray(any.Where(x => !IsNull(x)).Select(NoNull).ToArray()); continue; }
                    if (kv.Key == "enum" && kv.Value is JsonArray en && en.Any(x => x is null)) { c["enum"] = new JsonArray(en.Where(x => x is not null).Select(x => x!.DeepClone()).ToArray()); continue; }
                    c[kv.Key] = NoNull(kv.Value);
                }
                return c;
            case JsonArray a: var ca = new JsonArray(); foreach (var x in a) ca.Add(NoNull(x)); return ca;
            default: return n?.DeepClone();
        }
    }

    /// <summary>The key <see cref="Deep"/> puts on a schema it resolved from a reference, when asked to mark them.</summary>
    public const string ResolvedReference = "x-probe-resolved-reference";

    public static JsonNode? Deep(JsonNode doc, JsonNode? n, HashSet<string>? seen = null, bool mark = false)
    {
        seen ??= [];
        switch (n)
        {
            case JsonObject o:
                if (o["$ref"] is { } rf)
                {
                    var key = rf.GetValue<string>();
                    if (!seen.Add(key)) return JsonValue.Create("<cycle>");
                    var target = doc; foreach (var seg in key.TrimStart('#', '/').Split('/')) target = target?[seg];
                    var res = Deep(doc, target, seen, mark); seen.Remove(key);
                    if (mark && res is JsonObject resolved) resolved[ResolvedReference] = true;
                    return res;
                }
                var c = new JsonObject();
                foreach (var kv in o.OrderBy(k => k.Key, StringComparer.Ordinal))
                    if (kv.Key is not ("title" or "description" or "example" or "examples"))
                        c[kv.Key] = kv.Key == "discriminator" ? Discriminator(o, kv.Value) : Deep(doc, kv.Value, seen, mark);
                return c;
            case JsonArray a: var ca = new JsonArray(); foreach (var x in a) ca.Add(Deep(doc, x, seen, mark)); return ca;
            default: return n?.DeepClone();
        }
    }

    /// <summary>
    /// A discriminator's mapping names a component per value, and a component's name is made of the type's, which
    /// differs between the two sides by construction (row 347 of C07: <c>PB&lt;TimeSpan&gt;</c> against
    /// <c>PB&lt;V&gt;</c>). Each value names instead the branch of the same schema's anyOf or oneOf it maps to, whose
    /// content <see cref="Deep"/> compares; a target that is no branch stays as it is, and so still differs.
    /// </summary>
    static JsonNode? Discriminator(JsonObject schema, JsonNode? discriminator)
    {
        if (discriminator is not JsonObject d || d["mapping"] is not JsonObject mapping) return discriminator?.DeepClone();
        var branches = (schema["anyOf"] ?? schema["oneOf"]) as JsonArray;
        var c = (JsonObject)d.DeepClone();
        var named = new JsonObject();
        foreach (var (value, target) in mapping.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var index = -1;
            for (var i = 0; target is JsonValue && i < (branches?.Count ?? 0); i++)
                if (branches![i]?["$ref"]?.GetValue<string>() == target.GetValue<string>()) { index = i; break; }
            named[value] = index >= 0 ? $"branch {index}" : target?.DeepClone();
        }
        c["mapping"] = named;
        return c;
    }
}
