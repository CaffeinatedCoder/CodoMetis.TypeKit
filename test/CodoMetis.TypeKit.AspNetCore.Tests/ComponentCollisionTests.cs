using CodoMetis.TypeKit.AspNetCore.Tests.Collisions;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.AspNetCore.Tests
{
    /// <summary>
    /// A nested value object or closed set is named after its nesting chain (decision 24), so
    /// <c>Order.Status</c> is <c>OrderStatus</c>, which a top-level <c>OrderStatus</c> is too. One
    /// component then described both, as whichever ASP.NET met first, and nothing said so: the
    /// document is refused, naming both types and the way out.
    /// </summary>
    public sealed class ComponentCollisionTests
    {
        [Fact]
        public async Task A_nested_closed_set_and_a_top_level_one_of_its_chains_name_are_refused()
        {
            var refusal = await Should.ThrowAsync<InvalidOperationException>(() => ProbeHost.DocumentAsync(
                openApi => openApi.AddTypeKit(),
                endpoints: app => app.MapPost("/orders", int (OrderStatuses statuses) => 0)));

            refusal.Message.ShouldContain("'OrderStatus'");
            refusal.Message.ShouldContain("CodoMetis.TypeKit.AspNetCore.Tests.Collisions.Order.Status");
            refusal.Message.ShouldContain("CodoMetis.TypeKit.AspNetCore.Tests.Collisions.OrderStatus");
            refusal.Message.ShouldContain("CreateSchemaReferenceId");
        }

        /// <summary>The companion classes of these two do not collide, being in different namespaces, so nothing refused them at compile time.</summary>
        [Fact]
        public async Task A_nested_value_object_and_a_top_level_one_of_its_chains_name_are_refused()
        {
            var refusal = await Should.ThrowAsync<InvalidOperationException>(() => ProbeHost.DocumentAsync(
                openApi => openApi.AddTypeKit(),
                endpoints: app => app.MapPost("/carts", int (Carts carts) => 0)));

            refusal.Message.ShouldContain("'CartId'");
            refusal.Message.ShouldContain("CodoMetis.TypeKit.AspNetCore.Tests.Collisions.Cart.Id");
            refusal.Message.ShouldContain("CodoMetis.TypeKit.AspNetCore.Tests.Collisions.Elsewhere.CartId");
        }

        /// <summary>The way out the refusal names: a name of the host's own, set before <c>AddTypeKit()</c>.</summary>
        [Fact]
        public async Task A_name_the_host_gives_one_of_them_separates_them()
        {
            var document = await ProbeHost.DocumentAsync(
                openApi =>
                {
                    openApi.CreateSchemaReferenceId = type =>
                        type.Type == typeof(OrderStatus) ? "PlainOrderStatus" : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
                    openApi.AddTypeKit();
                },
                endpoints: app => app.MapPost("/orders", int (OrderStatuses statuses) => 0));

            Names(document.Resolve(document.Property(nameof(OrderStatuses), "nested"))).ShouldBe(["Draft", "Placed"]);
            Names(document.Resolve(document.Property(nameof(OrderStatuses), "topLevel"))).ShouldBe(["Open", "Shipped"]);
        }

        /// <summary>
        /// Two documents of one host are two namespaces of components: a name one claims is free in the
        /// other, and a document generated again is not refused for what it claimed the first time.
        /// </summary>
        [Fact]
        public async Task Each_document_has_its_own_names()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddOpenApi("nested", openApi => openApi.AddTypeKit());
            builder.Services.AddOpenApi("top", openApi => openApi.AddTypeKit());

            await using var app = builder.Build();
            app.MapOpenApi();
            app.MapPost("/nested", int (NestedOnly body) => 0).WithGroupName("nested");
            app.MapPost("/top", int (TopLevelOnly body) => 0).WithGroupName("top");
            await app.StartAsync(TestContext.Current.CancellationToken);
            var client = app.GetTestClient();

            var documents = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                client.GetStringAsync(i % 2 == 0 ? "/openapi/nested.json" : "/openapi/top.json", TestContext.Current.CancellationToken)));

            var nested = new OpenApiJson(JsonNode.Parse(documents[0])!);
            var top = new OpenApiJson(JsonNode.Parse(documents[1])!);
            Names(nested.Component("OrderStatus")).ShouldBe(["Draft", "Placed"]);
            Names(top.Component("OrderStatus")).ShouldBe(["Open", "Shipped"]);
        }

        private static string[] Names(JsonNode schema) =>
            [.. schema["enum"]!.AsArray().Select(name => name!.GetValue<string>())];
    }
}

namespace CodoMetis.TypeKit.AspNetCore.Tests.Collisions
{
    public static class Order
    {
        public sealed partial class Status : IClosedSet<Status>
        {
            public static readonly Status Draft = new();
            public static readonly Status Placed = new();
        }
    }

    public sealed partial class OrderStatus : IClosedSet<OrderStatus>
    {
        public static readonly OrderStatus Open = new();
        public static readonly OrderStatus Shipped = new();
    }

    public sealed record OrderStatuses(Order.Status Nested, OrderStatus TopLevel);

    public sealed record NestedOnly(Order.Status Status);

    public sealed record TopLevelOnly(OrderStatus Status);

    public static partial class Cart
    {
        public readonly partial record struct Id : IValue<Guid>;
    }

    public sealed record Carts(Cart.Id Nested, Elsewhere.CartId TopLevel);
}

namespace CodoMetis.TypeKit.AspNetCore.Tests.Collisions.Elsewhere
{
    public readonly partial record struct CartId : IValue<int>;
}
