using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Mvc;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

// Every value-object shape has a control beside it that uses the wrapped type directly, under the
// same JSON property or parameter name. The tests assert that the two publish the same schema, so
// they follow whatever ASP.NET publishes for the wrapped type rather than a copy of it.

/// <summary>One property per wrapped-type family, each a value object.</summary>
public sealed record ProbeDocument(
    ProbeId Id,
    ProbeCount Count,
    ProbeAmount Amount,
    ProbeFlag Flag,
    ProbeTimestamp Timestamp,
    ProbeDate Date,
    ProbeMoment Moment,
    ProbeTime Time,
    ProbeUri Uri,
    ProbeWeekday Weekday,
    ProbeLabel Label,
    ProbeCode Code,
    ProbePercentage Percentage,
    ProbeCustomerId CustomerId,
    ProbeContainer.NestedId Nested,
    ProbeCount? MaybeCount,
    ProbeLabel? MaybeLabel,
    List<ProbeId> Ids,
    ProbeId[] IdArray,
    IReadOnlyList<ProbeId> ReadOnlyIds,
    HashSet<ProbeId> IdSet,
    Dictionary<string, ProbeCount> Counts,
    Dictionary<string, List<ProbeId>> NestedIds,
    List<List<ProbeId>> ListOfLists,
    List<ProbeCount?> MaybeCounts,
    ProbeCount?[] MaybeCountArray,
    Dictionary<string, ProbeCount?> MaybeCountsByKey,
    ProbeName Name,
    ProbeName? MaybeName,
    ProbeId? MaybeId,
    ProbeWeekday? MaybeWeekday);

/// <summary>The same names as <see cref="ProbeDocument"/>, with the wrapped types.</summary>
public sealed record ControlDocument(
    Guid Id,
    int Count,
    decimal Amount,
    bool Flag,
    DateTime Timestamp,
    DateOnly Date,
    DateTimeOffset Moment,
    TimeOnly Time,
    Uri Uri,
    DayOfWeek Weekday,
    string Label,
    string Code,
    int Percentage,
    Guid CustomerId,
    int Nested,
    int? MaybeCount,
    string? MaybeLabel,
    List<Guid> Ids,
    Dictionary<string, int> Counts,
    List<int?> MaybeCounts,
    int?[] MaybeCountArray,
    Dictionary<string, int?> MaybeCountsByKey,
    string Name,
    string? MaybeName,
    Guid? MaybeId,
    DayOfWeek? MaybeWeekday);

public sealed record ProbeSearch(ProbeId Customer, ProbeCount? Limit);

public sealed record ControlSearch(Guid Customer, int? Limit);

public sealed record ProbeNumbers(ProbeCount Count, ProbeAmount Amount, ProbePercentage Percentage);

public sealed record ProbeTimes(ProbeInstant Instant);

public sealed record ProbeDay(ProbeWeekday Weekday);

public sealed class ProbeFilter
{
    public ProbeId? Customer { get; set; }
    public ProbeCount Limit { get; set; }
}

public sealed class ControlFilter
{
    public Guid? Customer { get; set; }
    public int Limit { get; set; }
}

[ApiController]
[Route("mvc/probes")]
public sealed class ProbeController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<ProbeDocument> Get(ProbeId id, [FromQuery] ProbeCount? limit) => NotFound();

    [HttpGet("search")]
    public int Search([FromQuery] ProbeFilter filter) => 0;

    [HttpGet("by-ids")]
    public int ByIds([FromQuery] ProbeId[] ids) => ids.Length;

    [HttpPost]
    public ActionResult<ProbeId> Post(ProbeDocument document) => document.Id;
}

[ApiController]
[Route("mvc/controls")]
public sealed class ControlController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<ControlDocument> Get(Guid id, [FromQuery] int? limit) => NotFound();

    [HttpGet("search")]
    public int Search([FromQuery] ControlFilter filter) => 0;

    [HttpGet("by-ids")]
    public int ByIds([FromQuery] Guid[] ids) => ids.Length;
}
