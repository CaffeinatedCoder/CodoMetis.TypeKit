using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

/// <summary>
/// The JSON converter behind <see cref="Option{T}"/>, <see cref="Result{TError}"/>,
/// <see cref="Result{T,TError}"/> and the <c>Result.Ok</c>/<c>Result.Error</c> markers. It refuses to
/// read or write any of them, because none of them is a wire type.
/// </summary>
/// <remarks>
/// <para>
/// Without it, System.Text.Json sees no public members on an <see cref="Option{T}"/> and writes
/// <c>{}</c>, which reads back as <c>None</c>; a result writes only its <c>State</c> and reads back
/// uninitialized. Nothing raises, and the only evidence is the missing data. Refusing both directions
/// with <see cref="NotSupportedException"/> is what System.Text.Json itself does for a type it cannot
/// represent, such as <see cref="Type"/>.
/// </para>
/// <para>
/// A serialized shape says absent with a nullable, and an outcome is matched to a response or a
/// document at the boundary; <c>ToOption()</c> and <c>OrNull()</c> convert in either direction. An
/// application that wants a wire format of its own registers a converter for the type on its
/// <see cref="JsonSerializerOptions"/>, which takes precedence over this one.
/// </para>
/// <para>
/// A property that is absent from a document reaches no converter at all and is left <c>default</c>:
/// a <c>None</c>, or an uninitialized result. Where that must be an error too, mark the property
/// <c>required</c> or set <see cref="JsonSerializerOptions.RespectRequiredConstructorParameters"/>.
/// </para>
/// <para>
/// Public only so that a source-generated <see cref="JsonSerializerContext"/> can instantiate it.
/// There is nothing to call.
/// </para>
/// <para>
/// Every type gets the same converter, typed <see cref="object"/>, which the serializer wraps for the
/// type it asked for. A converter per type would have to be constructed with
/// <see cref="Type.MakeGenericType"/>, and under Native AOT that construction itself failed for these
/// structs, with the runtime's "missing native code" message instead of this one (measured
/// 2026-09-27).
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NotWireTypeJsonConverterFactory : JsonConverterFactory
{
    private static readonly Type[] RefusedDefinitions =
        [typeof(Option<>), typeof(Result<>), typeof(Result<,>), typeof(Success<>), typeof(Error<>)];

    private static readonly Refusing Converter = new();

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => IsRefused(typeToConvert);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => Converter;

    private static bool IsRefused(Type type) =>
        type == typeof(Success) || type.IsGenericType && RefusedDefinitions.Contains(type.GetGenericTypeDefinition());

    /// <summary>The message for <paramref name="type"/>, naming the alternative. Never the content.</summary>
    internal static string Message(Type type)
    {
        var name = Name(type);

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Option<>)
            ? $"{name} is not a wire type. A serialized shape says absent with a nullable ({Name(type.GetGenericArguments()[0])}?), "
            + "and ToOption() and OrNull() convert at the boundary. To serialize it anyway, register a converter for it on the "
            + "JsonSerializerOptions, which takes precedence over this refusal."
            : $"{name} is an outcome, not a wire type. Match it to a response or a document at the boundary. To serialize it "
            + "anyway, register a converter for it on the JsonSerializerOptions, which takes precedence over this refusal.";
    }

    /// <summary><c>Option&lt;Customer&gt;</c> rather than <c>Option`1</c>.</summary>
    private static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>"
            : type.Name;

    /// <summary>
    /// Refuses every refused type. The serializer hands a read the type it asked for, and a write the
    /// value itself, a boxed struct that is never null; either names the type in the message.
    /// </summary>
    private sealed class Refusing : JsonConverter<object>
    {
        /// <summary>A JSON <c>null</c> is refused with the same message, not with the serializer's own.</summary>
        public override bool HandleNull => true;

        public override bool CanConvert(Type typeToConvert) => IsRefused(typeToConvert);

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(typeToConvert));

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(value.GetType()));

        /// <summary>A dictionary key too, rather than the serializer's message about <see cref="object"/> keys.</summary>
        public override object ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(typeToConvert));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(value.GetType()));
    }
}
