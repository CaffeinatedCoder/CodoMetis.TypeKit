using System.Text.Json.Serialization;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

[CompileTime]
internal enum ValueJsonStrategy
{
    StringValue,
    GuidValue,
    DateTimeValue,
    DateOnlyValue,
    DateTimeOffsetValue,
    TimeOnlyValue,
    BooleanValue,
    NumericInvariant,
    NodaTimeValue, // delegate to a named NodaConverters property
    Fallback
}

[CompileTime]
internal sealed class JsonImplementationArguments
{
    public required INamedType        ValueType       { get; init; }
    public required INamedType        ValueObjectType { get; init; }
    public required IMethod           FromJson        { get; init; }
    public required ValueJsonStrategy Strategy        { get; init; }

    /// <summary>Only for <see cref="ValueJsonStrategy.NodaTimeValue"/>: the NodaConverters property, e.g. <c>InstantConverter</c>.</summary>
    public string? NodaConverterProperty { get; init; }
}

/// <summary>
/// A nested <c>JsonConverter&lt;TSelf&gt;</c> that reads and writes the wrapped value, also as a
/// dictionary key, and <c>[JsonConverter]</c> on the type.
/// </summary>
/// <remarks>
/// Every read goes through <see cref="ValueObjectAspectState.FromJson"/>, so a validated value
/// object applies <c>Create</c> and a refusal is a <c>JsonException</c>. A JSON <c>null</c> is a
/// <c>JsonException</c> too, rather than an instance wrapping <c>null</c>.
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
            ValueType             = valueType,
            ValueObjectType       = builder.Target,
            FromJson              = state.FromJson.GetTarget(),
            Strategy              = ResolveStrategy(valueType, out var nodaConverterProperty),
            NodaConverterProperty = nodaConverterProperty
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

        IntroduceWriteMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.Write),               asPropertyName: false);
        IntroduceWriteMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.WriteAsPropertyName), asPropertyName: true);
        IntroduceReadMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.Read),               asPropertyName: false);
        IntroduceReadMethod(jsonConverter, builder.Target, nameof(JsonConverter<>.ReadAsPropertyName), asPropertyName: true);

        builder.IntroduceAttribute(AttributeConstruction.Create(typeof(JsonConverterAttribute), constructorArguments: [jsonConverter.Target]));
        builder.IntroduceAttribute(CodeAnnotations.CompilerGenerated);
    }

    private static ValueJsonStrategy ResolveStrategy(INamedType valueType, out string? nodaConverterProperty)
    {
        nodaConverterProperty = null;

        if (valueType.SpecialType == SpecialType.String) return ValueJsonStrategy.StringValue;
        if (valueType.Equals(typeof(Guid))) return ValueJsonStrategy.GuidValue;
        if (valueType.Equals(typeof(DateTime))) return ValueJsonStrategy.DateTimeValue;
        if (valueType.Equals(typeof(DateOnly))) return ValueJsonStrategy.DateOnlyValue;
        if (valueType.Equals(typeof(DateTimeOffset))) return ValueJsonStrategy.DateTimeOffsetValue;
        if (valueType.Equals(typeof(TimeOnly))) return ValueJsonStrategy.TimeOnlyValue;
        if (valueType.SpecialType == SpecialType.Boolean) return ValueJsonStrategy.BooleanValue;
        if (IsNumericInvariantType(valueType)) return ValueJsonStrategy.NumericInvariant;

        return TryGetNodaConverterProperty(valueType, out nodaConverterProperty)
                   ? ValueJsonStrategy.NodaTimeValue
                   : ValueJsonStrategy.Fallback;
    }

    private static bool IsNumericInvariantType(INamedType type) =>
        type.SpecialType is SpecialType.Byte or SpecialType.SByte
                         or SpecialType.Int16 or SpecialType.UInt16
                         or SpecialType.Int32 or SpecialType.UInt32
                         or SpecialType.Int64 or SpecialType.UInt64
                         or SpecialType.Single or SpecialType.Double
                         or SpecialType.Decimal
     || type.Equals(typeof(Half))
     || type.Equals(typeof(Int128))
     || type.Equals(typeof(UInt128));

    /// <summary>
    /// NodaTime is resolved from the consumer's compilation, never referenced here: a compile-time
    /// dependency would force NodaTime on every consumer. The types are compared by symbol once
    /// found, and the strategy applies only when NodaTime.Serialization.SystemTextJson is there too,
    /// since the generated code calls its converters. Otherwise the fallback serializes through the
    /// options, which works when the consumer configured NodaTime there.
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
