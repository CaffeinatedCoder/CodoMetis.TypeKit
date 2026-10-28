using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace AspectTestingSpike;

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
