// Composed-fault errors for C03/C05 (harness). e: 1 first set, 2 last set, 3 last set with detail, 4/5/6 first set with a rule (enum / closed set / nested enum).
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
namespace Probes;

public sealed partial class KFault : IFaultSet<KFault>
{
    public static readonly KFault Clash = new(FaultKind.Conflict, null);
    public static readonly KFault Lost = new(FaultKind.NotFound, null);
    private KFault(FaultKind kind, string? description) { Kind = kind; Description = description; }
    public FaultKind Kind { get; }
    public string? Description { get; }
}

public static class Cmp
{
    public const string Detail = "It is at version 3, not 2.";
    static Fault<HFault> Rule(int e) => e switch
    {
        4 => Fault.Of(HFault.Clash).WithRule(PRule.A),
        5 => Fault.Of(HFault.Clash).WithRule(RuleSet.X),
        6 => Fault.Of(HFault.Clash).WithRule(Holder.NestedRule.N),
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
    public static Result<Fault<HFault, GFault>> Two(int e) => e switch
    {
        1 => Fault<HFault, GFault>.Of(HFault.Missing),
        2 => Fault<HFault, GFault>.Of(GFault.Raced),
        3 => Fault<HFault, GFault>.Of(Fault.Of(GFault.Old, Detail)),
        >= 4 and <= 6 => Fault<HFault, GFault>.Of(Rule(e)),
        _ => Result<Fault<HFault, GFault>>.Success(),
    };
    public static Result<Fault<HFault, KFault>> TwoK(int e) => e switch
    {
        1 => Fault<HFault, KFault>.Of(HFault.Missing),
        2 => Fault<HFault, KFault>.Of(KFault.Clash),
        3 => Fault<HFault, KFault>.Of(Fault.Of(KFault.Lost, Detail)),
        >= 4 and <= 6 => Fault<HFault, KFault>.Of(Rule(e)),
        _ => Result<Fault<HFault, KFault>>.Success(),
    };
    public static Result<Fault<HFault, GFault, SFault>> Three(int e) => e switch
    {
        1 => Fault<HFault, GFault, SFault>.Of(HFault.Missing),
        2 => Fault<HFault, GFault, SFault>.Of(SFault.First),
        3 => Fault<HFault, GFault, SFault>.Of(Fault.Of(SFault.Second, Detail)),
        >= 4 and <= 6 => Fault<HFault, GFault, SFault>.Of(Rule(e)),
        _ => Result<Fault<HFault, GFault, SFault>>.Success(),
    };
    public static Result<Fault<HFault, KFault, SFault>> ThreeK(int e) => e switch
    {
        1 => Fault<HFault, KFault, SFault>.Of(HFault.Missing),
        2 => Fault<HFault, KFault, SFault>.Of(SFault.First),
        3 => Fault<HFault, KFault, SFault>.Of(Fault.Of(SFault.Second, Detail)),
        >= 4 and <= 6 => Fault<HFault, KFault, SFault>.Of(Rule(e)),
        _ => Result<Fault<HFault, KFault, SFault>>.Success(),
    };

    // ---- expectations
    /// <summary>(fault type name, member name, status) of the error e answers, by arity and variant; variant group moves HFault.Clash to 422.</summary>
    public static (string Type, string Member, int Status) Expect(int arity, string variant, int e)
    {
        var k = variant == "dup";
        int clash = variant == "group" ? 422 : 409;
        return e switch
        {
            1 => ("HFault", "Missing", 404),
            2 => arity == 2 ? (k ? ("KFault", "Clash", 409) : ("GFault", "Raced", 409)) : ("SFault", "First", 409),
            3 => arity == 2 ? (k ? ("KFault", "Lost", 404) : ("GFault", "Old", 412)) : ("SFault", "Second", 410),
            _ => ("HFault", "Clash", clash),
        };
    }
    /// <summary>The statuses >= 400 the operation lists: those of every member of every answered type, minus the left-out ones, plus the declared 429.</summary>
    public static SortedSet<int> DocStatuses(int arity, string variant)
    {
        var s = new SortedSet<int>();
        var h = new (string, int)[] { ("Missing", 404), ("Clash", variant == "group" ? 422 : 409), ("Bad", 422), ("Denied", 403), ("Down", 503), ("Stale", 412), ("Garbled", 400) };
        foreach (var (m, st) in h) if (!(variant == "wo" && m == "Bad")) s.Add(st);
        if (variant == "dup") { s.Add(409); s.Add(404); } else { s.Add(404); s.Add(409); s.Add(412); }
        if (arity == 3) { s.Add(409); s.Add(410); }
        if (variant == "declared") s.Add(429);
        return s;
    }

    public static RouteHandlerBuilder Decorate(RouteHandlerBuilder rb, string variant) => variant switch
    {
        "wo" => rb.WithoutFaults(HFault.Bad),
        "group" => rb.WithFaultStatus(HFault.Clash, 422),
        "declared" => rb.ProducesProblem(429),
        _ => rb,
    };
    public static void MapMinimal(IEndpointRouteBuilder b)
    {
        foreach (var v in new[] { "plain", "wo", "group", "declared", "dup" })
        {
            var k = v == "dup";
            Decorate(k ? b.MapGet($"/m/2/{v}", (int e) => TwoK(e).ToHttpResult()) : b.MapGet($"/m/2/{v}", (int e) => Two(e).ToHttpResult()), v);
            Decorate(k ? b.MapGet($"/m/3/{v}", (int e) => ThreeK(e).ToHttpResult()) : b.MapGet($"/m/3/{v}", (int e) => Three(e).ToHttpResult()), v);
        }
    }
}
