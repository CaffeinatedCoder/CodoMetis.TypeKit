using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ValidationFaults;

public enum EmailFault { Blank, NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(EmailFault.Blank);
        if (!value.Contains('@')) return Result.Error(EmailFault.NoAt);
        return new Email(value.Trim());
    }
}

public enum QuantityFault { NotPositive }

public readonly partial record struct Quantity : IValidatedValue<Quantity, int, QuantityFault>
{
    public static Result<Quantity, QuantityFault> Create(int value) =>
        value > 0 ? new Quantity(value) : Result.Error(QuantityFault.NotPositive);
}

public sealed record Signup(Email Email, [property: System.ComponentModel.DataAnnotations.MinLength(2)] string Name, Quantity Seats);

/// <summary>
/// The plumbing probe: a converter that refuses as the generated one does, but also attaches its fault
/// to the exception, to see whether the same instance reaches a hook the package could use.
/// </summary>
[JsonConverter(typeof(ProbeConverter))]
public readonly record struct Probe(string Text);

public sealed record ProbeBody(Probe Probe);

public sealed class ProbeConverter : JsonConverter<Probe>
{
    public static readonly object Marker = "CodoMetis.TypeKit.Fault";

    public override Probe Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString()!;
        if (text.Contains('@')) return new Probe(text);
        var refused = new JsonException("Probe refused the JSON value (NoAt).");
        refused.Data[Marker] = "NoAt";
        throw refused;
    }

    public override void Write(Utf8JsonWriter writer, Probe value, JsonSerializerOptions options) => writer.WriteStringValue(value.Text);
}
