using System.Collections.Concurrent;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// A closed set in the document: a string enum of its members' names, as an <c>enum</c> is
/// described, read from the attribute the generators put on the set, never by reflection over
/// its members.
/// </summary>
internal sealed class ClosedSetSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly ConcurrentDictionary<Type, GeneratedClosedSetAttribute?> Descriptions = new();

    public static GeneratedClosedSetAttribute? Describe(Type type) =>
        Descriptions.GetOrAdd(type, static candidate =>
            candidate.GetCustomAttribute<GeneratedClosedSetAttribute>(inherit: false) is { } set && set.SetType == candidate ? set : null);

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (Describe(context.JsonTypeInfo.Type) is not { } set) return Task.CompletedTask;

        schema.Properties?.Clear();
        schema.Required?.Clear();
        schema.Type = JsonSchemaType.String;
        schema.Enum = [.. set.Names.Select(name => (System.Text.Json.Nodes.JsonNode)name)];

        return Task.CompletedTask;
    }
}
