using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using System.Diagnostics;
using System.Numerics;

namespace ReadmeSamples.Docs.ValueObjects;

// sample: value-objects/struct-or-class
public readonly partial record struct Sku : IValue<string>;   // a struct: never null, and default is CMTK0001

public sealed partial record Note : IValue<string>;           // a class: for a string column in an EF Core compiled model
// end sample

// sample: value-objects/identifiers
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct InvoiceNo : IValue<long>;
// end sample

// sample: value-objects/email
public enum EmailFault { Blank, NoAt, TooLong }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(EmailFault.Blank);

        var normalised = value.Trim().ToLowerInvariant();   // equal addresses hold the same wrapped value
        if (!normalised.Contains('@')) return Result.Error(EmailFault.NoAt);
        if (normalised.Length > 254) return Result.Error(EmailFault.TooLong);

        return new Email(normalised);                       // the private constructor is generated
    }

    public string Domain => Value[(Value.IndexOf('@') + 1)..];   // computed from Value: fine. A second field is CMTK1012
}
// end sample

// sample: value-objects/fault-with-data
public abstract record QuantityFault
{
    public sealed record NotPositive : QuantityFault;

    public sealed record AboveLimit(int Limit) : QuantityFault;   // the limit, never the value given
}

public readonly partial record struct Quantity : IValidatedValue<Quantity, int, QuantityFault>
{
    public const int Limit = 1_000;

    public static Result<Quantity, QuantityFault> Create(int value)
    {
        if (value <= 0) return Result.Error<QuantityFault>(new QuantityFault.NotPositive());
        if (value > Limit) return Result.Error<QuantityFault>(new QuantityFault.AboveLimit(Limit));

        return new Quantity(value);
    }
}
// end sample

// sample: value-objects/numbers
public readonly partial record struct Weight : IValue<decimal>;   // any decimal, with MinValue and MaxValue

public enum PercentageFault { BelowZero, AboveHundred }

public readonly partial record struct Percentage : IValidatedValue<Percentage, int, PercentageFault>
{
    public static Result<Percentage, PercentageFault> Create(int value)
    {
        if (value < 0) return Result.Error(PercentageFault.BelowZero);
        if (value > 100) return Result.Error(PercentageFault.AboveHundred);

        return new Percentage(value);
    }

    public override string ToString() => $"{Value} %";   // kept; then no formatting interface is generated, so interpolation reaches it too
}
// end sample

// sample: value-objects/arithmetic
public readonly partial record struct Money : IValue<decimal>,
    IAdditionOperators<Money, Money, Money>,
    ISubtractionOperators<Money, Money, Money>,
    IMultiplyOperators<Money, decimal, Money>,   // Money * 3m
    IDivisionOperators<Money, Money, decimal>;   // two amounts make a ratio
// end sample

// sample: value-objects/other-wrapped-types
public readonly partial record struct Weekday : IValue<DayOfWeek>;

public readonly partial record struct DueOn : IValue<DateOnly>;

public readonly partial record struct Homepage : IValue<Uri>;
// end sample

// sample: value-objects/enum-numbers
public enum DayFault { NoSuchDay }

public readonly partial record struct ScheduleDay : IValidatedValue<ScheduleDay, DayOfWeek, DayFault>
{
    public static Result<ScheduleDay, DayFault> Create(DayOfWeek value) =>
        Enum.IsDefined(value) ? new ScheduleDay(value) : Result.Error(DayFault.NoSuchDay);
}
// end sample

// sample: value-objects/own-compare-to
public readonly partial record struct Priority : IValue<int>
{
    public int CompareTo(Priority other) => other.Value.CompareTo(Value);   // 1 ranks above 2; the operators follow
}
// end sample

// sample: value-objects/nested
public sealed partial class Shipment
{
    public readonly partial record struct Id : IValue<Guid>;   // Shipment.Id; its companion class is ShipmentIdExtensions

    public required Id Key { get; init; }
}
// end sample

/// <summary>The request the factory sample reads: a bound shape, whose text is input.</summary>
public sealed record RegisterRequest(string Email);

public static class Usage
{
    public static (OrderId Id, OrderId Imported, InvoiceNo No, int Version) Identifiers()
    {
        // sample: value-objects/identifiers-use
        var id = OrderId.New();                                                  // a version 7 Guid, which sorts by creation time
        var imported = OrderId.New(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));   // its timestamp, for a migrated record
        var no = InvoiceNo.From(42);

        int version = id.Value.Version;                                          // 7
        // end sample

        return (id, imported, no, version);
    }

    public static (string Lowered, bool Same, string Domain) Emails()
    {
        // sample: value-objects/email-use
        string lowered = Email.Create(" Ops@Example.com ").Match(email => email.Value, fault => fault.ToString());   // "ops@example.com"
        bool same = Email.FromKnownGood("Ops@Example.com") == Email.FromKnownGood("ops@example.com");                 // true
        string domain = Email.FromKnownGood("ops@example.com").Domain;                                                 // "example.com"
        // end sample

        return (lowered, same, domain);
    }

    // sample: value-objects/fault-with-data-use
    static string Describe(QuantityFault fault) => fault switch
    {
        QuantityFault.NotPositive => "must be positive",
        QuantityFault.AboveLimit { Limit: var limit } => $"at most {limit}",
        _ => throw new UnreachableException(),
    };

    // end sample
    public static string Quantities()
    {
        // sample: value-objects/fault-with-data-use
        string explained = Quantity.Create(5_000).Match(quantity => quantity.ToString(), Describe);   // "at most 1000"
        // end sample

        return explained;
    }

    public static (bool Widest, string Shown, bool Ordered) Numbers()
    {
        // sample: value-objects/numbers-use
        bool widest = Weight.MaxValue.Value == decimal.MaxValue;   // true
        string shown = $"{Percentage.FromKnownGood(42)}";          // "42 %"
        bool ordered = Percentage.FromKnownGood(5) < Percentage.FromKnownGood(50);   // true, the wrapped type's ordering
        // end sample

        return (widest, shown, ordered);
    }

    public static (Money Total, Money Tripled, decimal Share) Arithmetic()
    {
        // sample: value-objects/arithmetic-use
        Money total = Money.From(10.50m) + Money.From(4.50m);   // 15.00
        Money tripled = total * 3m;                             // 45.00
        decimal share = Money.From(3m) / Money.From(4m);        // 0.75
        // end sample

        return (total, tripled, share);
    }

    public static (Weekday Day, Weekday Fifth, DueOn Due, bool Sooner, bool Sortable) OtherWrappedTypes()
    {
        // sample: value-objects/other-wrapped-types-use
        var day = Weekday.Parse("Friday", null);          // Friday
        var fifth = Weekday.Parse("5", null);             // Friday, as Enum.TryParse reads a number
        var due = DueOn.Parse("2026-10-13", null);        // 2026-10-13
        bool sooner = DueOn.From(new DateOnly(2026, 1, 1)) < due;                   // true
        bool sortable = typeof(IComparable<Homepage>).IsAssignableFrom(typeof(Homepage));   // false, a Uri has no ordering
        // end sample

        return (day, fifth, due, sooner, sortable);
    }

    public static (Weekday Ninth, bool Scheduled, bool Friday) EnumNumbers()
    {
        // sample: value-objects/enum-numbers-use
        var ninth = Weekday.Parse("9", null);                              // 9, a Weekday no member names
        bool scheduled = ScheduleDay.TryParse("9", null, out _);           // false, and JSON's 9 throws JsonException naming ScheduleDay and NoSuchDay
        bool friday = ScheduleDay.TryParse("Friday", null, out _);         // true
        // end sample

        return (ninth, scheduled, friday);
    }

    public static void Factories(RegisterRequest request)
    {
        // sample: value-objects/factories
        Result<Email, EmailFault> fromRequest = Email.Create(request.Email);   // the fault says which rule refused it
        Option<Email> maybe = Email.TryFrom(request.Email);                    // is it valid?
        Email support = Email.FromKnownGood("support@example.com");            // a refusal here is a bug at this line
        InvoiceNo first = InvoiceNo.From(1);                                   // no rules
        Result<Email, EmailFault> stillValid = support.Revalidate();           // today's rules, applied again
        // end sample

        _ = (fromRequest, maybe, first, stillValid);
    }

    public static (Priority First, bool Urgent) Priorities()
    {
        // sample: value-objects/own-compare-to-use
        var first = new[] { Priority.From(3), Priority.From(1) }.Min();   // 3
        bool urgent = Priority.From(1) > Priority.From(2);                // true
        // end sample

        return (first, urgent);
    }

    // sample: value-objects/generic
    static Result<IReadOnlyList<TSelf>, TFault> CreateAll<TSelf, T, TFault>(IEnumerable<T> values)
        where TSelf : IValidatedValue<TSelf, T, TFault>
        where T : notnull
        where TFault : notnull =>
        values.Traverse(TSelf.Create);   // the first fault stops it; nothing after it is created

    // end sample
    public static string Generic()
    {
        // sample: value-objects/generic-use
        string outcome = CreateAll<Email, string, EmailFault>(["ops@example.com", "nobody"]).Match(list => $"{list.Count} valid", fault => $"refused: {fault}");   // "refused: NoAt"
        // end sample

        return outcome;
    }
}

public sealed class ValueObjectsTests
{
    private const string Readme = "docs/value-objects.md";

    private static string Quoted(string text) => $"\"{text}\"";

    private static string Bool(bool value) => value ? "true" : "false";

    [Fact]
    public void An_identifier_is_what_the_comments_say()
    {
        var (id, imported, no, version) = Usage.Identifiers();

        id.Value.Version.ShouldBeShownIn(Readme, v => v.ToString());
        version.ShouldBe(7);
        imported.Value.Version.ShouldBe(7);
        no.Value.ShouldBe(42);
        Sku.From("A-1").ShouldBe(Sku.From("A-1"));
        Note.From("n").ShouldBe(Note.From("n"));
    }

    [Fact]
    public void An_email_is_normalised_as_the_comments_say()
    {
        var (lowered, same, domain) = Usage.Emails();

        Email.Create(" Ops@Example.com ").Match(email => email.Value, fault => fault.ToString()).ShouldBeShownIn(Readme, Quoted);
        (Email.FromKnownGood("Ops@Example.com") == Email.FromKnownGood("ops@example.com")).ShouldBeShownIn(Readme, Bool);
        Email.FromKnownGood("ops@example.com").Domain.ShouldBeShownIn(Readme, Quoted);
        (lowered, same, domain).ShouldBe(("ops@example.com", true, "example.com"));

        Email.Create("").TryGetValue(out _, out var blank).ShouldBeFalse();
        blank.ShouldBe(EmailFault.Blank);
        Email.Create(new string('a', 250) + "@x.test").TryGetValue(out _, out var tooLong).ShouldBeFalse();
        tooLong.ShouldBe(EmailFault.TooLong);
    }

    [Fact]
    public void A_fault_carries_the_rule_s_data_as_the_comment_says()
    {
        Usage.Quantities().ShouldBeShownIn(Readme, Quoted, "Quantity.Create(5_000).Match(quantity => quantity.ToString(), Describe)");

        Quantity.Create(0).TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBeOfType<QuantityFault.NotPositive>();
        Quantity.Create(5_000).TryGetValue(out _, out var above).ShouldBeFalse();
        above.ShouldBe(new QuantityFault.AboveLimit(1_000));
    }

    [Fact]
    public void Numbers_are_what_the_comments_say()
    {
        var (widest, shown, ordered) = Usage.Numbers();

        (Weight.MaxValue.Value == decimal.MaxValue).ShouldBeShownIn(Readme, Bool);
        $"{Percentage.FromKnownGood(42)}".ShouldBeShownIn(Readme, Quoted);
        (Percentage.FromKnownGood(5) < Percentage.FromKnownGood(50)).ShouldBeShownIn(Readme, Bool);
        (widest, shown, ordered).ShouldBe((true, "42 %", true));

        typeof(Percentage).GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMinMaxValue<>)).ShouldBeFalse("a validated value object has no MinValue and MaxValue");
        typeof(Weight).GetInterfaces().ShouldContain(typeof(IMinMaxValue<Weight>));
        Weight.From(1.5m).ToString().ShouldBe("1.5");
    }

    [Fact]
    public void Arithmetic_is_what_the_comments_say()
    {
        var (total, tripled, share) = Usage.Arithmetic();

        (Money.From(10.50m) + Money.From(4.50m)).ShouldBeShownIn(Readme, money => money.ToString());
        (total * 3m).ShouldBeShownIn(Readme, money => money.ToString());
        (Money.From(3m) / Money.From(4m)).ShouldBeShownIn(Readme, ratio => ratio.ToString(System.Globalization.CultureInfo.InvariantCulture));
        (total, tripled, share).ShouldBe((Money.From(15.00m), Money.From(45.00m), 0.75m));
        Should.Throw<OverflowException>(() => unchecked(Money.From(decimal.MaxValue) + Money.From(1m)), "a decimal overflows in either context");
    }

    [Fact]
    public void Other_wrapped_types_parse_and_compare_as_the_comments_say()
    {
        var (day, fifth, due, sooner, sortable) = Usage.OtherWrappedTypes();

        Weekday.Parse("Friday", null).ShouldBeShownIn(Readme, d => d.Value.ToString());
        Weekday.Parse("5", null).ShouldBeShownIn(Readme, d => d.Value.ToString());
        DueOn.Parse("2026-10-13", null).ShouldBeShownIn(Readme, d => d.Value.ToString("yyyy-MM-dd"));
        (DueOn.From(new DateOnly(2026, 1, 1)) < due).ShouldBeShownIn(Readme, Bool);
        typeof(IComparable<Homepage>).IsAssignableFrom(typeof(Homepage)).ShouldBeShownIn(Readme, Bool);
        (day, fifth, sooner, sortable).ShouldBe((Weekday.From(DayOfWeek.Friday), Weekday.From(DayOfWeek.Friday), true, false));
        Homepage.Parse("https://example.com/", null).Value.ShouldBe(new Uri("https://example.com/"));
    }

    /// <summary>
    /// A plain value object over an enum reads a number no member names, by every way in, as the enum does;
    /// one whose <c>Create</c> checks <c>Enum.IsDefined</c> refuses it by every way in.
    /// </summary>
    [Fact]
    public void A_number_no_member_names_is_read_by_a_plain_value_object_and_refused_by_a_validated_one()
    {
        var (ninth, scheduled, friday) = Usage.EnumNumbers();

        Weekday.Parse("9", null).ShouldBeShownIn(Readme, d => ((int)d.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        ScheduleDay.TryParse("9", null, out _).ShouldBeShownIn(Readme, Bool);
        ScheduleDay.TryParse("Friday", null, out _).ShouldBeShownIn(Readme, Bool);
        (ninth.Value, scheduled, friday).ShouldBe(((DayOfWeek)9, false, true));

        System.Text.Json.JsonSerializer.Deserialize<Weekday>("9").Value.ShouldBe((DayOfWeek)9, "JSON reads what the enum reads");
        var refused = Should.Throw<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<ScheduleDay>("9"));
        refused.Message.ShouldContain(nameof(ScheduleDay));
        refused.Message.ShouldContain(nameof(DayFault.NoSuchDay));
        ScheduleDay.TryFrom((DayOfWeek)9).IsNone().ShouldBeTrue();
    }

    [Fact]
    public void The_factories_answer_as_the_table_says()
    {
        Usage.Factories(new RegisterRequest("nobody"));

        Email.Create("nobody").TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBe(EmailFault.NoAt);
        Email.TryFrom("nobody").IsNone().ShouldBeTrue();
        var typed = "nobody";
        var refusal = Should.Throw<InvalidOperationException>(() => Email.FromKnownGood(typed)).Message;
        refusal.ShouldContain("typed", Case.Sensitive, "the call site's expression");
        refusal.ShouldNotContain("nobody", Case.Sensitive, "never the value");
        Email.FromKnownGood("support@example.com").Revalidate().TryGetValue(out var revalidated, out _).ShouldBeTrue();
        revalidated.ShouldBe(Email.FromKnownGood("support@example.com"));
    }

    [Fact]
    public void A_hand_written_CompareTo_orders_as_the_comments_say()
    {
        var (first, urgent) = Usage.Priorities();

        new[] { Priority.From(3), Priority.From(1) }.Min().ShouldBeShownIn(Readme, p => p.Value.ToString());
        (Priority.From(1) > Priority.From(2)).ShouldBeShownIn(Readme, Bool);
        (first, urgent).ShouldBe((Priority.From(3), true));
    }

    [Fact]
    public void A_nested_value_object_has_its_companion_named_after_the_chain()
    {
        var shipment = new Shipment { Key = Shipment.Id.New() };

        shipment.Key.Value.Version.ShouldBe(7);
        typeof(Shipment).Assembly.GetType("ReadmeSamples.Docs.ValueObjects.ShipmentIdExtensions").ShouldNotBeNull();
    }

    [Fact]
    public void Generic_code_stops_at_the_first_fault_as_the_comment_says() =>
        Usage.Generic().ShouldBeShownIn(Readme, Quoted, "CreateAll<Email, string, EmailFault>([\"ops@example.com\", \"nobody\"]).Match(list => $\"{list.Count} valid\", fault => $\"refused: {fault}\")");
}
