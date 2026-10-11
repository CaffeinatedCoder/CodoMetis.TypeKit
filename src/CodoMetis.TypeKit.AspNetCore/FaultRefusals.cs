namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// The end of this package's refusals: a link to the README subsection that explains them, as the base
/// package's refusals end (FaultRefusalLinkTests holds the anchor to the README's headings).
/// </summary>
internal static class FaultRefusals
{
    private const string Readme = "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#";

    /// <summary>A fault type with no status, or a mapping that gives one no error status.</summary>
    public const string Mapping = " See " + Readme + "mapping-faults-to-statuses";
}
