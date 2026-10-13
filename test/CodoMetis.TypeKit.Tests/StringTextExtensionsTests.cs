namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// The two guards a string passes before it is text an application keeps.
/// </summary>
/// <remarks>
/// <para>
/// The character table is the point of this file. Every entry is a code point that walks through
/// <c>IsNullOrWhiteSpace</c> and survives <c>Trim</c>, and between them they are refused for four
/// different structural reasons: category <c>Control</c>, category <c>Format</c>, a category that
/// claims "letter" or "symbol" while the glyph is empty, and a plane above the BMP. Pinning them by
/// value rather than by category means a runtime that reclassifies one fails here instead of
/// quietly reopening the defect.
/// </para>
/// <para>
/// Every entry is written as an escape and named in the second column, because the subject of this
/// file is characters a reader cannot see. The five above <c>U+FFFF</c> are the ones a plausible
/// implementation gets wrong: <c>CharUnicodeInfo.GetUnicodeCategory</c> over a <see cref="char"/>
/// answers <c>Surrogate</c> for either half of a pair and never the category of the character the
/// pair encodes, so a per-<c>char</c> scan for <c>Format</c> accepts all five.
/// </para>
/// </remarks>
public sealed class StringTextExtensionsTests
{
    /// <summary>Refused by both guards, whichever way the value arrived.</summary>
    public static TheoryData<string, string> Hidden => new()
    {
        { "\u0000", "NUL" },
        { "\u0009", "TAB" },
        { "\u034F", "COMBINING GRAPHEME JOINER" },
        { "\u115F", "HANGUL CHOSEONG FILLER" },
        { "\u1160", "HANGUL JUNGSEONG FILLER" },
        { "\u180E", "MONGOLIAN VOWEL SEPARATOR" },
        { "\u200B", "ZERO WIDTH SPACE" },
        { "\u200E", "LEFT-TO-RIGHT MARK" },
        { "\u202E", "RIGHT-TO-LEFT OVERRIDE" },
        { "\u2028", "LINE SEPARATOR" },
        { "\u2029", "PARAGRAPH SEPARATOR" },
        { "\u2060", "WORD JOINER" },
        { "\u2800", "BRAILLE PATTERN BLANK" },
        { "\u3164", "HANGUL FILLER" },
        { "\uFEFF", "ZERO WIDTH NO-BREAK SPACE" },
        { "\uFFA0", "HALFWIDTH HANGUL FILLER" },
        { "\U0001D173", "MUSICAL SYMBOL BEGIN BEAM" },
        { "\U000E0001", "LANGUAGE TAG" },
        { "\U000E0041", "TAG LATIN CAPITAL A" },
        { "\U000E007F", "CANCEL TAG" },
        { "\U000F0000", "PRIVATE USE" }
    };

    /// <summary>
    /// The two characters the split is about: orthography in Persian, Arabic and several Indic
    /// scripts, and the glue inside an emoji sequence, so a name may carry them and a value another
    /// system issued may not.
    /// </summary>
    public static TheoryData<string, string> Joiners => new()
    {
        { "\u200C", "ZERO WIDTH NON-JOINER" },
        { "\u200D", "ZERO WIDTH JOINER" }
    };

    /// <summary>
    /// Every entry renders exactly as an ordinary space and, before folding, made two values out of
    /// one name. Confusable letters across scripts are deliberately not folded.
    /// </summary>
    public static TheoryData<string, string> Spaces => new()
    {
        { "\u00A0", "NO-BREAK SPACE" },
        { "\u2002", "EN SPACE" },
        { "\u2007", "FIGURE SPACE" },
        { "\u202F", "NARROW NO-BREAK SPACE" },
        { "\u3000", "IDEOGRAPHIC SPACE" }
    };

    [Theory]
    [MemberData(nameof(Hidden))]
    public void A_character_that_renders_as_nothing_is_refused_as_text(string character, string name) =>
        Refusal($"Muster{character}mann".AsText()).ShouldBe(TextFault.Hidden, name);

    [Theory]
    [MemberData(nameof(Hidden))]
    public void A_character_that_renders_as_nothing_is_refused_as_an_identifier(string character, string name) =>
        Refusal($"subject{character}id".AsIdentifier()).ShouldBe(TextFault.Hidden, name);

    [Theory]
    [MemberData(nameof(Joiners))]
    public void A_joiner_is_orthography_in_text_and_noise_in_an_identifier(string character, string name)
    {
        var written = $"Muster{character}mann";

        Kept(written.AsText()).ShouldBe(written, name);
        Refusal(written.AsIdentifier()).ShouldBe(TextFault.Hidden, name);
    }

    /// <summary>
    /// A zero-width joiner inside an emoji sequence is the concrete reason the permissive guard
    /// exists: refusing it would refuse a character people type on a phone keyboard.
    /// </summary>
    [Fact]
    public void An_emoji_sequence_survives_as_text()
    {
        const string developer = "\U0001F469\u200D\U0001F4BB";

        Kept(developer.AsText()).ShouldBe(developer);
    }

    /// <summary>
    /// The two faults stay apart because the caller decides what a blank means: a required field
    /// turns it into an error, an optional one into absence. Collapsing them would make every
    /// optional field swallow a hidden character.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Text_with_nothing_in_it_is_blank_rather_than_hidden(string value)
    {
        Refusal(value.AsText()).ShouldBe(TextFault.Blank);
        Refusal(value.AsIdentifier()).ShouldBe(TextFault.Blank);
    }

    /// <summary>
    /// The extension is the door a bound request property goes through, and binding enforces no
    /// non-nullability, so the one input a caller cannot see coming is absorbed here.
    /// </summary>
    [Fact]
    public void A_null_is_blank_rather_than_a_throw()
    {
        string? missing = null;

        Refusal(missing.AsText()).ShouldBe(TextFault.Blank);
        Refusal(missing.AsIdentifier()).ShouldBe(TextFault.Blank);
    }

    [Fact]
    public void Text_that_is_nothing_but_a_hidden_character_is_hidden_rather_than_blank() =>
        Refusal("\u200B".AsText()).ShouldBe(TextFault.Hidden);

    /// <summary>
    /// The half no character check can give. Without composition these are two names, two rows and
    /// two entries in a unique index a database compares byte by byte, and they render identically
    /// on every screen either way.
    /// </summary>
    [Fact]
    public void Text_is_composed_so_that_one_name_has_one_spelling()
    {
        const string composed   = "M\u00FCller";
        const string decomposed = "Mu\u0308ller";

        decomposed.ShouldNotBe(composed);

        var normalized = Kept(decomposed.AsText());
        normalized.ShouldBe(composed);
        normalized.Length.ShouldBe(6);
    }

    /// <summary>
    /// The one place the two guards disagree about content rather than about joiners: a value
    /// another system issued is compared against what that system sends next, so this side does
    /// not get to canonicalise it.
    /// </summary>
    [Fact]
    public void An_identifier_is_left_in_the_spelling_it_arrived_in()
    {
        const string decomposed = "Mu\u0308ller";

        Kept(decomposed.AsIdentifier()).ShouldBe(decomposed);
    }

    /// <summary>
    /// Not a category question: an unpaired surrogate is not a character at all.
    /// <c>string.Normalize</c> throws on one, so refusing it here is what keeps a malformed string
    /// from becoming an unhandled exception further down.
    /// </summary>
    [Fact]
    public void An_unpaired_surrogate_is_refused_rather_than_thrown_over()
    {
        Refusal("Muster\uD834mann".AsText()).ShouldBe(TextFault.Hidden);
        Refusal("Muster\uD834mann".AsIdentifier()).ShouldBe(TextFault.Hidden);
    }

    [Fact]
    public void Surrounding_whitespace_is_not_content()
    {
        Kept("  Erika Mustermann \n".AsText()).ShouldBe("Erika Mustermann");
        Kept("  7f3a \n".AsIdentifier()).ShouldBe("7f3a");
    }

    [Theory]
    [MemberData(nameof(Spaces))]
    public void A_space_that_renders_as_an_ordinary_one_is_folded_to_it(string character, string name) =>
        Kept($"Jean{character}Paul".AsText()).ShouldBe("Jean Paul", name);

    /// <summary>
    /// The written path folds; the identifier path alters nothing beyond the trim, because a value
    /// another system issued is compared against what that system sends next time.
    /// </summary>
    [Fact]
    public void An_identifier_keeps_its_own_spacing()
    {
        const string nonBreaking = "subject\u00A0id";

        Kept(nonBreaking.AsIdentifier()).ShouldBe(nonBreaking);
    }

    /// <summary>Text with nothing to fold or compose comes back as the same instance, not a copy.</summary>
    [Fact]
    public void Clean_text_is_returned_as_it_is()
    {
        const string clean = "Erika Mustermann";

        ReferenceEquals(Kept(clean.AsText()), clean).ShouldBeTrue();
    }

    private static string Kept(Result<string, TextFault> result) =>
        result.Match(text => text, fault => throw new ShouldAssertException($"Refused as {fault}."));

    private static TextFault Refusal(Result<string, TextFault> result) =>
        result.Match(text => throw new ShouldAssertException($"Kept \"{text}\"."), fault => fault);
}
