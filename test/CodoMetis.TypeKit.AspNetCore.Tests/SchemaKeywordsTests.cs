using System.Collections;
using System.Reflection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// Every property of <see cref="OpenApiSchema"/> is either carried over from the wrapped type's
/// schema or deliberately left behind, and each copied keyword carries over itself and nothing else.
/// </summary>
/// <remarks>
/// The schemas are compared as written, in OpenAPI 3.1 and 3.0, not only through their getters: a
/// setter is not always a no-op for "nothing" (assigning null to <c>Const</c> writes
/// <c>"const": null</c>), and only the written document shows it.
/// </remarks>
public sealed class SchemaKeywordsTests
{
    private static readonly PropertyInfo[] Settable =
        typeof(OpenApiSchema).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(property => property is { CanRead: true, CanWrite: true })
                             .ToArray();

    public static TheoryData<string> CopiedKeywords => new(SchemaKeywords.Copied.Select(keyword => keyword.Name));

    public static TheoryData<string> KeywordsLeftBehind => new(SchemaKeywords.NotCopied);

    [Fact]
    public void Every_schema_property_is_either_copied_or_deliberately_left_behind()
    {
        var copied = SchemaKeywords.Copied.Select(keyword => keyword.Name).ToList();
        var settable = Settable.Select(property => property.Name).ToList();

        copied.ShouldBeUnique();
        copied.Intersect(SchemaKeywords.NotCopied).ShouldBeEmpty("a keyword cannot be both copied and left behind");
        settable.Except(copied).Except(SchemaKeywords.NotCopied)
                .ShouldBeEmpty("Microsoft.OpenApi has schema properties SchemaKeywords does not classify");
        copied.Concat(SchemaKeywords.NotCopied).Except(settable)
              .ShouldBeEmpty("SchemaKeywords classifies properties OpenApiSchema no longer has");
    }

    [Fact]
    public void A_wrapped_schema_that_says_nothing_adds_nothing()
    {
        var target = new OpenApiSchema();

        target.FillFrom(new OpenApiSchema());

        Written(target).ShouldBe(Written(new OpenApiSchema()));
    }

    [Theory]
    [MemberData(nameof(CopiedKeywords))]
    public void A_keyword_the_value_object_lacks_is_filled_in_and_nothing_else(string keyword)
    {
        var property = Property(keyword);
        var source = new OpenApiSchema();
        var value = NonDefault(property, variant: 1);
        property.SetValue(source, value);
        var target = new OpenApiSchema();

        target.FillFrom(source);

        Same(property.GetValue(target), value).ShouldBeTrue($"{keyword} was not carried over");
        if (value is ICollection)
            ReferenceEquals(property.GetValue(target), value).ShouldBeFalse($"{keyword} is shared with the wrapped type's schema, not copied");

        var pristine = new OpenApiSchema();
        foreach (var other in Settable.Where(other => other != property))
            Same(other.GetValue(target), other.GetValue(pristine)).ShouldBeTrue($"filling in {keyword} also changed {other.Name}");

        var expected = new OpenApiSchema();
        property.SetValue(expected, value);
        Written(target).ShouldBe(Written(expected), $"filling in {keyword} wrote something else too");
    }

    [Theory]
    [MemberData(nameof(CopiedKeywords))]
    public void A_keyword_the_value_object_already_has_is_kept(string keyword)
    {
        var property = Property(keyword);
        var own = NonDefault(property, variant: 1);
        var target = new OpenApiSchema();
        property.SetValue(target, own);
        var source = new OpenApiSchema();
        // For a boolean the only other value is the default, which must not undo the value object's.
        property.SetValue(source, property.PropertyType == typeof(bool) ? property.GetValue(new OpenApiSchema()) : NonDefault(property, variant: 2));

        target.FillFrom(source);

        Same(property.GetValue(target), own).ShouldBeTrue($"{keyword} was overwritten");
        var expected = new OpenApiSchema();
        property.SetValue(expected, own);
        Written(target).ShouldBe(Written(expected), $"keeping {keyword} wrote something else too");
    }

    [Theory]
    [MemberData(nameof(KeywordsLeftBehind))]
    public void A_keyword_left_behind_is_not_copied(string keyword)
    {
        var property = Property(keyword);
        var source = new OpenApiSchema();
        property.SetValue(source, NonDefault(property, variant: 1));
        var target = new OpenApiSchema();

        target.FillFrom(source);

        Same(property.GetValue(target), property.GetValue(new OpenApiSchema())).ShouldBeTrue($"{keyword} was copied");
        Written(target).ShouldBe(Written(new OpenApiSchema()), $"leaving {keyword} behind wrote something");
    }

    /// <summary>The schema as the document would contain it, in OpenAPI 3.1 and in 3.0.</summary>
    private static string Written(OpenApiSchema schema)
    {
        var v31 = new StringWriter();
        schema.SerializeAsV31(new OpenApiJsonWriter(v31));
        var v30 = new StringWriter();
        schema.SerializeAsV3(new OpenApiJsonWriter(v30));
        return $"3.1: {v31}\n3.0: {v30}";
    }

    private static PropertyInfo Property(string name) => typeof(OpenApiSchema).GetProperty(name).ShouldNotBeNull();

    /// <summary>A value for <paramref name="property"/> that differs from a new schema's, one per variant.</summary>
    private static object NonDefault(PropertyInfo property, int variant) =>
        property.PropertyType == typeof(bool)
            ? !(bool)property.GetValue(new OpenApiSchema())!
            : Sample(property.PropertyType, variant);

    private static object Sample(Type type, int variant)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying) type = underlying;

        if (type == typeof(string)) return $"v{variant}";
        if (type == typeof(object)) return $"o{variant}";
        if (type == typeof(bool)) return variant == 1;
        if (type == typeof(int)) return variant;
        if (type == typeof(uint)) return (uint)variant;
        if (type == typeof(decimal)) return (decimal)variant;
        if (type == typeof(JsonSchemaType)) return variant == 1 ? JsonSchemaType.Integer : JsonSchemaType.String;
        if (type == typeof(Uri)) return new Uri($"https://example.org/{variant}");
        if (type == typeof(JsonNode)) return JsonValue.Create(variant);
        if (type == typeof(IOpenApiSchema)) return new OpenApiSchema { Title = $"s{variant}" };
        if (type == typeof(IOpenApiExtension)) return new JsonNodeExtension(JsonValue.Create(variant));
        if (type == typeof(OpenApiDiscriminator)) return new OpenApiDiscriminator { PropertyName = $"d{variant}" };
        if (type == typeof(OpenApiExternalDocs)) return new OpenApiExternalDocs { Description = $"e{variant}" };
        if (type == typeof(OpenApiXml)) return new OpenApiXml { Name = $"x{variant}" };

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(IList<>))
                return Collection(typeof(List<>).MakeGenericType(arguments), Sample(arguments[0], variant));
            if (definition == typeof(ISet<>) || definition == typeof(HashSet<>))
                return Collection(typeof(HashSet<>).MakeGenericType(arguments), Sample(arguments[0], variant));
            if (definition == typeof(IDictionary<,>))
            {
                var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments))!;
                dictionary.Add(Sample(arguments[0], variant), Sample(arguments[1], variant));
                return dictionary;
            }
        }

        throw new NotSupportedException($"No sample for {type}: a new OpenApiSchema property type needs one here.");
    }

    private static object Collection(Type type, object element)
    {
        var collection = Activator.CreateInstance(type)!;
        type.GetMethod("Add")!.Invoke(collection, [element]);
        return collection;
    }

    private static bool Same(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (JsonNode l, JsonNode r) => JsonNode.DeepEquals(l, r),
        (string l, string r) => l == r,
        (IDictionary l, IDictionary r) => l.Count == r.Count && l.Keys.Cast<object>().All(key => r.Contains(key) && Same(l[key], r[key])),
        (IEnumerable l, IEnumerable r) => l.Cast<object?>().SequenceEqual(r.Cast<object?>(), EqualityComparer<object?>.Create((a, b) => Same(a, b))),
        _ => left.Equals(right),
    };
}
