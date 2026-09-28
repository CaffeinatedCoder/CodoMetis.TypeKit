using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Advising;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

[CompileTime]
internal sealed class JsonImplementationArguments
{
    public required INamedType ValueType       { get; init; }
    public required INamedType ValueObjectType { get; init; }
    public required IMethod    FromJson        { get; init; }

    /// <summary>A <see cref="DateTime"/>, which is written and read as UTC (<c>GeneratedJson.AsUtc</c>).</summary>
    public required bool IsDateTime { get; init; }

    /// <summary>
    /// C# for the converter the wrapped type is read and written with when the options have none of
    /// their own: the serializer's built-in one (<c>JsonMetadataServices.Int32Converter</c>,
    /// <c>JsonMetadataServices.GetEnumConverter&lt;T&gt;(options)</c>), NodaTime's, or <c>null</c> where there is none.
    /// </summary>
    public required string BuiltInConverter { get; init; }
}

/// <summary>
/// A nested <c>JsonConverter&lt;TSelf&gt;</c> that reads and writes the wrapped value, also as a
/// dictionary key, and <c>[JsonConverter]</c> on the type.
/// </summary>
/// <remarks>
/// <para>
/// Every read goes through <see cref="ValueObjectAspectState.FromJson"/>, so a validated value
/// object applies <c>Create</c> and a refusal is a <c>JsonException</c>. A JSON <c>null</c> is a
/// <c>JsonException</c> too, rather than an instance wrapping <c>null</c>. The one exception is the
/// materializing mode for stored JSON, which only <c>StoredJsonConverterFactory</c> can create.
/// Malformed input is a <c>JsonException</c> as well, naming the value object and the wrapped type and
/// never the input.
/// </para>
/// <para>
/// The wrapped value is written and read by the converter the serializer would use for it under the
/// caller's options, values and dictionary keys alike (<c>GeneratedJson</c>): a converter the options
/// have for the type, else its built-in one. So a value object writes exactly the bytes the serializer
/// writes for what it wraps, which a value object is documented to do.
/// </para>
/// </remarks>
internal sealed partial class ValueObjectJsonAspect : TypeAspect
{
    /// <summary>
    /// The NodaTime types with a parameterless NodaConverters property. ZonedDateTime and
    /// DateTimeZone are absent: they need an IDateTimeZoneProvider and cannot be resolved statically.
    /// </summary>
    private static readonly (string TypeName, string ConverterProperty)[] NodaConverterMap =
    [
        ("NodaTime.Instant", "InstantConverter"),
        ("NodaTime.LocalDate", "LocalDateConverter"),
        ("NodaTime.LocalTime", "LocalTimeConverter"),
        ("NodaTime.LocalDateTime", "LocalDateTimeConverter"),
        ("NodaTime.OffsetDateTime", "OffsetDateTimeConverter"),
        ("NodaTime.OffsetDate", "OffsetDateConverter"),
        ("NodaTime.OffsetTime", "OffsetTimeConverter"),
        ("NodaTime.Offset", "OffsetConverter"),
        ("NodaTime.Duration", "DurationConverter"),
        ("NodaTime.Period", "RoundtripPeriodConverter"),
        ("NodaTime.AnnualDate", "AnnualDateConverter"),
    ];

    private const string NodaConverters = "NodaTime.Serialization.SystemTextJson.NodaConverters";

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType = state.ValueType.GetTarget();

        builder.Tags = new JsonImplementationArguments
        {
            ValueType        = valueType,
            ValueObjectType  = builder.Target,
            FromJson         = state.FromJson.GetTarget(),
            IsDateTime       = valueType.Equals(typeof(DateTime)),
            BuiltInConverter = ResolveBuiltInConverter(valueType)
        };

        var jsonConverter = builder.IntroduceClass(
            $"{builder.Target.Name}JsonConverter",
            buildType: type =>
            {
                type.Accessibility = Accessibility.Public;
                type.BaseType      = typeof(JsonConverter<>).ToNamedType().MakeGenericInstance(builder.Target);
                type.IsPartial     = true;
                type.IsStatic      = false;
                type.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // Two modes, one code path: the public constructor validates through Create, and the
        // private one, reachable only through StoredJsonConverterFactory, reads JSON the
        // application stored itself without it, in exactly the format the converter writes.
        jsonConverter.IntroduceField(nameof(_materialize), IntroductionScope.Instance, OverrideStrategy.Fail,
            field => field.AddAttribute(CodeAnnotations.CompilerGenerated));

        // The plan for the options this converter last saw (GeneratedJsonPlan), in a field of its own:
        // an instance field costs nothing to reach, where a static one per wrapped type went through
        // the shared generic code for a string on every call, and the built-in converter is evaluated
        // only when a plan is made.
        jsonConverter.IntroduceField(
            JsonPlanField,
            typeof(GeneratedJsonPlan<>).ToNamedType().MakeGenericInstance(valueType).ToNullable(),
            IntroductionScope.Instance,
            OverrideStrategy.Fail,
            field =>
            {
                field.Accessibility = Accessibility.Private;
                field.AddAttribute(CodeAnnotations.CompilerGenerated);
            });
        jsonConverter.IntroduceConstructor(nameof(ValidatingConstructor),
            buildConstructor: constructor => constructor.Accessibility = Accessibility.Public);
        var materializing = jsonConverter.IntroduceConstructor(nameof(MaterializingConstructor),
            buildConstructor: constructor => constructor.Accessibility = Accessibility.Private);

        // How StoredJsonConverterFactory reaches the private constructor: through an interface, not
        // by reflection, which a trimmed or Native AOT application may no longer find (it did not).
        jsonConverter.ImplementInterface(typeof(IStoredJsonConverterSource), OverrideStrategy.Fail).ExplicitMembers.IntroduceMethod(
            nameof(CreateStoredJsonConverterTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Fail,
            method =>
            {
                method.Name = nameof(IStoredJsonConverterSource.CreateStoredJsonConverter);
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { materializing = materializing.Declaration }
        );

        IntroduceWriteMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.Write),               asPropertyName: false);
        IntroduceWriteMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.WriteAsPropertyName), asPropertyName: true);
        IntroduceReadMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.Read),               asPropertyName: false);
        IntroduceReadMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.ReadAsPropertyName), asPropertyName: true);

        builder.IntroduceAttribute(AttributeConstruction.Create(typeof(JsonConverterAttribute), constructorArguments: [jsonConverter.Target]));
        builder.IntroduceAttribute(CodeAnnotations.CompilerGenerated);
    }

    /// <summary>
    /// NodaTime is resolved from the consumer's compilation, never referenced here: a compile-time
    /// dependency would force NodaTime on every consumer. The types are compared by symbol once
    /// found, and its converter applies only when NodaTime.Serialization.SystemTextJson is there too,
    /// since the generated code names it. Otherwise the type is written through the options, which
    /// works when the consumer configured NodaTime there.
    /// </summary>
    private static bool TryGetNodaConverterProperty(INamedType valueType, out string? converterProperty)
    {
        converterProperty = null;

        if (!TypeFactory.TryGetType(NodaConverters, out _)) return false;

        foreach (var (typeName, property) in NodaConverterMap)
        {
            if (TypeFactory.TryGetType(typeName, out var nodaType) && valueType.Equals(nodaType))
            {
                converterProperty = property;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The converter the wrapped type falls back to where the options have none: for an enum the
    /// serializer's (<c>JsonMetadataServices.GetEnumConverter</c>); for a NodaTime type NodaTime's own;
    /// otherwise the static property of <c>JsonMetadataServices</c> typed <c>JsonConverter&lt;T&gt;</c>
    /// for exactly that type, found by its type rather than its name. <c>null</c> for any other type,
    /// which the options must then know (<c>GeneratedJson.TypeInfo</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is only the fallback. A converter the options have for the type comes first, values and keys
    /// alike, so the value object writes exactly the bytes the serializer writes for what it wraps.
    /// </para>
    /// <para>
    /// The expression is evaluated only when the converter makes a plan for new options. Passed on
    /// every call, <c>GetEnumConverter</c> built a new converter each time: 4.7 KB per enum value and key.
    /// </para>
    /// </remarks>
    private static string ResolveBuiltInConverter(INamedType valueType)
    {
        const string metadataServices = "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices";

        if (valueType.TypeKind == TypeKind.Enum)
            return $"{metadataServices}.GetEnumConverter<{ValueObjectTypes.SourceName(valueType)}>(options)";

        if (TryGetNodaConverterProperty(valueType, out var nodaConverterProperty))
            return $"global::{NodaConverters}.{nodaConverterProperty}";

        var converterType = typeof(JsonConverter<>).ToNamedType().MakeGenericInstance(valueType);
        var property = TypeFactory.GetNamedType(typeof(JsonMetadataServices)).Properties
                                  .FirstOrDefault(candidate => candidate is { IsStatic: true, Accessibility: Accessibility.Public } && candidate.Type.Equals(converterType));

        return property is null ? "null" : $"{metadataServices}.{property.Name}";
    }

    /// <summary>The nested converter's field for its <c>GeneratedJsonPlan</c>.</summary>
    private const string JsonPlanField = "__jsonPlan";

    private static void IntroduceWriteMethod(IAdviser<INamedType> converter, INamedType valueObjectType, string methodName, bool asPropertyName) =>
        converter.IntroduceMethod(
            nameof(JsonConverterWriteTemplate),
            whenExists: OverrideStrategy.Override,
            buildMethod: method =>
            {
                method.Name               = methodName;
                method.Parameters[1].Type = valueObjectType;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { asPropertyName }
        );

    private static void IntroduceReadMethod(IAdviser<INamedType> converter, INamedType valueObjectType, string methodName, bool asPropertyName) =>
        converter.IntroduceMethod(
            nameof(JsonConverterReadTemplate),
            whenExists: OverrideStrategy.Override,
            buildMethod: method =>
            {
                method.Name       = methodName;
                method.ReturnType = valueObjectType;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { asPropertyName }
        );
}
