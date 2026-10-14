using CodoMetis.TypeKit;
using FaultSetComposition.Shared;

namespace FaultSetComposition.Copy;

/// <summary>Shape 1's marker: the set includes every member of <typeparamref name="TIncluded"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludesFaultsAttribute<TIncluded> : Attribute;

// NOT counted as the consumer's: what a generator would emit for the two [IncludesFaults] above.
// Written by hand here; ClosedSetAspect still weaves the class and sees these fields as members.
public sealed partial class TeamFault
{
    public static readonly TeamFault Conflict = new(ConcurrencyFault.Conflict.Kind, ConcurrencyFault.Conflict.Description);
    public static readonly TeamFault PreconditionFailed = new(ConcurrencyFault.PreconditionFailed.Kind, ConcurrencyFault.PreconditionFailed.Description);
    public static readonly TeamFault HiddenCharacters = new(ConcurrencyFault.HiddenCharacters.Kind, ConcurrencyFault.HiddenCharacters.Description);
    public static readonly TeamFault NotCompleted = new(ConcurrencyFault.NotCompleted.Kind, ConcurrencyFault.NotCompleted.Description);
    public static readonly TeamFault StillReferenced = new(RemovalFault.StillReferenced.Kind, RemovalFault.StillReferenced.Description);
    public static readonly TeamFault RestoreBlocked = new(RemovalFault.RestoreBlocked.Kind, RemovalFault.RestoreBlocked.Description);
    public static readonly TeamFault ReasonRequired = new(RemovalFault.ReasonRequired.Kind, RemovalFault.ReasonRequired.Description);

    // The rule cannot be carried from outside the base package (Fault.Naming is internal); generated, it would be.
    public static Fault<TeamFault> From(Fault<ConcurrencyFault> fault) => Fault.Of(Parse(fault.Reason.Name), fault.Detail);

    public static Fault<TeamFault> From(Fault<RemovalFault> fault) => Fault.Of(Parse(fault.Reason.Name), fault.Detail);
}
