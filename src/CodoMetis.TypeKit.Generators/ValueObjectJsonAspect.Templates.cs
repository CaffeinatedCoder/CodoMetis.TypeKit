using System.Text.Json;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectJsonAspect
{
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

        // Fallback: an unknown type round-trips through JsonSerializer and the caller's options.
        if (asPropertyName)
            writer.WritePropertyName(JsonSerializer.Serialize(value?.Value, options));
        else
            JsonSerializer.Serialize(writer, value?.Value, options);
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
            return tag.FromJson.Invoke(reader.GetString());

        if (meta.CompileTime(strategy == ValueJsonStrategy.GuidValue))
        {
            if (asPropertyName)
            {
                var parsed = ExpressionFactory.Parse("global::System.Guid.Parse(reader.GetString()!)", TypeFactory.GetNamedType(typeof(Guid)), false);
                return tag.FromJson.Invoke(parsed.Value!);
            }

            var guidExpr = ExpressionFactory.Parse("reader.GetGuid()", TypeFactory.GetNamedType(typeof(Guid)), false);
            return tag.FromJson.Invoke(guidExpr.Value!);
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
                return tag.FromJson.Invoke(parsed.Value!);
            }

            var dateTimeExpr = ExpressionFactory.Parse("reader.GetDateTime().ToUniversalTime()", TypeFactory.GetNamedType(typeof(DateTime)), false);
            return tag.FromJson.Invoke(dateTimeExpr.Value!);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.DateOnlyValue))
        {
            var parsed = ExpressionFactory.Parse(
                "global::System.DateOnly.ParseExact(reader.GetString()!, \"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture)",
                TypeFactory.GetNamedType(typeof(DateOnly)),
                false
            );
            return tag.FromJson.Invoke(parsed.Value!);
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
                return tag.FromJson.Invoke(parsed.Value!);
            }

            var offsetExpr = ExpressionFactory.Parse("reader.GetDateTimeOffset()", TypeFactory.GetNamedType(typeof(DateTimeOffset)), false);
            return tag.FromJson.Invoke(offsetExpr.Value!);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.TimeOnlyValue))
        {
            var parsed = ExpressionFactory.Parse(
                "global::System.TimeOnly.Parse(reader.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture)",
                TypeFactory.GetNamedType(typeof(TimeOnly)),
                false
            );
            return tag.FromJson.Invoke(parsed.Value!);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.BooleanValue))
        {
            if (asPropertyName)
            {
                var parsed = ExpressionFactory.Parse("bool.Parse(reader.GetString()!)", TypeFactory.GetNamedType(typeof(bool)), false);
                return tag.FromJson.Invoke(parsed.Value!);
            }

            var boolExpr = ExpressionFactory.Parse("reader.GetBoolean()", TypeFactory.GetNamedType(typeof(bool)), false);
            return tag.FromJson.Invoke(boolExpr.Value!);
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
                return tag.FromJson.Invoke(parsed.Value!);
            }

            var deserialized = ExpressionFactory.Parse(
                $"global::System.Text.Json.JsonSerializer.Deserialize(ref reader, (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{typeName}>)options.GetTypeInfo(typeof({typeName})))",
                tag.ValueType,
                false
            );
            return tag.FromJson.Invoke(deserialized.Value!);
        }

        if (meta.CompileTime(strategy == ValueJsonStrategy.NodaTimeValue))
        {
            string converterExpr = meta.CompileTime($"global::NodaTime.Serialization.SystemTextJson.NodaConverters.{tag.NodaConverterProperty}");

            if (asPropertyName)
            {
                var key = ExpressionFactory.Parse($"{converterExpr}.ReadAsPropertyName(ref reader, typeof({typeName}), options)", tag.ValueType, false);
                return tag.FromJson.Invoke(key.Value!);
            }

            var nodaValue = ExpressionFactory.Parse($"{converterExpr}.Read(ref reader, typeof({typeName}), options)", tag.ValueType, false);
            return tag.FromJson.Invoke(nodaValue.Value!);
        }

        // Fallback: an unknown type round-trips through JsonSerializer and the caller's options.
        var fallback = ExpressionFactory.Parse($"global::System.Text.Json.JsonSerializer.Deserialize<{typeName}>(ref reader, options)!", tag.ValueType, false);
        return tag.FromJson.Invoke(fallback.Value!);
    }
}
