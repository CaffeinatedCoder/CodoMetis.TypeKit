using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectImplementationAspect
{
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    // Metalama 2027.0 requires [Durable] (Metalama.Framework.Utilities) on this placeholder, and
    // 2026.1 does not have the attribute. It is never assigned, so the upgrade adds it back as a
    // one-line change.
    [Template] private readonly dynamic? _value;
#pragma warning restore CS0649

    [Template]
    public void PrivateConstructor(dynamic? value, [CompileTime] IField valueField)
    {
        valueField.Value = value;
    }

    /// <summary>No validation, by contract: see <c>IValueObjectMaterializer</c>.</summary>
    [Template]
    private static dynamic? MaterializeTemplate(dynamic? value, [CompileTime] IConstructor constructor) => constructor.Invoke(value);

    /// <summary>
    /// The JSON converter's way in. A refusal throws <c>JsonException</c>, which a JSON read reports.
    /// <c>materialize</c> is set only by a converter <c>StoredJsonConverterFactory</c> created, for
    /// JSON the application stored itself.
    /// </summary>
    [Template]
    private static dynamic FromJsonTemplate(
        dynamic?                      value,
        bool                          materialize,
        [CompileTime] IConstructor    constructor,
        [CompileTime] INamedType      target,
        [CompileTime] ValueObjectKind kind
    )
    {
        if (meta.CompileTime(kind == ValueObjectKind.SimpleValue))
            return constructor.Invoke(value)!;

        if (materialize)
            return constructor.Invoke(value)!;

        return ExpressionFactory.Parse(
            $"global::CodoMetis.TypeKit.ValueObjects.Accepted.OrJsonException({ValueObjectTypes.SourceName(target)}.Create(value))"
        ).Value!;
    }

    /// <summary>Parsing's and the type converter's way in. A refusal throws <c>FormatException</c>, as <c>Parse</c> does.</summary>
    [Template]
    private static dynamic FromTextTemplate(
        dynamic?                      value,
        [CompileTime] IConstructor    constructor,
        [CompileTime] INamedType      target,
        [CompileTime] ValueObjectKind kind
    )
    {
        if (meta.CompileTime(kind == ValueObjectKind.SimpleValue))
            return constructor.Invoke(value)!;

        return ExpressionFactory.Parse(
            $"global::CodoMetis.TypeKit.ValueObjects.Accepted.OrFormatException({ValueObjectTypes.SourceName(target)}.Create(value))"
        ).Value!;
    }

    /// <summary><c>TryParse</c>'s way in. A refusal returns <see langword="false"/>, as <c>TryParse</c> does.</summary>
    [Template]
    private static bool TryFromTextTemplate(
        dynamic?                      value,
        out dynamic?                  result,
        [CompileTime] IConstructor    constructor,
        [CompileTime] INamedType      target,
        [CompileTime] ValueObjectKind kind
    )
    {
        if (meta.CompileTime(kind == ValueObjectKind.SimpleValue))
        {
            result = constructor.Invoke(value);
            return true;
        }

        result = meta.Default(target);

        return (bool)ExpressionFactory.Parse(
            $"{ValueObjectTypes.SourceName(target)}.Create(value).TryGetValue(out result, out _)",
            TypeFactory.GetType(SpecialType.Boolean),
            false
        ).Value!;
    }
}
