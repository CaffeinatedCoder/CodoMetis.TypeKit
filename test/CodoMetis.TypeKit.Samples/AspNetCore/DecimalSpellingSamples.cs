using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples.Web.Decimals;

public readonly partial record struct Fare : IValue<decimal>;

public sealed record Ticket(Fare Fare, decimal Tax);

/// <summary>The decimal spelling is chosen on the JSON options, both sets where the host has controllers; the document follows.</summary>
public static class DecimalSpellingHost
{
    public static void Configure(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/decimal-spelling
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
        builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        // end sample
    }
}

public sealed class DecimalSpellingHostTests
{
    [Fact]
    public async Task The_document_describes_decimals_as_the_options_write_them()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        DecimalSpellingHost.Configure(builder);
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts => parts.ApplicationParts.Clear());   // the other samples' controllers stay out

        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapGet("/ticket", () => new Ticket(Fare.From(7.80m), 1.50m));
        await app.StartAsync(TestContext.Current.CancellationToken);

        var client = app.GetTestClient();
        (await client.GetStringAsync("/ticket", TestContext.Current.CancellationToken)).ShouldBe("""{"fare":"7.8","tax":"1.5"}""");

        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        document["components"]!["schemas"]!["Fare"]!["type"]!.GetValue<string>().ShouldBe("string", "a value object over a decimal, as the converter writes it");
        document["components"]!["schemas"]!["Ticket"]!["properties"]!["tax"]!["type"]!.GetValue<string>().ShouldBe("string");
    }
}
