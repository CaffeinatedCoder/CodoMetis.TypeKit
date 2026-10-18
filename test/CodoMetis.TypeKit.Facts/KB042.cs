using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-042: under <c>JsonSerializerOptions.NumberHandling</c> with <c>WriteAsString</c>, ASP.NET refuses the OpenAPI document of
/// an endpoint whose schema holds a <c>char</c>, with a <c>JsonException</c> reading the schema back as its <c>OpenApiJsonSchema</c>: the
/// exporter's <c>minLength</c> and <c>maxLength</c> of a <c>char</c> are written as strings. A number's schema is written, and so is a
/// <c>char</c>'s under the default number handling.
/// </summary>
public sealed class KB042
{
    public sealed class Holder<T>
    {
        public T P { get; set; } = default!;
    }

    private static readonly FactFixture Fixture = FactFixture.Beside("KB-042");

    public static TheoryData<string> Answers() => [.. Fixture.Answers.Select(answer => answer.Id)];

    /// <summary>The document of a minimal API whose one endpoint takes a body of <c>Holder&lt;T&gt;</c> for the answer's type.</summary>
    private static async Task<JsonNode> DocumentAsync(IReadOnlyDictionary<string, string> when)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            if (when["json.NumberHandling"] == "WriteAsString") options.SerializerOptions.NumberHandling = JsonNumberHandling.WriteAsString;
        });
        builder.Services.AddOpenApi();

        await using var app = builder.Build();
        _ = when["shape.wrapped"] switch
        {
            "char" => app.MapPost("/kb042", ([FromBody] Holder<char> body) => Results.Ok()),
            "int" => app.MapPost("/kb042", ([FromBody] Holder<int> body) => Results.Ok()),
            var other => throw new ArgumentOutOfRangeException(nameof(when), other, "the fixture's types are char and int"),
        };
        app.MapOpenApi();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var text = await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        return JsonNode.Parse(text)!;
    }

    /// <summary>Each answer of KB042.fixture.json: the document of a body holding the answer's type, under the number handling it names.</summary>
    [Theory, MemberData(nameof(Answers))]
    public async Task ASP_NET_writes_or_refuses_the_document_as_the_fixture_holds(string id)
    {
        var answer = Fixture[id];

        if (answer.Kind == FactAnswer.Exception)
        {
            var thrown = await Should.ThrowAsync<Exception>(() => DocumentAsync(answer.When));
            answer.Mismatch(thrown).ShouldBeNull();
        }
        else
        {
            answer.Mismatch(await DocumentAsync(answer.When)).ShouldBeNull();
        }
    }
}
