using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-001: under OpenAPI 2.0, ASP.NET writes a nullable request body as
/// <c>allOf: [{x-nullable: true}]</c>, without the <c>pattern</c> the same type's non-nullable body has, and without
/// its <c>type</c>, in MVC and in minimal APIs alike.
/// </summary>
public sealed class KB001
{
    private const string Int32Pattern = @"""pattern"":""^-?(?:0|[1-9]\\d*)$""";

    [ApiController]
    [Route("kb001")]
    public sealed class BodyController : ControllerBase
    {
        [HttpPost("plain")]
        public IActionResult Plain([FromBody] int value) => Ok();

        [HttpPost("nullable")]
        public IActionResult Nullable([FromBody] int? value) => Ok();
    }

    private static async Task<JsonNode> BodyAsync(OpenApiSpecVersion version, string path)
    {
        var document  = await FactHost.DocumentAsync(version, [typeof(BodyController)]);
        var operation = document["paths"]![$"/kb001/{path}"]!["post"]!;

        return version == OpenApiSpecVersion.OpenApi2_0
            ? operation["parameters"]!.AsArray().Single(parameter => (string?)parameter!["in"] == "body")!
            : operation["requestBody"]!["content"]!["application/json"]!;
    }

    [Fact]
    public async Task Under_OpenApi_2_0_a_nullable_int_body_is_allOf_x_nullable_without_its_pattern()
    {
        var plain    = await BodyAsync(OpenApiSpecVersion.OpenApi2_0, "plain");
        var nullable = await BodyAsync(OpenApiSpecVersion.OpenApi2_0, "nullable");

        plain.ToJsonString().ShouldBe("""{"in":"body","name":"body","required":true,"schema":{"format":"int32",""" + Int32Pattern + "}}");
        nullable.ToJsonString().ShouldBe("""{"in":"body","name":"body","schema":{"format":"int32","allOf":[{"x-nullable":true}]}}""", "optional, and no pattern");
    }

    public enum Shade { Light, Dark }

    [Fact]
    public async Task Under_OpenApi_2_0_a_minimal_API_nullable_enum_body_is_allOf_x_nullable_alone()
    {
        var document = await FactHost.DocumentAsync(OpenApiSpecVersion.OpenApi2_0, [],
            minimal: app => app.MapPost("/kb001/minimal", ([FromBody] Shade? value) => Results.Ok()));
        var body = document["paths"]!["/kb001/minimal"]!["post"]!["parameters"]!.AsArray().Single(parameter => (string?)parameter!["in"] == "body")!;

        body.ToJsonString().ShouldBe("""{"in":"body","name":"body","schema":{"allOf":[{"x-nullable":true}]}}""", "no type, nothing but the null");
    }

    [Fact]
    public async Task Under_OpenApi_3_1_the_nullable_body_keeps_its_pattern_inside_oneOf()
    {
        var nullable = await BodyAsync(OpenApiSpecVersion.OpenApi3_1, "nullable");

        nullable["schema"]!.ToJsonString().ShouldBe("""{"oneOf":[{"type":"null"},{""" + Int32Pattern + ""","type":["null","integer","string"],"format":"int32"}]}""");
    }
}
