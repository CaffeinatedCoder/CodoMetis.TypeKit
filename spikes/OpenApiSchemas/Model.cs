using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Mvc;

// Each value object appears in the positions named beside it and nowhere else, so a position that
// a transformer never reaches shows up as that type's schema staying empty.

/// <summary>Property, list element, dictionary key, route parameter, top-level response body.</summary>
public readonly partial record struct OrderId : IValue<Guid>;

/// <summary>Nullable property and dictionary value only.</summary>
public readonly partial record struct Quantity : IValue<int>;

/// <summary>Property only.</summary>
public readonly partial record struct Price : IValue<decimal>;

public enum CodeFault { NotUpperCase }

/// <summary>Validated. Property and query parameter.</summary>
public readonly partial record struct Code : IValidatedValue<Code, string, CodeFault>
{
    public static Result<Code, CodeFault> Create(string value) =>
        value == value.ToUpperInvariant() ? new Code(value) : Result.Error(CodeFault.NotUpperCase);
}

/// <summary>List element only.</summary>
public readonly partial record struct ShipDate : IValue<DateOnly>;

/// <summary>Query parameter only.</summary>
public readonly partial record struct Since : IValue<DateTimeOffset>;

/// <summary>Property only; an enum as the wrapped type.</summary>
public readonly partial record struct Day : IValue<DayOfWeek>;

/// <summary>Nullable property only; a record class.</summary>
public sealed partial record Note : IValue<string>;

/// <summary>Property only; a wrapped type whose own schema a consumer's transformer supplies.</summary>
public readonly partial record struct Stamp : IValue<NodaTime.Instant>;

/// <summary>Controller parameters only (route, query) and a controller body property.</summary>
public readonly partial record struct TicketId : IValue<long>;

public sealed record OrderDto(
    OrderId Id,
    Price Price,
    Code Code,
    Day Day,
    Stamp Stamp,
    Quantity? Discount,
    Note? Note,
    List<OrderId> Related,
    List<ShipDate> Shipments,
    Dictionary<string, Quantity> PerWarehouse,
    Dictionary<OrderId, int> ByOrder,
    OrderId[] Siblings,
    Dictionary<string, List<OrderId>> Nested,
    List<Quantity?> Maybe);

public sealed record TicketDto(TicketId Id, string Title);

public sealed record NumbersDto(Quantity Count, Price Price, TicketId Ticket);

[ApiController]
[Route("tickets")]
public sealed class TicketsController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<TicketDto> Get(TicketId id, [FromQuery] TicketId? parent) => new TicketDto(id, "t");

    [HttpGet("search")]
    public int Search([FromQuery] TicketFilter filter) => 0;

    [HttpPost]
    public ActionResult<TicketId> Post(TicketDto ticket) => ticket.Id;
}

public sealed class TicketFilter
{
    public TicketId? Parent { get; set; }
    public Quantity Limit { get; set; }
}

public sealed record SearchQuery(OrderId Order, Quantity? Min);

public sealed record DayDto(Day Day);

public sealed record StampDto(Stamp Stamp, NodaTime.Instant Raw);
