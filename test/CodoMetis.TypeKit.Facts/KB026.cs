using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-026: under OpenAPI 2.0, ASP.NET writes a nullable property that refers to a component as
/// <c>allOf: [{x-nullable: true}]</c>, without the reference; 3.1 writes <c>oneOf: [null, $ref]</c>.
/// </summary>
public sealed class KB026
{
    public readonly record struct Spot(int X, int Y);

    public sealed record Holder(Spot Always, Spot? Maybe);

    [ApiController]
    [Route("kb026")]
    public sealed class HolderController : ControllerBase
    {
        [HttpPost]
        public IActionResult Post([FromBody] Holder holder) => Ok();
    }

    private static async Task<JsonNode> PropertiesAsync(OpenApiSpecVersion version)
    {
        var document = await FactHost.DocumentAsync(version, [typeof(HolderController)]);
        var schemas  = version == OpenApiSpecVersion.OpenApi2_0 ? document["definitions"]! : document["components"]!["schemas"]!;

        return schemas["Holder"]!["properties"]!;
    }

    [Fact]
    public async Task Under_OpenApi_2_0_a_nullable_property_of_a_component_loses_its_reference()
    {
        var properties = await PropertiesAsync(OpenApiSpecVersion.OpenApi2_0);

        properties["always"]!.ToJsonString().ShouldBe("""{"$ref":"#/definitions/Spot"}""");
        properties["maybe"]!.ToJsonString().ShouldBe("""{"allOf":[{"x-nullable":true}]}""", "no reference, no type");
    }

    [Fact]
    public async Task Under_OpenApi_3_1_the_nullable_property_is_null_or_the_reference()
    {
        var properties = await PropertiesAsync(OpenApiSpecVersion.OpenApi3_1);

        properties["maybe"]!.ToJsonString().ShouldBe("""{"oneOf":[{"type":"null"},{"$ref":"#/components/schemas/Spot"}]}""");
    }
}
