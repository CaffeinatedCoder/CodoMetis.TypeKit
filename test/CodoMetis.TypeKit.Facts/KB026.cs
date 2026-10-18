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

    private static readonly FactFixture Fixture = FactFixture.Beside("KB-026");

    public static TheoryData<string> Answers() => [.. Fixture.Answers.Select(answer => answer.Id)];

    /// <summary>
    /// Each answer of KB026.fixture.json: under OpenAPI 2.0 the nullable property is <c>allOf: [{x-nullable: true}]</c>
    /// where its non-nullable twin is the reference; under 3.1 it is null or the reference.
    /// </summary>
    [Theory, MemberData(nameof(Answers))]
    public async Task The_framework_writes_the_property_the_fixture_holds(string id)
    {
        var answer   = Fixture[id];
        var document = await FactHost.DocumentAsync(Enum.Parse<OpenApiSpecVersion>(answer.When["openapi.OpenApiVersion"]), [typeof(HolderController)]);

        answer.Mismatch(document).ShouldBeNull();
    }
}
