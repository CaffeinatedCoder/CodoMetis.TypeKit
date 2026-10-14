using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using NodaTime;

namespace CodoMetis.TypeKit.Generators.Tests;

public enum ThrowingFault { Refused }

/// <summary>Parse strategy: the wrapped type's static <c>Parse(string)</c>.</summary>
public readonly partial record struct ThrowingSku : IValidatedValue<ThrowingSku, ProbeSkuText, ThrowingFault>
{
    public static Result<ThrowingSku, ThrowingFault> Create(ProbeSkuText value) =>
        value.Text == ThrowingCreateTests.Sku ? throw new InvalidOperationException("Create threw.") : new ThrowingSku(value);
}

/// <summary>Parse strategy: the wrapped type's string constructor.</summary>
public readonly partial record struct ThrowingIsbn : IValidatedValue<ThrowingIsbn, ProbeIsbnText, ThrowingFault>
{
    public static Result<ThrowingIsbn, ThrowingFault> Create(ProbeIsbnText value) =>
        value.Text == ThrowingCreateTests.Isbn ? throw new InvalidOperationException("Create threw.") : new ThrowingIsbn(value);
}

/// <summary>Parse strategy: the wrapped type's <c>[TypeConverter]</c>.</summary>
public readonly partial record struct ThrowingDate : IValidatedValue<ThrowingDate, LocalDate, ThrowingFault>
{
    public static Result<ThrowingDate, ThrowingFault> Create(LocalDate value) =>
        value == new LocalDate(2026, 10, 13) ? throw new InvalidOperationException("Create threw.") : new ThrowingDate(value);
}

/// <summary>Parse strategy: the wrapped type's own <c>TryParse</c>, which already let it through.</summary>
public readonly partial record struct ThrowingCount : IValidatedValue<ThrowingCount, int, ThrowingFault>
{
    public static Result<ThrowingCount, ThrowingFault> Create(int value) =>
        value == 13 ? throw new InvalidOperationException("Create threw.") : new ThrowingCount(value);
}

/// <summary>
/// An exception <c>Create</c> throws is not a refusal, and leaves <c>TryParse</c> as it leaves
/// <c>Parse</c>, whatever the wrapped type parses through. Over a wrapped type without a <c>TryParse</c>
/// of its own (a type converter, a static <c>Parse</c>, a string constructor) a bare <c>catch</c> around
/// the inner parse and the wrapping made it a <c>false</c>, while over an <c>int</c> it was thrown. Only
/// the wrapped type's refusal of the text is caught now.
/// </summary>
public sealed class ThrowingCreateTests
{
    public const string Sku = "THROW00";
    public const string Isbn = "THROW00000000";

    public static TheoryData<string> Strategies => ["static Parse", "string constructor", "type converter", "TryParse"];

    private static readonly Dictionary<string, (Func<string, object> Parse, Func<string, bool> TryParse, string Throwing, string Fine, string Unreadable)> Cases = new()
    {
        ["static Parse"] = (text => ThrowingSku.Parse(text, null), text => ThrowingSku.TryParse(text, null, out _), Sku, "ABC1234", "short"),
        ["string constructor"] = (text => ThrowingIsbn.Parse(text, null), text => ThrowingIsbn.TryParse(text, null, out _), Isbn, "9780000000001", "short"),
        ["type converter"] = (text => ThrowingDate.Parse(text, null), text => ThrowingDate.TryParse(text, null, out _), "2026-10-13", "2026-10-14", "not a date"),
        ["TryParse"] = (text => ThrowingCount.Parse(text, null), text => ThrowingCount.TryParse(text, null, out _), "13", "14", "x"),
    };

    [Theory]
    [MemberData(nameof(Strategies))]
    public void An_exception_from_Create_leaves_TryParse_as_it_leaves_Parse(string strategy)
    {
        var (parse, tryParse, throwing, fine, unreadable) = Cases[strategy];

        Should.Throw<InvalidOperationException>(() => parse(throwing)).Message.ShouldBe("Create threw.");
        Should.Throw<InvalidOperationException>(() => tryParse(throwing)).Message.ShouldBe("Create threw.");

        tryParse(fine).ShouldBeTrue();
        tryParse(unreadable).ShouldBeFalse("text the wrapped type cannot read is still a false");
        Should.Throw<FormatException>(() => parse(unreadable));
    }
}
