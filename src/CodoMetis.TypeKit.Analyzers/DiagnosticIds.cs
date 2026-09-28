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

    public const string DefaultProducingCall = "CMTK0009";

    /// <summary>
    /// The rule's section of the analyzer README on GitHub, which an IDE opens from the diagnostic.
    /// The README's <c>## CMTK000N</c> headings give the anchors (AnalyzerHelpLinkTests).
    /// </summary>
    public static string HelpLink(string id) =>
        $"https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#{id.ToLowerInvariant()}";
}
