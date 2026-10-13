using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace MergePatch;

public enum EmailFault { NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value.Trim()) : Result.Error(EmailFault.NoAt);
}

public sealed record Address(string Street, string City);

/// <summary>Shape A's request: a reference type, a value object, a primitive, a class, a nullable value object.</summary>
public sealed record UpdateA(
    PatchA<string?> Name,
    PatchA<Email> Email,
    PatchA<int> Quantity,
    PatchA<Address?> Address);

/// <summary>What the write claims serialize: the ignore condition that leaves an absent patch out.</summary>
public sealed record OutgoingA(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PatchA<string?> Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PatchA<int> Quantity);

public sealed record BareOutgoingA(PatchA<string?> Name);

public sealed record PatchStates(string Name, string Email, string Quantity, string Address);

#if OPEN_GENERIC
public sealed record UpdateB(PatchB<string?> Name, PatchB<int> Quantity);
#endif

// The analyzers run here as in a consumer: a default patch is a state and must stay silent (claim 9).
public sealed class Defaults
{
    public PatchA<string?> Unassigned { get; set; }
    public static PatchA<int> Written() => default;
    public static PatchA<int> Made() => new();
}

[JsonSerializable(typeof(UpdateA))]
[JsonSerializable(typeof(Address))]   // a class a patch wraps must be in the context: the patch's converter hides it from the generator (claim 4)
[JsonSerializable(typeof(OutgoingA))]
[JsonSerializable(typeof(BareOutgoingA))]
[JsonSerializable(typeof(PatchStates))]
#if OPEN_GENERIC
[JsonSerializable(typeof(UpdateB))]
#endif
internal partial class SpikeJson : JsonSerializerContext;
