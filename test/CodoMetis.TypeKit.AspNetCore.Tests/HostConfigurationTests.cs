using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
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

    [Fact]
    public async Task A_host_that_inlines_value_objects_gets_the_wrapped_types_schema_in_place()
    {
        var document = await ProbeHost.DocumentAsync(openApi =>
        {
            openApi.AddTypeKit();
            openApi.CreateSchemaReferenceId = type =>
                type.Type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueObject<,>))
                    ? null
                    : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
        });

        document.Components.ContainsKey("ProbeId").ShouldBeFalse();
        document.Property("ProbeDocument", "id").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
        document.Property("ProbeDocument", "ids")["items"].ShouldNotBeNull("the list lost its items")
                .ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
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
/// works against the interface, so it has to refuse such a type rather than recurse.
/// </summary>
public sealed class Ouroboros : IValueObject<Ouroboros, Ouroboros>
{
    public Ouroboros Value => this;

    public static bool operator ==(Ouroboros left, Ouroboros right) => ReferenceEquals(left, right);

    public static bool operator !=(Ouroboros left, Ouroboros right) => !ReferenceEquals(left, right);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => 0;
}
