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
    private static readonly FactFixture Fixture = FactFixture.Beside("KB-001");

    public static TheoryData<string> Answers() => [.. Fixture.Answers.Select(answer => answer.Id)];

    [ApiController]
    [Route("kb001")]
    public sealed class BodyController : ControllerBase
    {
        [HttpPost("plain")]
        public IActionResult Plain([FromBody] int value) => Ok();

        [HttpPost("nullable")]
        public IActionResult Nullable([FromBody] int? value) => Ok();
    }

    public enum Shade { Light, Dark }

    /// <summary>
    /// The document an answer was measured in: the MVC host's, with a plain and a nullable <c>int</c> body, or the minimal
    /// API's, with a nullable enum body, under the answer's OpenAPI version.
    /// </summary>
    private static Task<JsonNode> DocumentAsync(FactAnswer answer)
    {
        var version = Enum.Parse<OpenApiSpecVersion>(answer.When["openapi.OpenApiVersion"]);
        return answer.When["http.host"] == "minimal"
            ? FactHost.DocumentAsync(version, [], minimal: app => app.MapPost("/kb001/minimal", ([FromBody] Shade? value) => Results.Ok()))
            : FactHost.DocumentAsync(version, [typeof(BodyController)]);
    }

    /// <summary>
    /// Each answer of KB001.fixture.json: under OpenAPI 2.0 a nullable body is <c>allOf: [{x-nullable: true}]</c>, without
    /// the pattern and type its non-nullable form has, in MVC and in minimal APIs alike; under 3.1 it keeps its pattern.
    /// </summary>
    [Theory, MemberData(nameof(Answers))]
    public async Task The_framework_writes_the_body_the_fixture_holds(string id)
    {
        var answer = Fixture[id];

        answer.Mismatch(await DocumentAsync(answer)).ShouldBeNull();
    }
}
