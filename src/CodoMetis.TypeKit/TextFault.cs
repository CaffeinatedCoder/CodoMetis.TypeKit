namespace CodoMetis.TypeKit;

/// <summary>Why a string is not the text it appears to be.</summary>
/// <remarks>
/// Two faults rather than one because the caller decides what <see cref="Blank"/> means: a required
/// field reads it as an error, an optional one as absence. <see cref="Hidden"/> is a refusal in both,
/// since nothing asks for a character that renders as nothing, and a value of one line asks for no line
/// break.
/// </remarks>
public enum TextFault
{
    /// <summary>Null, empty, or nothing but whitespace.</summary>
    Blank,

    /// <summary>
    /// Carries at least one character that renders as nothing, that changes how the text around it is
    /// shown, or that ends the line; or renders no character of its own.
    /// </summary>
    /// <remarks>
    /// <para>Exactly, with the surrounding whitespace trimmed first, a value that carries:</para>
    /// <list type="bullet">
    /// <item><description>a control character (category Cc): NUL, a tab, a line feed, a carriage return, the rest of the C0 range, DEL and the C1 range;</description></item>
    /// <item><description>a format character (Cf): the soft hyphen, the zero-width space, the word joiner, the byte-order mark, the bidirectional marks, embeddings, overrides and isolates, the invisible operators, the tag characters; but not a prepended concatenation mark, such as the Arabic number sign, which is drawn, nor, as <c>AsText()</c> reads it, the zero-width joiner and non-joiner;</description></item>
    /// <item><description>a private-use character (Co), or the line or paragraph separator (Zl, Zp);</description></item>
    /// <item><description>an unpaired surrogate, or <c>U+FFFE</c>, the byte-order mark in the wrong byte order: what <c>string.Normalize</c> throws on;</description></item>
    /// <item><description>a character that renders nothing whatever its category: the combining grapheme joiner, the Hangul fillers, the Braille pattern blank, the Khmer inherent vowels and the Khitan filler;</description></item>
    /// <item><description>a variation selector that follows no character it could vary;</description></item>
    /// <item><description>more than 30 combining marks in a row, the Stream-Safe Text Format's bound;</description></item>
    /// </list>
    /// <para>
    /// or a value of joiners, variation selectors and combining marks alone. Unassigned code points and
    /// confusable letters are not refused.
    /// </para>
    /// </remarks>
    Hidden
}
