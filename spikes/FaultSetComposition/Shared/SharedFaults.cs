using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;

namespace FaultSetComposition.Shared;

// What a shared request-path library decides for every slice of an application.

public enum CommandOutcome { Applied, Raced, Stale, Failed }

public enum RemovalOutcome { Removed, NotFound, Referenced, Blocked, NoReason }

/// <summary>Refusals every write decides alike: a race, a stale version, a hidden character, an outcome nothing translated.</summary>
public sealed partial class ConcurrencyFault : IFaultSet<ConcurrencyFault>
{
    public static readonly ConcurrencyFault Conflict = new(FaultKind.Conflict, null);
    public static readonly ConcurrencyFault PreconditionFailed = new(FaultKind.PreconditionFailed, null);
    public static readonly ConcurrencyFault HiddenCharacters = new(FaultKind.Invalid, null);
    public static readonly ConcurrencyFault NotCompleted = new(FaultKind.Invalid, Rejections.NotCompletedDetail);

    private ConcurrencyFault(FaultKind kind, string? description)
    {
        Kind = kind;
        Description = description;
    }

    public FaultKind Kind { get; }

    public string? Description { get; }
}

/// <summary>Refusals the shared removal path decides for every slice that removes entries.</summary>
public sealed partial class RemovalFault : IFaultSet<RemovalFault>
{
    public static readonly RemovalFault StillReferenced = new(FaultKind.Conflict, null);
    public static readonly RemovalFault RestoreBlocked = new(FaultKind.Conflict, null);
    public static readonly RemovalFault ReasonRequired = new(FaultKind.Invalid, Rejections.ReasonRequiredDetail);

    private RemovalFault(FaultKind kind, string? description)
    {
        Kind = kind;
        Description = description;
    }

    public FaultKind Kind { get; }

    public string? Description { get; }
}

/// <summary>Today's shared translators: string codes, since one fault set cannot hold another's members.</summary>
public readonly record struct Rejection(string Code, string Detail);

public static class Rejections
{
    public const string ConflictCode = "Conflict";
    public const string PreconditionFailedCode = "PreconditionFailed";
    public const string HiddenCharactersCode = "HiddenCharacters";
    public const string NotCompletedCode = "NotCompleted";
    public const string StillReferencedCode = "StillReferenced";
    public const string RestoreBlockedCode = "RestoreBlocked";
    public const string ReasonRequiredCode = "ReasonRequired";
    public const string NotCompletedDetail = "The request could not be completed.";
    public const string ReasonRequiredDetail = "A reason is required.";

    public static Rejection Translate(CommandOutcome outcome, string subject) =>
        outcome switch
        {
            CommandOutcome.Raced => new(ConflictCode, $"{subject} was changed meanwhile; read it again."),
            CommandOutcome.Stale => new(PreconditionFailedCode, $"{subject} is at a newer version than the one sent."),
            _ => new(NotCompletedCode, NotCompletedDetail),
        };

    public static Rejection Translate(RemovalOutcome outcome) =>
        outcome switch
        {
            RemovalOutcome.Referenced => new(StillReferencedCode, "It is still referenced; withdraw it instead."),
            RemovalOutcome.Blocked => new(RestoreBlockedCode, "Another entry has taken its names since."),
            RemovalOutcome.NoReason => new(ReasonRequiredCode, ReasonRequiredDetail),
            _ => new(NotCompletedCode, NotCompletedDetail),
        };
}

/// <summary>The same translators once a set can be shared: a fault of the shared set.</summary>
public static class SharedFaults
{
    public static Fault<ConcurrencyFault> Translate(CommandOutcome outcome, string subject) =>
        outcome switch
        {
            CommandOutcome.Raced => Fault.Of(ConcurrencyFault.Conflict, $"{subject} was changed meanwhile; read it again."),
            CommandOutcome.Stale => Fault.Of(ConcurrencyFault.PreconditionFailed, $"{subject} is at a newer version than the one sent."),
            _ => Fault.Of(ConcurrencyFault.NotCompleted),
        };

    public static Fault<RemovalFault> Translate(RemovalOutcome outcome) =>
        outcome switch
        {
            RemovalOutcome.Referenced => Fault.Of(RemovalFault.StillReferenced, "It is still referenced; withdraw it instead."),
            RemovalOutcome.Blocked => Fault.Of(RemovalFault.RestoreBlocked, "Another entry has taken its names since."),
            _ => Fault.Of(RemovalFault.ReasonRequired),
        };
}

public sealed record TeamRequest(string? Name);
