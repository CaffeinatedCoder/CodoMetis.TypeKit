using System.Text.Json.Nodes;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// An MVC action with closed-set parameters, top-level since MVC discovers only top-level public
/// controllers: the transformer reaches its parameters through their model metadata.
/// </summary>
[ApiController]
public sealed class TicketsController : ControllerBase
{
    [HttpGet("/mvc/tickets/{status}")]
    public int ByRoute(ProbeStatus status) => 0;

    [HttpGet("/mvc/tickets")]
    public int ByQuery([FromQuery] ProbeStatus status) => 0;
}

/// <summary>A closed set in the document: a string enum of its members' names, as an <c>enum</c> is described.</summary>
public sealed class ClosedSetSchemaTests
{
    public sealed record Ticket(ProbeStatus Status, ProbeTier Tier);

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_closed_set_is_a_string_enum_of_its_members(OpenApiSpecVersion version)
    {
        var document = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), version: version,
            endpoints: app => app.MapPost("/tickets", int (Ticket ticket) => 0));

        var status = document.Resolve(document.Property(nameof(Ticket), "status"));

        status["type"]!.GetValue<string>().ShouldBe("string");
        status["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe(["Open", "Shipped", "Cancelled"]);
        status["properties"].ShouldBeNull();
    }

    public sealed record Board(List<ProbeStatus> Statuses, ProbeStatus[] History, Dictionary<string, ProbeStatus> ByName);

    public sealed record Shipments(ProbeOrder.Status Order, ProbeShipment.Status Shipment);

    private static IEnumerable<string> Names(JsonNode schema) =>
        schema["enum"].ShouldNotBeNull($"no enum: {schema.ToJsonString()}").AsArray().Select(node => node!.GetValue<string>());

    /// <summary>
    /// A set bound from the route or the query string arrives as ASP.NET's placeholder string, as a
    /// value object does, and was published as a bare string: it is the enum an <c>enum</c> parameter
    /// is, and a query array of sets has the enum as its items.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_closed_set_parameter_is_the_string_enum_an_enum_parameter_is(OpenApiSpecVersion version)
    {
        var document = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), version: version, endpoints: app =>
        {
            app.MapGet("/tickets/{status}", int (ProbeStatus status) => 0);
            app.MapGet("/tickets", int (ProbeStatus status, ProbeStatus[] any) => 0);
        });

        foreach (var (path, name) in new[] { ("/tickets/{status}", "status"), ("/tickets", "status") })
        {
            var parameter = document.Resolve(document.Parameter(path, "get", name));

            parameter["type"]!.GetValue<string>().ShouldBe("string", $"{path} {name}: {parameter.ToJsonString()}");
            Names(parameter).ShouldBe(["Open", "Shipped", "Cancelled"], $"{path} {name}");
        }

        var items = document.Parameter("/tickets", "get", "any")["items"].ShouldNotBeNull("the query array lost its items");
        Names(document.Resolve(items)).ShouldBe(["Open", "Shipped", "Cancelled"]);
    }

    /// <summary>ASP.NET drops the element of a container of converter-backed types; the set's own schema is put back, as a value object's is.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_container_of_closed_sets_has_their_enum_as_its_elements(OpenApiSpecVersion version)
    {
        var document = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), version: version,
            endpoints: app => app.MapPost("/boards", int (Board board) => 0));

        foreach (var (property, keyword) in new[] { ("statuses", "items"), ("history", "items"), ("byName", "additionalProperties") })
        {
            var element = document.Property(nameof(Board), property)[keyword].ShouldNotBeNull($"'{property}' lost its {keyword}");

            Names(document.Resolve(element)).ShouldBe(["Open", "Shipped", "Cancelled"], property);
        }
    }

    /// <summary>MVC names a bound parameter's type in its model metadata, where a minimal API names it in the parameter's type: both are the enum.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task An_MVC_closed_set_parameter_is_the_string_enum_too(OpenApiSpecVersion version)
    {
        var document = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), version: version);

        foreach (var path in new[] { "/mvc/tickets/{status}", "/mvc/tickets" })
        {
            var parameter = document.Resolve(document.Parameter(path, "get", "status"));

            parameter["type"]!.GetValue<string>().ShouldBe("string", $"{path}: {parameter.ToJsonString()}");
            Names(parameter).ShouldBe(["Open", "Shipped", "Cancelled"], path);
        }
    }

    /// <summary>Two nested sets of one simple name shared one component, named after the first described, whose names then stood for both.</summary>
    [Fact]
    public async Task Two_nested_sets_of_one_simple_name_are_two_components()
    {
        var document = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(),
            endpoints: app => app.MapPost("/shipments", int (Shipments shipments) => 0));

        Names(document.Resolve(document.Property(nameof(Shipments), "order"))).ShouldBe(["Open"]);
        Names(document.Resolve(document.Property(nameof(Shipments), "shipment"))).ShouldBe(["Sent"]);
        document.Components.ContainsKey("ProbeOrderStatus").ShouldBeTrue(string.Join(", ", document.Components.Select(c => c.Key)));
        document.Components.ContainsKey("ProbeShipmentStatus").ShouldBeTrue();
    }
}
