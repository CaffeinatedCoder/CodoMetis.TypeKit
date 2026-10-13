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
/// is not a character, and <c>U+FFFE</c>, the byte-order mark read in the wrong byte order, is the one
/// noncharacter <see cref="string.Normalize()"/> refuses: it throws on both, so both are refused as
/// <see cref="TextFault.Hidden"/> rather than thrown over, by both guards, so that no string makes either
/// throw.
/// </para>
/// <para>
/// Both refuse a run of more than 30 combining marks in a row as <see cref="TextFault.Hidden"/>: no
/// script stacks that many on one character, and composing such a run reorders it in time that grows with
/// the square of its length, so one request body of stacked marks could hold a core for a minute. Thirty
/// is the bound of the Stream-Safe Text Format of Unicode Standard Annex #15. The scan that counts them
/// is linear, and stops at the 31st.
/// </para>
/// <para>
/// Not refused: unassigned code points, since the runtime's Unicode tables lag the standard and a
/// name spelled with a newly assigned character would be refused until the SDK caught up; the other
/// noncharacters (<c>U+FFFF</c>, <c>U+FDD0</c>, <c>U+1FFFE</c>), which no runtime call refuses; the
/// prepended concatenation marks, which Unicode files as format characters although each is drawn, such
/// as the Arabic number sign, including one with no digit after it, since it then draws itself; and
/// confusable letters across scripts, which is a comparison question and not a rejection.
/// </para>
/// </remarks>
public static class StringTextExtensions
{
    private const int ZeroWidthNonJoiner   = 0x200C;
    private const int ZeroWidthJoiner      = 0x200D;
    private const int SwappedByteOrderMark = 0xFFFE;

    /// <summary>The longest run of combining marks kept: the Stream-Safe Text Format's bound (UAX #15).</summary>
    private const int LongestRunOfMarks = 30;

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

    /// <summary>
    /// Whether the text carries a character that renders as nothing, a code point composition throws
    /// on, or a run of marks too long to compose in linear time, or renders no character of its own at
    /// all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A joiner, a variation selector and a combining mark render only beside a base character, so a
    /// value of nothing else, <c>"\u200D"</c> or <c>"\uFE0F"</c>, shows nothing and is hidden. A variation
    /// selector chooses how the character before it is drawn: after a base it is an emoji's
    /// presentation or an ideograph's variant, and anywhere else it selects nothing. A joiner is not
    /// placed: an Arabic letter shown in its joining form on its own ends with one, which is
    /// orthography; a value of joiners alone is caught by needing a base.
    /// </para>
    /// </remarks>
    private static bool IsHidden(string text, bool tolerateJoiners)
    {
        var rest            = text.AsSpan();
        var afterBase       = false;
        var rendersBase     = false;
        var marksInARow     = 0;

        while (!rest.IsEmpty)
        {
            // An unpaired surrogate and U+FFFE are what string.Normalize throws on.
            if (Rune.DecodeFromUtf16(rest, out var rune, out var consumed) != OperationStatus.Done || rune.Value == SwappedByteOrderMark)
                return true;

            if (!IsRendered(rune, tolerateJoiners)) return true;

            marksInARow = IsCombiningMark(rune) ? marksInARow + 1 : 0;
            if (marksInARow > LongestRunOfMarks) return true;

            if (IsVariationSelector(rune))
            {
                if (!afterBase) return true;
                afterBase = false;
            }
            else
            {
                afterBase   =  IsBase(rune);
                rendersBase |= afterBase;
            }

            rest = rest[consumed..];
        }

        return !rendersBase;
    }

    /// <summary>
    /// A character drawn on its own: not a space, not a mark drawn on another, not a joiner and not a
    /// variation selector.
    /// </summary>
    private static bool IsBase(Rune rune) =>
        rune.Value is not (ZeroWidthNonJoiner or ZeroWidthJoiner)
     && !Rune.IsWhiteSpace(rune)
     && !IsCombiningMark(rune);

    /// <summary>
    /// A mark drawn on another character. Every character that composition may reorder is one, so a
    /// bound on a run of these bounds the reordering.
    /// </summary>
    private static bool IsCombiningMark(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// The variation selectors, which Unicode files as non-spacing marks: the sixteen of the BMP, the
    /// 240 ideographic ones, and the Mongolian free variation selectors.
    /// </summary>
    private static bool IsVariationSelector(Rune rune) =>
        rune.Value is >= 0xFE00 and <= 0xFE0F
                   or >= 0xE0100 and <= 0xE01EF
                   or >= 0x180B and <= 0x180D
                   or 0x180F;

    private static bool IsRendered(Rune rune, bool tolerateJoiners) =>
        (tolerateJoiners && rune.Value is ZeroWidthNonJoiner or ZeroWidthJoiner)
     || IsPrependedConcatenationMark(rune)
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
        rune.Value is 0x034F   // COMBINING GRAPHEME JOINER          (NonSpacingMark)
                   or 0x115F   // HANGUL CHOSEONG FILLER             (OtherLetter)
                   or 0x1160   // HANGUL JUNGSEONG FILLER            (OtherLetter)
                   or 0x17B4   // KHMER VOWEL INHERENT AQ            (NonSpacingMark)
                   or 0x17B5   // KHMER VOWEL INHERENT AA            (NonSpacingMark)
                   or 0x2800   // BRAILLE PATTERN BLANK              (OtherSymbol)
                   or 0x3164   // HANGUL FILLER                      (OtherLetter)
                   or 0xFFA0   // HALFWIDTH HANGUL FILLER            (OtherLetter)
                   or 0x16FE4; // KHITAN SMALL SCRIPT FILLER         (NonSpacingMark)

    /// <summary>
    /// The prepended concatenation marks, which Unicode files as format characters although each is
    /// drawn: a sign that spans the digits or letters after it. The set of Unicode 16, whose tables the
    /// runtime follows. One with nothing after it draws itself, so it is a character of its own.
    /// </summary>
    private static bool IsPrependedConcatenationMark(Rune rune) =>
        rune.Value is >= 0x0600 and <= 0x0605   // ARABIC NUMBER SIGN .. ARABIC NUMBER MARK ABOVE
                   or 0x06DD                    // ARABIC END OF AYAH
                   or 0x070F                    // SYRIAC ABBREVIATION MARK
                   or 0x0890                    // ARABIC POUND MARK ABOVE
                   or 0x0891                    // ARABIC PIASTRE MARK ABOVE
                   or 0x08E2                    // ARABIC DISPUTED END OF AYAH
                   or 0x110BD                   // KAITHI NUMBER SIGN
                   or 0x110CD;                  // KAITHI NUMBER SIGN ABOVE
}
