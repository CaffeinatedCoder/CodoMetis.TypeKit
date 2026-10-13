namespace CodoMetis.TypeKit;

/// <summary>Why a string is not the text it appears to be.</summary>
/// <remarks>
/// Two faults rather than one because the caller decides what <see cref="Blank"/> means: a required
/// field reads it as an error, an optional one as absence. <see cref="Hidden"/> is a refusal in both,
/// since nothing asks for a character that renders as nothing.
/// </remarks>
public enum TextFault
{
    /// <summary>Null, empty, or nothing but whitespace.</summary>
    Blank,

    /// <summary>Carries at least one character that renders as nothing.</summary>
    Hidden
}
