using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

/// <summary>
/// The JSON converter behind <see cref="Option{T}"/>, <see cref="Result{TError}"/>,
/// <see cref="Result{T,TError}"/> and the <c>Option.None</c>, <c>Result.Success</c> and <c>Result.Error</c>
/// markers. It refuses to read or write any of them, because none of them is a wire type.
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
/// Every type gets a converter of the same class, typed <see cref="object"/>, which the serializer
/// wraps for the type it asked for. A converter typed per type would have to be constructed with
/// <see cref="Type.MakeGenericType"/>, and under Native AOT that construction itself failed for these
/// structs, with the runtime's "missing native code" message instead of this one (measured
/// 2026-09-27).
/// </para>
/// <para>
/// A nullable of a refused type, <c>Option&lt;T&gt;?</c> in a PATCH-style shape, is refused too. The
/// serializer uses a converter typed <see cref="object"/> for <c>Nullable&lt;T&gt;</c> directly, and
/// hands it a JSON <c>null</c>, and a <see langword="null"/> to write that names no type; so each
/// converter remembers the type it was created for. A source-generated context cannot build the
/// contract of such a shape at all: its contract for <c>T?</c> takes the converter of <c>T</c> only as
/// a <c>JsonConverter&lt;T&gt;</c>, and fails with the serializer's own
/// <see cref="InvalidOperationException"/> before any document is read or written. A typed converter
/// would not refuse there either: that contract writes and reads a null without asking it (measured
/// 2026-09-28). The README names that message, so a search for it finds the reason.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NotWireTypeJsonConverterFactory : JsonConverterFactory
{
    private static readonly Type[] RefusedDefinitions =
        [typeof(Option<>), typeof(Result<>), typeof(Result<,>), typeof(Success<>), typeof(Error<>)];

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => IsRefused(typeToConvert);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => new Refusing(typeToConvert);

    private static bool IsRefused(Type type) =>
        type == typeof(None) || type == typeof(Success) || type.IsGenericType && RefusedDefinitions.Contains(type.GetGenericTypeDefinition());

    /// <summary>The message for <paramref name="type"/>, naming the alternative. Never the content.</summary>
    internal static string Message(Type type)
    {
        // The converter of Option<T> refuses Option<T>? too, and a read hands it the nullable.
        type = Nullable.GetUnderlyingType(type) ?? type;

        var name = Name(type);

        // The None marker is absence too, with no type argument to name.
        var nullable = type == typeof(None) ? ""
                     : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Option<>) ? $" ({Name(type.GetGenericArguments()[0])}?)"
                     : null;

        return nullable is not null
            ? $"{name} is not a wire type. A serialized shape says absent with a nullable{nullable}, "
            + "and ToOption() and OrNull() convert at the boundary. To serialize it anyway, register a converter for it on the "
            + "JsonSerializerOptions, which takes precedence over this refusal." + Refusals.NotAWireType
            : $"{name} is an outcome, not a wire type. Match it to a response or a document at the boundary. To serialize it "
            + "anyway, register a converter for it on the JsonSerializerOptions, which takes precedence over this refusal." + Refusals.NotAWireType;
    }

    /// <summary><c>Option&lt;Customer&gt;</c> rather than <c>Option`1</c>.</summary>
    private static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>"
            : type.Name;

    /// <summary>
    /// Refuses the type it was created for. A read is handed the type the serializer asked for, the
    /// nullable of it included; a write is handed a value, which for a nullable can be null, so a
    /// write names the type this converter was created for.
    /// </summary>
    /// <param name="refused">The type the factory created this converter for.</param>
    private sealed class Refusing(Type refused) : JsonConverter<object>
    {
        /// <summary>A JSON <c>null</c> is refused with the same message, not with the serializer's own.</summary>
        public override bool HandleNull => true;

        public override bool CanConvert(Type typeToConvert) => IsRefused(typeToConvert);

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(typeToConvert));

        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(refused));

        /// <summary>A dictionary key too, rather than the serializer's message about <see cref="object"/> keys.</summary>
        public override object ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(typeToConvert));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            throw new NotSupportedException(Message(refused));
    }
}
