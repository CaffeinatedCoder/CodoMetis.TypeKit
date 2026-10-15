using System.Reflection;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-023: a JSON converter of one's own cannot say what it writes. System.Text.Json's hook for
/// a converter's schema is internal, so its schema exporter and ASP.NET's OpenAPI document describe a property
/// written by such a converter without what it writes.
/// </summary>
public sealed class KB023
{
    public sealed record Price([property: JsonConverter(typeof(DecimalStringConverter))] decimal Amount, decimal Plain);

    [ApiController]
    [Route("kb023")]
    public sealed class PriceController : ControllerBase
    {
        [HttpGet]
        public Price Get() => new(1.2m, 1.2m);
    }

    [Fact]
    public void The_converter_schema_hook_is_not_public()
    {
        typeof(JsonConverter).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(method => method.Name)
            .ShouldNotContain("GetSchema");
        typeof(JsonConverter).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Select(method => method.Name)
            .ShouldContain("GetSchema", "the hook exists, internal");
    }

    [Fact]
    public void The_schema_exporter_describes_the_converter_s_property_as_the_empty_schema()
    {
        var properties = JsonSerializerOptions.Web.GetJsonSchemaAsNode(typeof(Price))["properties"]!;

        properties["amount"]!.ToJsonString().ShouldBe("true", "anything: the string the converter writes is not said");
        properties["plain"]!.ToJsonString().ShouldContain("number", customMessage: "the same type without the converter is described");
    }

    [Fact]
    public async Task The_OpenAPI_document_describes_the_converter_s_property_by_its_CLR_type_s_format_alone()
    {
        var document = await FactHost.DocumentAsync(OpenApiSpecVersion.OpenApi3_1, [typeof(PriceController)]);

        document["components"]!["schemas"]!["Price"]!["properties"]!["amount"]!.ToJsonString().ShouldBe("""{"format":"double"}""", "no type, and not the string it writes");
    }
}
