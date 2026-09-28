using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// The published schema follows the host: its JSON options, its other schema transformers, and
/// whether <c>AddTypeKit()</c> was called at all.
/// </summary>
public sealed class HostConfigurationTests
{
    [Fact]
    public async Task Without_AddTypeKit_a_value_object_is_the_empty_schema()
    {
        // The reason this package exists, and a tripwire: once ASP.NET describes a value object on its
        // own, this fails and the transformer can be reconsidered.
        var document = await ProbeHost.DocumentAsync();

        document.Component("ProbeId").ShouldDescribeTheSameAs(new JsonObject());
        document.Property("ProbeDocument", "ids")["items"].ShouldBeNull();
        document.Parameter("/probes/{id}", "get", "id").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string" });
    }

    [Fact]
    public async Task An_enum_value_object_follows_the_hosts_enum_converter_in_the_document_and_on_the_wire()
    {
        await using var host = await ProbeHost.StartAsync(
            openApi => openApi.AddTypeKit(),
            json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()),
            endpoints: app => app.MapPost("/weekday", ProbeDay (ProbeDay day) => day));
        var document = await host.DocumentAsync();

        var weekday = document.Component("ProbeWeekday");

        weekday["enum"].ShouldNotBeNull("the names are missing").AsArray().Select(v => v!.GetValue<string>()).ShouldContain("Monday");
        weekday.ShouldDescribeTheSameAs(document.Resolve(document.Property("ControlDocument", "weekday")));

        var cancellation = TestContext.Current.CancellationToken;
        var echoed = await host.Client.PostAsync(
            "/weekday", new StringContent("""{"weekday":"Monday"}""", System.Text.Encoding.UTF8, "application/json"), cancellation);
        (await echoed.Content.ReadAsStringAsync(cancellation)).ShouldBe("""{"weekday":"Monday"}""");
    }

    [Fact]
    public async Task A_number_value_object_follows_the_hosts_number_handling()
    {
        var document = await ProbeHost.DocumentAsync(
            openApi => openApi.AddTypeKit(),
            json => json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

        var count = document.Component("ProbeCount");

        count.ShouldDescribeTheSameAs(new JsonObject { ["type"] = "integer", ["format"] = "int32" });
        count.ShouldDescribeTheSameAs(document.Property("ControlDocument", "count"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Another_transformers_description_of_the_wrapped_type_carries_over_in_either_order(bool typeKitFirst)
    {
        // NodaTime values are written by NodaTime's converters, which ASP.NET cannot see through: a host
        // that serves them describes them with a transformer of its own, and that description reaches
        // the value objects wrapping them too.
        var document = await ProbeHost.DocumentAsync(
            openApi =>
            {
                if (typeKitFirst) openApi.AddTypeKit();
                openApi.AddSchemaTransformer<InstantSchemaTransformer>();
                if (!typeKitFirst) openApi.AddTypeKit();
            },
            json => json.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb),
            endpoints: app => app.MapGet("/times", ProbeTimes () => throw new NotSupportedException()));

        document.Resolve(document.Property("ProbeTimes", "instant"))
                .ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "date-time" });
    }

    [Fact]
    public async Task A_container_another_transformer_already_described_is_left_alone()
    {
        var document = await ProbeHost.DocumentAsync(openApi =>
        {
            openApi.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(List<ProbeId>))
                    schema.Items = new OpenApiSchema { Description = "described elsewhere" };
                if (context.JsonTypeInfo.Type == typeof(Dictionary<string, ProbeCount>))
                    schema.AdditionalProperties = new OpenApiSchema { Description = "described elsewhere" };
                return Task.CompletedTask;
            });
            openApi.AddTypeKit();
        });

        // Kept, not replaced by a reference to the value object's component. ASP.NET then visits the
        // kept schema as the value object's, so what it leaves open is filled in like anywhere else.
        foreach (var kept in new[]
                 {
                     document.Property("ProbeDocument", "ids")["items"].ShouldNotBeNull(),
                     document.Property("ProbeDocument", "counts")["additionalProperties"].ShouldNotBeNull(),
                 })
        {
            kept["$ref"].ShouldBeNull($"replaced: {kept.ToJsonString()}");
            kept["description"]?.GetValue<string>().ShouldBe("described elsewhere");
        }
    }

    /// <summary>The predicate the package README shows, a nullable value object included.</summary>
    [Fact]
    public async Task A_host_that_inlines_value_objects_gets_the_wrapped_types_schema_in_place()
    {
        var document = await ProbeHost.DocumentAsync(openApi =>
        {
            openApi.AddTypeKit();
            openApi.CreateSchemaReferenceId = type =>
                (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
                    ? null
                    : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
        });

        document.Components.ContainsKey("ProbeId").ShouldBeFalse();
        document.Components.ContainsKey("ProbeCount").ShouldBeFalse("the nullable ProbeCount? still made a component");
        document.Property("ProbeDocument", "id").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
        document.Property("ProbeDocument", "ids")["items"].ShouldNotBeNull("the list lost its items")
                .ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
    }

    /// <summary>
    /// ASP.NET names a component after the type's simple name, so <c>Shop.Id</c> and <c>Stock.Id</c>
    /// shared one component <c>Id</c>, and a stock id was documented as a uuid. A nested value object's
    /// component is named after the whole nesting chain, as its companion class is.
    /// </summary>
    [Fact]
    public async Task Nested_value_objects_of_one_name_get_a_component_each()
    {
        var document = await ProbeHost.DocumentAsync(
            openApi => openApi.AddTypeKit(),
            endpoints: app => app.MapPost("/inventory", Inventory (Inventory inventory) => inventory));

        document.Component("ShopId").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
        document.Component("StockId")["format"]!.GetValue<string>().ShouldBe("int32");
        document.Property("Inventory", "shop")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ShopId");
        document.Property("Inventory", "stock")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/StockId");
        document.Components.ContainsKey("Id").ShouldBeFalse();
    }

    /// <summary>A name the host chose itself is kept; only ASP.NET's default name is replaced.</summary>
    [Fact]
    public async Task A_component_name_the_host_chose_is_kept()
    {
        var document = await ProbeHost.DocumentAsync(
            openApi =>
            {
                openApi.CreateSchemaReferenceId = type => type.Type == typeof(Shop.Id) ? "ShopIdentifier" : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
                openApi.AddTypeKit();
            },
            endpoints: app => app.MapPost("/inventory", Inventory (Inventory inventory) => inventory));

        document.Components.ContainsKey("ShopIdentifier").ShouldBeTrue();
        document.Components.ContainsKey("StockId").ShouldBeTrue();
    }

    [Fact]
    public async Task Adding_it_twice_publishes_the_same_document()
    {
        var once = await ProbeHost.DefaultDocumentAsync();
        var twice = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit().AddTypeKit());

        twice.Root.ShouldDescribeTheSameAs(once.Root);
    }

    [Fact]
    public async Task A_value_object_that_wraps_itself_is_refused_by_name()
    {
        var generate = () => ProbeHost.DocumentAsync(
            openApi => openApi.AddTypeKit(),
            endpoints: app => app.MapGet("/ouroboros", Ouroboros () => throw new NotSupportedException()));

        var refusal = await generate.ShouldThrowAsync<InvalidOperationException>();

        refusal.Message.ShouldContain("Ouroboros");
        refusal.Message.ShouldContain("wraps itself");
    }

    /// <summary>
    /// With source-generated JSON only, as under Native AOT, ASP.NET has no contract for a type the
    /// host never serializes itself, so it cannot build the wrapped type's schema. The refusal says
    /// which value object needs which type in the host's context; the serializer's alone named a Guid
    /// the host never asked for.
    /// </summary>
    [Fact]
    public async Task A_source_generated_host_without_the_wrapped_type_is_told_which_to_add()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => SourceGeneratedDocumentAsync(IdsWithoutGuid.Default));

        refusal.Message.ShouldContain(nameof(ProbeId));
        refusal.Message.ShouldContain("[JsonSerializable(typeof(Guid))]");
    }

    [Fact]
    public async Task A_source_generated_host_with_the_wrapped_type_describes_the_value_object()
    {
        var document = await SourceGeneratedDocumentAsync(IdsWithGuid.Default);

        document["components"]!["schemas"]![nameof(ProbeId)]!.ToJsonString().ShouldBe("""{"type":"string","format":"uuid"}""");
    }

    private static async Task<JsonNode> SourceGeneratedDocumentAsync(JsonSerializerContext context)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolver = context);
        builder.Services.AddOpenApi(openApi => openApi.AddTypeKit());

        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapGet("/ids", () => new IdHolder(ProbeId.From(Guid.Empty)));
        await app.StartAsync();

        return JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json"))!;
    }

    private sealed class InstantSchemaTransformer : IOpenApiSchemaTransformer
    {
        public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.JsonTypeInfo.Type == typeof(Instant))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = "date-time";
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// A value object, by its contract, that wraps itself. It has no finite schema, and the satellite
/// works against the contract, so it has to refuse such a type rather than recurse. Declared by
/// hand as the generators declare one: the interfaces, and the attribute run-time code reads.
/// </summary>
[GeneratedValueObject<Ouroboros, Ouroboros>]
public sealed class Ouroboros : IValueObject<Ouroboros, Ouroboros>, IValueObjectMaterializer<Ouroboros, Ouroboros>
{
    public Ouroboros Value => this;

    public static Ouroboros Materialize(Ouroboros value) => value;

    public static bool operator ==(Ouroboros left, Ouroboros right) => ReferenceEquals(left, right);

    public static bool operator !=(Ouroboros left, Ouroboros right) => !ReferenceEquals(left, right);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => 0;
}

public sealed record IdHolder(ProbeId Id);

[JsonSerializable(typeof(IdHolder))]
internal sealed partial class IdsWithoutGuid : JsonSerializerContext;

[JsonSerializable(typeof(IdHolder))]
[JsonSerializable(typeof(Guid))]
internal sealed partial class IdsWithGuid : JsonSerializerContext;

public static partial class Shop
{
    public readonly partial record struct Id : IValue<Guid>;
}

public static partial class Stock
{
    public readonly partial record struct Id : IValue<int>;
}

public sealed record Inventory(Shop.Id Shop, Stock.Id Stock);
