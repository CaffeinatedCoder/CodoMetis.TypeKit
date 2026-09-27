using System.Text.Json;
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

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateTimeValue))
        {
            if (asPropertyName)
            {
                var expr = ExpressionFactory.Parse(
                    "value!.Value.ToUniversalTime().ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture)",
                    TypeFactory.GetType(SpecialType.String),
                    false
                );
                writer.WritePropertyName((string)expr.Value!);
            }
            else
            {
                var expr = ExpressionFactory.Parse("value!.Value.ToUniversalTime()", TypeFactory.GetNamedType(typeof(DateTime)), false);
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
                JsonSerializer.Serialize(writer, value?.Value, options);
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

        // Fallback: an unknown type round-trips through JsonSerializer and the caller's options. As a
        // key it goes through the wrapped type's own converter, which knows the type's key format
        // where it has one (an enum by name, a Uri as its text) and throws NotSupportedException
        // where it has none. Writing the serialized value as the name gave a Uri key quotes inside
        // its quotes and an enum key its number, and neither read back.
        if (asPropertyName)
        {
            string fallbackType = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));

            meta.InsertStatement(ExpressionFactory.Parse(
                $"((global::System.Text.Json.Serialization.JsonConverter<{fallbackType}>)options.GetConverter(typeof({fallbackType}))).WriteAsPropertyName(writer, value!.Value, options)"));
        }
        else
        {
            JsonSerializer.Serialize(writer, value?.Value, options);
        }
    }

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
            {
                var parsed = ExpressionFactory.Parse("global::System.Guid.Parse(reader.GetString()!)", TypeFactory.GetNamedType(typeof(Guid)), false);
                return tag.FromJson.Invoke(parsed.Value!, meta.This._materialize);
            }

            var guidExpr = ExpressionFactory.Parse("reader.GetGuid()", TypeFactory.GetNamedType(typeof(Guid)), false);
            return tag.FromJson.Invoke(guidExpr.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateTimeValue))
        {
            if (asPropertyName)
            {
                var parsed = ExpressionFactory.Parse(
                    "global::System.DateTime.ParseExact(reader.GetString()!, \"O\", global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind)",
                    TypeFactory.GetNamedType(typeof(DateTime)),
                    false
                );
                return tag.FromJson.Invoke(parsed.Value!, meta.This._materialize);
            }

            var dateTimeExpr = ExpressionFactory.Parse("reader.GetDateTime().ToUniversalTime()", TypeFactory.GetNamedType(typeof(DateTime)), false);
            return tag.FromJson.Invoke(dateTimeExpr.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateOnlyValue))
        {
            var parsed = ExpressionFactory.Parse(
                "global::System.DateOnly.ParseExact(reader.GetString()!, \"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture)",
                TypeFactory.GetNamedType(typeof(DateOnly)),
                false
            );
            return tag.FromJson.Invoke(parsed.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateTimeOffsetValue))
        {
            if (asPropertyName)
            {
                var parsed = ExpressionFactory.Parse(
                    "global::System.DateTimeOffset.ParseExact(reader.GetString()!, \"O\", global::System.Globalization.CultureInfo.InvariantCulture)",
                    TypeFactory.GetNamedType(typeof(DateTimeOffset)),
                    false
                );
                return tag.FromJson.Invoke(parsed.Value!, meta.This._materialize);
            }

            var offsetExpr = ExpressionFactory.Parse("reader.GetDateTimeOffset()", TypeFactory.GetNamedType(typeof(DateTimeOffset)), false);
            return tag.FromJson.Invoke(offsetExpr.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.TimeOnlyValue))
        {
            var parsed = ExpressionFactory.Parse(
                "global::System.TimeOnly.Parse(reader.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture)",
                TypeFactory.GetNamedType(typeof(TimeOnly)),
                false
            );
            return tag.FromJson.Invoke(parsed.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.BooleanValue))
        {
            if (asPropertyName)
            {
                var parsed = ExpressionFactory.Parse("bool.Parse(reader.GetString()!)", TypeFactory.GetNamedType(typeof(bool)), false);
                return tag.FromJson.Invoke(parsed.Value!, meta.This._materialize);
            }

            var boolExpr = ExpressionFactory.Parse("reader.GetBoolean()", TypeFactory.GetNamedType(typeof(bool)), false);
            return tag.FromJson.Invoke(boolExpr.Value!, meta.This._materialize);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.NumericInvariant))
        {
            if (asPropertyName)
            {
                var parsed = ExpressionFactory.Parse(
                    $"{typeName}.Parse(reader.GetString()!, global::System.Globalization.NumberStyles.Any, global::System.Globalization.CultureInfo.InvariantCulture)",
                    tag.ValueType,
                    false
                );
                return tag.FromJson.Invoke(parsed.Value!, meta.This._materialize);
            }

            var deserialized = ExpressionFactory.Parse(
                $"global::System.Text.Json.JsonSerializer.Deserialize(ref reader, (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{typeName}>)options.GetTypeInfo(typeof({typeName})))",
                tag.ValueType,
                false
            );
            return tag.FromJson.Invoke(deserialized.Value!, meta.This._materialize);
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

        // Fallback: an unknown type round-trips through JsonSerializer and the caller's options. A
        // key is read by the wrapped type's own converter, the counterpart of the write.
        if (asPropertyName)
        {
            var key = ExpressionFactory.Parse(
                $"((global::System.Text.Json.Serialization.JsonConverter<{typeName}>)options.GetConverter(typeof({typeName}))).ReadAsPropertyName(ref reader, typeof({typeName}), options)",
                tag.ValueType,
                false
            );
            return tag.FromJson.Invoke(key.Value!, meta.This._materialize);
        }

        var fallback = ExpressionFactory.Parse($"global::System.Text.Json.JsonSerializer.Deserialize<{typeName}>(ref reader, options)!", tag.ValueType, false);
        return tag.FromJson.Invoke(fallback.Value!, meta.This._materialize);
    }
}
