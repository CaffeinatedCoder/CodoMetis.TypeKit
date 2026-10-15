using CodoMetis.TypeKit.ClosedSets;

namespace FaultSetComposition.Union;

public sealed partial class TeamFault : IFaultSet<TeamFault>
{
    public static readonly TeamFault NotFound = new(FaultKind.NotFound, "No such team.");
    public static readonly TeamFault NameRequired = new(FaultKind.Invalid, "A name is required.");
    public static readonly TeamFault NameInUse = new(FaultKind.Conflict, "Another team already has that name.");
    public static readonly TeamFault ShortNameInUse = new(FaultKind.Conflict, "Another team already has that short name.");

    private TeamFault(FaultKind kind, string? description)
    {
        Kind = kind;
        Description = description;
    }

    public FaultKind Kind { get; }

    public string? Description { get; }
}
