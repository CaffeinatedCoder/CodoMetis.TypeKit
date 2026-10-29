using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Root.Intro;

public enum EmailFault { NoAt }

// sample: README/intro
public readonly partial record struct OrderId : IValue<Guid>;
// end sample

// sample: README/intro-validated
public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value.Trim()) : Result.Error(EmailFault.NoAt);
}
// end sample

public readonly partial record struct CustomerId : IValue<Guid>;

public sealed record Customer(CustomerId Id, Email Email);

public enum SignUpFault { InvalidEmail, EmailTaken }

public sealed class IntroUsage(List<Customer> customers)
{
    public string Greet(Email email)
    {
        // sample: README/intro-option
        Option<Customer> found = customers.FirstOrNone(c => c.Email == email);

        string greeting = found.Match(
            customer => $"Welcome back, {customer.Email}",
            () => "Please sign up first");
        // end sample

        return greeting;
    }

    public string SignUp(string input)
    {
        // sample: README/intro-result
        Result<Customer, SignUpFault> signedUp = Email.Create(input)
            .MapError(_ => SignUpFault.InvalidEmail)
            .Ensure(email => !customers.Exists(c => c.Email == email), SignUpFault.EmailTaken)
            .Map(email => new Customer(CustomerId.New(), email));

        string reply = signedUp.Match(
            customer => $"Welcome, {customer.Email}",
            fault => $"Sign-up failed: {fault}");
        // end sample

        return reply;
    }
}

// sample: README/closed-set
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}
// end sample
