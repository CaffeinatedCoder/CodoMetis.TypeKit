using System.Text;
using FaultSetComposition.Shared;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace FaultSetComposition;

/// <summary>What AddFaultResponses changes in a host's own error responses, with its own middleware.</summary>
public static class SideEffect
{
    public static async Task RunAsync()
    {
        foreach (var faults in new[] { false, true })
        {
            Console.WriteLine(faults ? "  -- after services.AddFaultResponses<ConcurrencyFault>():" : "  -- without AddFaultResponses:");
            var builder = Builder(faults);
            var app = Build(builder, exceptionHandler: true);
            try
            {
                await app.StartAsync();
            }
            catch (InvalidOperationException refused)
            {
                Console.WriteLine($"  UseExceptionHandler() refused at start: {refused.Message[..72]}…");
                app = Build(Builder(faults), exceptionHandler: false);
                await app.StartAsync();
            }
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            foreach (var path in new[] { "/other/bad-request", "/other/throws", "/nowhere" })
            {
                using var response = await client.GetAsync(path);
                Console.WriteLine($"  GET {path,-20} {(int)response.StatusCode} {response.Content.Headers.ContentType} {await response.Content.ReadAsStringAsync()}");
            }
            await app.StopAsync();
        }
    }

    private static WebApplicationBuilder Builder(bool faults)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (faults) builder.Services.AddFaultResponses<ConcurrencyFault>();
        return builder;
    }

    private static WebApplication Build(WebApplicationBuilder builder, bool exceptionHandler)
    {
        var app = builder.Build();
        if (exceptionHandler) app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapGet("/other/bad-request", () => Results.BadRequest());
        app.MapGet("/other/throws", string () => throw new InvalidOperationException("boom"));
        return app;
    }
}
