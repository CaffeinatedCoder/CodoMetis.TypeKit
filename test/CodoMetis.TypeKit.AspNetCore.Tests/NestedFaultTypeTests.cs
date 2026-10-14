using CodoMetis.TypeKit.AspNetCore.Tests.NestedFaults;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.AspNetCore.Tests
{
    /// <summary>
    /// Two fault enums of one simple name, <c>Payments.Fault</c> and <c>Parcels.Fault</c>: a qualified
    /// fault name followed the simple name, so both sent <c>Fault.NotFound</c>, and the startup check
    /// merged both into one message about a type <c>Fault</c>. An enum's qualified name follows its
    /// nesting chain, as a closed set's <c>QualifiedName</c> does.
    /// </summary>
    public sealed class NestedFaultTypeTests
    {
        [Fact]
        public async Task A_nested_enums_qualified_name_follows_its_nesting_chain()
        {
            await using var app = await StartAsync(services =>
            {
                Map(services);
                services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);
            });

            (await FaultAsync(app, "/payments")).ShouldBe("Payments.Fault.NotFound");
            (await FaultAsync(app, "/parcels")).ShouldBe("Parcels.Fault.NotFound");
        }

        /// <summary>The one rule for both kinds of fault type: the satellite's chain for a nested closed set is its generated <c>QualifiedName</c>.</summary>
        [Fact]
        public void An_enum_is_qualified_as_a_closed_set_is()
        {
            FaultTypes.Set<ProbeOrder.Status>().QualifiedNameOf(ProbeOrder.Status.Open).ShouldBe(ProbeOrder.Status.Open.QualifiedName);
            TypeNames.NestingChain(typeof(ProbeOrder.Status), ".").ShouldBe("ProbeOrder.Status");
            FaultTypes.Enum<Payments.Fault>().QualifiedNameOf(Payments.Fault.NotFound).ShouldBe("Payments.Fault.NotFound");
            FaultTypes.Enum<Outer<int>.Fault>().QualifiedNameOf(Outer<int>.Fault.Gone).ShouldBe("Outer.Fault.Gone");
        }

        [Fact]
        public async Task Two_unmapped_fault_types_of_one_simple_name_are_two_refusals()
        {
            // Another type's mapping brings the startup check; neither of these two is mapped.
            var refusal = await Should.ThrowAsync<InvalidOperationException>(() =>
                StartAsync(services => services.AddFaultResponses<Unrelated>(_ => StatusCodes.Status410Gone)));

            refusal.Message.ShouldContain("No HTTP status is mapped for the fault type Payments.Fault, which these endpoints answer with: HTTP: GET /payments.");
            refusal.Message.ShouldContain("No HTTP status is mapped for the fault type Parcels.Fault, which these endpoints answer with: HTTP: GET /parcels.");
            refusal.Message.ShouldContain("services.AddFaultResponses<Payments.Fault>(fault => …)");

            // One chain in two namespaces: two types, so two refusals, each naming its own endpoints.
            refusal.Message.ShouldContain("No HTTP status is mapped for the fault type Payments.Fault, which these endpoints answer with: HTTP: GET /elsewhere/payments.");
        }

        private static Result<int, Payments.Fault> Bill() => Result.Error(Payments.Fault.NotFound);

        private static Result<int, Parcels.Fault> Ship() => Result.Error(Parcels.Fault.NotFound);

        private static Result<int, NestedFaults.Elsewhere.Payments.Fault> PayElsewhere() => Result.Error(NestedFaults.Elsewhere.Payments.Fault.NotFound);

        private static void Map(IServiceCollection services)
        {
            services.AddFaultResponses<Payments.Fault>(_ => StatusCodes.Status404NotFound);
            services.AddFaultResponses<Parcels.Fault>(_ => StatusCodes.Status404NotFound);
            services.AddFaultResponses<NestedFaults.Elsewhere.Payments.Fault>(_ => StatusCodes.Status404NotFound);
        }

        private static async Task<string> FaultAsync(WebApplication app, string path)
        {
            using var response = await app.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
            return problem["fault"]!.GetValue<string>();
        }

        private static async Task<WebApplication> StartAsync(Action<IServiceCollection> services)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            services(builder.Services);

            var app = builder.Build();
            app.MapGet("/payments", () => Bill().ToHttpResult());
            app.MapGet("/parcels", () => Ship().ToHttpResult());
            app.MapGet("/elsewhere/payments", () => PayElsewhere().ToHttpResult());

            try
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            return app;
        }
    }
}

namespace CodoMetis.TypeKit.AspNetCore.Tests.NestedFaults
{
    public static class Payments
    {
        public enum Fault { NotFound }
    }

    public static class Parcels
    {
        public enum Fault { NotFound }
    }

    public enum Unrelated { Gone }

    public static class Outer<T>
    {
        public enum Fault { Gone }
    }
}

namespace CodoMetis.TypeKit.AspNetCore.Tests.NestedFaults.Elsewhere
{
    public static class Payments
    {
        public enum Fault { NotFound }
    }
}
