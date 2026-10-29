using System.Reflection;
using CodoMetis.TypeKit.AspNetCore.Tests.Collisions;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// <see cref="TypeKitOpenApiOptions.InlineValueObjects"/> writes every value object in place and names no component
/// after one, and the document without it is the one <c>AddTypeKit()</c> writes (decision 115). The schemas it writes
/// are held to the wrapped type's by <c>SchemaTests</c> and <c>NullablePositionTests</c>, under both settings.
/// </summary>
public sealed class InlineValueObjectsSettingTests
{
    private static readonly OpenApiSpecVersion[] Versions = [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0];

    public static TheoryData<OpenApiSpecVersion> EveryVersion() => [.. Versions];

    /// <summary>The names a value object of these assemblies would have as a component, after its nesting chain.</summary>
    private static readonly HashSet<string> ValueObjectNames =
    [
        .. new[] { typeof(ProbeId).Assembly, typeof(InlineValueObjectsSettingTests).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<GeneratedValueObjectAttribute>(inherit: false) is { } valueObject && valueObject.ValueObjectType == type)
            .Select(Chain),
    ];

    [Theory]
    [MemberData(nameof(EveryVersion))]
    public async Task No_component_is_a_value_object(OpenApiSpecVersion version)
    {
        var inlined = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = true), version: version);
        var components = await ProbeHost.DefaultDocumentAsync(version);

        // The control: without the setting, the same endpoints name components after value objects.
        components.Components.Select(component => component.Key).Where(ValueObjectNames.Contains).ShouldNotBeEmpty();

        inlined.Components.Select(component => component.Key).Where(ValueObjectNames.Contains).ShouldBeEmpty();
        var json = inlined.Root.ToJsonString();
        foreach (var name in ValueObjectNames) json.ShouldNotContain($"/{name}\"", customMessage: $"a reference to '{name}' is left");
    }

    [Theory]
    [MemberData(nameof(EveryVersion))]
    public async Task Without_the_setting_the_document_is_the_one_AddTypeKit_writes(OpenApiSpecVersion version)
    {
        var plain = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), version: version);
        var unset = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(_ => { }), version: version);
        var off = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = false), version: version);

        unset.Root.ShouldDescribeTheSameAs(plain.Root, "no setting chosen");
        off.Root.ShouldDescribeTheSameAs(plain.Root, "InlineValueObjects = false");
    }

    /// <summary>A second, plain <c>AddTypeKit()</c> asks the first for the name, which is none.</summary>
    [Fact]
    public async Task A_later_AddTypeKit_keeps_them_inlined()
    {
        var once = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = true));
        var twice = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = true).AddTypeKit());

        twice.Root.ShouldDescribeTheSameAs(once.Root);
    }

    /// <summary>A name the host's own delegate gives a value object does not make it a component; every other type keeps its name.</summary>
    [Fact]
    public async Task The_host_s_own_names_are_kept_for_every_other_type()
    {
        var document = await ProbeHost.DocumentAsync(openApi =>
        {
            openApi.CreateSchemaReferenceId = type =>
                type.Type == typeof(ProbeDocument) ? "Probes" : type.Type == typeof(ProbeId) ? "Identifier" : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = true);
        });

        document.Components.ContainsKey("Identifier").ShouldBeFalse("the host's name made the value object a component");
        document.Property("Probes", "id").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
    }

    /// <summary>
    /// Decision 55 refuses a name two types claim. Inlined, a value object claims none, so a nested one and a top-level one
    /// of its chain's name are two schemas in place; a closed set still claims its name, and is still refused.
    /// </summary>
    [Fact]
    public async Task Value_objects_claim_no_name_and_closed_sets_still_do()
    {
        var carts = await ProbeHost.DocumentAsync(
            openApi => openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = true),
            endpoints: app => app.MapPost("/carts", int (Carts carts) => 0));

        carts.Property(nameof(Carts), "nested").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
        carts.Property(nameof(Carts), "topLevel")["format"]!.GetValue<string>().ShouldBe("int32");

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => ProbeHost.DocumentAsync(
            openApi => openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = true),
            endpoints: app => app.MapPost("/orders", int (OrderStatuses statuses) => 0)));
        refusal.Message.ShouldContain("'OrderStatus'");
    }

    [Fact]
    public void A_null_configuration_is_refused() =>
        Should.Throw<ArgumentNullException>(() => new OpenApiOptions().AddTypeKit(configure: null!)).ParamName.ShouldBe("configure");

    private static string Chain(Type type) => type.DeclaringType is { } declaring ? Chain(declaring) + type.Name : type.Name;
}
