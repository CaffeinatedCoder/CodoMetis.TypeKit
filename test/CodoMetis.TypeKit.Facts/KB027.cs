using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-027: the component ASP.NET makes for an enum written by name is the schema of the first use
/// it meets. Met first as a nullable parameter, it lists null among its values, for its non-nullable uses too; met
/// first as itself, it does not, for its nullable uses too.
/// </summary>
public sealed class KB027
{
    [JsonConverter(typeof(JsonStringEnumConverter<MetNullableFirst>))]
    public enum MetNullableFirst { X, Y }

    [JsonConverter(typeof(JsonStringEnumConverter<MetPlainFirst>))]
    public enum MetPlainFirst { X, Y }

    private static async Task<JsonNode> DocumentAsync(OpenApiSpecVersion version) =>
        await FactHost.DocumentAsync(version, [], minimal: app =>
        {
            app.MapGet("/nullable-first/a", ([FromHeader(Name = "v")] MetNullableFirst? v) => 0);
            app.MapGet("/nullable-first/b", ([FromHeader(Name = "v")] MetNullableFirst v) => 0);
            app.MapGet("/plain-first/a", ([FromHeader(Name = "v")] MetPlainFirst v) => 0);
            app.MapGet("/plain-first/b", ([FromHeader(Name = "v")] MetPlainFirst? v) => 0);
        });

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    public async Task An_enum_component_is_the_schema_of_its_first_use(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version);
        var schemas = document["components"]!["schemas"]!;

        schemas[nameof(MetNullableFirst)]!["enum"]!.ToJsonString().ShouldBe("""["X","Y",null]""", "met first as nullable: null among its values");
        schemas[nameof(MetPlainFirst)]!["enum"]!.ToJsonString().ShouldBe("""["X","Y"]""", "met first as itself: no null");

        // Both uses of each refer to the one component, so the null goes with the component, not with the use.
        foreach (var path in new[] { "/nullable-first/a", "/nullable-first/b" })
            document["paths"]![path]!["get"]!["parameters"]![0]!["schema"]!["$ref"]!.GetValue<string>().ShouldEndWith(nameof(MetNullableFirst));
        foreach (var path in new[] { "/plain-first/a", "/plain-first/b" })
            document["paths"]![path]!["get"]!["parameters"]![0]!["schema"]!["$ref"]!.GetValue<string>().ShouldEndWith(nameof(MetPlainFirst));
    }
}
