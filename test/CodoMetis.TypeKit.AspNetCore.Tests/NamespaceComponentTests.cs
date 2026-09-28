using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.AspNetCore.Tests
{
    /// <summary>
    /// ASP.NET names a component after the type's simple name, and <c>AddTypeKit()</c> renames only a nested
    /// value object (decision 24), so two top-level value objects of one name in different namespaces share
    /// one component, as any two types of one name do. The README says so and names the way out.
    /// </summary>
    public sealed class NamespaceComponentTests
    {
        /// <summary>What the README describes; fails once ASP.NET or the package names them apart.</summary>
        [Fact]
        public async Task Value_objects_of_one_name_in_two_namespaces_share_a_component()
        {
            var document = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), endpoints: MapInvoices);

            document.Property("Invoices", "billed")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/InvoiceNo");
            document.Property("Invoices", "shipped")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/InvoiceNo");
        }

        /// <summary>The way out the README names: a name of the host's own, set before <c>AddTypeKit()</c>.</summary>
        [Fact]
        public async Task A_name_the_host_gives_one_of_them_separates_them()
        {
            var document = await ProbeHost.DocumentAsync(
                openApi =>
                {
                    openApi.CreateSchemaReferenceId = type =>
                        type.Type == typeof(Billing.InvoiceNo) ? "BillingInvoiceNo" : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
                    openApi.AddTypeKit();
                },
                endpoints: MapInvoices);

            document.Component("BillingInvoiceNo").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
            document.Component("InvoiceNo")["format"]!.GetValue<string>().ShouldBe("int32");
        }

        private static void MapInvoices(WebApplication app) => app.MapPost("/invoices", Invoices (Invoices invoices) => invoices);
    }

    public sealed record Invoices(Billing.InvoiceNo Billed, Shipping.InvoiceNo Shipped);
}

namespace CodoMetis.TypeKit.AspNetCore.Tests.Billing
{
    public readonly partial record struct InvoiceNo : IValue<Guid>;
}

namespace CodoMetis.TypeKit.AspNetCore.Tests.Shipping
{
    public readonly partial record struct InvoiceNo : IValue<int>;
}
