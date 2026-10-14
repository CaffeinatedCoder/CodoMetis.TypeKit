using System.Text.Json;
using System.Text.Json.Serialization;
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

public readonly partial record struct OverTracked : IValue<Tracked>;

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
