using Spike.Abstractions;

namespace Spike.Domain;

public readonly partial record struct OrderId : IValue<Guid>;

public enum EmailFault { Blank, NoAt }

public readonly partial record struct EmailAddress : IValidatedValue<EmailAddress, string, EmailFault>
{
    public static Result<EmailAddress, EmailFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result<EmailAddress, EmailFault>.Fail(EmailFault.Blank)
        : !value.Contains('@')          ? Result<EmailAddress, EmailFault>.Fail(EmailFault.NoAt)
        : new EmailAddress(value.Trim());
}
