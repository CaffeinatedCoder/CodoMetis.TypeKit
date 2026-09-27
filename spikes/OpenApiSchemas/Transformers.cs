using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

static class ValueObjects
{
    public static Type? WrappedType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.GetInterfaces()
                   .FirstOrDefault(contract => contract.IsGenericType
                                            && contract.GetGenericTypeDefinition() == typeof(IValueObject<,>)
                                            && contract.GetGenericArguments()[0] == type)
                  ?.GetGenericArguments()[1];
    }
}

/// <summary>Records every schema it is called for, and stamps value objects, to see which stamps survive.</summary>
sealed class ObservingTransformer(List<string> visits) : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        var isValueObject = ValueObjects.WrappedType(type) is not null;
        visits.Add($"{(isValueObject ? "VO " : "   ")}{Name(type),-34} kind={context.JsonTypeInfo.Kind,-10} property={context.JsonPropertyInfo?.Name ?? "-",-13} " +
                   $"parameter={context.ParameterDescription?.Name ?? "-"}{(context.ParameterDescription is { } p ? $":{Name(p.Type)} metadata={(p.ModelMetadata is { } m ? Name(m.ModelType) : "-")} descriptor={(p.ParameterDescriptor is { } d ? Name(d.ParameterType) : "-")} source={p.Source?.Id}" : "")}");

        if (isValueObject) schema.Description = $"seen:{Name(type)}";

        return Task.CompletedTask;
    }

    public static string Name(Type type) =>
        type.IsGenericType ? $"{type.Name.Split('`')[0]}<{string.Join(",", type.GetGenericArguments().Select(Name))}>" : type.Name;
}

/// <summary>
/// The candidate: a value object's schema becomes its wrapped type's schema, obtained from ASP.NET
/// itself through <c>GetOrCreateSchemaAsync</c>, so it is whatever ASP.NET publishes for that type.
/// </summary>
sealed class CandidateTransformer(bool containers, bool parameters, bool passParameterDescription) : IOpenApiSchemaTransformer
{
    public static readonly List<string> Notes = [];
    private static readonly AsyncLocal<int> Depth = new();

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (Depth.Value > 3) { Notes.Add($"recursion stopped at {ObservingTransformer.Name(context.JsonTypeInfo.Type)}"); return; }
        Depth.Value++;
        try { await Transform(schema, context, cancellationToken); }
        finally { Depth.Value--; }
    }

    private async Task Transform(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var info = context.JsonTypeInfo;

        // A value object in a JSON position: property, element reached through GetOrCreateSchemaAsync, body, response.
        if (ValueObjects.WrappedType(info.Type) is { } wrapped)
        {
            CopyKeywords(await context.GetOrCreateSchemaAsync(wrapped, null, cancellationToken), schema);
            return;
        }

        // A parameter bound through TryParse: ASP.NET hands the transformer a string, and only the
        // parameter description still knows the value object.
        if (parameters
         && context.JsonPropertyInfo is null
         && context.ParameterDescription is { } parameter
         && (ValueObjects.WrappedType(parameter.Type) ?? (parameter.ModelMetadata is { } metadata ? ValueObjects.WrappedType(metadata.ModelType) : null)) is { } parameterWrapped
         && parameterWrapped != info.Type)
        {
            var description = passParameterDescription ? context.ParameterDescription : null;
            CopyKeywords(await context.GetOrCreateSchemaAsync(parameterWrapped, description, cancellationToken), schema);
            Notes.Add($"parameter {context.ParameterDescription.Name}: json type {ObservingTransformer.Name(info.Type)} -> {ObservingTransformer.Name(parameterWrapped)}");
            return;
        }

        if (!containers || info.ElementType is not { } element || ValueObjects.WrappedType(element) is null) return;

        // The element schema of a value-object container is dropped before any transformer sees it.
        // The value object's own schema is put back, so the element keeps its component.
        if (info.Kind == JsonTypeInfoKind.Enumerable && schema.Items is null)
            schema.Items = await context.GetOrCreateSchemaAsync(element, null, cancellationToken);

        if (info.Kind == JsonTypeInfoKind.Dictionary && schema.AdditionalProperties is null)
            schema.AdditionalProperties = await context.GetOrCreateSchemaAsync(element, null, cancellationToken);
    }

    /// <summary>
    /// Every settable property except ASP.NET's own bookkeeping (<c>Metadata</c> holds the schema's
    /// reference id; copying it made the value object's position a reference to the wrapped type).
    /// </summary>
    public static void CopyKeywords(OpenApiSchema source, OpenApiSchema target)
    {
        foreach (var property in typeof(OpenApiSchema).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || !property.CanRead || property.Name is "Metadata") continue;

            var value = property.GetValue(source);
            if (value is null || value is System.Collections.ICollection { Count: 0 }) continue;
            if (value is bool b && !b) continue;

            property.SetValue(target, value);
        }
    }
}

/// <summary>What a consumer would register for a wrapped type ASP.NET cannot describe on its own.</summary>
sealed class ConsumerInstantTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(NodaTime.Instant))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date-time";
        }

        return Task.CompletedTask;
    }
}
