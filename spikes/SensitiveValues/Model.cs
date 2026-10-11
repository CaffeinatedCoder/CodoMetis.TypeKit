using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Extensions.Compliance.Classification;

namespace SensitiveValues;

public enum EmailFault { NoAt }

/// <summary>As generated: prints its value.</summary>
public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) => value.Contains('@') ? new Email(value) : Result.Error(EmailFault.NoAt);
}

/// <summary>What a user can write today: the ToString() seam, which the formatting interfaces then defer to.</summary>
public readonly partial record struct MaskedEmail : IValidatedValue<MaskedEmail, string, EmailFault>
{
    public static Result<MaskedEmail, EmailFault> Create(string value) => value.Contains('@') ? new MaskedEmail(value) : Result.Error(EmailFault.NoAt);

    public override string ToString() => "MaskedEmail(***)";
}

/// <summary>The masked value object, classified on the type for Microsoft.Extensions.Compliance.</summary>
[PrivateData]
public readonly partial record struct ClassifiedEmail : IValidatedValue<ClassifiedEmail, string, EmailFault>
{
    public static Result<ClassifiedEmail, EmailFault> Create(string value) => value.Contains('@') ? new ClassifiedEmail(value) : Result.Error(EmailFault.NoAt);

    public override string ToString() => "ClassifiedEmail(***)";
}

public static class Taxonomy
{
    public static DataClassification Private => new("Spike", "Private");
}

public sealed class PrivateDataAttribute : DataClassificationAttribute
{
    public PrivateDataAttribute() : base(Taxonomy.Private) { }
}

public sealed record Signup(Email Email, MaskedEmail Masked, string Name);
