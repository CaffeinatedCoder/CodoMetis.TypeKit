namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// The CMTK ids. They are public contract: suppressions and <c>.editorconfig</c> entries name them,
/// so a rule keeps its id for good.
/// </summary>
internal static class DiagnosticIds
{
    public const string ForbiddenDefaultInitialization = "CMTK0001";

    public const string ValueObjectWithoutGenerators = "CMTK0002";

    public const string IgnoredOutcome = "CMTK0003";

    public const string MaterializeOutsidePersistence = "CMTK0004";

    public const string DefaultFilledCollection = "CMTK0005";

    public const string UnassignedNoDefaultMember = "CMTK0006";

    public const string KnownGoodFromCaller = "CMTK0007";

    public const string MixedValueComparison = "CMTK0008";

}
