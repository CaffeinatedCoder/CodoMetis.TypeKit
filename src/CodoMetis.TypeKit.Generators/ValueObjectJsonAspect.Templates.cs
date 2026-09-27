using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectJsonAspect
{
#pragma warning disable CS0649 // Field is never assigned to: it is, by the introduced constructor.
    /// <summary>Set only through the private constructor, which <c>StoredJsonConverterFactory</c> calls.</summary>
    [Template] private readonly bool _materialize;
#pragma warning restore CS0649

    /// <summary>What <c>[JsonConverter]</c> instantiates: the validating converter.</summary>
    [Template]
    public void ValidatingConstructor()
    {
    }

    [Template]
    public void MaterializingConstructor(bool materialize)
    {
        meta.This._materialize = materialize;
    }

    /// <summary>
    /// <c>IStoredJsonConverterSource</c>: the materializing twin, for <c>StoredJsonConverterFactory</c>,
    /// which reaches it through this interface rather than by reflecting over the private constructor.
    /// </summary>
    [Template]
    public JsonConverter CreateStoredJsonConverterTemplate(StoredJsonConverterFactory factory, [CompileTime] IConstructor materializing)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return materializing.Invoke(true)!;
    }

    [Template]
    public void JsonConverterWriteTemplate(
        Utf8JsonWriter        writer,
        dynamic?              value,
        JsonSerializerOptions options,
        [CompileTime] bool    asPropertyName
    )
    {
        var tag      = (JsonImplementationArguments)meta.Tags.Source!;
        var strategy = meta.CompileTime(tag.Strategy);

        if (meta.CompileTime(strategy == ValueJsonStrategy.StringValue))
        {
            if (asPropertyName)
                writer.WritePropertyName(value!.Value);
            else
                writer.WriteStringValue(value!.Value);
            return;
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.GuidValue))
        {
            if (asPropertyName)
                writer.WritePropertyName(value!.Value.ToString());
            else
                writer.WriteStringValue(value!.Value);
            return;
        }

        // Always UTC, through GeneratedJson.AsUtc: an Unspecified value is taken as UTC rather than
        // as server-local time, which ToUniversalTime() assumed.
        if (meta.CompileTime(strategy == ValueJsonStrategy.DateTimeValue))
        {
            if (asPropertyName)
            {
                var expr = ExpressionFactory.Parse(
                    $"{GeneratedJson}.AsUtc(value!.Value).ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture)",
                    TypeFactory.GetType(SpecialType.String),
                    false
                );
                writer.WritePropertyName((string)expr.Value!);
            }
            else
            {
                var expr = ExpressionFactory.Parse($"{GeneratedJson}.AsUtc(value!.Value)", TypeFactory.GetNamedType(typeof(DateTime)), false);
                writer.WriteStringValue((DateTime)expr.Value!);
            }

            return;
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateOnlyValue))
        {
            var formatted = ExpressionFactory.Parse(
                "value!.Value.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture)",
                TypeFactory.GetType(SpecialType.String),
                false
            );
            if (asPropertyName)
                writer.WritePropertyName((string)formatted.Value!);
            else
                writer.WriteStringValue((string)formatted.Value!);
            return;
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateTimeOffsetValue))
        {
            if (asPropertyName)
            {
                var expr = ExpressionFactory.Parse(
                    "value!.Value.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture)",
                    TypeFactory.GetType(SpecialType.String),
                    false
                );
                writer.WritePropertyName((string)expr.Value!);
            }
            else
            {
                var expr = ExpressionFactory.Parse("value!.Value", TypeFactory.GetNamedType(typeof(DateTimeOffset)), false);
                writer.WriteStringValue((DateTimeOffset)expr.Value!);
            }

            return;
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.TimeOnlyValue))
        {
            var formatted = ExpressionFactory.Parse(
                "value!.Value.ToString(\"o\", global::System.Globalization.CultureInfo.InvariantCulture)",
                TypeFactory.GetType(SpecialType.String),
                false
            );
            if (asPropertyName)
                writer.WritePropertyName((string)formatted.Value!);
            else
                writer.WriteStringValue((string)formatted.Value!);
            return;
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.BooleanValue))
        {
            if (asPropertyName)
                writer.WritePropertyName(value!.Value.ToString());
            else
                writer.WriteBooleanValue(value!.Value);
            return;
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.NumericInvariant))
        {
            if (asPropertyName)
            {
                var formatted = ExpressionFactory.Parse(
                    "value!.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture)",
                    TypeFactory.GetType(SpecialType.String),
                    false
                );
                writer.WritePropertyName((string)formatted.Value!);
            }
            else
            {
                meta.InsertStatement(ExpressionFactory.Parse(WrappedWrite(tag)));
            }

            return;
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.NodaTimeValue))
        {
            // The NodaConverters property was resolved at compile time. ExpressionFactory.Parse
            // because NodaConverters lives in an assembly this package does not reference.
            string converterExpr = meta.CompileTime($"global::NodaTime.Serialization.SystemTextJson.NodaConverters.{tag.NodaConverterProperty}");

            // NodaTime's converters handle dictionary keys themselves, with the same pattern as the
            // value. No one format string fits every NodaTime type ("g" is invalid for LocalDate).
            if (asPropertyName)
                meta.InsertStatement(ExpressionFactory.Parse($"{converterExpr}.WriteAsPropertyName(writer, value!.Value, options)"));
            else
                meta.InsertStatement(ExpressionFactory.Parse($"{converterExpr}.Write(writer, value!.Value, options)"));

            return;
        }

        // Fallback: an unknown type round-trips through its contract under the caller's options. As
        // a key it goes through the wrapped type's own converter, which knows the type's key format
        // where it has one (an enum by name, a Uri as its text) and throws NotSupportedException
        // where it has none. Writing the serialized value as the name gave a Uri key quotes inside
        // its quotes and an enum key its number, and neither read back.
        if (asPropertyName)
            meta.InsertStatement(ExpressionFactory.Parse($"{WrappedKeyConverter(tag)}.WriteAsPropertyName(writer, value!.Value, options)"));
        else
            meta.InsertStatement(ExpressionFactory.Parse(WrappedWrite(tag)));
    }

    private const string GeneratedJson = "global::CodoMetis.TypeKit.ValueObjects.GeneratedJson";

    /// <summary>
    /// The wrapped type's own built-in converter (<c>JsonMetadataServices.Int32Converter</c> and so
    /// on), which parses exactly what the serializer parses for that type and reports malformed text
    /// as a <c>JsonException</c>. A <c>Parse</c> in the generated code let a
    /// <c>FormatException</c> escape, which ASP.NET Core answers with 500 rather than 400, and parsed
    /// keys by rules of their own.
    /// </summary>
    private static string BuiltInConverter(JsonImplementationArguments tag) => tag.BuiltInConverter;

    /// <summary>
    /// C# for the wrapped type's contract under the caller's options (<c>GeneratedJson.TypeInfo</c>):
    /// the options' own where their resolver has one, otherwise made as the serializer makes it. Never
    /// <c>JsonSerializer.Serialize(writer, value, options)</c>, which needs reflection and, with a
    /// source-generated context that never saw the wrapped type, refused it.
    /// </summary>
    private static string WrappedTypeInfo(JsonImplementationArguments tag) =>
        $"{GeneratedJson}.TypeInfo<{ValueObjectTypes.SourceName(tag.ValueType)}>(options, {tag.BuiltInConverter})";

    private static string WrappedKeyConverter(JsonImplementationArguments tag) =>
        $"{GeneratedJson}.KeyConverter<{ValueObjectTypes.SourceName(tag.ValueType)}>(options, {tag.BuiltInConverter})";

    private static string WrappedWrite(JsonImplementationArguments tag) =>
        $"global::System.Text.Json.JsonSerializer.Serialize(writer, value!.Value, {WrappedTypeInfo(tag)})";

    private static IExpression WrappedReadExpression(JsonImplementationArguments tag) =>
        ExpressionFactory.Parse($"global::System.Text.Json.JsonSerializer.Deserialize(ref reader, {WrappedTypeInfo(tag)})!", tag.ValueType, false);

    /// <summary>C# that reads the wrapped value as a dictionary key or as a value, through <see cref="BuiltInConverter"/>.</summary>
    private static string BuiltInRead(JsonImplementationArguments tag, bool asPropertyName) =>
        $"{BuiltInConverter(tag)}.{(asPropertyName ? "ReadAsPropertyName" : "Read")}(ref reader, typeof({ValueObjectTypes.SourceName(tag.ValueType)}), options)";

    private static IExpression BuiltInReadExpression(JsonImplementationArguments tag, bool asPropertyName) =>
        ExpressionFactory.Parse(BuiltInRead(tag, asPropertyName), tag.ValueType, false);

    [Template]
    public dynamic? JsonConverterReadTemplate(
        ref Utf8JsonReader    reader,
        Type                  typeToConvert,
        JsonSerializerOptions options,
        [CompileTime] bool    asPropertyName
    )
    {
        var tag      = (JsonImplementationArguments)meta.Tags.Source!;
        var strategy = meta.CompileTime(tag.Strategy);
        var typeName = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));

        // A JSON null is not a value object. Without this, a string-backed one would wrap null.
        // A property name is never null, so only the value path needs it.
        if (meta.CompileTime(!asPropertyName))
        {
            if (reader.TokenType == JsonTokenType.Null)
                throw new JsonException($"{meta.CompileTime(tag.ValueObjectType.Name)} cannot be read from a JSON null.");
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.StringValue))
            return tag.FromJson.Invoke(reader.GetString(), meta.This._materialize);

        if (meta.CompileTime(strategy == ValueJsonStrategy.GuidValue))
        {
            if (asPropertyName)
                return tag.FromJson.Invoke(BuiltInReadExpression(tag, asPropertyName: true).Value!, meta.This._materialize);

            var guidExpr = ExpressionFactory.Parse("reader.GetGuid()", TypeFactory.GetNamedType(typeof(Guid)), false);
            return tag.FromJson.Invoke(guidExpr.Value!, meta.This._materialize);
        }

        // A key and a value are the same instant: both are normalised by GeneratedJson.AsUtc.
        if (meta.CompileTime(strategy == ValueJsonStrategy.DateTimeValue))
        {
            string read = meta.CompileTime(asPropertyName ? BuiltInRead(tag, asPropertyName: true) : "reader.GetDateTime()");

            var dateTimeExpr = ExpressionFactory.Parse($"{GeneratedJson}.AsUtc({read})", TypeFactory.GetNamedType(typeof(DateTime)), false);
            return tag.FromJson.Invoke(dateTimeExpr.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateOnlyValue))
            return tag.FromJson.Invoke(BuiltInReadExpression(tag, asPropertyName).Value!, meta.This._materialize);

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateTimeOffsetValue))
        {
            if (asPropertyName)
                return tag.FromJson.Invoke(BuiltInReadExpression(tag, asPropertyName: true).Value!, meta.This._materialize);

            var offsetExpr = ExpressionFactory.Parse("reader.GetDateTimeOffset()", TypeFactory.GetNamedType(typeof(DateTimeOffset)), false);
            return tag.FromJson.Invoke(offsetExpr.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.TimeOnlyValue))
            return tag.FromJson.Invoke(BuiltInReadExpression(tag, asPropertyName).Value!, meta.This._materialize);

        if (meta.CompileTime(strategy == ValueJsonStrategy.BooleanValue))
        {
            if (asPropertyName)
                return tag.FromJson.Invoke(BuiltInReadExpression(tag, asPropertyName: true).Value!, meta.This._materialize);

            var boolExpr = ExpressionFactory.Parse("reader.GetBoolean()", TypeFactory.GetNamedType(typeof(bool)), false);
            return tag.FromJson.Invoke(boolExpr.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.NumericInvariant))
        {
            if (asPropertyName)
                return tag.FromJson.Invoke(BuiltInReadExpression(tag, asPropertyName: true).Value!, meta.This._materialize);

            return tag.FromJson.Invoke(WrappedReadExpression(tag).Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.NodaTimeValue))
        {
            string converterExpr = meta.CompileTime($"global::NodaTime.Serialization.SystemTextJson.NodaConverters.{tag.NodaConverterProperty}");

            if (asPropertyName)
            {
                var key = ExpressionFactory.Parse($"{converterExpr}.ReadAsPropertyName(ref reader, typeof({typeName}), options)", tag.ValueType, false);
                return tag.FromJson.Invoke(key.Value!, meta.This._materialize);
            }

            var nodaValue = ExpressionFactory.Parse($"{converterExpr}.Read(ref reader, typeof({typeName}), options)", tag.ValueType, false);
            return tag.FromJson.Invoke(nodaValue.Value!, meta.This._materialize);
        }

        // Fallback: an unknown type round-trips through its contract under the caller's options. A
        // key is read by the wrapped type's own converter, the counterpart of the write.
        if (asPropertyName)
        {
            var key = ExpressionFactory.Parse(
                $"{WrappedKeyConverter(tag)}.ReadAsPropertyName(ref reader, typeof({typeName}), options)",
                tag.ValueType,
                false
            );
            return tag.FromJson.Invoke(key.Value!, meta.This._materialize);
        }

        return tag.FromJson.Invoke(WrappedReadExpression(tag).Value!, meta.This._materialize);
    }
}
