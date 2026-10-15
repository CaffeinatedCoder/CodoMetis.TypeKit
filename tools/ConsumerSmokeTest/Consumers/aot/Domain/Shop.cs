using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;

namespace Shop;

// A closed set: its name in JSON, as a value and a dictionary key, a string enum in OpenAPI, and a
// text column in EF Core, a JSON column's included.
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
}

// A fault set: each member's kind gives its status, its description the problem's detail.
public sealed partial class AccountFault : IFaultSet<AccountFault>
{
    public static readonly AccountFault NotFound = new(FaultKind.NotFound, "The account does not exist.");
    public static readonly AccountFault EmailTaken = new(FaultKind.Conflict, null);

    private AccountFault(FaultKind kind, string? description) { Kind = kind; Description = description; }

    public FaultKind Kind { get; }
    public string? Description { get; }
}

// A rule behind a fault, nested, so its name is read after its nesting chain at run time.
public static class Billing
{
    public enum Rule { Reserved }
}

// One per JSON strategy: a reader method (Guid), the serializer's contract for a number, the
// fallback with a built-in converter (Uri, an enum), a string, and parsing through a TypeConverter.
public readonly partial record struct OrderId : IValue<Guid>;
public readonly partial record struct Quantity : IValue<int>;
public readonly partial record struct Amount : IValue<decimal>;
public readonly partial record struct Link : IValue<Uri>;
public readonly partial record struct Weekday : IValue<DayOfWeek>;
public readonly partial record struct Name : IValue<string>;
public readonly partial record struct Day : IValue<NodaTime.LocalDate>;
public readonly partial record struct Due : IValue<NodaTime.Instant>;
public readonly partial record struct ShipmentNo : IValue<int>;

public enum CodeFault { Blank, NotUpperCase }

// A record class: EF's compiled model cannot write the sentinel of a struct that wraps a reference
// type (its default converts to null), so a string-backed value object on an entity is a class here.
public sealed partial record Code : IValidatedValue<Code, string, CodeFault>
{
    public static Result<Code, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);
        if (value != value.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);
        return new Code(value);
    }
}
