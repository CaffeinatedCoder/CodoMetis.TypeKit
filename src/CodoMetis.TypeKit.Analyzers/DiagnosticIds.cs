namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// The CMTK ids. They are public contract: suppressions and <c>.editorconfig</c> entries name them,
/// so a rule keeps its id for good.
/// </summary>
internal static class DiagnosticIds
{
    public const string ForbiddenDefaultInitialization = "CMTK0001";

    public const string ValueObjectWithoutGenerators = "CMTK0002";
}
