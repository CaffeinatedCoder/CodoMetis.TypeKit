// Shared code of the claims C08-same-name-warning, C12-described-converter, C13-patch-required and C16-options-agreement
// (harness; the probes are in probes/C08..., C12..., C13..., C16...). Every check starts the shared host (Hx.Start, which applies the
// row's json.*, openapi.*, mvc.* and http.* axes) with only the endpoints of its claim: minimal ones under Hx.M, controllers of its own
// under Hx.C, the actions it does not need removed by a convention.
// - C08 (AspNetCore README#faults-of-several-types, "Keep names distinct"): two fault types an endpoint answers with that write one
//   name for two errors make the host log a warning at startup naming both; qualified names, or leaving one out, fix it.
// - C12 (docs/json.md#describing-what-a-converter-writes, AspNetCore README "A described type"): the document describes a type as its
//   described converter or the options' JsonSchemaDescriptions say, as a property, a nullable one, a list's items, a dictionary's
//   additionalProperties, inside a value object and inside a patch; a parameter keeps ASP.NET's schema.
// - C13 (docs/merge-patches.md#required-constructor-parameters): under RespectRequiredConstructorParameters a constructor-parameter
//   patch is required in the document and on the wire, an init one is neither, and without the option neither is.
// - C16 (AspNetCore README#what-the-document-says, "Register the same on both option sets"): a host with controllers whose two option
//   sets register different converters or descriptions has its document refused, naming the type and what the options say of it,
//   also for a type held only inside a patch; a minimal-API host builds it. The docs name converters and descriptions: a different
//   naming policy or number handling is not "described differently", so the document builds there (DescribedOptionsAgreement
//   compares JsonSchemaDescription.For, which neither setting changes).
#pragma warning disable xUnit1051
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Probes.Shapes;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;
namespace Probes.Schema.NsA
{
    /// <summary>One of two fault sets of one nesting chain (Twin) in two namespaces: qualified names meet too.</summary>
    public sealed partial class Twin : IFaultSet<Twin>
    {
        public static readonly Twin Same = new(FaultKind.Conflict, null);
        public static readonly Twin OnlyA = new(FaultKind.NotFound, null);
        private Twin(FaultKind kind, string? description) { Kind = kind; Description = description; }
        public FaultKind Kind { get; }
        public string? Description { get; }
    }
}
namespace Probes.Schema.NsB
{
    public sealed partial class Twin : IFaultSet<Twin>
    {
        public static readonly Twin Same = new(FaultKind.Conflict, null);
        public static readonly Twin OnlyB = new(FaultKind.Unavailable, null);
        private Twin(FaultKind kind, string? description) { Kind = kind; Description = description; }
        public FaultKind Kind { get; }
        public string? Description { get; }
    }
}
namespace Probes.Schema
{
    using TwinA = Probes.Schema.NsA.Twin;
    using TwinB = Probes.Schema.NsB.Twin;

    // ---------------------------------------------------------------- types the documents describe

    /// <summary>A type an application writes with a converter of its own (docs/json.md's Coordinates).</summary>
    public readonly record struct Coord(double Lat, double Lon);
    /// <summary>A second one, held only inside a patch (C16).</summary>
    public readonly record struct CoordP(double Lat, double Lon);
    public readonly partial record struct CoordVo : IValue<Coord>;
    public readonly partial record struct DecVo : IValue<decimal>;

    static class CoordText
    {
        public static (double, double) Read(ref Utf8JsonReader reader, string prefix) =>
            reader.GetString() is { } s && s.StartsWith(prefix, StringComparison.Ordinal) && s[prefix.Length..].Split(',') is [var a, var b]
                ? (double.Parse(a, CultureInfo.InvariantCulture), double.Parse(b, CultureInfo.InvariantCulture))
                : throw new JsonException();
        public static string Write(string prefix, double a, double b) => string.Create(CultureInfo.InvariantCulture, $"{prefix}{a},{b}");
    }

    /// <summary>source=IDescribedJsonConverter: a converter that says what it writes.</summary>
    public sealed class CoordConverter : JsonConverter<Coord>, IDescribedJsonConverter
    {
        public static JsonObject Schema() => new() { ["type"] = "string", ["pattern"] = @"^c:-?\d+(\.\d+)?,-?\d+(\.\d+)?$" };
        public override Coord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { var (a, b) = CoordText.Read(ref reader, "c:"); return new(a, b); }
        public override void Write(Utf8JsonWriter writer, Coord value, JsonSerializerOptions options) => writer.WriteStringValue(CoordText.Write("c:", value.Lat, value.Lon));
        public JsonSchemaDescription? DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.Of(Schema());
    }
    /// <summary>source=JsonSchemaDescriptions: a converter someone else wrote, which says nothing; the options describe it.</summary>
    public sealed class CoordPlainConverter : JsonConverter<Coord>
    {
        public static JsonObject Schema() => new() { ["type"] = "string", ["pattern"] = @"^d:-?\d+(\.\d+)?,-?\d+(\.\d+)?$" };
        public override Coord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { var (a, b) = CoordText.Read(ref reader, "d:"); return new(a, b); }
        public override void Write(Utf8JsonWriter writer, Coord value, JsonSerializerOptions options) => writer.WriteStringValue(CoordText.Write("d:", value.Lat, value.Lon));
    }
    public sealed class CoordPConverter : JsonConverter<CoordP>, IDescribedJsonConverter
    {
        public static JsonObject Schema() => new() { ["type"] = "string", ["pattern"] = @"^p:-?\d+(\.\d+)?,-?\d+(\.\d+)?$" };
        public override CoordP Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { var (a, b) = CoordText.Read(ref reader, "p:"); return new(a, b); }
        public override void Write(Utf8JsonWriter writer, CoordP value, JsonSerializerOptions options) => writer.WriteStringValue(CoordText.Write("p:", value.Lat, value.Lon));
        public JsonSchemaDescription? DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.Of(Schema());
    }

    /// <summary>C12: every position the docs name ("a property, a body, a nullable one, a list's element, a dictionary's value, and inside a value object or a patch").</summary>
    public sealed record CoordDto
    {
        public Coord At { get; init; }
        public Coord? Maybe { get; init; }
        /// <summary>A plain nullable struct beside Maybe: what ASP.NET writes for a nullable reference in the same document.</summary>
        public Box? Twin { get; init; }
        public List<Coord> Route { get; init; } = [];
        public Dictionary<string, Coord> ByName { get; init; } = [];
        public required CoordVo Vo { get; init; }
        public Patch<Coord> Patched { get; init; }
    }
    public sealed record DecDto
    {
        public decimal At { get; init; }
        public decimal? Maybe { get; init; }
        public List<decimal> Route { get; init; } = [];
        public Dictionary<string, decimal> ByName { get; init; } = [];
        public required DecVo Vo { get; init; }
        public Patch<decimal> Patched { get; init; }
    }
    /// <summary>A plain struct without a converter of its own: the baselines' twin.</summary>
    public readonly record struct Box(int V);
    public sealed record BoxDto { public Box Inner { get; init; } }
    /// <summary>C13: the docs' two declarations of a patch field.</summary>
    public sealed record InitPatchDto { public Patch<int> Floor { get; init; } }
    public sealed record CtorPatchDto(Patch<int> Floor);
    /// <summary>C16: a described type as a property, and one held only inside a patch.</summary>
    public sealed record AgreeDto { public Coord At { get; init; } }
    public sealed record AgreePatchDto { public Patch<CoordP> P { get; init; } }

    /// <summary>
    /// The application lists its own types for json.TypeInfoResolver=source-generated-context: appended to the resolver chain, it
    /// answers only where the row's resolver does not (a reflection resolver always does).
    /// </summary>
    [JsonSerializable(typeof(CoordDto)), JsonSerializable(typeof(DecDto)), JsonSerializable(typeof(InitPatchDto)), JsonSerializable(typeof(CtorPatchDto))]
    [JsonSerializable(typeof(AgreeDto)), JsonSerializable(typeof(AgreePatchDto)), JsonSerializable(typeof(Coord)), JsonSerializable(typeof(CoordP))]
    [JsonSerializable(typeof(Coord?)), JsonSerializable(typeof(List<Coord>)), JsonSerializable(typeof(Dictionary<string, Coord>)), JsonSerializable(typeof(CoordVo))]
    [JsonSerializable(typeof(Patch<Coord>)), JsonSerializable(typeof(Patch<CoordP>)), JsonSerializable(typeof(Patch<int>)), JsonSerializable(typeof(Patch<decimal>))]
    [JsonSerializable(typeof(decimal)), JsonSerializable(typeof(decimal?)), JsonSerializable(typeof(List<decimal>)), JsonSerializable(typeof(Dictionary<string, decimal>))]
    [JsonSerializable(typeof(BoxDto)), JsonSerializable(typeof(Box?))]
    [JsonSerializable(typeof(ProblemDetails))]   // what a fault is answered as (C08), as an application on a source-generated context lists it
    [JsonSerializable(typeof(DecVo)), JsonSerializable(typeof(int)), JsonSerializable(typeof(string)), JsonSerializable(typeof(double))]
    public partial class SchemaJson : JsonSerializerContext { }

    // ---------------------------------------------------------------- controllers

    /// <summary>Keeps only the named actions of the probe's own controllers: one controller type serves every row.</summary>
    public sealed class KeepOnly(Type controllerBase, params string[] actions) : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            foreach (var c in application.Controllers.Where(c => controllerBase.IsAssignableFrom(c.ControllerType)))
                foreach (var a in c.Actions.ToList())
                    if (!actions.Contains(a.ActionName)) c.Actions.Remove(a);
        }
    }

    public abstract class SchemaCtlBase : ControllerBase
    {
        [HttpPost("s/coord/c")] public CoordDto Coord([FromBody] CoordDto b) => b;
        [HttpPost("s/dec/c")] public DecDto Dec([FromBody] DecDto b, [FromQuery] decimal q) => b;
        [HttpPost("s/init/c")] public IActionResult Init([FromBody] InitPatchDto b) => ModelState.IsValid && b is not null ? Content(b.Floor.State.ToString()) : BadRequest();
        [HttpPost("s/ctor/c")] public IActionResult Ctor([FromBody] CtorPatchDto b) => ModelState.IsValid && b is not null ? Content(b.Floor.State.ToString()) : BadRequest();
        [HttpPost("s/box/c")] public IActionResult Boxed([FromBody] BoxDto b) => ModelState.IsValid && b is not null ? Content("Box") : BadRequest();
        [HttpPost("s/agree/c")] public AgreeDto Agree([FromBody] AgreeDto b) => b;
        [HttpPost("s/agreep/c")] public AgreePatchDto AgreeP([FromBody] AgreePatchDto b) => b;
    }
    [ApiController] public sealed class SchemaApiCtl : SchemaCtlBase { }
    public sealed class SchemaPlainCtl : SchemaCtlBase { }

    /// <summary>C08: errors that two fault types write alike. e: 1 the first type's, 2 the second type's, 3 SFault.First (arity 3).</summary>
    public static class AlikeErr
    {
        public static Result<Fault<HFault, KFault>> K2(int e) => e switch
        { 1 => Fault<HFault, KFault>.Of(HFault.Clash), 2 => Fault<HFault, KFault>.Of(KFault.Clash), _ => Result<Fault<HFault, KFault>>.Success() };
        public static Result<Fault<HFault, KFault, SFault>> K3(int e) => e switch
        {
            1 => Fault<HFault, KFault, SFault>.Of(HFault.Clash), 2 => Fault<HFault, KFault, SFault>.Of(KFault.Clash), 3 => Fault<HFault, KFault, SFault>.Of(SFault.First),
            _ => Result<Fault<HFault, KFault, SFault>>.Success(),
        };
        public static Result<Fault<TwinA, TwinB>> T2(int e) => e switch
        { 1 => Fault<TwinA, TwinB>.Of(TwinA.Same), 2 => Fault<TwinA, TwinB>.Of(TwinB.Same), _ => Result<Fault<TwinA, TwinB>>.Success() };
        public static Result<Fault<TwinA, TwinB, SFault>> T3(int e) => e switch
        {
            1 => Fault<TwinA, TwinB, SFault>.Of(TwinA.Same), 2 => Fault<TwinA, TwinB, SFault>.Of(TwinB.Same), 3 => Fault<TwinA, TwinB, SFault>.Of(SFault.First),
            _ => Result<Fault<TwinA, TwinB, SFault>>.Success(),
        };
    }
    public abstract class AlikeCtlBase : ControllerBase
    {
        [HttpGet("s/alike/c")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<KFault>> K2None(int e) => AlikeErr.K2(e).ToHttpResult();
        [HttpGet("s/alike/c"), WithoutFaults<KFault>(nameof(KFault.Clash))] public Results<NoContent, FaultProblem<HFault>, FaultProblem<KFault>> K2Wo(int e) => AlikeErr.K2(e).ToHttpResult();
        [HttpGet("s/alike/c")] public Results<NoContent, FaultProblem<HFault>, FaultProblem<KFault>, FaultProblem<SFault>> K3None(int e) => AlikeErr.K3(e).ToHttpResult();
        [HttpGet("s/alike/c"), WithoutFaults<KFault>(nameof(KFault.Clash))] public Results<NoContent, FaultProblem<HFault>, FaultProblem<KFault>, FaultProblem<SFault>> K3Wo(int e) => AlikeErr.K3(e).ToHttpResult();
        [HttpGet("s/alike/c")] public Results<NoContent, FaultProblem<TwinA>, FaultProblem<TwinB>> T2None(int e) => AlikeErr.T2(e).ToHttpResult();
        [HttpGet("s/alike/c"), WithoutFaults<TwinB>(nameof(TwinB.Same))] public Results<NoContent, FaultProblem<TwinA>, FaultProblem<TwinB>> T2Wo(int e) => AlikeErr.T2(e).ToHttpResult();
        [HttpGet("s/alike/c")] public Results<NoContent, FaultProblem<TwinA>, FaultProblem<TwinB>, FaultProblem<SFault>> T3None(int e) => AlikeErr.T3(e).ToHttpResult();
        [HttpGet("s/alike/c"), WithoutFaults<TwinB>(nameof(TwinB.Same))] public Results<NoContent, FaultProblem<TwinA>, FaultProblem<TwinB>, FaultProblem<SFault>> T3Wo(int e) => AlikeErr.T3(e).ToHttpResult();
    }
    [ApiController] public sealed class AlikeApiCtl : AlikeCtlBase { }
    public sealed class AlikePlainCtl : AlikeCtlBase { }

    /// <summary>Every log line with its category and level (ListedLogs keeps neither).</summary>
    public sealed class CategoryLogs : ILoggerProvider
    {
        public readonly List<(string Category, LogLevel Level, string Message)> Lines = [];
        public ILogger CreateLogger(string categoryName) => new L(this, categoryName);
        public void Dispose() { }
        sealed class L(CategoryLogs o, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { lock (o.Lines) o.Lines.Add((category, logLevel, formatter(state, exception))); }
        }
    }

    // ---------------------------------------------------------------- the checks

    public static class SchemaChecks
    {
        static string Cut(string? s) => s is null ? "(null)" : s.Length > 300 ? s[..300] + "..." : s;
        static void Process(Row r, Entry en) { if (r.Is("process.converter-lookup", "wrapped-type-converter-looked-up-first")) _ = System.ComponentModel.TypeDescriptor.GetConverter(Make.Held(en.Plain)); }
        /// <summary>The application's own string converter (json.Converters=own-for-wrapped over string) wraps every string it writes: take it off again.</summary>
        static string? Un(Row r, string? s) => r.Is("json.Converters", "own-for-wrapped") && s is not null && s.StartsWith("own:") ? s[4..].Trim('"') : s;

        /// <summary>The host's minimal-API JSON options, which the document is built from.</summary>
        static JsonSerializerOptions HttpOptions(WebApplication app) => app.Services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        /// <summary>The JSON name of a CLR property under the options (naming policy, renaming modifier).</summary>
        static string JsonName(JsonSerializerOptions o, Type t, string clr) =>
            o.GetTypeInfo(t).Properties.First(p => (p.AttributeProvider as MemberInfo)?.Name == clr).Name;
        static void ListOwnTypes(JsonSerializerOptions o) => o.TypeInfoResolverChain.Add(SchemaJson.Default);

        /// <summary>
        /// json.TypeInfoResolver=source-generated-context, strictly: JsonRow falls back to reflection where the context does not list
        /// the row's payload shape, but these hosts map no payload, so the context and this claim's own (SchemaJson) answer for every
        /// type they serialize, without reflection, and the setting runs as the row asks. Registered last, on both option sets.
        /// </summary>
        static void StrictSourceGen(Row r, IServiceCollection s)
        {
            if (!r.Is("json.TypeInfoResolver", "source-generated-context")) return;
            void Strict(JsonSerializerOptions o)
            {
                o.TypeInfoResolver = JsonTypeInfoResolver.Combine(ProbeContext.Default, SchemaJson.Default);
                if (r.Is("json.TypeInfoResolverChain", "context-then-default")) { o.TypeInfoResolverChain.Add(ProbeContext.Default); o.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver()); }
                // after JsonRow.Apply, which ran in Configure and may have recorded the reflection fallback for the unmapped payload
                lock (r.Ev) r.Ev.Applied.Remove("json.TypeInfoResolver");
            }
            s.PostConfigure<HttpJsonOptions>(o => Strict(o.SerializerOptions));
            s.PostConfigure<MvcJsonOptions>(o => Strict(o.JsonSerializerOptions));
            r.Note("ctx-strict", "the payload is not mapped; the context and SchemaJson list every type the host serializes");
        }

        static HostCfg Cfg(Row r, string[] actions, Action<IEndpointRouteBuilder> minimal, Action<IServiceCollection>? services = null) => new()
        {
            Payload = false, FaultControllers = true,
            Controllers = [Hx.Plain(r) ? typeof(SchemaPlainCtl) : typeof(SchemaApiCtl)],
            Minimal = minimal,
            Services = s =>
            {
                s.Configure<MvcOptions>(o => o.Conventions.Add(new KeepOnly(typeof(SchemaCtlBase), actions)));
                s.PostConfigure<HttpJsonOptions>(o => ListOwnTypes(o.SerializerOptions));
                s.PostConfigure<MvcJsonOptions>(o => ListOwnTypes(o.JsonSerializerOptions));
                services?.Invoke(s);
                StrictSourceGen(r, s);
            },
        };

        /// <summary>The paths of one endpoint family on the row's sides: the minimal one and the controller's.</summary>
        static List<string> Sides(Row r, string family)
        {
            var sides = new List<string>();
            if (Hx.HasMinimal(r)) sides.Add(Hx.M(r, $"/s/{family}/m"));
            if (Hx.HasMvc(r)) sides.Add(Hx.C(r, $"/s/{family}/c"));
            return sides;
        }

        /// <summary>The operation of a path, by the document's spelling of it.</summary>
        static JsonNode? Op(JsonNode doc, string path, string method) => doc["paths"]?[path]?[method];
        /// <summary>The request body's schema, references resolved: OpenAPI 3 (requestBody) and 2.0 (a body parameter).</summary>
        static JsonNode? BodySchema(JsonNode doc, JsonNode op)
        {
            var s = op["requestBody"]?["content"]?["application/json"]?["schema"]
                 ?? op["parameters"]?.AsArray().FirstOrDefault(p => p?["in"]?.GetValue<string>() == "body")?["schema"];
            return s is null ? null : Hx.Deep(doc, s);
        }
        /// <summary>A schema with its keys sorted and its nullability markers gone, to compare with a description.</summary>
        static JsonNode? Norm(JsonNode? n) => Hx.NoNull(Hx.Deep(new JsonObject(), n));
        static bool Admits(JsonNode? n)
        {
            var t = n?.ToJsonString() ?? "";
            return t.Contains("\"null\"") || t.Contains("\"nullable\":true") || t.Contains("\"x-nullable\":true");
        }

        /// <summary>Fetches the document; a failure ASP.NET alone shares is noted and leaves nothing to check (doc is null), any other is returned.</summary>
        static async Task<(JsonNode? Doc, string? Fail)> DocOrBaseline(Row r, Entry en, HostCfg cfg, WebApplication app)
        {
            try { return (await Hx.Doc(app), null); }
            catch (Exception ex) { return (null, await Hx.DocRefusedByAspNetAlone(r, en, cfg, ex)); }
        }

        // ============================================================ C08
        public static async Task<string?> C08(Row r)
        {
            var en = Registry.Of(r); Process(r, en);
            var qualified = r["naming"] == "qualified";
            var arity = int.Parse(r["arity"]);
            var leave = r["leave-out"] == "WithoutFaults-one";
            return await Alike(r, en, twin: false, arity, leave, qualified) ?? await Alike(r, en, twin: true, arity, leave, qualified);
        }

        /// <summary>
        /// One endpoint per side answering two fault types whose clashing members are HFault.Clash and KFault.Clash (bare names meet,
        /// qualified ones do not) or NsA.Twin.Same and NsB.Twin.Same (one nesting chain in two namespaces: both meet).
        /// </summary>
        static async Task<string?> Alike(Row r, Entry en, bool twin, int arity, bool leave, bool qualified)
        {
            var logs = new CategoryLogs();
            var action = (twin ? "T" : "K") + arity + (leave ? "Wo" : "None");
            var cfg = new HostCfg
            {
                Payload = false, FaultControllers = true, Qualified = qualified,
                Controllers = [Hx.Plain(r) ? typeof(AlikePlainCtl) : typeof(AlikeApiCtl)],
                Minimal = b =>
                {
                    RouteHandlerBuilder m = (twin, arity) switch
                    {
                        (false, 2) => b.MapGet("/s/alike/m", (int e) => AlikeErr.K2(e).ToHttpResult()),
                        (false, _) => b.MapGet("/s/alike/m", (int e) => AlikeErr.K3(e).ToHttpResult()),
                        (true, 2) => b.MapGet("/s/alike/m", (int e) => AlikeErr.T2(e).ToHttpResult()),
                        (true, _) => b.MapGet("/s/alike/m", (int e) => AlikeErr.T3(e).ToHttpResult()),
                    };
                    if (leave && twin) m.WithoutFaults(TwinB.Same);
                    else if (leave) m.WithoutFaults(KFault.Clash);
                },
                Services = s =>
                {
                    s.AddSingleton<ILoggerProvider>(logs);
                    s.Configure<MvcOptions>(o => o.Conventions.Add(new KeepOnly(typeof(AlikeCtlBase), action)));
                    StrictSourceGen(r, s);
                },
            };
            WebApplication app;
            try { app = await Hx.Start(r, en, cfg); }
            catch (Exception ex) { return $"{action}: host did not start: {ex.GetType().Name}: {Cut(ex.Message)}"; }
            await using var _ = app;

            // what each error is written as on the wire: the client's view of the two
            var (first, second) = twin ? ("Twin", "Twin") : ("HFault", "KFault");
            var member = twin ? "Same" : "Clash";
            string Written(string type) => qualified ? $"{type}.{member}" : member;
            var alike = Written(first) == Written(second);
            var warn = alike && !leave;
            var sides = Sides(r, "alike");
            foreach (var path in sides)
            {
                foreach (var e in arity == 3 ? new[] { 1, 2, 3 } : [1, 2])
                {
                    var w = await Hx.Get(app, $"{path}?e={e}");
                    if (e == 2 && leave)
                    {
                        // the member left out is not answered (README#leaving-out-what-a-route-cannot-answer): proof that the leave-out applied
                        if (w.Thrown is null || !w.Thrown.StartsWith(nameof(InvalidOperationException))) return $"{action} {path} e=2: the member left out was answered: {(int)w.Status} {Cut(w.Body)} {w.Thrown}";
                        continue;
                    }
                    if (w.Thrown is not null) return $"{action} {path} e={e}: request threw {w.Thrown}";
                    var want = e == 3 ? (qualified ? "SFault.First" : "First") : Written(e == 1 ? first : second);
                    const int status = 409;   // Conflict for both clashing members, and SFault.First as the host maps it
                    if ((int)w.Status != status) return $"{action} {path} e={e}: status {(int)w.Status}, expected {status}: {Cut(w.Body)}";
                    JsonNode? body; try { body = JsonNode.Parse(w.Body); } catch { return $"{action} {path} e={e}: body is not JSON: {Cut(w.Body)}"; }
                    var fault = Un(r, body?["fault"]?.GetValue<string>());
                    if (fault != want) return $"{action} {path} e={e}: fault {fault}, expected {want}";
                }
            }

            List<string> lines;
            lock (logs.Lines) lines = logs.Lines.Where(l => l.Category == "CodoMetis.TypeKit.AspNetCore.FaultResponses" && l.Level == LogLevel.Warning).Select(l => l.Message).ToList();
            if (!warn)
                return lines.Count == 0 ? null : $"{action} ({(qualified ? "qualified" : "bare")} names, {(leave ? "one left out" : "none left out")}): the errors are written {Written(first)} and {Written(second)}, yet the host warned: {Cut(lines[0])}";
            if (lines.Count != sides.Count) return $"{action}: {sides.Count} endpoint(s) answer {first}.{member} and {second}.{member} alike as \"{Written(first)}\", {lines.Count} warning(s) at startup{(lines.Count > 0 ? ": " + Cut(lines[0]) : "")}";
            var names = twin ? (qualified ? new[] { "NsA.Twin.Same", "NsB.Twin.Same" } : ["Twin.Same"]) : ["HFault.Clash", "KFault.Clash"];
            foreach (var l in lines)
                if (names.FirstOrDefault(n => !l.Contains(n, StringComparison.Ordinal)) is { } missing) return $"{action}: the warning does not name {missing}: {Cut(l)}";
            return null;
        }

        // ============================================================ C12
        public static async Task<string?> C12(Row r)
        {
            var en = Registry.Of(r); Process(r, en);
            var source = r["source"];
            var dec = source == "decimal-converter";
            var family = dec ? "dec" : "coord";
            var dto = dec ? typeof(DecDto) : typeof(CoordDto);
            void Register(JsonSerializerOptions o)
            {
                // first among the options' converters, on both option sets in the same order ("Register the same on both option sets")
                switch (source)
                {
                    case "IDescribedJsonConverter": o.Converters.Insert(0, new CoordConverter()); break;
                    case "JsonSchemaDescriptions":
                        o.Converters.Insert(0, new CoordPlainConverter());
                        o.Converters.Insert(1, new JsonSchemaDescriptions().Describe<Coord>(JsonSchemaDescription.Of(CoordPlainConverter.Schema())));
                        break;
                    case "decimal-converter": o.Converters.Insert(0, new DecimalStringJsonConverter()); break;
                    default: throw new ArgumentException("unknown source " + source);
                }
            }
            var cfg = Cfg(r, [dec ? "Dec" : "Coord"],
                b => { if (dec) b.MapPost("/s/dec/m", (DecDto body, decimal q) => body); else b.MapPost("/s/coord/m", (CoordDto body) => body); },
                s => { s.PostConfigure<HttpJsonOptions>(o => Register(o.SerializerOptions)); s.PostConfigure<MvcJsonOptions>(o => Register(o.JsonSerializerOptions)); });
            WebApplication app;
            try { app = await Hx.Start(r, en, cfg); }
            catch (Exception ex) { return "host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
            await using var _ = app;
            var (doc, fail) = await DocOrBaseline(r, en, cfg, app);
            if (fail is not null) return fail;
            if (doc is null) return null;
            var http = HttpOptions(app);
            var want = Norm(dec ? new DecimalStringJsonConverter().DescribeJson(typeof(decimal), http)!.Schema
                                : source == "IDescribedJsonConverter" ? CoordConverter.Schema() : CoordPlainConverter.Schema());
            foreach (var path in Sides(r, family))
            {
                var op = Op(doc, path, "post");
                if (op is null) return $"document has no POST {path}";
                if (BodySchema(doc, op) is not JsonObject body) return $"{path}: no request body schema";
                var props = body["properties"]?.AsObject();
                JsonNode? P(string clr) => props?[JsonName(http, dto, clr)];
                var checks = new (string What, JsonNode? Got, bool Null)[]
                {
                    ("a property", P("At"), false), ("a nullable property", P("Maybe"), true), ("a list's items", P("Route")?["items"], false),
                    ("a dictionary's additionalProperties", P("ByName")?["additionalProperties"], false), ("inside a value object", P("Vo"), false),
                    ("inside a patch", P("Patched"), true),
                };
                foreach (var (what, got, admitsNull) in checks)
                {
                    if (got is null) return $"{path}: {what} is missing from the body schema: {Cut(body.ToJsonString())}";
                    // ASP.NET's OpenAPI 2.0 writer drops the reference of a nullable struct component ({"allOf":[{"x-nullable":true}]}):
                    // the plain struct beside it, which has no converter, is written the same, so the position has nothing to check
                    if (what == "a nullable property" && !dec && P("Twin") is { } twin && JsonNode.DeepEquals(got, twin) && Norm(twin) is JsonObject { Count: 0 })
                    {
                        r.Note("schema:nullable-reference-lost-for-plain-too", path);
                        continue;
                    }
                    if (!JsonNode.DeepEquals(Norm(got), want)) return $"{path}: {what} is {Cut(got.ToJsonString())}, the {source} says {want!.ToJsonString()}";
                    if (admitsNull && !Admits(got)) return $"{path}: {what} does not admit null: {Cut(got.ToJsonString())}";
                }
                if (dec)
                {
                    // "Parameters aren't JSON": the query parameter keeps ASP.NET's number, never the converter's string
                    var q = op["parameters"]?.AsArray().FirstOrDefault(p => p?["name"]?.GetValue<string>() == "q");
                    if (q is null) return $"{path}: no parameter q";
                    var qs = Hx.Deep(doc, q["schema"] ?? q);
                    if (!(qs?.ToJsonString() ?? "").Contains("\"number\"")) return $"{path}: the query parameter q is described as {Cut(qs?.ToJsonString())}, not ASP.NET's number";
                    if (JsonNode.DeepEquals(Norm(qs), want)) return $"{path}: the query parameter q is described as the converter's JSON";
                }
            }
            r.Note("compared", source);
            return null;
        }

        // ============================================================ C13
        public static async Task<string?> C13(Row r)
        {
            var en = Registry.Of(r); Process(r, en);
            var ctor = r["declared"] == "ctor-parameter";
            var family = ctor ? "ctor" : "init";
            var dto = ctor ? typeof(CtorPatchDto) : typeof(InitPatchDto);
            var cfg = Cfg(r, [ctor ? "Ctor" : "Init", "Boxed"], b =>
            {
                if (ctor) b.MapPost("/s/ctor/m", (CtorPatchDto body) => Results.Text(body.Floor.State.ToString()));
                else b.MapPost("/s/init/m", (InitPatchDto body) => Results.Text(body.Floor.State.ToString()));
            },
            // The actions answer a rejected body with 400 themselves: the serializer's rejection reaches ModelState under both kinds of
            // controller, and ASP.NET's own ValidationProblemDetails is not this claim's (it cannot be written under MaxDepth=3 or a
            // source-generated context that does not list it).
            s => s.Configure<ApiBehaviorOptions>(o => { o.SuppressModelStateInvalidFilter = true; o.SuppressMapClientErrors = true; }));
            WebApplication app;
            try { app = await Hx.Start(r, en, cfg); }
            catch (Exception ex) { return "host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
            await using var _ = app;
            var http = HttpOptions(app);
            var respect = http.RespectRequiredConstructorParameters;
            // "a patch that is a constructor parameter is required" under the option; an init property "keeps its default, which is Absent"
            var required = ctor && respect;
            var floor = JsonName(http, dto, "Floor");
            // 3 as the options read an int: an application's own converter for int (json.Converters=own-for-wrapped) reads its own spelling
            var three = Reads(http, "3") ? "3" : JsonSerializer.Serialize(3, http);
            var (doc, fail) = await DocOrBaseline(r, en, cfg, app);
            if (fail is not null) return fail;
            foreach (var path in Sides(r, family))
            {
                // MVC's validator throws for any nested struct of a body under a small MaxValidationDepth: the plain twin on the same side shows it
                async Task<(Hx.Wire? Wire, string? Fail)> Post(string body)
                {
                    var w = await Hx.Send(app, HttpMethod.Post, path, body);
                    if (w.Thrown is null) return (w, null);
                    var twin = path.EndsWith("/c", StringComparison.Ordinal) ? await Hx.Send(app, HttpMethod.Post, path.Replace($"/s/{family}/c", "/s/box/c"), "{}") : null;
                    if (twin?.Thrown is { } t && t.Split(':')[0] == w.Thrown.Split(':')[0]) { r.Note("mvc:plain-nested-body-throws-too", t.Split(':')[0]); return (null, null); }
                    return (null, $"{path} {body}: request threw {w.Thrown}");
                }
                var (absent, f1) = await Post("{}");
                if (f1 is not null) return f1;
                if (absent is not null)
                {
                    if (required && absent.Status != HttpStatusCode.BadRequest) return $"{path}: a body without the constructor-parameter patch was answered {(int)absent.Status} {Cut(absent.Body)} under RespectRequiredConstructorParameters, not rejected";
                    if (!required && (absent.Status != HttpStatusCode.OK || absent.Body != "Absent")) return $"{path}: a body without the {(ctor ? "constructor-parameter" : "init")} patch was answered {(int)absent.Status} {Cut(absent.Body)}, not Absent";
                }
                var (cleared, f2) = await Post($"{{\"{floor}\":null}}");
                if (f2 is not null) return f2;
                if (cleared is not null && (cleared.Status != HttpStatusCode.OK || cleared.Body != "Null")) return $"{path}: a body with {floor}: null was answered {(int)cleared.Status} {Cut(cleared.Body)}, not Null";
                var (set, f3) = await Post($"{{\"{floor}\":{three}}}");
                if (f3 is not null) return f3;
                if (set is not null && (set.Status != HttpStatusCode.OK || set.Body != "Value")) return $"{path}: a body with {floor}: {three} was answered {(int)set.Status} {Cut(set.Body)}, not Value";
            }
            if (doc is null) return null;   // ASP.NET alone refuses this document too (noted)
            foreach (var path in Sides(r, family))
            {
                var op = Op(doc, path, "post");
                if (op is null) return $"document has no POST {path}";
                if (BodySchema(doc, op) is not JsonObject body) return $"{path}: no request body schema";
                if (body["properties"]?[floor] is null) return $"{path}: the body schema has no property {floor}: {Cut(body.ToJsonString())}";
                var listed = body["required"]?.AsArray().Any(x => x?.GetValue<string>() == floor) == true;
                if (listed != required)
                    return $"{path}: the {(ctor ? "constructor-parameter" : "init")} patch {floor} is {(listed ? "" : "not ")}in required under RespectRequiredConstructorParameters={respect}, the serializer {(required ? "rejects" : "accepts")} a body without it";
            }
            return null;
        }
        static bool Reads(JsonSerializerOptions o, string json) { try { return JsonSerializer.Deserialize<int>(json, o) == 3; } catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return false; } }

        // ============================================================ C16
        public static async Task<string?> C16(Row r)
        {
            var en = Registry.Of(r); Process(r, en);
            return await Agree(r, en, patch: false) ?? await Agree(r, en, patch: true);
        }

        /// <summary>A naming policy no row sets: the minimal-API options' names then differ from MVC's whatever the row says.</summary>
        sealed class PrefixPolicy : JsonNamingPolicy { public override string ConvertName(string name) => "h_" + name; }

        static async Task<string?> Agree(Row r, Entry en, bool patch)
        {
            var difference = r["difference"];
            var type = patch ? typeof(CoordP) : typeof(Coord);
            var dto = patch ? typeof(AgreePatchDto) : typeof(AgreeDto);
            JsonConverter Converter() => patch ? new CoordPConverter() : new CoordConverter();
            var cfg = Cfg(r, [patch ? "AgreeP" : "Agree"],
                b => { if (patch) b.MapPost("/s/agreep/m", (AgreePatchDto body) => body); else b.MapPost("/s/agree/m", (AgreeDto body) => body); },
                s =>
                {
                    s.PostConfigure<HttpJsonOptions>(o =>
                    {
                        var x = o.SerializerOptions;
                        x.Converters.Insert(0, Converter());
                        switch (difference)
                        {
                            case "converter-only-http": break;
                            case "naming-policy": x.PropertyNamingPolicy = new PrefixPolicy(); break;
                            case "number-handling": x.NumberHandling = x.NumberHandling == JsonNumberHandling.Strict ? JsonNumberHandling.AllowReadingFromString : JsonNumberHandling.Strict; break;
                            default: throw new ArgumentException("unknown difference " + difference);
                        }
                    });
                    if (difference != "converter-only-http") s.PostConfigure<MvcJsonOptions>(o => o.JsonSerializerOptions.Converters.Insert(0, Converter()));
                });
            WebApplication app;
            try { app = await Hx.Start(r, en, cfg); }
            catch (Exception ex) { return "host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
            await using var _ = app;
            var refuse = difference == "converter-only-http" && Hx.HasMvc(r);
            var said = (patch ? CoordPConverter.Schema() : CoordConverter.Schema()).ToJsonString();
            var want = Norm(patch ? CoordPConverter.Schema() : CoordConverter.Schema());
            JsonNode? doc;
            try { doc = await Hx.Doc(app); }
            catch (Exception ex)
            {
                if (refuse && ex is InvalidOperationException && ex.Message.Contains($"'{type}'", StringComparison.Ordinal) && ex.Message.Contains(said, StringComparison.Ordinal))
                {
                    r.Note("compared", "refused naming the type and the minimal-API options' description");
                    return null;
                }
                // not the refusal the claim names: judged against ASP.NET alone
                return await Hx.DocRefusedByAspNetAlone(r, en, cfg, ex) is { } alone
                    ? (refuse ? $"{(patch ? "patch-held " : "")}{type.Name}: the document was refused otherwise than naming the type and what the options say: {ex.GetType().Name}: {Cut(ex.Message)}" : alone)
                    : null;
            }
            if (refuse) return $"{(patch ? "a type held only inside a patch, " : "")}{type.Name}: the host has controllers, its minimal-API options alone describe it ({want!.ToJsonString()}), and the document was built";
            if (doc is null) return null;
            var http = HttpOptions(app);
            var name = JsonName(http, dto, patch ? "P" : "At");
            foreach (var path in Sides(r, patch ? "agreep" : "agree"))
            {
                var op = Op(doc, path, "post");
                if (op is null) return $"document has no POST {path}";
                if (BodySchema(doc, op) is not JsonObject body) return $"{path}: no request body schema";
                var got = body["properties"]?[name];
                if (got is null) return $"{path}: the body schema has no property {name} (the minimal-API options' name): {Cut(body.ToJsonString())}";
                if (!JsonNode.DeepEquals(Norm(got), want)) return $"{path}: {name} is {Cut(got.ToJsonString())}, the converter on both option sets says {want!.ToJsonString()}";
            }
            return null;
        }
    }
}
