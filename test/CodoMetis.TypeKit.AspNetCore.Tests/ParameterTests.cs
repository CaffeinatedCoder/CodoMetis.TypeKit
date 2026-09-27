using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A value object bound from the route, the query string or a header publishes exactly the schema a
/// parameter of the wrapped type publishes.
/// </summary>
/// <remarks>
/// ASP.NET hands the transformer such a parameter as <see cref="string"/>, since it binds through the
/// generated <c>TryParse</c>. Minimal APIs name the value object in the parameter's type, MVC in its
/// model metadata, and for an MVC <c>[FromQuery]</c> object the parameter's descriptor names the
/// container, so each shape has a case of its own.
/// </remarks>
public sealed class ParameterTests
{
    public static TheoryData<string, string, string, string, OpenApiSpecVersion> Parameters()
    {
        var data = new TheoryData<string, string, string, string, OpenApiSpecVersion>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0 })
        foreach (var (probe, control, name) in new[]
                 {
                     ("/probes/{id}", "/controls/{id}", "id"),                     // minimal API, route
                     ("/probes", "/controls", "since"),                            // minimal API, query
                     ("/probes", "/controls", "limit"),                            // minimal API, optional query
                     ("/probes", "/controls", "code"),                             // minimal API, optional query, validated, wraps a string
                     ("/probes/search", "/controls/search", "Customer"),           // [AsParameters]
                     ("/probes/search", "/controls/search", "Limit"),              // [AsParameters], optional
                     ("/probes/header", "/controls/header", "X-Probe"),            // header
                     ("/mvc/probes/{id}", "/mvc/controls/{id}", "id"),             // MVC, route
                     ("/mvc/probes/{id}", "/mvc/controls/{id}", "limit"),          // MVC, optional query
                     ("/mvc/probes/search", "/mvc/controls/search", "Customer"),   // MVC, [FromQuery] object, optional
                     ("/mvc/probes/search", "/mvc/controls/search", "Limit"),      // MVC, [FromQuery] object
                 })
            data.Add(probe, control, "get", name, version);

        return data;
    }

    [Theory]
    [MemberData(nameof(Parameters))]
    public async Task A_value_object_parameter_publishes_the_schema_of_a_wrapped_type_parameter(
        string probePath, string controlPath, string method, string name, OpenApiSpecVersion version)
    {
        var document = await ProbeHost.DefaultDocumentAsync(version);

        var valueObject = document.Parameter(probePath, method, name);
        var control = document.Parameter(controlPath, method, name);

        document.Resolve(valueObject).ShouldDescribeTheSameAs(document.Resolve(control), $"{method} {probePath} {name}");
    }

    [Theory]
    [InlineData("/probes")]
    [InlineData("/mvc/probes/by-ids")]
    public async Task A_query_array_of_value_objects_has_their_component_as_its_items(string path)
    {
        var document = await ProbeHost.DefaultDocumentAsync();

        var items = document.Parameter(path, "get", "ids")["items"].ShouldNotBeNull("the array lost its items");

        document.Resolve(items).ShouldDescribeTheSameAs(document.Component("ProbeId"), path);
    }
}
