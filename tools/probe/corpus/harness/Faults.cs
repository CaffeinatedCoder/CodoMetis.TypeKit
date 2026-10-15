// Fault types, error tables and shared logging for the HTTP probes (harness; see HttpHost.cs).
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using System.Reflection;
namespace Probes;

public sealed partial class HFault : IFaultSet<HFault>
{
    public static readonly HFault Missing = new(FaultKind.NotFound, "Missing.");
    public static readonly HFault Clash = new(FaultKind.Conflict, null);
    public static readonly HFault Bad = new(FaultKind.Invalid, null);
    public static readonly HFault Denied = new(FaultKind.Forbidden, null);
    public static readonly HFault Down = new(FaultKind.Unavailable, null);
    public static readonly HFault Stale = new(FaultKind.PreconditionFailed, null);
    public static readonly HFault Garbled = new(FaultKind.Malformed, null);
    private HFault(FaultKind kind, string? description) { Kind = kind; Description = description; }
    public FaultKind Kind { get; }
    public string? Description { get; }
}
public sealed partial class GFault : IFaultSet<GFault>
{
    public static readonly GFault Gone = new(FaultKind.NotFound, null);
    public static readonly GFault Raced = new(FaultKind.Conflict, null);
    public static readonly GFault Old = new(FaultKind.PreconditionFailed, null);
    private GFault(FaultKind kind, string? description) { Kind = kind; Description = description; }
    public FaultKind Kind { get; }
    public string? Description { get; }
}
public enum SFault { First, Second }
public enum PRule { A, B }
[Flags] public enum FRule { P = 1, Q = 2 }
public static class Holder { public enum NestedRule { N } }
public sealed partial class RuleSet : IClosedSet<RuleSet> { public static readonly RuleSet X = new(); public static readonly RuleSet Y = new(); }

public static class Errors
{
    public static Result<Fault<HFault, GFault>> Two(int e) => e switch
    {
        0 => Result<Fault<HFault, GFault>>.Success(),
        1 => Fault<HFault, GFault>.Of(HFault.Missing),
        2 => Fault<HFault, GFault>.Of(HFault.Clash),
        3 => Fault<HFault, GFault>.Of(HFault.Bad),
        4 => Fault<HFault, GFault>.Of(HFault.Denied),
        5 => Fault<HFault, GFault>.Of(HFault.Down),
        6 => Fault<HFault, GFault>.Of(HFault.Stale),
        7 => Fault<HFault, GFault>.Of(HFault.Garbled),
        8 => Fault<HFault, GFault>.Of(GFault.Gone),
        9 => Fault<HFault, GFault>.Of(GFault.Raced),
        10 => Fault<HFault, GFault>.Of(GFault.Old),
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
    public static Result<Fault<HFault, GFault, SFault>> Three(int e) => e switch
    {
        0 => Result<Fault<HFault, GFault, SFault>>.Success(),
        1 => Fault<HFault, GFault, SFault>.Of(HFault.Missing),
        8 => Fault<HFault, GFault, SFault>.Of(GFault.Gone),
        11 => Fault<HFault, GFault, SFault>.Of(SFault.First),
        12 => Fault<HFault, GFault, SFault>.Of(Fault.Of(SFault.Second, "second detail").WithRule(PRule.B)),
        13 => Fault<HFault, GFault, SFault>.Of(Fault.Of(HFault.Clash).WithRule(RuleSet.X)),
        14 => Fault<HFault, GFault, SFault>.Of(Fault.Of(GFault.Raced).WithRule(Holder.NestedRule.N)),
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
    public static Result<Fault<HFault>> One(int e) => e switch
    {
        0 => Result<Fault<HFault>>.Success(),
        1 => Fault.Of(HFault.Missing),
        2 => Fault.Of(HFault.Clash),
        4 => Fault.Of(HFault.Denied),
        5 => Fault.Of(HFault.Down),
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
    public static Result<Fault<GFault>> OneG(int e) => e switch
    {
        0 => Result<Fault<GFault>>.Success(),
        8 => Fault.Of(GFault.Gone),
        9 => Fault.Of(GFault.Raced),
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
}

public sealed class ListedLogs : ILoggerProvider
{
    public List<string> Lines { get; } = [];
    public ILogger CreateLogger(string categoryName) => new L(this);
    public void Dispose() { }
    private sealed class L(ListedLogs o) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            o.Lines.Add($"{logLevel}: {formatter(state, exception)}");
    }
}

public sealed class OnlyControllers(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var c in controllers) feature.Controllers.Add(c.GetTypeInfo());
    }
}

