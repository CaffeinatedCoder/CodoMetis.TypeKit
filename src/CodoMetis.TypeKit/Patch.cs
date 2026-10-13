using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.CompilerServices;

namespace CodoMetis.TypeKit;

/// <summary>What a JSON Merge Patch field says about a value.</summary>
public enum PatchState
{
    /// <summary>The field was not mentioned: the value stays as it is. The <c>default</c> of a <see cref="Patch{T}"/>.</summary>
    Absent,

    /// <summary>The field was <c>null</c>: the value is cleared.</summary>
    Null,

    /// <summary>The field carried a value: the value is set to it.</summary>
    Value
}

/// <summary>
/// A field of a JSON Merge Patch (RFC 7396): absent, null, or a value. System.Text.Json cannot
/// tell an absent property from a null one, and every PATCH endpoint rebuilt this by hand.
/// </summary>
/// <remarks>
/// <para>
/// A wire type, on purpose, beside <see cref="Option{T}"/>, which refuses JSON: an option says
/// whether a value exists, a patch whether a request mentioned it. The serializer calls no converter
/// for a property it does not find, so <c>default(Patch&lt;T&gt;)</c> is <see cref="PatchState.Absent"/>,
/// the one <c>default</c> of this package that means something, and the analyzers leave it alone.
/// </para>
/// <para>
/// Reading never refuses a null: the Null state is part of the wire format whatever <typeparamref name="T"/>
/// is, and whether a clear is accepted is the application's rule, said in <see cref="Match{TResult}"/>.
/// A <c>Patch&lt;T?&gt;</c> over a struct is therefore never needed. A value <typeparamref name="T"/>'s
/// own converter refuses, a value object's rule, is refused as it is anywhere else.
/// </para>
/// <para>
/// Writing an absent patch is refused: a converter cannot leave a property out. Mark a property that
/// is written <c>[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]</c>, and an absent
/// patch is left out of the document.
/// </para>
/// <para>
/// Under a source-generated <see cref="JsonSerializerContext"/>, the context never sees
/// <typeparamref name="T"/>, since this type's converter hides it. A primitive, a string, a
/// <see cref="Guid"/>, a date or a <see cref="Uri"/> is read with the serializer's built-in converter,
/// a value object with its own; any other type, a class or an enum, is listed in the context, or the
/// serializer refuses with its own message naming the context.
/// </para>
/// <para>
/// Applied without reflection: <c>patch.Name.Tap(name =&gt; order.Name = name)</c> for a field a
/// clear cannot reach, as <see cref="Option{T}.Tap"/> runs on a value, and
/// <c>patch.Nickname.IfPresent(nickname =&gt; order.Nickname = nickname)</c> for one it can, which
/// receives a <c>T?</c>: null for a clear, over a struct as over a class. Both return the patch
/// unchanged. <c>ToString()</c> never prints the value.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
[JsonConverter(typeof(PatchJsonConverterFactory))]
[DebuggerDisplay("{State}")]
public readonly record struct Patch<T>
{
    private readonly T?         _value;
    private readonly PatchState _state;

    // The converter for this closed type, registered where the type itself is compiled: under Native
    // AOT, a converter type built with MakeGenericType has no constructor to invoke, and a static field
    // read by reflection is trimmed (measured, spikes/MergePatch). The factory runs this constructor
    // for the type it is asked for and looks the converter up.
    static Patch() => PatchJsonConverterFactory.Register(typeof(Patch<T>), new PatchJsonConverter<T>());

    internal Patch(T? value, PatchState state)
    {
        _value = value;
        _state = state;
    }

    /// <summary>Absent, null or a value.</summary>
    public PatchState State => _state;

    /// <summary>Picks the branch for the state. All three are required: a patch is handled whole, or not at all.</summary>
    /// <param name="onValue">Called with the value when the field carried one.</param>
    /// <param name="onNull">Called when the field was null: the value is to be cleared.</param>
    /// <param name="onAbsent">Called when the field was not mentioned: the value stays.</param>
    /// <typeparam name="TResult">The type of the outcome.</typeparam>
    /// <returns>The branch's outcome.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public TResult Match<TResult>(Func<T, TResult> onValue, Func<TResult> onNull, Func<TResult> onAbsent)
    {
        ArgumentNullException.ThrowIfNull(onValue);
        ArgumentNullException.ThrowIfNull(onNull);
        ArgumentNullException.ThrowIfNull(onAbsent);

        return _state switch
        {
            PatchState.Value => onValue(_value!),
            PatchState.Null  => onNull(),
            _                => onAbsent(),
        };
    }

    /// <summary>The value, when the field carried one.</summary>
    /// <param name="value">The value, or <c>default</c>.</param>
    /// <returns>Whether the field carried a value.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value!;
        return _state == PatchState.Value;
    }

    /// <summary>Runs <paramref name="action"/> with the value when the field carried one, for a target a clear cannot reach.</summary>
    /// <param name="action">What to do with the value.</param>
    /// <returns>This patch, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    public Patch<T> Tap(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_state == PatchState.Value) action(_value!);

        return this;
    }

    /// <summary>
    /// A patch of what <paramref name="selector"/> makes of the value: a field that carried one
    /// carries the produced value, a clear stays a clear, and an absent field stays absent.
    /// </summary>
    /// <param name="selector">Called with the value when the field carried one.</param>
    /// <typeparam name="TResult">The type of the produced value.</typeparam>
    /// <returns>A patch in the same state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is null, or returned null: a clear is the Null state, never a value.</exception>
    public Patch<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (_state != PatchState.Value) return new Patch<TResult>(default, _state);

        var produced = selector(_value!);
        if (produced is null) throw ThrowHelper.NullPatchValue(nameof(selector));

        return new Patch<TResult>(produced, PatchState.Value);
    }

    /// <summary>The state only, never the value.</summary>
    public override string ToString() => $"Patch {{ State = {_state} }}";
}

/// <summary>The three states of a <see cref="Patch{T}"/>, <c>IfPresent</c> and <c>ToOption()</c>.</summary>
public static class Patch
{
    /// <summary>A field that was not mentioned: the <c>default</c>.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>An absent patch.</returns>
    public static Patch<T> Absent<T>() => default;

    /// <summary>A field that was null: a clear.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A null patch.</returns>
    public static Patch<T> Null<T>() => new(default, PatchState.Null);

    /// <summary>A field that carried <paramref name="value"/>; a null value is the Null state.</summary>
    /// <param name="value">The value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A patch holding the value, or a null patch.</returns>
    public static Patch<T> Value<T>(T? value) => value is null ? Null<T>() : new(value, PatchState.Value);

    /// <param name="patch">The patch.</param>
    /// <typeparam name="T">The type of the value, a class.</typeparam>
    extension<T>(Patch<T> patch) where T : class?
    {
        /// <summary>Runs <paramref name="action"/> with the value, or with null when the field was null, for a target a clear can reach. An absent field runs nothing.</summary>
        /// <param name="action">What to do with the value or the clear.</param>
        /// <returns>The patch, unchanged.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        public Patch<T> IfPresent(Action<T?> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (patch.State != PatchState.Absent) action(patch.TryGetValue(out var value) ? value : null);

            return patch;
        }
    }

    /// <param name="patch">The patch.</param>
    /// <typeparam name="T">The type of the value, a struct.</typeparam>
    extension<T>(Patch<T> patch) where T : struct
    {
        /// <summary>
        /// Runs <paramref name="action"/> with the value, or with null when the field was null, for a
        /// target a clear can reach: a <c>T?</c>, so a clear is never <c>default(T)</c>, a zero or an
        /// uninitialized value object. An absent field runs nothing.
        /// </summary>
        /// <param name="action">What to do with the value or the clear.</param>
        /// <returns>The patch, unchanged.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        public Patch<T> IfPresent(Action<T?> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (patch.State != PatchState.Absent) action(patch.TryGetValue(out var value) ? value : null);

            return patch;
        }
    }

    /// <param name="patch">The patch.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    extension<T>(Patch<T> patch) where T : notnull
    {
        /// <summary><c>Some</c> for a value, <c>None</c> for a null or an absent field: whether a value was given, when a clear and an absence are the same to the caller.</summary>
        /// <returns>The value as an option.</returns>
        public Option<T> ToOption() => patch.TryGetValue(out var value) ? Option.Some(value) : Option.None<T>();
    }
}

/// <summary>
/// The JSON converter behind <see cref="Patch{T}"/>: a JSON null is the Null state, a value is read
/// with <typeparamref name="T"/>'s contract, and an absent property never reaches it.
/// </summary>
internal sealed class PatchJsonConverter<T> : JsonConverter<Patch<T>>
{
    /// <summary>A JSON null is a state, not the serializer's default.</summary>
    public override bool HandleNull => true;

    public override Patch<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return Patch.Null<T>();

        try
        {
            return Patch.Value(JsonSerializer.Deserialize(ref reader, Contract(options)));
        }
        catch (JsonException nested) when (nested.Path is not null)
        {
            throw AtThePatchedProperty(nested);
        }
    }

    /// <summary>
    /// The nested call reads the value as a document of its own, so its refusal carries the path within
    /// that document, <c>$</c>, and the serializer keeps a path it finds set: a client was told
    /// <c>$</c> where a property of <typeparamref name="T"/> gives <c>$.quantity</c>. Thrown again without
    /// one, the serializer sets the patched property's path, <c>$.quantity</c> or <c>$[1].quantity</c>.
    /// The nested refusal's sentence is kept, a value object's own among them, less the location the
    /// nested call appended to the serializer's default sentence, which named <c>$</c>; its inner
    /// exception is kept, the nested exception itself is not, since its message names <c>$</c> too.
    /// Nothing of the input is added.
    /// </summary>
    private static JsonException AtThePatchedProperty(JsonException nested)
    {
        var appended = $" Path: {nested.Path} | LineNumber: {nested.LineNumber} | BytePositionInLine: {nested.BytePositionInLine}.";
        var message  = nested.Message.EndsWith(appended, StringComparison.Ordinal) ? nested.Message[..^appended.Length] : nested.Message;

        return new JsonException(message, nested.InnerException);
    }

    public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options)
    {
        switch (value.State)
        {
            case PatchState.Absent:
                throw new NotSupportedException(
                    $"An absent {Name()} cannot be written, since a converter cannot leave a property out. " +
                    "Mark the property [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)], and an absent patch is left out of the document." + Refusals.NotAWireType);
            case PatchState.Null:
                writer.WriteNullValue();
                break;
            default:
                value.TryGetValue(out var inner);
                JsonSerializer.Serialize(writer, inner!, Contract(options));
                break;
        }
    }

    private static string Name() => $"Patch<{typeof(T).Name}>";

    /// <summary>
    /// <typeparamref name="T"/>'s contract: the options' own where they have one; else made as the
    /// generated converters make a wrapped type's, from a registered converter, the type's own
    /// attribute (a value object) or the serializer's built-in converter for a primitive. A class or an
    /// enum a source-generated context never saw is refused by the serializer, naming the context.
    /// </summary>
    private static JsonTypeInfo<T> Contract(JsonSerializerOptions options) =>
        options.TryGetTypeInfo(typeof(T), out var info) ? (JsonTypeInfo<T>)info : GeneratedJson.TypeInfo<T>(options, BuiltInConverters.For<T>());
}

/// <summary>
/// Creates the converter of a <see cref="Patch{T}"/>: the one its closed type registered when it was
/// first touched. Public only so that a source-generated <see cref="JsonSerializerContext"/> can
/// instantiate it; there is nothing to call.
/// </summary>
/// <remarks>
/// Not <see cref="Type.MakeGenericType"/>: under Native AOT the converter type built that way has no
/// constructor to invoke, and a static field read by reflection is trimmed. The registry the struct's
/// static constructor fills is what works there (measured in spikes/MergePatch).
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PatchJsonConverterFactory : JsonConverterFactory
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, JsonConverter> Registry = new();

    /// <summary>Called by <see cref="Patch{T}"/>'s static constructor, with the converter for that closed type.</summary>
    internal static void Register(Type patch, JsonConverter converter) => Registry[patch] = converter;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2059",
        Justification = "The static constructor of Patch<T> is kept wherever Patch<T> is instantiated, which it is wherever it is serialized; measured under Native AOT in spikes/MergePatch and the consumer smoke test.")]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        RuntimeHelpers.RunClassConstructor(typeToConvert.TypeHandle);

        return Registry.TryGetValue(typeToConvert, out var converter)
            ? converter
            : throw new NotSupportedException($"{typeToConvert} registered no JSON converter: its static constructor did not run.");
    }
}
