using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.Extensions.DependencyInjection;

namespace ReadmeSamples.Analyzers.UndefinedEnumValue;

#pragma warning disable CMTK0014 // what the sample shows the rule reporting
// sample: CodoMetis.TypeKit.Analyzers/cmtk0014
[RequireParameterlessConstructor((ConstructorAccessibility)7)]   // CMTK0014: no member, read as public
public interface IPlugin { }

public sealed partial class ImportFault : IFaultSet<ImportFault>
{
    public static readonly ImportFault Unreadable = new(default);   // CMTK0014: default is no FaultKind
    public static readonly ImportFault Duplicate = new(FaultKind.Conflict);

    private ImportFault(FaultKind kind) => Kind = kind;

    public FaultKind Kind { get; }
}
// end sample
#pragma warning restore CMTK0014

/// <summary>What the README says the suppressed rule would have prevented.</summary>
public sealed class UndefinedEnumValueTests
{
    [Fact]
    public void A_fault_set_member_of_no_kind_stops_the_host_where_the_set_is_mapped() =>
        Should.Throw<ArgumentException>(() => new ServiceCollection().AddFaultResponses<ImportFault>())
              .Message.ShouldStartWith("ImportFault.Unreadable: 0 is no FaultKind");
}
