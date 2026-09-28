// sample: CodoMetis.TypeKit.Generators/declaring
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

// end sample
using System.Globalization;

namespace ReadmeSamples.Generators;

// sample: CodoMetis.TypeKit.Generators/declaring
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Quantity : IValue<int>;

public enum EmailFault { Blank, NoAt, TooLong }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(EmailFault.Blank);

        var trimmed = value.Trim();
        if (!trimmed.Contains('@')) return Result.Error(EmailFault.NoAt);
        if (trimmed.Length > 254) return Result.Error(EmailFault.TooLong);

        return new Email(trimmed);   // the private constructor is generated
    }
}
// end sample

/// <summary>What the parsing and formatting sample formats.</summary>
public readonly partial record struct Amount : IValue<decimal>;

public static class Factories
{
    public static void Use(string input)
    {
        // sample: CodoMetis.TypeKit.Generators/factories
        var id = OrderId.New();                          // a version 7 Guid
        var quantity = Quantity.From(3);

        Result<Email, EmailFault> created = Email.Create(input);       // says what to fix
        Option<Email> maybe = Email.TryFrom(input);                    // is it valid?
        Email known = Email.FromKnownGood("ops@example.com");          // yours to guarantee; throws otherwise

        int three = quantity.Value;                      // 3
        bool same = quantity == Quantity.From(3);        // true, value equality
        string text = quantity.ToString();               // "3"
        // end sample
    }
}

public sealed class FactoryTests
{
    private const string Readme = "src/CodoMetis.TypeKit.Generators/README.md";

    [Fact]
    public void New_makes_a_version_7_Guid() => OrderId.New().Value.Version.ShouldBe(7);

    [Fact]
    public void Create_says_what_to_fix()
    {
        Email.Create("nobody").TryGetValue(out _, out var fault).ShouldBeFalse();

        fault.ShouldBe(EmailFault.NoAt);
    }

    [Fact]
    public void FromKnownGood_throws_otherwise() => Should.Throw<InvalidOperationException>(() => Email.FromKnownGood("nobody"));

    [Fact]
    public void The_members_are_what_the_comments_say()
    {
        var quantity = Quantity.From(3);

        quantity.Value.ShouldBeShownIn(Readme, value => value.ToString(CultureInfo.InvariantCulture));
        (quantity == Quantity.From(3)).ShouldBeShownIn(Readme, same => same ? "true" : "false");
        quantity.ToString().ShouldBeShownIn(Readme, text => $"\"{text}\"");
    }
}
