using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>A class the serializer walks as an object, so a preserving handler gives it an <c>$id</c>.</summary>
public sealed class Tracked
{
    public string Name { get; set; } = "";
}

/// <summary>A struct the serializer walks, with nothing it could give an <c>$id</c>.</summary>
public struct Untracked
{
    public int X { get; set; }

    public string Label { get; set; }
}

/// <summary>A struct the serializer walks, with a class among its properties.</summary>
public struct HoldingTracked
{
    public Tracked? Inner { get; set; }
}

/// <summary>A struct the serializer walks, with a nullable struct holding a class among its properties.</summary>
public struct HoldingNullableHolder
{
    public HoldingTracked? Holder { get; set; }
}

/// <summary>A struct the serializer walks, whose only class is ignored, so it writes nothing that could have an <c>$id</c>.</summary>
public struct HoldingIgnored
{
    public int Number { get; set; }

    [JsonIgnore]
    public Tracked? Ignored { get; set; }
}

public readonly partial record struct OverTracked : IValue<Tracked>;

public readonly partial record struct OverHoldingNullableHolder : IValue<HoldingNullableHolder>;

public readonly partial record struct OverHoldingIgnored : IValue<HoldingIgnored>;

public readonly partial record struct OverUntracked : IValue<Untracked>;

public readonly partial record struct OverHoldingTracked : IValue<HoldingTracked>;

/// <summary>
/// A value object under a reference handler. The serializer keeps the references of one call, and a
/// value object's converter reaches a wrapped type that the serializer walks through a call of its
/// own, which starts again: under <see cref="ReferenceHandler.Preserve"/> a value object over a class
/// wrote <c>"$id":"1"</c> inside a document that had one already, and two around one instance wrote
/// it twice where the wrapped type writes a <c>$ref</c>, which the converter could not read back.
/// </summary>
public sealed class ReferenceHandlingTests
{
    private static readonly JsonSerializerOptions Preserving = new() { ReferenceHandler = ReferenceHandler.Preserve };

    private sealed class Pair<T>
    {
        public T? First { get; set; }

        public T? Second { get; set; }
    }

    [Fact]
    public void Preserving_references_inside_a_value_object_over_a_class_is_refused_on_writing()
    {
        var shared = new Tracked { Name = "x" };

        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(new Pair<OverTracked> { First = OverTracked.From(shared), Second = OverTracked.From(shared) }, Preserving))
              .Message.ShouldContain("ReferenceHandler.Preserve");
    }

    /// <summary>A struct has no <c>$id</c> of its own, but a class among its properties has.</summary>
    [Fact]
    public void Preserving_references_inside_a_value_object_over_a_struct_holding_a_class_is_refused_on_writing() =>
        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(new Pair<OverHoldingTracked> { First = OverHoldingTracked.From(new HoldingTracked { Inner = new Tracked() }) }, Preserving))
              .Message.ShouldContain("ReferenceHandler.Preserve");

    /// <summary>
    /// A nullable struct is written as the struct it holds, so a class inside that has an <c>$id</c>: two
    /// value objects around one instance wrote it twice, and the wrapped type's document, with a <c>$ref</c>,
    /// failed to read (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void Preserving_references_inside_a_nullable_struct_holding_a_class_is_refused()
    {
        var shared = new HoldingNullableHolder { Holder = new HoldingTracked { Inner = new Tracked { Name = "x" } } };
        var json   = JsonSerializer.Serialize(new Pair<HoldingNullableHolder> { First = shared, Second = shared }, Preserving);

        json.ShouldContain("\"$ref\"");
        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(new Pair<OverHoldingNullableHolder> { First = OverHoldingNullableHolder.From(shared), Second = OverHoldingNullableHolder.From(shared) }, Preserving))
              .Message.ShouldContain("ReferenceHandler.Preserve");
        Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<Pair<OverHoldingNullableHolder>>(json, Preserving)).Message.ShouldContain("ReferenceHandler.Preserve");
    }

    /// <summary>
    /// An ignored property is neither written nor read, so a class there has no <c>$id</c>: the struct was
    /// refused under <see cref="ReferenceHandler.Preserve"/>, where it had been written as the wrapped type
    /// before the refusal (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_struct_whose_only_class_is_ignored_is_written_and_read_under_Preserve()
    {
        var value = new HoldingIgnored { Number = 42, Ignored = new Tracked() };
        var json  = JsonSerializer.Serialize(new Pair<OverHoldingIgnored> { First = OverHoldingIgnored.From(value) }, Preserving);

        json.ShouldBe(JsonSerializer.Serialize(new Pair<HoldingIgnored> { First = value }, Preserving));
        JsonSerializer.Deserialize<Pair<OverHoldingIgnored>>(json, Preserving)!.First.Value.Number.ShouldBe(42);
    }

    /// <summary>The wrapped type's document, which a value object read as an unreadable value.</summary>
    [Fact]
    public void Preserving_references_inside_a_value_object_is_refused_on_reading()
    {
        var shared = new Tracked { Name = "x" };
        var json   = JsonSerializer.Serialize(new Pair<Tracked> { First = shared, Second = shared }, Preserving);

        json.ShouldContain("\"$ref\"");
        Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<Pair<OverTracked>>(json, Preserving)).Message.ShouldContain("ReferenceHandler.Preserve");
    }

    /// <summary>A struct has no <c>$id</c> of its own, so one holding nothing that could have one writes the wrapped type's bytes.</summary>
    [Fact]
    public void A_struct_without_references_is_written_and_read_under_Preserve()
    {
        var value = new Untracked { X = 1, Label = "a" };
        var json  = JsonSerializer.Serialize(new Pair<OverUntracked> { First = OverUntracked.From(value) }, Preserving);

        json.ShouldBe(JsonSerializer.Serialize(new Pair<Untracked> { First = value }, Preserving));
        JsonSerializer.Deserialize<Pair<OverUntracked>>(json, Preserving)!.First.Value.ShouldBe(value);
    }

    /// <summary>A cycle through the value object fails at the depth limit there, which is loud; anything else is the wrapped type's bytes.</summary>
    [Fact]
    public void IgnoreCycles_is_left_alone()
    {
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles };
        var shared  = new Tracked { Name = "x" };

        JsonSerializer.Serialize(new Pair<OverTracked> { First = OverTracked.From(shared), Second = OverTracked.From(shared) }, options)
                      .ShouldBe(JsonSerializer.Serialize(new Pair<Tracked> { First = shared, Second = shared }, options));
    }

    private sealed class TrackedConverter : JsonConverter<Tracked>
    {
        public override Tracked Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new() { Name = reader.GetString()! };

        public override void Write(Utf8JsonWriter writer, Tracked value, JsonSerializerOptions options) => writer.WriteStringValue(value.Name);
    }

    /// <summary>A nullable holder written as its inner name, which has no <c>$id</c>.</summary>
    private sealed class HolderNameConverter : JsonConverter<HoldingTracked?>
    {
        public override HoldingTracked? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new HoldingTracked { Inner = new Tracked { Name = reader.GetString()! } };

        public override void Write(Utf8JsonWriter writer, HoldingTracked? value, JsonSerializerOptions options) => writer.WriteStringValue(value!.Value.Inner?.Name);
    }

    /// <summary>A resolver that puts one contract in charge of one type, before the reflection resolver and the options' converters.</summary>
    private sealed class ResolverOf<T>(Func<JsonSerializerOptions, JsonTypeInfo<T>> contract) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => type == typeof(T) ? contract(options) : null;
    }

    private static JsonSerializerOptions PreservingWith(IJsonTypeInfoResolver first, params JsonConverter[] converters)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve, TypeInfoResolver = JsonTypeInfoResolver.Combine(first, new DefaultJsonTypeInfoResolver()) };
        foreach (var converter in converters) options.Converters.Add(converter);
        return options;
    }

    private static HoldingNullableHolder SharedHolder() => new() { Holder = new HoldingTracked { Inner = new Tracked { Name = "x" } } };

    /// <summary>
    /// A resolver's converter of the nullable struct writes it, not the serializer's walk of the struct it
    /// holds: one that writes no <c>$id</c> lifts the refusal, as one in the options does. The check asked
    /// the options alone, and refused it (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_resolver_s_converter_of_a_nullable_struct_lifts_the_refusal()
    {
        var options = PreservingWith(new ResolverOf<HoldingTracked?>(o => JsonMetadataServices.CreateValueInfo<HoldingTracked?>(o, new HolderNameConverter())));
        var shared  = SharedHolder();
        var json    = JsonSerializer.Serialize(new Pair<OverHoldingNullableHolder> { First = OverHoldingNullableHolder.From(shared), Second = OverHoldingNullableHolder.From(shared) }, options);

        json.ShouldBe(JsonSerializer.Serialize(new Pair<HoldingNullableHolder> { First = shared, Second = shared }, options));
        JsonSerializer.Deserialize<Pair<OverHoldingNullableHolder>>(json, options)!.Second.Value.Holder!.Value.Inner!.Name.ShouldBe("x");
    }

    /// <summary>
    /// A resolver that puts the serializer's own converter in charge of the nullable struct, over one in the
    /// options, has it walked: the class inside has an <c>$id</c>, so the value object is refused. The check
    /// saw the options' converter, and two value objects around one instance wrote the <c>$id</c> twice
    /// (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_resolver_that_walks_a_nullable_struct_over_the_options_converter_is_refused()
    {
        var options = PreservingWith(
            new ResolverOf<HoldingTracked?>(o => JsonMetadataServices.CreateValueInfo<HoldingTracked?>(o, JsonMetadataServices.GetNullableConverter((JsonTypeInfo<HoldingTracked>)o.GetTypeInfo(typeof(HoldingTracked))))),
            new HolderNameConverter());
        var shared = SharedHolder();

        JsonSerializer.Serialize(new Pair<HoldingNullableHolder> { First = shared, Second = shared }, options).ShouldContain("\"$ref\"", customMessage: "the control: the resolver's contract walks the struct");
        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(new Pair<OverHoldingNullableHolder> { First = OverHoldingNullableHolder.From(shared), Second = OverHoldingNullableHolder.From(shared) }, options))
              .Message.ShouldContain("ReferenceHandler.Preserve");
    }

    /// <summary>Writes a <see cref="HoldingTracked"/> as its name, so nothing of it can have an <c>$id</c>.</summary>
    private sealed class HoldingNameConverter : JsonConverter<HoldingTracked>
    {
        public override HoldingTracked Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new() { Inner = new Tracked { Name = reader.GetString()! } };

        public override void Write(Utf8JsonWriter writer, HoldingTracked value, JsonSerializerOptions options) => writer.WriteStringValue(value.Inner?.Name);
    }

    /// <summary>
    /// A known limit: System.Text.Json's adapter of a nullable struct writes what the converter it was built
    /// over writes, and no public API names that converter, so the check walks the struct's own contract. A
    /// resolver that builds the nullable contract by hand over a converter that writes no <c>$id</c> is
    /// refused, though it could be written: loud, never a duplicate <c>$id</c>. The way out is the refusal's
    /// advice, a converter of the nullable type itself (<see cref="A_resolver_s_converter_of_a_nullable_struct_lifts_the_refusal"/>).
    /// </summary>
    [Fact]
    public void A_nullable_contract_built_by_hand_over_a_converter_of_its_own_is_refused_as_the_struct_s_walk()
    {
        var options = PreservingWith(new ResolverOf<HoldingTracked?>(o => JsonMetadataServices.CreateValueInfo<HoldingTracked?>(
            o, JsonMetadataServices.GetNullableConverter(JsonMetadataServices.CreateValueInfo<HoldingTracked>(o, new HoldingNameConverter())))));
        var shared = SharedHolder();

        JsonSerializer.Serialize(new Pair<HoldingNullableHolder> { First = shared, Second = shared }, options)
                      .ShouldNotContain("\"$ref\"", customMessage: "the control: the hand-built adapter writes the name, nothing that is shared");
        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(new Pair<OverHoldingNullableHolder> { First = OverHoldingNullableHolder.From(shared), Second = OverHoldingNullableHolder.From(shared) }, options))
              .Message.ShouldContain("ReferenceHandler.Preserve");
    }

    /// <summary>What the refusal advises: a converter of its own writes no <c>$id</c>, for the wrapped type and the value object alike.</summary>
    [Fact]
    public void A_converter_for_the_wrapped_type_lifts_the_refusal()
    {
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve, Converters = { new TrackedConverter() } };
        var shared  = new Tracked { Name = "x" };
        var json    = JsonSerializer.Serialize(new Pair<OverTracked> { First = OverTracked.From(shared), Second = OverTracked.From(shared) }, options);

        json.ShouldBe(JsonSerializer.Serialize(new Pair<Tracked> { First = shared, Second = shared }, options));
        JsonSerializer.Deserialize<Pair<OverTracked>>(json, options)!.Second.Value.Name.ShouldBe("x");
    }
}
