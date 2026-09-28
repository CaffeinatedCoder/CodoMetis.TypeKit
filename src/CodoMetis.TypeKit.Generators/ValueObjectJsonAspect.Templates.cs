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

    /// <summary>
    /// The wrapped value, written as the serializer writes it under the caller's options, byte for byte,
    /// as a value and as a dictionary key: through the converter the options have for the wrapped type,
    /// or the built-in one where they have none (<see cref="JsonImplementationArguments.BuiltInConverter"/>).
    /// Writing it here with formats of its own gave <c>03:04:05.0000000</c> for a <see cref="TimeOnly"/>
    /// the serializer writes as <c>03:04:05</c>, seven fractional digits on date-time keys, no
    /// <c>DictionaryKeyPolicy</c> on string keys, and ignored a converter the host registered.
    /// </summary>
    [Template]
    public void JsonConverterWriteTemplate(
        Utf8JsonWriter        writer,
        dynamic?              value,
        JsonSerializerOptions options,
        [CompileTime] bool    asPropertyName
    )
    {
        var tag = (JsonImplementationArguments)meta.Tags.Source!;

        meta.InsertStatement(ExpressionFactory.Parse($"{Plan(tag)}.{(asPropertyName ? "WriteKey" : "Write")}(writer, {Wrapped(tag)}, options)"));
    }

    private const string GeneratedJson = "global::CodoMetis.TypeKit.CompilerServices.GeneratedJson";

    /// <summary>
    /// C# for the plan for these options: the one this converter keeps, or a new one where the options
    /// changed. The built-in converter is evaluated only then.
    /// </summary>
    private static string Plan(JsonImplementationArguments tag) =>
        $"(this.{JsonPlanField} is {{ }} __plan && global::System.Object.ReferenceEquals(__plan.Options, options) ? __plan : "
      + $"this.{JsonPlanField} = global::CodoMetis.TypeKit.CompilerServices.GeneratedJsonPlan<{ValueObjectTypes.SourceName(tag.ValueType)}>.For(options, {tag.BuiltInConverter}))";

    /// <summary>
    /// The value to write. A <see cref="DateTime"/> is always UTC, through <c>GeneratedJson.AsUtc</c>: an
    /// Unspecified value is taken as UTC rather than as server-local time, which <c>ToUniversalTime()</c> assumed.
    /// </summary>
    private static string Wrapped(JsonImplementationArguments tag) =>
        tag.IsDateTime ? $"{GeneratedJson}.AsUtc(value!.Value)" : "value!.Value";

    /// <summary>
    /// The wrapped value, read as the serializer reads it under the caller's options: by the same
    /// converter the write uses, never by a <c>Parse</c> of its own, which let a <c>FormatException</c>
    /// escape (500 rather than 400 in ASP.NET Core) and read number keys with <c>NumberStyles.Any</c>. A
    /// value the wrapped type cannot read is a <c>JsonException</c> naming the value object and the
    /// wrapped type, never the input, with no inner exception (<c>GeneratedJson.Unreadable</c>): NodaTime's
    /// own quoted it.
    /// </summary>
    [Template]
    public dynamic? JsonConverterReadTemplate(
        ref Utf8JsonReader    reader,
        Type                  typeToConvert,
        JsonSerializerOptions options,
        [CompileTime] bool    asPropertyName
    )
    {
        var tag = (JsonImplementationArguments)meta.Tags.Source!;

        // A JSON null is not a value object. Without this, a string-backed one would wrap null.
        // A property name is never null, so only the value path needs it.
        if (meta.CompileTime(!asPropertyName))
        {
            if (reader.TokenType == JsonTokenType.Null)
                throw (Exception)ExpressionFactory.Parse(
                    $"global::CodoMetis.TypeKit.CompilerServices.GeneratedJson.NullToken<{ValueObjectTypes.SourceName(tag.ValueObjectType)}>()").Value!;
        }

        string read = meta.CompileTime($"{Plan(tag)}.{(asPropertyName ? "ReadKey" : "Read")}<{ValueObjectTypes.SourceName(tag.ValueObjectType)}>(ref reader, options)");

        // A key and a value are the same instant: both are normalised by GeneratedJson.AsUtc.
        var wrapped = ExpressionFactory.Parse(tag.IsDateTime ? $"{GeneratedJson}.AsUtc({read})" : read, tag.ValueType, false);

        return tag.FromJson.Invoke(wrapped.Value!, meta.This._materialize);
    }
}
