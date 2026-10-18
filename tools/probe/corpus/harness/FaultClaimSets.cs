// Fault types for C07-fault-responses-blanket (AspNetCore README#every-fault-set-at-once), each answered by one endpoint of its own beside the
// claim's: an enum (which no mapping for every set covers), a fault set with a member of no kind, and two fault sets the host's mapping
// refuses (one it throws for, one it gives a status that is no error).
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
namespace Probes.Faults;

public enum FxEnum { Lost, Taken }
public sealed partial class FxNoKind : IFaultSet<FxNoKind>
{
    public static readonly FxNoKind Fine = new(FaultKind.NotFound, null);
#pragma warning disable CMTK0014 // a member of no kind, on purpose: the claim is that it stops the start
    public static readonly FxNoKind Blank = new(default, null);
#pragma warning restore CMTK0014
    private FxNoKind(FaultKind kind, string? description) { Kind = kind; Description = description; }
    public FaultKind Kind { get; }
    public string? Description { get; }
}
public sealed partial class FxThrows : IFaultSet<FxThrows>
{
    public static readonly FxThrows Odd = new(FaultKind.Conflict, null);
    private FxThrows(FaultKind kind, string? description) { Kind = kind; Description = description; }
    public FaultKind Kind { get; }
    public string? Description { get; }
}
public sealed partial class FxStatus : IFaultSet<FxStatus>
{
    public static readonly FxStatus Moved = new(FaultKind.NotFound, null);
    private FxStatus(FaultKind kind, string? description) { Kind = kind; Description = description; }
    public FaultKind Kind { get; }
    public string? Description { get; }
}
/// <summary>What the application's mapping throws (set=mapping-throws).</summary>
public sealed class FxMappingRefused() : Exception("the mapping refuses FxThrows");

public static class FxSets
{
    public static Result<Fault<FxEnum>> Enum(int e) { if (e == 0) return Result<Fault<FxEnum>>.Success(); return Fault.Of(FxEnum.Lost); }
    public static Result<Fault<FxNoKind>> NoKind(int e) { if (e == 0) return Result<Fault<FxNoKind>>.Success(); return Fault.Of(FxNoKind.Fine); }
    public static Result<Fault<FxThrows>> Throws(int e) { if (e == 0) return Result<Fault<FxThrows>>.Success(); return Fault.Of(FxThrows.Odd); }
    public static Result<Fault<FxStatus>> Status(int e) { if (e == 0) return Result<Fault<FxStatus>>.Success(); return Fault.Of(FxStatus.Moved); }

    /// <summary>The minimal endpoint answering the row's set, at /c7x.</summary>
    public static void Map(IEndpointRouteBuilder b, string set)
    {
        switch (set)
        {
            case "enum-unmapped": b.MapGet("/c7x", (int e) => Enum(e).ToHttpResult()); break;
            case "member-without-kind": b.MapGet("/c7x", (int e) => NoKind(e).ToHttpResult()); break;
            case "mapping-throws": b.MapGet("/c7x", (int e) => Throws(e).ToHttpResult()); break;
            case "status-not-error": b.MapGet("/c7x", (int e) => Status(e).ToHttpResult()); break;
        }
    }
    /// <summary>The MVC controller answering the row's set.</summary>
    public static Type[] Controllers(string set) => set switch
    {
        "enum-unmapped" => [typeof(FxC07Enum)], "member-without-kind" => [typeof(FxC07NoKind)],
        "mapping-throws" => [typeof(FxC07Throws)], "status-not-error" => [typeof(FxC07Status)], _ => [],
    };
}
[ApiController] internal sealed class FxC07Enum : ControllerBase { [HttpGet("c/c7x/enum")] public Results<NoContent, FaultProblem<FxEnum>> Get(int e) => FxSets.Enum(e).ToHttpResult(); }
[ApiController] internal sealed class FxC07NoKind : ControllerBase { [HttpGet("c/c7x/nokind")] public Results<NoContent, FaultProblem<FxNoKind>> Get(int e) => FxSets.NoKind(e).ToHttpResult(); }
[ApiController] internal sealed class FxC07Throws : ControllerBase { [HttpGet("c/c7x/throws")] public Results<NoContent, FaultProblem<FxThrows>> Get(int e) => FxSets.Throws(e).ToHttpResult(); }
[ApiController] internal sealed class FxC07Status : ControllerBase { [HttpGet("c/c7x/status")] public Results<NoContent, FaultProblem<FxStatus>> Get(int e) => FxSets.Status(e).ToHttpResult(); }
