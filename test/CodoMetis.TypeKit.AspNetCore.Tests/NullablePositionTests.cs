using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A nullable value object, or a value object where it is not nullable, publishes in every position of an
/// operation (body, query, header, form, route, response, a list's elements, a dictionary's values, a query or header
/// array's items), in MVC
/// and in minimal APIs, with enums written by number or by name, and with everything inlined, value objects alone
/// inlined or components, exactly the schema ASP.NET publishes for the wrapped type in the same position at the same
/// nullability.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET treats the null of each position its own way: a <c>[FromBody] int?</c> is <c>oneOf: [null, X]</c>
/// whose <c>X</c> admits null again, a <c>[FromBody] string?</c> is the same without the inner null, and a
/// query, header or route <c>int?</c> admits no null at all. MVC hands a <c>[FromBody] Quantity?</c> to the
/// transformer as a <see cref="string"/> parameter, so it took the parameter's rule and lost the inner null;
/// minimal APIs hand a query <c>Quantity?</c> to it as the nullable value object, so it took a JSON
/// position's rule and gained a null <c>int?</c> does not have.
/// </para>
/// <para>
/// Where the host makes the value object a component, a nullable one in a body or a response is
/// <c>oneOf: [null, component]</c> (decision 36): the component is shared with every non-nullable use, so it
/// cannot admit null, and the inner null the wrapped type carries is the one difference allowed.
/// </para>
/// <para>
/// A query, header, route or form <c>DayOfWeek?</c> written by name lists null among its values, where its type
/// admits none: the value object over it left the null out (finding F3, C21 row 285). An element of a nullable
/// enum that is ASP.NET's component is the bare reference, without the null its JSON holds; an inlined nullable
/// value object over it admits the null (decision 36), which the comparison takes out.
/// </para>
/// </remarks>
public sealed class NullablePositionTests
{
    private static readonly ConcurrentDictionary<(OpenApiSpecVersion, string, bool), Task<OpenApiJson>> Documents = new();

    /// <summary>The value object and the wrapped type it is compared with, each at its nullability.</summary>
    private static readonly (string ValueObject, string Wrapped)[] Pairs =
    [
        // Nullable structs over a number, a validated number, a Guid, an enum, a TimeSpan, a decimal, a bool and a date.
        (Key<ProbeCount?>(), Key<int?>()),
        (Key<ProbePercentage?>(), Key<int?>()),
        (Key<ProbeId?>(), Key<Guid?>()),
        (Key<ProbeWeekday?>(), Key<DayOfWeek?>()),
        // A nullable struct over a flags enum, written by name or by number as the host writes the enum.
        (Key<PositionAccessMask?>(), Key<PositionAccess?>()),
        (Key<ProbeDuration?>(), Key<TimeSpan?>()),
        (Key<ProbeAmount?>(), Key<decimal?>()),
        (Key<ProbeFlag?>(), Key<bool?>()),
        (Key<ProbeDate?>(), Key<DateOnly?>()),
        // Nullable structs over a reference type, compared with that type declared nullable.
        (Key<ProbeName?>(), "String-nullable"),
        (Key<ProbeUri?>(), "Uri-nullable"),
        // A record class declared nullable.
        ("ProbeLabel-nullable", "String-nullable"),
        // Where they are not nullable, which must stay so.
        (Key<ProbeCount>(), Key<int>()),
        (Key<ProbeId>(), Key<Guid>()),
        (Key<ProbeLabel>(), Key<string>()),
        (Key<ProbeWeekday>(), Key<DayOfWeek>()),
        (Key<PositionAccessMask>(), Key<PositionAccess>()),
    ];

    public static TheoryData<OpenApiSpecVersion, string, bool, string, string> Positions()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, bool, string, string>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0 })
        foreach (var references in new[] { "inline-all", "inline-value-objects", "components" })
        foreach (var enumsByName in new[] { false, true })
        foreach (var host in new[] { "mvc", "minimal" })
        foreach (var position in new[] { "body", "query", "header", "form", "route", "response", "elements", "values", "query-items", "header-items" })
        {
            // Where the value object is a component, a nullable element is the component or null, which the
            // README names and SchemaTests holds; inlined, the element's schema is the position's own.
            if (position is "elements" or "values" or "query-items" or "header-items" && references == "components") continue;
            data.Add(version, references, enumsByName, host, position);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public async Task A_value_object_is_described_as_its_wrapped_type_in_the_same_position_at_the_same_nullability(
        OpenApiSpecVersion version, string references, bool enumsByName, string host, string position)
    {
        var document = await Documents.GetOrAdd((version, references, enumsByName), static key => ProbeHost.DocumentAsync(
            openApi =>
            {
                if (key.Item2 == "inline-all") openApi.CreateSchemaReferenceId = _ => null;
                openApi.AddTypeKit();

                // The README's way to inline value objects alone: the wrapped enum stays ASP.NET's component.
                if (key.Item2 == "inline-value-objects")
                    openApi.CreateSchemaReferenceId = type =>
                        (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
                            ? null
                            : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            },
            // An enum the host writes by name is a string enum, whose nullable form lists null among its values.
            json: key.Item3 ? json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()) : null,
            version: key.Item1,
            endpoints: MapMinimal,
            controllers: [.. TypeArguments.Select(type => typeof(PositionController<>).MakeGenericType(type)), typeof(NullableReferenceController)],
            services: services => services.Configure<MvcOptions>(mvc => mvc.Conventions.Add(new PositionRoutes()))));

        var differences = new List<string>();
        foreach (var (valueObject, wrapped) in Pairs)
        {
            var actual = Schema(document, version, host, position, valueObject);
            var expected = Schema(document, version, host, position, wrapped);

            // Decision 36: a component shared with the non-nullable uses cannot admit null; the null is around it.
            if (actual?["oneOf"] is JsonArray { Count: 2 } nullOr && nullOr[1]?["$ref"] is not null && expected?["oneOf"] is JsonArray { Count: 2 } wrappedNullOr)
                wrappedNullOr[1] = WithoutNull(wrappedNullOr[1]!);

            // Decision 36 too: an inlined nullable element admits null, where ASP.NET's own element of a nullable
            // component type (an enum's) is the bare reference, without the null its JSON holds.
            var keyword = position == "values" ? "additionalProperties" : "items";
            if (wrapped.EndsWith("-nullable", StringComparison.Ordinal)
             && Holder(actual) is { } container && container[keyword] is JsonObject element && element["$ref"] is null
             && Holder(expected)?[keyword]?["$ref"] is not null)
            {
                if (JsonNode.DeepEquals(WithoutNull(element), element)) differences.Add($"{valueObject}: the element admits no null: {element.ToJsonString()}");
                container[keyword] = WithoutNull(element);
            }

            if (!JsonNode.DeepEquals(Inline(document, actual), Inline(document, expected)))
                differences.Add($"{valueObject}: {Inline(document, actual)?.ToJsonString()}\n   {wrapped}: {Inline(document, expected)?.ToJsonString()}");
        }

        differences.ShouldBeEmpty(string.Join("\n", differences));
    }

    private static readonly Type[] TypeArguments =
    [
        typeof(ProbeCount?), typeof(ProbePercentage?), typeof(ProbeId?), typeof(ProbeWeekday?), typeof(ProbeDuration?), typeof(ProbeAmount?),
        typeof(ProbeFlag?), typeof(ProbeDate?), typeof(ProbeName?), typeof(ProbeUri?), typeof(ProbeCount), typeof(ProbeId), typeof(ProbeLabel),
        typeof(ProbeWeekday), typeof(PositionAccessMask?), typeof(PositionAccessMask),
        typeof(int?), typeof(Guid?), typeof(DayOfWeek?), typeof(TimeSpan?), typeof(decimal?), typeof(bool?), typeof(DateOnly?), typeof(int),
        typeof(Guid), typeof(string), typeof(DayOfWeek), typeof(PositionAccess?), typeof(PositionAccess),
    ];

    private static string Key<T>() => Key(typeof(T));

    private static string Key(Type type) => Nullable.GetUnderlyingType(type) is { } held ? held.Name + "-nullable" : type.Name;

    private static void MapMinimal(WebApplication app)
    {
        Map<ProbeCount?>(app); Map<ProbePercentage?>(app); Map<ProbeId?>(app); Map<ProbeWeekday?>(app); Map<ProbeDuration?>(app);
        Map<ProbeAmount?>(app); Map<ProbeFlag?>(app); Map<ProbeDate?>(app); Map<ProbeName?>(app); Map<ProbeUri?>(app);
        Map<ProbeCount>(app); Map<ProbeId>(app); Map<ProbeLabel>(app); Map<ProbeWeekday>(app); Map<PositionAccessMask?>(app); Map<PositionAccessMask>(app);
        Map<int?>(app); Map<Guid?>(app); Map<DayOfWeek?>(app); Map<TimeSpan?>(app); Map<decimal?>(app); Map<bool?>(app); Map<DateOnly?>(app);
        Map<int>(app); Map<Guid>(app); Map<string>(app); Map<DayOfWeek>(app); Map<PositionAccess?>(app); Map<PositionAccess>(app);

        // The reference types at their declared nullability, which a type argument does not carry.
        app.MapPost("/minimal/String-nullable/body", ([FromBody] string? v) => 0);
        app.MapPost("/minimal/String-nullable/elements", ([FromBody] List<string?> v) => 0);
        app.MapPost("/minimal/String-nullable/values", ([FromBody] Dictionary<string, string?> v) => 0);
        app.MapGet("/minimal/String-nullable/query", ([FromQuery(Name = "v")] string? v) => 0);
        app.MapGet("/minimal/String-nullable/header", ([FromHeader(Name = "v")] string? v) => 0);
        app.MapGet("/minimal/String-nullable/query-items", ([FromQuery(Name = "v")] string?[] v) => 0);
        app.MapGet("/minimal/String-nullable/header-items", ([FromHeader(Name = "v")] string?[] v) => 0);
        app.MapPost("/minimal/String-nullable/form", ([FromForm(Name = "v")] string? v) => 0).DisableAntiforgery();
        app.MapGet("/minimal/String-nullable/route/{v}", (string? v) => 0);
        app.MapGet("/minimal/String-nullable/response", string? () => null);
        app.MapPost("/minimal/Uri-nullable/body", ([FromBody] Uri? v) => 0);
        app.MapPost("/minimal/Uri-nullable/elements", ([FromBody] List<Uri?> v) => 0);
        app.MapPost("/minimal/Uri-nullable/values", ([FromBody] Dictionary<string, Uri?> v) => 0);
        app.MapGet("/minimal/Uri-nullable/query", ([FromQuery(Name = "v")] Uri? v) => 0);
        app.MapGet("/minimal/Uri-nullable/header", ([FromHeader(Name = "v")] Uri? v) => 0);
        app.MapGet("/minimal/Uri-nullable/query-items", ([FromQuery(Name = "v")] Uri?[] v) => 0);
        app.MapGet("/minimal/Uri-nullable/header-items", ([FromHeader(Name = "v")] Uri?[] v) => 0);
        app.MapPost("/minimal/Uri-nullable/form", ([FromForm(Name = "v")] Uri? v) => 0).DisableAntiforgery();
        app.MapGet("/minimal/Uri-nullable/route/{v}", (Uri? v) => 0);
        app.MapGet("/minimal/Uri-nullable/response", Uri? () => null);
        app.MapPost("/minimal/ProbeLabel-nullable/body", ([FromBody] ProbeLabel? v) => 0);
        app.MapPost("/minimal/ProbeLabel-nullable/elements", ([FromBody] List<ProbeLabel?> v) => 0);
        app.MapPost("/minimal/ProbeLabel-nullable/values", ([FromBody] Dictionary<string, ProbeLabel?> v) => 0);
        app.MapGet("/minimal/ProbeLabel-nullable/query", ([FromQuery(Name = "v")] ProbeLabel? v) => 0);
        app.MapGet("/minimal/ProbeLabel-nullable/header", ([FromHeader(Name = "v")] ProbeLabel? v) => 0);
        app.MapGet("/minimal/ProbeLabel-nullable/query-items", ([FromQuery(Name = "v")] ProbeLabel?[] v) => 0);
        app.MapGet("/minimal/ProbeLabel-nullable/header-items", ([FromHeader(Name = "v")] ProbeLabel?[] v) => 0);
        app.MapPost("/minimal/ProbeLabel-nullable/form", ([FromForm(Name = "v")] ProbeLabel? v) => 0).DisableAntiforgery();
        app.MapGet("/minimal/ProbeLabel-nullable/route/{v}", (ProbeLabel? v) => 0);
        app.MapGet("/minimal/ProbeLabel-nullable/response", ProbeLabel? () => null);
    }

    private static void Map<T>(IEndpointRouteBuilder app)
    {
        var at = "/minimal/" + Key<T>();
        app.MapPost(at + "/body", ([FromBody] T v) => 0);
        app.MapPost(at + "/elements", ([FromBody] List<T> v) => 0);
        app.MapPost(at + "/values", ([FromBody] Dictionary<string, T> v) => 0);
        app.MapGet(at + "/query", ([FromQuery(Name = "v")] T v) => 0);
        app.MapGet(at + "/header", ([FromHeader(Name = "v")] T v) => 0);
        app.MapGet(at + "/query-items", ([FromQuery(Name = "v")] T[] v) => 0);
        app.MapGet(at + "/header-items", ([FromHeader(Name = "v")] T[] v) => 0);
        app.MapPost(at + "/form", ([FromForm(Name = "v")] T v) => 0).DisableAntiforgery();
        app.MapGet(at + "/route/{v}", (T v) => 0);
        app.MapGet(at + "/response", T () => default!);
    }

    [ApiController]
    public sealed class PositionController<T> : ControllerBase
    {
        [HttpPost("body")] public int Body([FromBody] T v) => 0;
        [HttpPost("elements")] public int Elements([FromBody] List<T> v) => 0;
        [HttpPost("values")] public int Values([FromBody] Dictionary<string, T> v) => 0;
        [HttpGet("query")] public int Query([FromQuery(Name = "v")] T v) => 0;
        [HttpGet("header")] public int Header([FromHeader(Name = "v")] T v) => 0;
        [HttpGet("query-items")] public int QueryItems([FromQuery(Name = "v")] T[] v) => 0;
        [HttpGet("header-items")] public int HeaderItems([FromHeader(Name = "v")] T[] v) => 0;
        [HttpPost("form")] public int Form([FromForm(Name = "v")] T v) => 0;
        [HttpGet("route/{v}")] public int Route(T v) => 0;
        [HttpGet("response")] public T Answer() => default!;
    }

    [ApiController]
    public sealed class NullableReferenceController : ControllerBase
    {
        [HttpPost("mvc/String-nullable/body")] public int StringBody([FromBody] string? v) => 0;
        [HttpPost("mvc/String-nullable/elements")] public int StringElements([FromBody] List<string?> v) => 0;
        [HttpPost("mvc/String-nullable/values")] public int StringValues([FromBody] Dictionary<string, string?> v) => 0;
        [HttpGet("mvc/String-nullable/query")] public int StringQuery([FromQuery(Name = "v")] string? v) => 0;
        [HttpGet("mvc/String-nullable/header")] public int StringHeader([FromHeader(Name = "v")] string? v) => 0;
        [HttpGet("mvc/String-nullable/query-items")] public int StringQueryItems([FromQuery(Name = "v")] string?[] v) => 0;
        [HttpGet("mvc/String-nullable/header-items")] public int StringHeaderItems([FromHeader(Name = "v")] string?[] v) => 0;
        [HttpPost("mvc/String-nullable/form")] public int StringForm([FromForm(Name = "v")] string? v) => 0;
        [HttpGet("mvc/String-nullable/route/{v}")] public int StringRoute(string? v) => 0;
        [HttpGet("mvc/String-nullable/response")] public string? StringResponse() => null;
        [HttpPost("mvc/Uri-nullable/body")] public int UriBody([FromBody] Uri? v) => 0;
        [HttpPost("mvc/Uri-nullable/elements")] public int UriElements([FromBody] List<Uri?> v) => 0;
        [HttpPost("mvc/Uri-nullable/values")] public int UriValues([FromBody] Dictionary<string, Uri?> v) => 0;
        [HttpGet("mvc/Uri-nullable/query")] public int UriQuery([FromQuery(Name = "v")] Uri? v) => 0;
        [HttpGet("mvc/Uri-nullable/header")] public int UriHeader([FromHeader(Name = "v")] Uri? v) => 0;
        [HttpGet("mvc/Uri-nullable/query-items")] public int UriQueryItems([FromQuery(Name = "v")] Uri?[] v) => 0;
        [HttpGet("mvc/Uri-nullable/header-items")] public int UriHeaderItems([FromHeader(Name = "v")] Uri?[] v) => 0;
        [HttpPost("mvc/Uri-nullable/form")] public int UriForm([FromForm(Name = "v")] Uri? v) => 0;
        [HttpGet("mvc/Uri-nullable/route/{v}")] public int UriRoute(Uri? v) => 0;
        [HttpGet("mvc/Uri-nullable/response")] public Uri? UriResponse() => null;
        [HttpPost("mvc/ProbeLabel-nullable/body")] public int LabelBody([FromBody] ProbeLabel? v) => 0;
        [HttpPost("mvc/ProbeLabel-nullable/elements")] public int LabelElements([FromBody] List<ProbeLabel?> v) => 0;
        [HttpPost("mvc/ProbeLabel-nullable/values")] public int LabelValues([FromBody] Dictionary<string, ProbeLabel?> v) => 0;
        [HttpGet("mvc/ProbeLabel-nullable/query")] public int LabelQuery([FromQuery(Name = "v")] ProbeLabel? v) => 0;
        [HttpGet("mvc/ProbeLabel-nullable/header")] public int LabelHeader([FromHeader(Name = "v")] ProbeLabel? v) => 0;
        [HttpGet("mvc/ProbeLabel-nullable/query-items")] public int LabelQueryItems([FromQuery(Name = "v")] ProbeLabel?[] v) => 0;
        [HttpGet("mvc/ProbeLabel-nullable/header-items")] public int LabelHeaderItems([FromHeader(Name = "v")] ProbeLabel?[] v) => 0;
        [HttpPost("mvc/ProbeLabel-nullable/form")] public int LabelForm([FromForm(Name = "v")] ProbeLabel? v) => 0;
        [HttpGet("mvc/ProbeLabel-nullable/route/{v}")] public int LabelRoute(ProbeLabel? v) => 0;
        [HttpGet("mvc/ProbeLabel-nullable/response")] public ProbeLabel? LabelResponse() => null;
    }

    /// <summary>Puts each closed <see cref="PositionController{T}"/> under <c>mvc/{its type argument}</c>.</summary>
    private sealed class PositionRoutes : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            if (!controller.ControllerType.IsGenericType || controller.ControllerType.GetGenericTypeDefinition() != typeof(PositionController<>)) return;

            controller.Selectors.Clear();
            controller.Selectors.Add(new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel(new RouteAttribute("mvc/" + Key(controller.ControllerType.GenericTypeArguments[0]))),
            });
        }
    }

    /// <summary>The schema of the position: the request body's, the parameter's, or the response's.</summary>
    private static JsonNode? Schema(OpenApiJson document, OpenApiSpecVersion version, string host, string position, string key)
    {
        var path = $"/{host}/{key}/{position}" + (position == "route" ? "/{v}" : "");
        var method = position is "body" or "form" or "elements" or "values" ? "post" : "get";
        var operation = document.Root["paths"]?[path]?[method] ?? throw new InvalidOperationException($"No operation {method} {path}.");
        var v2 = version == OpenApiSpecVersion.OpenApi2_0;

        var schema = position switch
        {
            // A minimal API writes a string as text/plain: the first content is the one there is, in any case alike.
            "response" => v2 ? operation["responses"]!["200"]!["schema"] : operation["responses"]!["200"]!["content"]!.AsObject().First().Value!["schema"],
            "body" or "elements" or "values" when v2 => operation["parameters"]!.AsArray().Single(p => p!["in"]!.GetValue<string>() == "body")!["schema"],
            "body" or "elements" or "values" => operation["requestBody"]!["content"]!["application/json"]!["schema"],
            "form" when !v2 => operation["requestBody"]!["content"]!.AsObject().First().Value!["schema"],
            // A parameter: in 2.0 its keywords are on the parameter itself, so all but its name and description.
            _ => operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "v"),
        };

        return schema?.DeepClone();
    }

    /// <summary>What holds a container's <c>items</c> or <c>additionalProperties</c>: a body's schema, a 3.x parameter's schema, or a 2.0 parameter itself.</summary>
    private static JsonObject? Holder(JsonNode? schema) => schema?["schema"] as JsonObject ?? schema as JsonObject;

    private static JsonNode? Inline(OpenApiJson document, JsonNode? schema) => schema switch
    {
        JsonObject reference when reference["$ref"] is { } id => Inline(document, Target(document, id.GetValue<string>())),
        JsonObject keywords => new JsonObject(keywords
            .Where(keyword => keyword.Key is not ("name" or "description"))
            .Select(keyword => KeyValuePair.Create(keyword.Key, Inline(document, keyword.Value)))),
        JsonArray items => new JsonArray([.. items.Select(item => Inline(document, item))]),
        _ => schema?.DeepClone(),
    };

    private static JsonNode Target(OpenApiJson document, string reference)
    {
        var name = reference.Split('/')[^1];
        return document.Root["components"]?["schemas"]?[name] ?? document.Root["definitions"]?[name]
            ?? throw new InvalidOperationException($"No schema '{reference}'.");
    }

    /// <summary>The schema without the null it admits: <c>null</c> out of its type and its values, <c>nullable</c> and a null branch gone.</summary>
    private static JsonNode WithoutNull(JsonNode schema)
    {
        var copy = schema.DeepClone().AsObject();
        copy.Remove("nullable");
        copy.Remove("x-nullable");
        if (copy["type"] is JsonArray types)
        {
            var kept = types.Where(type => type!.GetValue<string>() != "null").Select(type => type!.DeepClone()).ToArray();
            copy["type"] = kept.Length == 1 ? kept[0] : new JsonArray(kept);
        }

        if (copy["enum"] is JsonArray values)
            copy["enum"] = new JsonArray([.. values.Where(value => value is not null).Select(value => value!.DeepClone())]);

        if (copy["anyOf"] is JsonArray branches)
            copy["anyOf"] = new JsonArray([.. branches.Where(branch => branch!["nullable"] is null).Select(branch => branch!.DeepClone())]);

        return copy;
    }
}

/// <summary>A flags enum, whose values combine: a host that writes it by name writes <c>"Read, Write"</c>.</summary>
[Flags]
public enum PositionAccess
{
    None = 0,
    Read = 1,
    Write = 2,
}

/// <summary>A value object over a flags enum.</summary>
public readonly partial record struct PositionAccessMask : IValue<PositionAccess>;
