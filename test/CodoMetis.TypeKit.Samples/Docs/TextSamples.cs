using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Docs.Text;

// sample: text/required-optional
public sealed record ProfileRequest(string? Name, string? Note);   // bound from a request: nothing enforces the non-nullability

// end sample
// sample: text/value-objects
public enum NameFault { Blank, Hidden, TooLong }

public readonly partial record struct DisplayName : IValidatedValue<DisplayName, string, NameFault>
{
    public static Result<DisplayName, NameFault> Create(string value) =>
        value.AsText()
             .MapError(fault => fault == TextFault.Blank ? NameFault.Blank : NameFault.Hidden)   // the guard's faults become the field's
             .Ensure(text => text.Length <= 50, NameFault.TooLong)                                // a rule of this field
             .Map(text => new DisplayName(text));
}

public readonly partial record struct SubjectId : IValidatedValue<SubjectId, string, TextFault>
{
    public static Result<SubjectId, TextFault> Create(string value) => value.AsIdentifier().Map(id => new SubjectId(id));
}
// end sample

public static class Usage
{
    public static (Result<string, TextFault> Composed, Result<string, TextFault> Folded, Result<string, TextFault> Joined, Result<string, TextFault> Hidden, Result<string, TextFault> Filler, Result<string, TextFault> Bare, Result<string, TextFault> Issued, Result<string, TextFault> Forged) Guards()
    {
        // sample: text/guards
        Result<string, TextFault> composed = "Zoe\u0308".AsText();            // Success: Zoë, composed to NFC: one code point for ë
        Result<string, TextFault> folded = "Jean\u2003Paul".AsText();           // Success: Jean Paul, an em space folded to a space
        Result<string, TextFault> joined = "\U0001F469\u200D\U0001F4BB".AsText();   // Success: 👩‍💻, the joiner holds the sequence together
        Result<string, TextFault> hidden = "Zoe\u200B".AsText();                // Error: Hidden, a zero-width space
        Result<string, TextFault> filler = "\u3164".AsText();                   // Error: Hidden, a Hangul filler that IsNullOrWhiteSpace accepts
        Result<string, TextFault> bare = "\u200D".AsText();                     // Error: Hidden, a joiner with nothing to join
        Result<string, TextFault> issued = " a1b2-c3 ".AsIdentifier();          // Success: a1b2-c3, trimmed and otherwise as issued
        Result<string, TextFault> forged = "sub\u200Dject".AsIdentifier();      // Error: Hidden, a joiner in an identifier
        // end sample

        return (composed, folded, joined, hidden, filler, bare, issued, forged);
    }

    // sample: text/required-optional
    static Result<string, TextFault> Required(string? text) => text.AsText();   // Blank is the error for a required field

    static Result<Option<string>, TextFault> Optional(string? text) =>
        text.AsText()
            .Map(clean => Option.Some(clean))
            .Recover(fault => fault == TextFault.Blank ? Option.None<string>() : Result.Error(fault));   // Blank is absence, Hidden still a refusal
    // end sample

    public static (string Name, string Note, string Tagged) Fields(ProfileRequest request)
    {
        // sample: text/required-optional-use
        string name = Required(request.Name).Match(text => text, fault => fault.ToString());                                  // "Blank"
        string note = Optional(request.Note).Match(text => text.Match(t => t, () => "none"), fault => fault.ToString());      // "none"
        string tagged = Optional("note\u200B").Match(text => text.Match(t => t, () => "none"), fault => fault.ToString());    // "Hidden"
        // end sample

        return (name, note, tagged);
    }

    public static (string Shown, string Refused, bool Equal) ValueObjects()
    {
        // sample: text/value-objects-use
        string shown = DisplayName.Create("  Zoe\u0308\u2003Q ").Match(name => name.Value, fault => fault.ToString());   // "Zoë Q"
        string refused = DisplayName.Create(new string('x', 51)).Match(name => name.Value, fault => fault.ToString());    // "TooLong"
        bool equal = DisplayName.FromKnownGood("Mu\u0308ller") == DisplayName.FromKnownGood("M\u00FCller");               // true, one spelling after NFC
        // end sample

        return (shown, refused, equal);
    }
}

public sealed class TextTests
{
    private const string Readme = "docs/text.md";

    private static string Quoted(string text) => $"\"{text}\"";

    private static string Outcome(Result<string, TextFault> result) => result.Match(text => $"Success: {text}", fault => $"Error: {fault}");

    [Fact]
    public void Each_guard_answers_what_its_comment_states()
    {
        "Zoe\u0308".AsText().ShouldBeShownIn(Readme, Outcome);
        "Jean\u2003Paul".AsText().ShouldBeShownIn(Readme, Outcome);
        "\U0001F469\u200D\U0001F4BB".AsText().ShouldBeShownIn(Readme, Outcome);
        "Zoe\u200B".AsText().ShouldBeShownIn(Readme, Outcome);
        "\u3164".AsText().ShouldBeShownIn(Readme, Outcome);
        "\u200D".AsText().ShouldBeShownIn(Readme, Outcome);
        " a1b2-c3 ".AsIdentifier().ShouldBeShownIn(Readme, Outcome);
        "sub\u200Dject".AsIdentifier().ShouldBeShownIn(Readme, Outcome);

        var (composed, _, _, _, _, _, _, _) = Usage.Guards();
        composed.TryGetValue(out var zoe, out _).ShouldBeTrue();
        zoe.ShouldBe("Zoë", "composed: one code point");
        zoe.Length.ShouldBe(3);
        string.IsNullOrWhiteSpace("\u3164").ShouldBeFalse("IsNullOrWhiteSpace takes the filler for text, which is why the guard exists");
    }

    [Fact]
    public void A_required_and_an_optional_field_answer_what_the_comments_say()
    {
        var (name, note, tagged) = Usage.Fields(new ProfileRequest(null, " "));

        name.ShouldBeShownIn(Readme, Quoted, "Required(request.Name).Match(text => text, fault => fault.ToString())");
        note.ShouldBeShownIn(Readme, Quoted, "Optional(request.Note).Match(text => text.Match(t => t, () => \"none\"), fault => fault.ToString())");
        tagged.ShouldBeShownIn(Readme, Quoted, "Optional(\"note\\u200B\").Match(text => text.Match(t => t, () => \"none\"), fault => fault.ToString())");

        Usage.Fields(new ProfileRequest(" Ann ", " a note ")).ShouldBe(("Ann", "a note", "Hidden"));
    }

    [Fact]
    public void A_value_object_over_the_guard_answers_what_the_comments_say()
    {
        var (shown, refused, equal) = Usage.ValueObjects();

        shown.ShouldBeShownIn(Readme, Quoted, "DisplayName.Create(\"  Zoe\\u0308\\u2003Q \").Match(name => name.Value, fault => fault.ToString())");
        refused.ShouldBeShownIn(Readme, Quoted, "DisplayName.Create(new string('x', 51)).Match(name => name.Value, fault => fault.ToString())");
        equal.ShouldBeShownIn(Readme, e => e ? "true" : "false", "DisplayName.FromKnownGood(\"Mu\\u0308ller\") == DisplayName.FromKnownGood(\"M\\u00FCller\")");

        DisplayName.TryFrom("\u200B").IsNone().ShouldBeTrue();
        DisplayName.Create("").TryGetValue(out _, out var blank).ShouldBeFalse();
        blank.ShouldBe(NameFault.Blank);
        SubjectId.Create(" sub-1 ").Match(id => id.Value, fault => fault.ToString()).ShouldBe("sub-1");
        SubjectId.Create("sub\u200D1").TryGetValue(out _, out var joiner).ShouldBeFalse();
        joiner.ShouldBe(TextFault.Hidden);
    }
}
