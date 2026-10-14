using CodoMetis.TypeKit.ClosedSets;
using FaultSetComposition.Shared;

namespace FaultSetComposition.Baseline;

public sealed partial class TeamFault : IFaultSet<TeamFault>
{
    public static readonly TeamFault NotFound = new(FaultKind.NotFound, "No such team.");
    public static readonly TeamFault NameRequired = new(FaultKind.Invalid, "A name is required.");
    public static readonly TeamFault NameInUse = new(FaultKind.Conflict, "Another team already has that name.");
    public static readonly TeamFault ShortNameInUse = new(FaultKind.Conflict, "Another team already has that short name.");

    // Copies of what the shared translators decide for every slice.
    public static readonly TeamFault Conflict = new(FaultKind.Conflict, null);
    public static readonly TeamFault PreconditionFailed = new(FaultKind.PreconditionFailed, null);
    public static readonly TeamFault HiddenCharacters = new(FaultKind.Invalid, null);
    public static readonly TeamFault NotCompleted = new(FaultKind.Invalid, Rejections.NotCompletedDetail);
    public static readonly TeamFault StillReferenced = new(FaultKind.Conflict, null);
    public static readonly TeamFault RestoreBlocked = new(FaultKind.Conflict, null);
    public static readonly TeamFault ReasonRequired = new(FaultKind.Invalid, Rejections.ReasonRequiredDetail);

    private TeamFault(FaultKind kind, string? description)
    {
        Kind = kind;
        Description = description;
    }

    public FaultKind Kind { get; }

    public string? Description { get; }
}
