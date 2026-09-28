using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Root.Intro;

public enum EmailFault { NoAt }

// sample: README/intro
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value.Trim()) : Result.Error(EmailFault.NoAt);
}
// end sample
