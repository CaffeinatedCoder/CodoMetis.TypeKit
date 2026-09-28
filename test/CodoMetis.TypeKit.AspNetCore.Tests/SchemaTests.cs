using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A value object in a JSON position (property, body, element) publishes the schema of the type it
/// wraps, under a component of its own.
/// </summary>
public sealed class SchemaTests
{
    public static TheoryData<string, OpenApiSpecVersion> WrappedTypeFamilies()
    {
        var data = new TheoryData<string, OpenApiSpecVersion>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0 })
        foreach (var property in new[]
                 {
                     "id", "count", "amount", "flag", "timestamp", "date", "moment", "time", "uri", "weekday",
                     "label", "code", "percentage", "customerId", "nested",
                 })
            data.Add(property, version);

        return data;
    }

    [Theory]
    [MemberData(nameof(WrappedTypeFamilies))]
    public async Task A_value_object_publishes_the_schema_of_the_type_it_wraps(string property, OpenApiSpecVersion version)
    {
        var document = await ProbeHost.DefaultDocumentAsync(version);

        var valueObject = document.Property("ProbeDocument", property);
        var control = document.Property("ControlDocument", property);

        valueObject["$ref"].ShouldNotBeNull($"'{property}' should refer to the value object's own component, but was {valueObject.ToJsonString()}");
        document.Resolve(valueObject).ShouldDescribeTheSameAs(document.Resolve(control), property);
    }

    [Fact]
    public async Task A_value_object_keeps_a_component_named_after_it()
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        document.Property("ProbeDocument", "id")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProbeId");
        document.Component("ProbeId").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "string", ["format"] = "uuid" });
    }

    [Theory]
    [InlineData("maybeCount", "ProbeCount")]
    [InlineData("maybeLabel", "ProbeLabel")]
    public async Task An_optional_value_object_is_its_component_or_null(string property, string component)
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        var oneOf = document.Property("ProbeDocument", property)["oneOf"].ShouldNotBeNull().AsArray();

        oneOf.Select(s => s!["type"]?.GetValue<string>()).ShouldContain("null");
        oneOf.Select(s => s!["$ref"]?.GetValue<string>()).ShouldContain($"#/components/schemas/{component}");
    }

    [Theory]
    [InlineData("ids")]
    [InlineData("idArray")]
    [InlineData("readOnlyIds")]
    [InlineData("idSet")]
    public async Task A_collection_of_value_objects_has_their_component_as_its_items(string property)
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        var items = document.Property("ProbeDocument", property)["items"].ShouldNotBeNull($"'{property}' lost its items");

        items["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProbeId");
    }

    [Fact]
    public async Task A_dictionary_of_value_objects_has_their_component_as_its_values()
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        var values = document.Property("ProbeDocument", "counts")["additionalProperties"].ShouldNotBeNull("the dictionary lost its value schema");

        values["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProbeCount");
    }

    [Fact]
    public async Task Nested_containers_reach_the_value_object()
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        var listsByKey = document.Property("ProbeDocument", "nestedIds")["additionalProperties"].ShouldNotBeNull("the dictionary lost its value schema");
        var listOfLists = document.Property("ProbeDocument", "listOfLists")["items"].ShouldNotBeNull("the outer list lost its items");

        foreach (var inner in new[] { listsByKey, listOfLists })
            inner["items"].ShouldNotBeNull($"the inner list lost its items: {inner.ToJsonString()}")["$ref"]?.GetValue<string>()
                          .ShouldBe("#/components/schemas/ProbeId");
    }

    [Fact]
    public async Task A_collection_of_optional_value_objects_has_their_component_as_its_items()
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        var items = document.Property("ProbeDocument", "maybeCounts")["items"].ShouldNotBeNull("the list lost its items");

        items.ToJsonString().ShouldContain("#/components/schemas/ProbeCount");
    }

    [Fact]
    public async Task A_value_object_as_the_whole_request_or_response_body_is_its_component()
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        document.Response("/probes", "post")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProbeId");
        document.Response("/mvc/probes", "post")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProbeId");
        document.Request("/probes", "post")["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProbeDocument");
    }

    [Fact]
    public async Task A_quoted_number_the_schema_allows_is_read_by_the_generated_converter()
    {
        // Under the web defaults ASP.NET publishes a number as "integer | string" with a pattern; the
        // generated converter reads numbers through the same options, so the document tells the truth.
        await using var host = await ProbeHost.StartAsync(openApi => openApi.AddTypeKit());
        var document = await host.DocumentAsync();
        document.Component("ProbeCount")["type"].ShouldNotBeNull("the count has no type").AsArray()
                .Select(t => t!.GetValue<string>()).ShouldBe(["integer", "string"], ignoreOrder: true);

        var cancellation = TestContext.Current.CancellationToken;
        var accepted = await host.Client.PostAsync("/probes/numbers", Json("""{"count":"5","amount":"1.5","percentage":"7"}"""), cancellation);
        var refused = await host.Client.PostAsync("/probes/numbers", Json("""{"count":"five","amount":1.5,"percentage":7}"""), cancellation);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadAsStringAsync(cancellation)).ShouldBe("""{"count":5,"amount":1.5,"percentage":7}""");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");
}
