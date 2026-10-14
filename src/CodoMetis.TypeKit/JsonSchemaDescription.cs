using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit;

/// <summary>
/// A JSON converter that says what it writes, so a JSON Schema, and with CodoMetis.TypeKit.AspNetCore an
/// OpenAPI document, describes a type the way the converter has it rather than as the empty schema a
/// converter of one's own gets.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by a <see cref="JsonConverter{T}"/>, which describes the type it converts. A converter
/// someone else wrote, or the serializer's own, is described by a <see cref="JsonSchemaDescriptions"/>
/// in the options instead.
/// </para>
/// <para>
/// The description says what one non-null value looks like. Where a position admits null, a nullable
/// value type, a nullable property, the reader of the description adds the null.
/// </para>
/// </remarks>
public interface IDescribedJsonConverter
{
    /// <summary>What the converter writes for one non-null value of <paramref name="typeToConvert"/>.</summary>
    /// <param name="typeToConvert">The type the converter converts: a <see cref="Nullable{T}"/> only where the converter is one of that.</param>
    /// <param name="options">The options the converter runs under.</param>
    /// <returns>The description, or <see langword="null"/> where the converter says nothing of this type.</returns>
    JsonSchemaDescription? DescribeJson(Type typeToConvert, JsonSerializerOptions options);
}

/// <summary>
/// What a converter writes for one value: a JSON Schema of its own, or the schema another type has.
/// </summary>
/// <remarks>
/// A schema is a JSON Schema 2020-12 object, as System.Text.Json's <see cref="JsonSchemaExporter"/>
/// writes one: <c>{"type":"string","pattern":"^-?\\d+(\\.\\d+)?$"}</c>. It is complete in itself, so the
/// keywords that give a schema an identity or point elsewhere (<c>$ref</c>, <c>$id</c>, <c>$defs</c> and
/// their kind) are refused where it is made. It describes a non-null value; null is added where the
/// position admits it. Copied in and out, so a description never changes once made.
/// </remarks>
public sealed class JsonSchemaDescription
{
    private static readonly string[] Unanchored = ["$ref", "$dynamicRef", "$id", "$anchor", "$dynamicAnchor", "$defs", "definitions", "$schema", "$vocabulary"];

    private readonly JsonObject? _schema;

    private JsonSchemaDescription(JsonObject? schema, Type? sameAs)
    {
        _schema = schema;
        SameAsType = sameAs;
    }

    /// <summary>The type whose schema this is, for a description made by <see cref="SameAs"/>; otherwise <see langword="null"/>.</summary>
    public Type? SameAsType { get; }

    /// <summary>A copy of the schema, for a description made by <see cref="Of"/>; otherwise <see langword="null"/>.</summary>
    public JsonObject? Schema => (JsonObject?)_schema?.DeepClone();

    /// <summary>A description by a schema of its own.</summary>
    /// <param name="schema">The JSON Schema of one non-null value. Copied.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentException">The schema refers elsewhere or names itself (<c>$ref</c>, <c>$id</c>, <c>$defs</c>), at any depth.</exception>
    public static JsonSchemaDescription Of(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (FirstUnanchored(schema) is { } keyword)
            throw new ArgumentException(
                $"A described schema is complete in itself, and '{keyword}' refers elsewhere or names it. Write the keywords it would refer to in place.",
                nameof(schema));

        return new((JsonObject)schema.DeepClone(), sameAs: null);
    }

    /// <summary>A description as the schema <paramref name="type"/> has where the description is read: a converter that writes one type as another.</summary>
    /// <param name="type">The type whose schema the converter's values have.</param>
    /// <returns>The description.</returns>
    public static JsonSchemaDescription SameAs(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new(schema: null, type);
    }

    /// <summary>
    /// What a value of <paramref name="type"/> looks like under <paramref name="options"/>, as its
    /// converter or the options' <see cref="JsonSchemaDescriptions"/> say, or <see langword="null"/> where
    /// neither says anything: where every reader of descriptions asks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The options' <see cref="JsonSchemaDescriptions"/>, the application's word, answer first, in the order
    /// they were added. Then the converter the serializer runs is asked, chosen as the serializer chooses it:
    /// the property's own, where <paramref name="property"/> names one; the first of the options' converters
    /// that converts the type, a <see cref="Nullable{T}"/> as itself before the value it holds, and a factory
    /// for the converter it makes; the type's own, a value object's generated converter, which writes the
    /// type it wraps, and a closed set's, which writes one of its members' names, each recognised by the
    /// attribute the generators put on the type; and the resolver's. A converter that describes itself
    /// answers; one that says nothing answers nothing, whatever the type's own converter would have said.
    /// </para>
    /// <para>
    /// A value object is described as its wrapped type under the same options, so one over a
    /// <see cref="decimal"/> is what the options' decimal converter says, as its JSON is what that
    /// converter writes.
    /// </para>
    /// </remarks>
    /// <param name="type">The type; a <see cref="Nullable{T}"/> is described as the value it holds.</param>
    /// <param name="options">The options.</param>
    /// <param name="property">The property the value is in, or <see langword="null"/>.</param>
    /// <returns>The description, or <see langword="null"/>.</returns>
    public static JsonSchemaDescription? For(Type type, JsonSerializerOptions options, JsonPropertyInfo? property = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        var value = Nullable.GetUnderlyingType(type) ?? type;

        if (JsonSchemaDescriptions.Registered(value, options) is { } registered) return registered;

        if (property?.CustomConverter is { } own) return Describe(PropertyConverter(own, property, type, value, options), value, options);

        // The options' converters before the type's own, as the serializer picks them: a converter registered
        // for a value object writes it instead of the generated one, and a source-generated context need not
        // list a type its value objects wrap, which the converter registered for it still writes.
        if (TryRegistered(type, options, out var declared)) return Describe(declared, value, options);
        if (type != value && TryRegistered(value, options, out var held)) return Describe(held, value, options);

        if (Generated.GetOrAdd(value, Generate) is { } generated) return generated;

        return options.TryGetTypeInfo(value, out var info) && info.Converter is IDescribedJsonConverter self ? self.DescribeJson(value, options) : null;
    }

    /// <summary>
    /// What <paramref name="converter"/>, in charge of <paramref name="value"/>, says it writes: its own word
    /// where it describes itself, the generated description where it is the converter the type's own
    /// <see cref="JsonConverterAttribute"/> names, and nothing otherwise.
    /// </summary>
    private static JsonSchemaDescription? Describe(JsonConverter? converter, Type value, JsonSerializerOptions options) => converter switch
    {
        IDescribedJsonConverter described => described.DescribeJson(converter.Type ?? value, options),
        not null when value.GetCustomAttribute<JsonConverterAttribute>(inherit: false)?.ConverterType == converter.GetType() => Generated.GetOrAdd(value, Generate),
        _ => null,
    };

    /// <summary>
    /// The first of the options' converters that converts <paramref name="type"/>, as the serializer picks
    /// it: a factory for the converter it makes, which is <see langword="null"/> where it makes none.
    /// </summary>
    private static bool TryRegistered(Type type, JsonSerializerOptions options, out JsonConverter? converter)
    {
        foreach (var candidate in options.Converters)
        {
            if (candidate is JsonSchemaDescriptions || !candidate.CanConvert(type)) continue;

            converter = candidate is JsonConverterFactory factory ? factory.CreateConverter(type, options) : candidate;
            return true;
        }

        converter = null;
        return false;
    }

    /// <summary>
    /// The converter in charge of a property that names one: the converter named, or the one its factory
    /// makes for the property's type. The serializer adapts a converter named on a <see cref="Nullable{T}"/>
    /// property to the nullable type, and the adapter says nothing, so the attribute is asked for the
    /// converter it names.
    /// </summary>
    private static JsonConverter? PropertyConverter(JsonConverter own, JsonPropertyInfo property, Type type, Type value, JsonSerializerOptions options)
    {
        if (own is JsonConverterFactory factory) return Made(factory, type, options) ?? Made(factory, value, options);
        if (own is IDescribedJsonConverter || own.Type != type || type == value) return own;

        if (property.AttributeProvider?.GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false) is not [JsonConverterAttribute attribute, ..]) return own;

        var named = attribute.CreateConverter(value) ?? (attribute.ConverterType is { } converterType ? Activator.CreateInstance(converterType) as JsonConverter : null);
        return named is JsonConverterFactory namedFactory ? Made(namedFactory, value, options) : named ?? own;
    }

    private static JsonConverter? Made(JsonConverterFactory factory, Type type, JsonSerializerOptions options) =>
        factory.CanConvert(type) ? factory.CreateConverter(type, options) : null;

    /// <summary>What the generators' attribute says of a type: a value object or a closed set, or neither.</summary>
    private static readonly ConcurrentDictionary<Type, JsonSchemaDescription?> Generated = new();

    private static JsonSchemaDescription? Generate(Type type) =>
        type.GetCustomAttribute<GeneratedValueObjectAttribute>(inherit: false) is { } valueObject && valueObject.ValueObjectType == type
            ? SameAs(valueObject.WrappedType)
            : type.GetCustomAttribute<GeneratedClosedSetAttribute>(inherit: false) is { } set && set.SetType == type
                ? Of(new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. set.Names.Select(name => JsonValue.Create(name))]) })
                : null;

    /// <summary>
    /// The first keyword that refers elsewhere or names a schema, in the schema or any schema inside it; a
    /// property's name or a value in <c>const</c>, <c>enum</c> or <c>examples</c> is data, not a keyword.
    /// </summary>
    private static string? FirstUnanchored(JsonObject schema) =>
        schema.Select(keyword => keyword.Key).FirstOrDefault(Unanchored.Contains)
     ?? JsonSchemaKeywords.Subschemas(schema).Select(FirstUnanchored).FirstOrDefault(found => found is not null);
}

/// <summary>Where JSON Schema 2020-12 holds schemas inside a schema, and nothing else.</summary>
internal static class JsonSchemaKeywords
{
    private static readonly string[] One = ["items", "additionalItems", "additionalProperties", "unevaluatedItems", "unevaluatedProperties", "not", "contains", "propertyNames", "if", "then", "else", "contentSchema"];
    private static readonly string[] Many = ["allOf", "anyOf", "oneOf", "prefixItems", "items"];
    private static readonly string[] Named = ["properties", "patternProperties", "dependentSchemas", "$defs", "definitions"];

    /// <summary>The schema objects directly inside <paramref name="schema"/>; a boolean schema has none.</summary>
    public static IEnumerable<JsonObject> Subschemas(JsonObject schema)
    {
        foreach (var (keyword, value) in schema)
        {
            if (One.Contains(keyword) && value is JsonObject single) yield return single;
            else if (Many.Contains(keyword) && value is JsonArray list)
            {
                foreach (var item in list.OfType<JsonObject>()) yield return item;
            }
            else if (Named.Contains(keyword) && value is JsonObject map)
            {
                foreach (var (_, item) in map)
                {
                    if (item is JsonObject named) yield return named;
                }
            }
        }
    }
}

/// <summary>
/// Descriptions for types whose converter says nothing of itself, a library's or the serializer's own,
/// carried in the options' converters: <c>options.Converters.Add(new JsonSchemaDescriptions().Describe&lt;Instant&gt;(…))</c>.
/// </summary>
/// <remarks>
/// <para>
/// A converter that implements <see cref="IDescribedJsonConverter"/> describes itself. One that does not,
/// NodaTime's or <see cref="JsonStringEnumConverter"/>, is described here, by type or by a rule over
/// types, and whatever converter writes the type is then taken to write that. An entry here comes before
/// the converter's own description: it is the application's word.
/// </para>
/// <para>
/// It converts nothing: <see cref="CanConvert"/> is <see langword="false"/> for every type, so the
/// serializer passes over it, and it travels with the options wherever they are copied. Describe every
/// type before the options are first used, as the serializer has every converter registered by then.
/// </para>
/// </remarks>
public sealed class JsonSchemaDescriptions : JsonConverterFactory
{
    private ImmutableDictionary<Type, JsonSchemaDescription> _byType = ImmutableDictionary<Type, JsonSchemaDescription>.Empty;
    private ImmutableArray<Func<Type, JsonSchemaDescription?>> _rules = [];

    /// <summary>Describes <typeparamref name="T"/>, and a <see cref="Nullable{T}"/> of it as one of its values or null.</summary>
    /// <param name="description">What one non-null value looks like.</param>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The same descriptions.</returns>
    public JsonSchemaDescriptions Describe<T>(JsonSchemaDescription description) => Describe(typeof(T), description);

    /// <summary>Describes <paramref name="type"/>, and a <see cref="Nullable{T}"/> of it as one of its values or null.</summary>
    /// <param name="type">The type.</param>
    /// <param name="description">What one non-null value looks like.</param>
    /// <returns>The same descriptions.</returns>
    public JsonSchemaDescriptions Describe(Type type, JsonSchemaDescription description)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(description);

        ImmutableInterlocked.AddOrUpdate(ref _byType, Nullable.GetUnderlyingType(type) ?? type, description, (_, _) => description);
        return this;
    }

    /// <summary>
    /// Describes every type <paramref name="describe"/> answers for, after the types described by name:
    /// <c>type =&gt; type.IsEnum ? … : null</c>.
    /// </summary>
    /// <param name="describe">What one non-null value of a type looks like, or <see langword="null"/> to say nothing of it.</param>
    /// <returns>The same descriptions.</returns>
    public JsonSchemaDescriptions Describe(Func<Type, JsonSchemaDescription?> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);

        ImmutableInterlocked.Update(ref _rules, static (rules, rule) => rules.Add(rule), describe);
        return this;
    }

    /// <summary>Never: these are descriptions, not a converter.</summary>
    /// <param name="typeToConvert">The type.</param>
    /// <returns><see langword="false"/>.</returns>
    public override bool CanConvert(Type typeToConvert) => false;

    /// <summary>Never called, since <see cref="CanConvert"/> is false for every type.</summary>
    /// <param name="typeToConvert">The type.</param>
    /// <param name="options">The options.</param>
    /// <returns>Never.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("JsonSchemaDescriptions converts nothing: it carries descriptions in the options.");

    /// <summary>
    /// What the options' descriptions say of <paramref name="type"/>, and nothing else: the application's
    /// word, which comes before any converter's.
    /// </summary>
    /// <param name="type">The type; a <see cref="Nullable{T}"/> is described as the value it holds.</param>
    /// <param name="options">The options.</param>
    /// <returns>The description, or <see langword="null"/>.</returns>
    public static JsonSchemaDescription? Registered(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        var value = Nullable.GetUnderlyingType(type) ?? type;
        foreach (var converter in options.Converters)
        {
            if (converter is JsonSchemaDescriptions descriptions && descriptions.Of(value) is { } described) return described;
        }

        return null;
    }

    internal JsonSchemaDescription? Of(Type type)
    {
        if (_byType.TryGetValue(type, out var described)) return described;

        foreach (var rule in _rules)
        {
            if (rule(type) is { } ruled) return ruled;
        }

        return null;
    }
}

/// <summary>
/// Puts each described converter's schema into what System.Text.Json's <see cref="JsonSchemaExporter"/>
/// exports: <c>options.GetJsonSchemaAsNode(typeof(Order), DescribedJsonSchema.ExporterOptions())</c>.
/// </summary>
/// <remarks>
/// The exporter has no way to see through a converter of one's own, so it writes the empty schema for
/// one; this hook asks the converter, wherever the type appears: as the root, a property, an element or
/// a dictionary's value. A position that admits null, a <see cref="Nullable{T}"/> or a nullable
/// property, gets the null beside the described values.
/// </remarks>
public static class DescribedJsonSchema
{
    [ThreadStatic] private static Stack<Type>? _sameAs;

    /// <summary>The exporter options that apply descriptions, with <paramref name="options"/>' own transform after them.</summary>
    /// <param name="options">Options to keep, or <see langword="null"/> for the defaults.</param>
    /// <returns>New options.</returns>
    public static JsonSchemaExporterOptions ExporterOptions(JsonSchemaExporterOptions? options = null)
    {
        var then = options?.TransformSchemaNode;

        JsonSchemaExporterOptions described = null!;
        described = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = options?.TreatNullObliviousAsNonNullable ?? false,
            TransformSchemaNode = (context, node) =>
            {
                var result = Describe(context, node, described);
                return then is null ? result : then(context, result);
            },
        };

        return described;
    }

    private static JsonNode Describe(JsonSchemaExporterContext context, JsonNode node, JsonSchemaExporterOptions options)
    {
        var info = context.TypeInfo;
        if (JsonSchemaDescription.For(info.Type, info.Options, context.PropertyInfo) is { } description)
        {
            var admitsNull = Nullable.GetUnderlyingType(info.Type) is not null
                          || node is JsonObject { } exported && exported["type"] is JsonArray types && types.Any(t => t?.GetValue<string>() == "null")
                          || !info.Type.IsValueType && (context.PropertyInfo is { } property ? property.IsGetNullable || property.IsSetNullable : !options.TreatNullObliviousAsNonNullable);

            return Place(Schema(description, info.Options, options), admitsNull, context.Path);
        }

        // The exporter writes no items and no additionalProperties for an element of the empty schema,
        // which a converter of one's own gets, and offers the element to no transform: the container
        // gets them back here, only where it has none.
        if (node is JsonObject container && info.ElementType is { } element
         && info.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary
         && JsonSchemaDescription.For(element, info.Options) is { } elementDescription)
        {
            var keyword = info.Kind == JsonTypeInfoKind.Enumerable ? "items" : "additionalProperties";
            if (!container.ContainsKey(keyword))
            {
                // An element admits null as the exporter has it for one of a plain type: a nullable value
                // type always, a reference type unless null-oblivious types are taken as non-nullable.
                var admitsNull = Nullable.GetUnderlyingType(element) is not null || !element.IsValueType && !options.TreatNullObliviousAsNonNullable;
                container[keyword] = Place(Schema(elementDescription, info.Options, options), admitsNull, [.. context.Path, keyword]);
            }
        }

        return node;
    }

    /// <summary>
    /// The described schema where it is put: with null beside it where the position admits null, and its
    /// references, which point from its own root, pointing from the document's.
    /// </summary>
    private static JsonObject Place(JsonObject schema, bool admitsNull, ReadOnlySpan<string> path)
    {
        var placed = admitsNull ? JsonSchemaNull.Admit(schema) : schema;
        var pointer = string.Concat(path.ToArray().Select(segment => "/" + segment.Replace("~", "~0").Replace("/", "~1")));
        Relocate(schema, ReferenceEquals(placed, schema) ? pointer : pointer + "/anyOf/0");
        return placed;
    }

    /// <summary>Every local reference in <paramref name="schema"/>, made to point from <paramref name="pointer"/>.</summary>
    private static void Relocate(JsonObject schema, string pointer)
    {
        if (pointer.Length == 0) return;

        if (schema["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var target) && target.StartsWith('#'))
            schema["$ref"] = "#" + pointer + target[1..];

        foreach (var inner in JsonSchemaKeywords.Subschemas(schema)) Relocate(inner, pointer);
    }

    private static JsonObject Schema(JsonSchemaDescription description, JsonSerializerOptions serializer, JsonSchemaExporterOptions options) =>
        description.Schema ?? SameAs(description.SameAsType!, serializer, options);

    private static JsonObject SameAs(Type type, JsonSerializerOptions serializer, JsonSchemaExporterOptions options)
    {
        var path = _sameAs ??= new Stack<Type>();
        if (path.Contains(type))
            throw new InvalidOperationException($"A converter is described as the schema of '{type}', which is described as itself again ({string.Join(" -> ", path.Reverse().Append(type).Select(t => t.Name))}): the schema has no end.");

        path.Push(type);
        try
        {
            // Described itself, the type needs no metadata: a source-generated context need not list a type
            // a value object wraps. Only a type nothing describes is asked of the resolver.
            if (JsonSchemaDescription.For(type, serializer) is { } described) return Schema(described, serializer, options);
            return NonNull(serializer.GetJsonSchemaAsNode(type, options) as JsonObject ?? []);
        }
        finally
        {
            path.Pop();
        }
    }

    /// <summary>
    /// A type's schema as one non-null value of it, which a description is: the exporter lets null into the
    /// root of a reference type's, and the position it is put in decides that.
    /// </summary>
    private static JsonObject NonNull(JsonObject schema)
    {
        if (schema["type"] is JsonArray types && types.Any(t => t?.GetValue<string>() == "null"))
        {
            var rest = types.Where(t => t?.GetValue<string>() != "null").Select(t => t!.GetValue<string>()).ToArray();
            schema["type"] = rest is [var single] ? JsonValue.Create(single) : new JsonArray([.. rest.Select(t => JsonValue.Create(t))]);
        }

        return schema;
    }
}

/// <summary>Null beside the values a schema admits, as JSON Schema says it for each shape.</summary>
internal static class JsonSchemaNull
{
    /// <summary>The keywords whose subschemas judge the whole value, null included, whatever the type says.</summary>
    private static readonly string[] Combining = ["anyOf", "oneOf", "allOf", "not", "if", "then", "else", "const"];

    /// <summary>
    /// A type gains <c>"null"</c>; an enum gains null as a value; a schema that combines subschemas
    /// (<c>anyOf</c>, <c>not</c>, <c>if</c> and its branches), holds a <c>const</c> or restricts nothing is
    /// kept whole in a branch beside a null one, since a subschema refuses null whatever the type says.
    /// </summary>
    public static JsonObject Admit(JsonObject schema)
    {
        if (Combining.Any(schema.ContainsKey) || !schema.ContainsKey("type"))
            return new JsonObject { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };

        schema["type"] = schema["type"] switch
        {
            JsonArray types when types.Any(t => t?.GetValue<string>() == "null") => types,
            JsonArray types => new JsonArray([.. types.Select(t => t?.DeepClone()), JsonValue.Create("null")]),
            var single => new JsonArray(single!.DeepClone(), "null"),
        };

        if (schema["enum"] is JsonArray values && !values.Any(value => value is null)) values.Add(null);

        return schema;
    }
}
