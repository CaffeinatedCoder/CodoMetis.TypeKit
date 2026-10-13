using System.Buffers;
using System.Globalization;
using System.Text;

namespace CodoMetis.TypeKit;

/// <summary>
/// The two ways a string becomes text an application keeps. <see cref="string.IsNullOrWhiteSpace"/>
/// is not one of them: it lets through a zero-width space, a Hangul filler and NUL, and
/// <see cref="string.Trim()"/> removes none of them, since none is whitespace.
/// </summary>
/// <remarks>
/// <para>
/// Which one to call is decided by who chose the value. <see cref="AsText"/> takes text a person
/// wrote, <see cref="AsIdentifier"/> a value another system issued; they differ in what they
/// tolerate and in whether they canonicalise.
/// </para>
/// <para>
/// Both scan by <see cref="Rune"/>, and anything extending them must stay that way: a per-<see cref="char"/>
/// category test cannot see a character above the Basic Multilingual Plane, and reports
/// <see cref="UnicodeCategory.Surrogate"/> where the real category is <see cref="UnicodeCategory.Format"/>,
/// which is how the tag characters used to hide text inside text pass such a test. An unpaired surrogate
/// is not a character: <see cref="string.Normalize()"/> throws on it, so it is refused as
/// <see cref="TextFault.Hidden"/> rather than thrown over.
/// </para>
/// <para>
/// Not refused: unassigned code points, since the runtime's Unicode tables lag the standard and a
/// name spelled with a newly assigned character would be refused until the SDK caught up; and
/// confusable letters across scripts, which is a comparison question and not a rejection.
/// </para>
/// </remarks>
public static class StringTextExtensions
{
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner    = 0x200D;

    /// <param name="text">The string, which may be null: a null is <see cref="TextFault.Blank"/>.</param>
    extension(string? text)
    {
        /// <summary>
        /// The string as text a person wrote, as it will be kept: trimmed, every space separator folded
        /// to an ordinary space, composed to Unicode normalization form C, and refused if it carries a
        /// character that renders as nothing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Neither canonicalisation is a convenience. Two canonically equivalent spellings of one name,
        /// or one name spaced with a no-break space, compare unequal under every ordinal comparison and
        /// under a unique index a database compares byte by byte, while rendering identically on every
        /// screen.
        /// </para>
        /// <para>
        /// The two zero-width joiners are kept: they are ordinary orthography in Persian, Arabic and
        /// several Indic scripts, and a zero-width joiner is what holds an emoji sequence together.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The text, or <see cref="TextFault.Blank"/> for a null, empty or whitespace-only string, or
        /// <see cref="TextFault.Hidden"/> for one carrying a character that renders as nothing.
        /// </returns>
        public Result<string, TextFault> AsText() => Clean(text, tolerateJoiners: true, canonicalize: true);

        /// <summary>
        /// The string as a value another system issued, a subject claim, a session id, a key: trimmed of
        /// surrounding whitespace, otherwise exactly as it arrived, and refused if it carries a
        /// character that renders as nothing.
        /// </summary>
        /// <remarks>
        /// Nothing is composed or folded, since an issued value is compared against what its issuer
        /// sends next time, and canonicalising it alters an attested value. Trimming survives because
        /// surrounding whitespace is a transport artefact. The zero-width joiners
        /// <see cref="AsText"/> keeps are refused here, since no issuer needs one.
        /// </remarks>
        /// <returns>
        /// The value, or <see cref="TextFault.Blank"/> for a null, empty or whitespace-only string, or
        /// <see cref="TextFault.Hidden"/> for one carrying a character that renders as nothing.
        /// </returns>
        public Result<string, TextFault> AsIdentifier() => Clean(text, tolerateJoiners: false, canonicalize: false);
    }

    private static Result<string, TextFault> Clean(string? text, bool tolerateJoiners, bool canonicalize)
    {
        var trimmed = text?.Trim() ?? string.Empty;

        if (trimmed.Length == 0) return Result.Error(TextFault.Blank);
        if (IsHidden(trimmed, tolerateJoiners)) return Result.Error(TextFault.Hidden);

        return canonicalize ? FoldSpaces(trimmed).Normalize(NormalizationForm.FormC) : trimmed;
    }

    /// <summary>
    /// Every separator that renders as an ordinary space becomes <c>U+0020</c>, so one name spaced two
    /// ways is one value. Runs on the written path only.
    /// </summary>
    private static string FoldSpaces(string text)
    {
        StringBuilder? folded = null;

        var rest     = text.AsSpan();
        var position = 0;

        while (!rest.IsEmpty)
        {
            Rune.DecodeFromUtf16(rest, out var rune, out var consumed);

            if (rune.Value != ' ' && Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator)
            {
                folded ??= new StringBuilder(text.Length).Append(text, 0, position);
                folded.Append(' ');
            }
            else
            {
                folded?.Append(text, position, consumed);
            }

            position += consumed;
            rest     =  rest[consumed..];
        }

        return folded?.ToString() ?? text;
    }

    private static bool IsHidden(string text, bool tolerateJoiners)
    {
        var rest = text.AsSpan();

        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var consumed) != OperationStatus.Done)
                return true;

            if (!IsRendered(rune, tolerateJoiners)) return true;

            rest = rest[consumed..];
        }

        return false;
    }

    private static bool IsRendered(Rune rune, bool tolerateJoiners) =>
        (tolerateJoiners && rune.Value is ZeroWidthNonJoiner or ZeroWidthJoiner)
     || (Rune.GetUnicodeCategory(rune) switch
         {
             // Control covers NUL and the C0/C1 range; Format covers the zero-width spaces, the
             // word joiner, the byte-order mark and the whole bidirectional family, including the
             // overrides that reorder what is rendered around them. Private use and the two
             // explicit separators render as whatever the font decides or as nothing at all.
             UnicodeCategory.Control
              or UnicodeCategory.Format
              or UnicodeCategory.PrivateUse
              or UnicodeCategory.LineSeparator
              or UnicodeCategory.ParagraphSeparator => false,

             _ => !IsInvisibleDespiteItsCategory(rune)
         });

    /// <summary>
    /// Characters whose whole purpose is to occupy a position without rendering, which Unicode
    /// nevertheless files as letters, marks or symbols, so no category test reaches them.
    /// </summary>
    private static bool IsInvisibleDespiteItsCategory(Rune rune) =>
        rune.Value is 0x034F  // COMBINING GRAPHEME JOINER          (NonSpacingMark)
                   or 0x115F  // HANGUL CHOSEONG FILLER             (OtherLetter)
                   or 0x1160  // HANGUL JUNGSEONG FILLER            (OtherLetter)
                   or 0x2800  // BRAILLE PATTERN BLANK              (OtherSymbol)
                   or 0x3164  // HANGUL FILLER                      (OtherLetter)
                   or 0xFFA0; // HALFWIDTH HANGUL FILLER            (OtherLetter)
}
