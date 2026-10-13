using System.Text.Json.Nodes;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

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
}
