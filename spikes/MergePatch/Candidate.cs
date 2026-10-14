using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MergePatch;

public enum PatchState { Absent, Null, Value }

/// <summary>Shape A: a converter factory that closes a generic converter with MakeGenericType, and a static reference from the struct to that converter so the instantiation exists under Native AOT.</summary>
#if FACTORY_STATIC
[JsonConverter(typeof(PatchAStaticFieldFactory))]
#elif FACTORY_REGISTRY
[JsonConverter(typeof(PatchARegistryFactory))]
#else
[JsonConverter(typeof(PatchAJsonConverterFactory))]
#endif
public readonly record struct PatchA<T>
{
    // The instantiation PatchAJsonConverter<T> is compiled whenever PatchA<T> is (claim 3). Under
    // Native AOT that is not enough for MakeGenericType + Activator: the constructor has no
    // reflection invoker ("missing native code or metadata", measured). The static field, read by
    // reflection, and the registry the static constructor fills are the two reflection-light ways
    // from a Type to the instance.
    internal static readonly PatchAJsonConverter<T> Converter = new();

    static PatchA() => PatchARegistryFactory.Register(typeof(PatchA<T>), Converter);

    private readonly T?        _value;
    private readonly PatchState _state;

    private PatchA(T? value, PatchState state) { _value = value; _state = state; }

    public static PatchA<T> Absent() => default;
    public static PatchA<T> Null() => new(default, PatchState.Null);
    public static PatchA<T> Of(T value) => new(value, PatchState.Value);

    public PatchState State => _state;

    public TResult Match<TResult>(Func<T, TResult> onValue, Func<TResult> onNull, Func<TResult> onAbsent) =>
        _state switch { PatchState.Value => onValue(_value!), PatchState.Null => onNull(), _ => onAbsent() };

    public bool TryGetValue([NotNullWhen(true)] out T? value) { value = _value; return _state == PatchState.Value; }

    public override string ToString() => $"PatchA {{ State = {_state} }}";
}

public sealed class PatchAJsonConverter<T> : JsonConverter<PatchA<T>>
{
    public override bool HandleNull => true;

    public override PatchA<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return PatchA<T>.Null();

        var value = JsonSerializer.Deserialize(ref reader, ContractOf(options));
        return value is null ? PatchA<T>.Null() : PatchA<T>.Of(value);
    }

    public override void Write(Utf8JsonWriter writer, PatchA<T> value, JsonSerializerOptions options)
    {
        switch (value.State)
        {
            case PatchState.Absent:
                throw new NotSupportedException("An absent patch cannot be written. Mark the property [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)], so an absent patch is left out of the document.");
            case PatchState.Null:
                writer.WriteNullValue();
                break;
            default:
                value.TryGetValue(out var inner);
                JsonSerializer.Serialize(writer, inner!, ContractOf(options));
                break;
        }
    }

    /// <summary>
    /// T's contract: the options' own where they have one; else made as the generated converters make a
    /// wrapped type's (GeneratedJson.TypeInfo): a registered converter, the type's own attribute (a value
    /// object), or the serializer's built-in converter for a primitive. A class T that a source-generated
    /// context never saw, because the patch's converter hides it, is refused with the serializer's own
    /// message naming the context (claim 4).
    /// </summary>
    private static JsonTypeInfo<T> ContractOf(JsonSerializerOptions options) =>
        options.TryGetTypeInfo(typeof(T), out var info) ? (JsonTypeInfo<T>)info : GeneratedJson.TypeInfo<T>(options, BuiltIn());

    private static JsonConverter<T>? BuiltIn() => (JsonConverter<T>?)(object?)(
        typeof(T) == typeof(string) ? JsonMetadataServices.StringConverter
      : typeof(T) == typeof(int) ? JsonMetadataServices.Int32Converter
      : typeof(T) == typeof(long) ? JsonMetadataServices.Int64Converter
      : typeof(T) == typeof(bool) ? JsonMetadataServices.BooleanConverter
      : typeof(T) == typeof(decimal) ? JsonMetadataServices.DecimalConverter
      : typeof(T) == typeof(double) ? JsonMetadataServices.DoubleConverter
      : typeof(T) == typeof(Guid) ? JsonMetadataServices.GuidConverter
      : typeof(T) == typeof(DateTime) ? JsonMetadataServices.DateTimeConverter
      : typeof(T) == typeof(DateTimeOffset) ? JsonMetadataServices.DateTimeOffsetConverter
      : typeof(T) == typeof(DateOnly) ? JsonMetadataServices.DateOnlyConverter
      : typeof(T) == typeof(TimeOnly) ? JsonMetadataServices.TimeOnlyConverter
      : (JsonConverter?)null);
}

public sealed class PatchAJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PatchA<>);

    // The instantiation exists: PatchA<T> holds a static PatchAJsonConverter<T>, compiled with it. Measured in this spike under Native AOT.
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "PatchA<T>.Converter keeps PatchAJsonConverter<T> compiled for every T a consumer uses; measured in spikes/MergePatch under Native AOT.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "Same: the closed converter type is reached from PatchA<T>, never only here.")]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var argument = typeToConvert.GetGenericArguments()[0];
        var closed   = typeof(PatchAJsonConverter<>).MakeGenericType(argument);
        return (JsonConverter)Activator.CreateInstance(closed)!;
    }
}

/// <summary>Shape A, variant: no MakeGenericType at all. The factory reads the static converter PatchA&lt;T&gt; holds, through the field the struct keeps for every T it is used with.</summary>
public sealed class PatchAStaticFieldFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PatchA<>);

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Measured: the field is kept with PatchA<T> under Native AOT, or it is not, which this spike shows.")]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)typeToConvert.GetField("Converter", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}

/// <summary>Shape A, variant: a registry the struct's static constructor fills; the factory runs that constructor for the closed type and looks the converter up. No reflection over members at all.</summary>
public sealed class PatchARegistryFactory : JsonConverterFactory
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, JsonConverter> Registry = new();

    public static void Register(Type patch, JsonConverter converter) => Registry[patch] = converter;

    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PatchA<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeToConvert.TypeHandle);
        return Registry.TryGetValue(typeToConvert, out var converter)
            ? converter
            : throw new NotSupportedException($"{typeToConvert} registered no converter: its static constructor did not run.");
    }
}

#if OPEN_GENERIC
/// <summary>Shape B: an open generic converter type named by the attribute, closed by the serializer if it supports that.</summary>
[JsonConverter(typeof(PatchBJsonConverter<>))]
public readonly record struct PatchB<T>
{
    private readonly T? _value;
    private readonly PatchState _state;
    private PatchB(T? value, PatchState state) { _value = value; _state = state; }
    public static PatchB<T> Null() => new(default, PatchState.Null);
    public static PatchB<T> Of(T value) => new(value, PatchState.Value);
    public PatchState State => _state;
}

public sealed class PatchBJsonConverter<T> : JsonConverter<PatchB<T>>
{
    public override bool HandleNull => true;
    public override PatchB<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? PatchB<T>.Null() : PatchB<T>.Of(JsonSerializer.Deserialize(ref reader, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))!);
    public override void Write(Utf8JsonWriter writer, PatchB<T> value, JsonSerializerOptions options) => throw new NotSupportedException();
}
#endif

/// <summary>
/// The candidate schema transformer. A patch property is described as its argument's schema, inlined
/// (the reference-id hook below returns null for a patch), never required, and admitting null, since
/// the Null state is part of the wire format whatever the argument: whether the application accepts
/// a clear is its own rule, not the document's.
/// </summary>
public sealed class PatchSchemaTransformer : IOpenApiSchemaTransformer
{
    public static bool IsPatch(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PatchA<>);

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var info = context.JsonTypeInfo;

        // The object holding patch properties: none of them is required.
        if (info.Kind == JsonTypeInfoKind.Object && schema.Required is { Count: > 0 } required)
        {
            foreach (var property in info.Properties)
                if (IsPatch(property.PropertyType)) required.Remove(property.Name);
        }

        if (!IsPatch(info.Type)) return;

        var argument = info.Type.GetGenericArguments()[0];

        // A value object that appears only inside a patch has no metadata in a source-generated
        // context (the patch's converter hides it), so ASP.NET cannot make its schema there (measured
        // under Native AOT). Its wrapped type's schema is what the value-object transformer would have
        // put in anyway: GeneratedValueObjectAttribute<TValueObject, T> names it, as the satellites read it.
        var wrapped = argument.GetCustomAttributes(inherit: false)
                              .Select(attribute => attribute.GetType())
                              .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CodoMetis.TypeKit.CompilerServices.GeneratedValueObjectAttribute<,>))
                              ?.GetGenericArguments()[1];
        var inner    = await context.GetOrCreateSchemaAsync(wrapped ?? argument, parameterDescription: null, cancellationToken);

        schema.Properties?.Clear();
        schema.Type = (inner.Type ?? JsonSchemaType.Null) | JsonSchemaType.Null;
        schema.Format = inner.Format;
        schema.Pattern = inner.Pattern;
        schema.Items = inner.Items;
        schema.AdditionalProperties = inner.AdditionalProperties;
        schema.Description = $"A JSON Merge Patch field: absent leaves the value as it is, null clears it, a value sets it.{(inner.Description is null ? "" : " " + inner.Description)}";
    }
}
