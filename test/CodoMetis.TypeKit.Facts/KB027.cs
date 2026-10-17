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

    private static readonly FactFixture Fixture = FactFixture.Beside("KB-027");

    public static TheoryData<string> Answers() => [.. Fixture.Answers.Select(answer => answer.Id)];

    /// <summary>Each answer of KB027.fixture.json: the enum's values in its component, null among them only where the nullable use came first.</summary>
    [Theory, MemberData(nameof(Answers))]
    public async Task An_enum_component_is_the_schema_of_its_first_use(string id)
    {
        var answer = Fixture[id];

        answer.Mismatch(await DocumentAsync(Enum.Parse<OpenApiSpecVersion>(answer.When["openapi.OpenApiVersion"]))).ShouldBeNull();
    }

    /// <summary>Both uses of each enum refer to the one component, so the null goes with the component, not with the use.</summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    public async Task Both_uses_refer_to_the_one_component(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version);

        foreach (var path in new[] { "/nullable-first/a", "/nullable-first/b" })
            document["paths"]![path]!["get"]!["parameters"]![0]!["schema"]!["$ref"]!.GetValue<string>().ShouldEndWith(nameof(MetNullableFirst));
        foreach (var path in new[] { "/plain-first/a", "/plain-first/b" })
            document["paths"]![path]!["get"]!["parameters"]![0]!["schema"]!["$ref"]!.GetValue<string>().ShouldEndWith(nameof(MetPlainFirst));
    }
}
