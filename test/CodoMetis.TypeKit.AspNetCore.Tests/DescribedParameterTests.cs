using System.Collections.Concurrent;
using System.ComponentModel;
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
/// A parameter outside the body binds through <c>TryParse</c>, not JSON, so a converter that describes its type, or the
/// type a value object over it wraps, says nothing of it: a <c>decimal</c>, a <c>decimal?</c>, and a value object over
/// either, as a route, query, header or form parameter and as the items of a query or header array, is what ASP.NET
/// writes for the same parameter of the underlying type in a host without the converter and without <c>AddTypeKit()</c>.
/// That holds in MVC and in minimal APIs, in OpenAPI 3.1, 3.0 and 2.0, with everything inlined, value objects alone
/// inlined or components, under both number handlings, for a value object declared here and one in a referenced
/// assembly, which ASP.NET hands to the transformers differently.
/// </summary>
/// <remarks>
/// Found by the code analyst (2026-10-16, A2 and A3): a query or header array of a described type lost its
/// <c>items</c>, and a value object over a described type was its JSON spelling (<c>"type":"string"</c>, whose
/// pattern refuses <c>1.50</c>, which <c>TryParse</c> binds) as a header, a form field, a query scalar and a query or
/// header array's items. Where value objects are components, a parameter of one over a described type is written
/// inline (decision 106): the component is the JSON positions', shared with every use.
/// </remarks>
public sealed class DescribedParameterTests
{
    private static readonly ConcurrentDictionary<(OpenApiSpecVersion, string, bool Strict, bool Described), Task<OpenApiJson>> Documents = new();

    /// <summary>The parameter in the described host, and the parameter of the underlying type in the plain host it is compared with.</summary>
    private static readonly (string Parameter, string Twin)[] Pairs =
    [
        // The described type itself, a sibling of c56d38a in the array position (A2).
        (Key<decimal>(), Key<decimal>()),
        (Key<decimal?>(), Key<decimal?>()),
        // A value object over it, declared in this assembly and in a referenced one (A3), which ASP.NET hands to the
        // transformers differently.
        (Key<ParameterMoney>(), Key<decimal>()),
        (Key<ParameterMoney?>(), Key<decimal?>()),
        (Key<ProbeAmount>(), Key<decimal>()),
        (Key<ProbeAmount?>(), Key<decimal?>()),
    ];

    private static readonly Type[] TypeArguments =
        [typeof(decimal), typeof(decimal?), typeof(ParameterMoney), typeof(ParameterMoney?), typeof(ProbeAmount), typeof(ProbeAmount?)];

    private static readonly string[] References = ["inline-all", "inline-value-objects", "components"];

    private static readonly OpenApiSpecVersion[] Versions = [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0];

    public static TheoryData<OpenApiSpecVersion, string, bool, string, string> Positions()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, bool, string, string>();
        foreach (var version in Versions)
        foreach (var references in References)
        foreach (var strict in new[] { false, true })
        foreach (var host in new[] { "mvc", "minimal" })
        foreach (var position in new[] { "route", "query", "query-default", "header", "form", "query-items", "header-items" })
            data.Add(version, references, strict, host, position);

        return data;
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public async Task A_parameter_of_a_described_type_or_a_value_object_over_one_is_what_ASP_NET_writes_for_the_underlying_type_without_the_converter(
        OpenApiSpecVersion version, string references, bool strict, string host, string position)
    {
        var described = await DocumentAsync(version, references, strict, described: true);
        var plain = await DocumentAsync(version, references, strict, described: false);

        var differences = new List<string>();
        foreach (var (parameter, twin) in Pairs)
        {
            // A value object's default in MVC stops ASP.NET's own document (DefaultController).
            if (position == "query-default" && host == "mvc" && parameter != twin) continue;

            var actual = NullablePositionTests.Inline(described, NullablePositionTests.Schema(described, version, host, position, parameter));
            var expected = NullablePositionTests.Inline(plain, NullablePositionTests.Schema(plain, version, host, position, twin));

            if (!JsonNode.DeepEquals(actual, expected))
                differences.Add($"{parameter}: {actual?.ToJsonString()}\n   {twin} without the converter: {expected?.ToJsonString()}");
        }

        differences.ShouldBeEmpty(string.Join("\n", differences));
    }

    public static TheoryData<OpenApiSpecVersion, string, bool> DocumentVariants()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, bool>();
        foreach (var version in Versions)
        foreach (var references in References)
        foreach (var strict in new[] { false, true })
            data.Add(version, references, strict);

        return data;
    }

    /// <summary>
    /// The JSON positions of the same value objects keep the converter's description in the same document, where the
    /// parameters come first: a component a parameter registered first would say the parameter's schema for every use
    /// (KB-027).
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentVariants))]
    public async Task A_value_object_s_JSON_positions_keep_the_described_spelling_beside_parameters_of_it(OpenApiSpecVersion version, string references, bool strict)
    {
        var document = await DocumentAsync(version, references, strict, described: true);
        var body = NullablePositionTests.Inline(document, Body(document, version))!;

        foreach (var property in new[] { "money", "amount" })
        {
            var schema = body["properties"]![property]!.AsObject();
            schema.ShouldDescribeTheSameAs(DecimalString, $"the property '{property}'");
        }
    }

    private static readonly JsonObject DecimalString = new()
    {
        ["pattern"] = @"^-?(?:0|[1-9]\d*)(?:\.\d*[1-9])?$",
        ["type"] = "string",
        ["format"] = "decimal",
    };

    private static JsonNode Body(OpenApiJson document, OpenApiSpecVersion version)
    {
        var operation = document.Root["paths"]!["/minimal/json"]!["post"]!;
        return version == OpenApiSpecVersion.OpenApi2_0
            ? operation["parameters"]!.AsArray().Single(p => p!["in"]!.GetValue<string>() == "body")!["schema"]!
            : operation["requestBody"]!["content"]!["application/json"]!["schema"]!;
    }

    private static Task<OpenApiJson> DocumentAsync(OpenApiSpecVersion version, string references, bool strict, bool described) =>
        Documents.GetOrAdd((version, references, strict, described), static key => ProbeHost.DocumentAsync(
            openApi =>
            {
                if (key.Item2 == "inline-all") openApi.CreateSchemaReferenceId = _ => null;
                if (key.Described) openApi.AddTypeKit();

                // The README's way to inline value objects alone.
                if (key.Item2 == "inline-value-objects")
                    openApi.CreateSchemaReferenceId = type =>
                        (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
                            ? null
                            : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            },
            json: json =>
            {
                if (key.Strict) json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
                if (key.Described) json.SerializerOptions.Converters.Add(new DecimalStringJsonConverter());
            },
            version: key.Item1,
            endpoints: MapMinimal,
            controllers: [.. TypeArguments.Select(type => typeof(ParameterController<>).MakeGenericType(type)), typeof(DefaultController)],
            services: services => services.Configure<MvcOptions>(mvc => mvc.Conventions.Add(new ParameterRoutes()))));

    private static string Key<T>() => Key(typeof(T));

    private static string Key(Type type) => Nullable.GetUnderlyingType(type) is { } held ? held.Name + "-nullable" : type.Name;

    private static void MapMinimal(WebApplication app)
    {
        Map<decimal>(app); Map<decimal?>(app); Map<ParameterMoney>(app); Map<ParameterMoney?>(app); Map<ProbeAmount>(app); Map<ProbeAmount?>(app);

        // A parameter with a default, which ASP.NET writes in the parameter's schema, or on the reference to a component.
        // A [DefaultValue] that the type converter reads: minimal APIs cannot bind a C# default of a type parameter, nor
        // default(T) of a struct ("Argument types do not match"), and MVC's attributes cannot name a type parameter.
        app.MapGet("/minimal/Decimal/query-default", ([FromQuery(Name = "v"), DefaultValue(typeof(decimal), "1.5")] decimal v) => 0);
        app.MapGet("/minimal/Decimal-nullable/query-default", ([FromQuery(Name = "v"), DefaultValue(typeof(decimal), "1.5")] decimal? v) => 0);
#pragma warning disable ASP0020 // ParameterMoney implements IParsable once generated; a query parameter with an attribute meets it too
        app.MapGet("/minimal/ParameterMoney/query-default", ([FromQuery(Name = "v"), DefaultValue(typeof(ParameterMoney), "1.5")] ParameterMoney v) => 0);
        app.MapGet("/minimal/ParameterMoney-nullable/query-default", ([FromQuery(Name = "v"), DefaultValue(typeof(ParameterMoney), "1.5")] ParameterMoney? v) => 0);
#pragma warning restore ASP0020
        app.MapGet("/minimal/ProbeAmount/query-default", ([FromQuery(Name = "v"), DefaultValue(typeof(ProbeAmount), "1.5")] ProbeAmount v) => 0);
        app.MapGet("/minimal/ProbeAmount-nullable/query-default", ([FromQuery(Name = "v"), DefaultValue(typeof(ProbeAmount), "1.5")] ProbeAmount? v) => 0);

        // A JSON position of each value object, after the parameters.
        app.MapPost("/minimal/json", ([FromBody] ParameterBody body) => 0);
    }

    private static void Map<T>(IEndpointRouteBuilder app)
    {
        var at = "/minimal/" + Key<T>();
        app.MapGet(at + "/query", ([FromQuery(Name = "v")] T v) => 0);
        app.MapGet(at + "/header", ([FromHeader(Name = "v")] T v) => 0);
        app.MapGet(at + "/query-items", ([FromQuery(Name = "v")] T[] v) => 0);
        app.MapGet(at + "/header-items", ([FromHeader(Name = "v")] T[] v) => 0);
        app.MapPost(at + "/form", ([FromForm(Name = "v")] T v) => 0).DisableAntiforgery();
        app.MapGet(at + "/route/{v}", (T v) => 0);
    }

    public sealed record ParameterBody(ParameterMoney Money, ProbeAmount Amount);

    [ApiController]
    public sealed class ParameterController<T> : ControllerBase
    {
        [HttpGet("query")] public int Query([FromQuery(Name = "v")] T v) => 0;
        [HttpGet("header")] public int Header([FromHeader(Name = "v")] T v) => 0;
        [HttpGet("query-items")] public int QueryItems([FromQuery(Name = "v")] T[] v) => 0;
        [HttpGet("header-items")] public int HeaderItems([FromHeader(Name = "v")] T[] v) => 0;
        [HttpPost("form")] public int Form([FromForm(Name = "v")] T v) => 0;
        [HttpGet("route/{v}")] public int Route(T v) => 0;
    }

    /// <summary>
    /// A query parameter with a default, which an attribute cannot give a type parameter. Not of a value object: MVC
    /// describes it by ASP.NET's placeholder <see cref="string"/>, and ASP.NET throws writing the value object's default as
    /// a string, with or without <c>AddTypeKit()</c>.
    /// </summary>
    [ApiController]
    public sealed class DefaultController : ControllerBase
    {
        [HttpGet("mvc/Decimal/query-default")] public int Decimal([FromQuery(Name = "v"), DefaultValue(typeof(decimal), "1.5")] decimal v) => 0;
        [HttpGet("mvc/Decimal-nullable/query-default")] public int NullableDecimal([FromQuery(Name = "v"), DefaultValue(typeof(decimal), "1.5")] decimal? v) => 0;
    }

    /// <summary>Puts each closed <see cref="ParameterController{T}"/> under <c>mvc/{its type argument}</c>.</summary>
    private sealed class ParameterRoutes : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            if (!controller.ControllerType.IsGenericType || controller.ControllerType.GetGenericTypeDefinition() != typeof(ParameterController<>)) return;

            controller.Selectors.Clear();
            controller.Selectors.Add(new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel(new RouteAttribute("mvc/" + Key(controller.ControllerType.GenericTypeArguments[0]))),
            });
        }
    }
}

/// <summary>A value object over a described type, declared in the endpoints' own assembly.</summary>
public readonly partial record struct ParameterMoney : IValue<decimal>;
