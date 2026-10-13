# Written text

Every application that stores what a person typed trims it and refuses a blank. Few refuse a
zero-width space, a Hangul filler or NUL, which `IsNullOrWhiteSpace` lets through and `Trim` leaves
in place. Fewer compose it, so a name written with `ü` and the same name written with `u` and a
combining diaeresis are two rows of a unique index that render alike. `AsText()` does all of this for
a line of text a person wrote, and `AsIdentifier()` trims and refuses for a value another system
issued; both are extensions on `string` in `CodoMetis.TypeKit` and answer with a
`Result<string, TextFault>`. The [base README](../src/CodoMetis.TypeKit/README.md#written-text) is
their reference and lists exactly what they refuse; this page is about using them: which one, where,
and inside a value object. The examples import `CodoMetis.TypeKit`.

## Two guards

| | `AsText()` | `AsIdentifier()` |
|---|---|---|
| For | one line of text a person wrote: a name, a title, a subject line | a value another system issued: a subject claim, a session id, a key |
| Trims | yes | yes |
| Folds every space separator to an ordinary space | yes | no |
| Composes to NFC, so one spelling per name | yes, and only that: a ligature, a superscript or a fullwidth letter stays as written, as NFKC would not keep it | no, since an issued value is compared against what its issuer sends next time |
| Refuses a character that renders as nothing | yes | yes |
| A line break, a tab or another control character | refused as `Hidden`: a value is one line, so a multi-line field such as a comment does not go through the guard | refused |
| A format character: a soft hyphen, a byte-order mark, a bidirectional mark or override, a tag character | refused, so an Arabic name ending in a right-to-left mark is refused too | refused |
| The Arabic number sign, the end of ayah and the other prepended concatenation marks | kept: format characters by category, but drawn, spanning the digits after them | kept |
| The two zero-width joiners | kept: they spell Persian, Arabic and Indic scripts and hold an emoji sequence together | refused: no issuer needs one |
| A variation selector | kept after the character it varies (an emoji's presentation, an ideograph's variant); elsewhere it selects nothing and is refused | the same |
| A value of joiners, variation selectors and combining marks alone | refused: it renders no character of its own | refused |
| More than 30 combining marks in a row | refused: no script stacks that many, and composing a run takes time that grows with the square of its length | refused |
| An unpaired surrogate, or `U+FFFE` | refused, where `Normalize` would throw | refused |
| A null receiver | `Blank` | `Blank` |
| In globalization-invariant mode | throws `PlatformNotSupportedException`, since it cannot compose there ([below](#globalization-invariant-mode)) | works, for issued values |

```csharp
Result<string, TextFault> composed = "Zoe\u0308".AsText();            // Success: Zoë, composed to NFC: one code point for ë
Result<string, TextFault> folded = "Jean\u2003Paul".AsText();           // Success: Jean Paul, an em space folded to a space
Result<string, TextFault> joined = "\U0001F469\u200D\U0001F4BB".AsText();   // Success: 👩‍💻, the joiner holds the sequence together
Result<string, TextFault> hidden = "Zoe\u200B".AsText();                // Error: Hidden, a zero-width space
Result<string, TextFault> filler = "\u3164".AsText();                   // Error: Hidden, a Hangul filler that IsNullOrWhiteSpace accepts
Result<string, TextFault> bare = "\u200D".AsText();                     // Error: Hidden, a joiner with nothing to join
Result<string, TextFault> issued = " a1b2-c3 ".AsIdentifier();          // Success: a1b2-c3, trimmed and otherwise as issued
Result<string, TextFault> forged = "sub\u200Dject".AsIdentifier();      // Error: Hidden, a joiner in an identifier
```

Both scan by `Rune`, so a character above the Basic Multilingual Plane is seen for what it is, and an
unpaired surrogate is `Hidden` rather than an exception, which `Normalize` and a database driver would
both throw on. No string makes either guard throw, and none makes either take more than linear time.
Not refused on purpose: unassigned code points, since the runtime's Unicode tables lag the standard
and a name spelled with a newly assigned character would be refused until the SDK caught up; and
confusable letters across scripts, which is a comparison question and not a rejection.

## Required and optional

`TextFault` has two members because the caller decides what a blank means. A required field makes
`Blank` an error; an optional one makes it absence. `Hidden` is a refusal in both.

```csharp
public sealed record ProfileRequest(string? Name, string? Note);   // bound from a request: nothing enforces the non-nullability

static Result<string, TextFault> Required(string? text) => text.AsText();   // Blank is the error for a required field

static Result<Option<string>, TextFault> Optional(string? text) =>
    text.AsText()
        .Map(clean => Option.Some(clean))
        .Recover(fault => fault == TextFault.Blank ? Option.None<string>() : Result.Error(fault));   // Blank is absence, Hidden still a refusal
```

```csharp
string name = Required(request.Name).Match(text => text, fault => fault.ToString());                                  // "Blank"
string note = Optional(request.Note).Match(text => text.Match(t => t, () => "none"), fault => fault.ToString());      // "none"
string tagged = Optional("note\u200B").Match(text => text.Match(t => t, () => "none"), fault => fault.ToString());    // "Hidden"
```

A null is `Blank` because the extension is the door a bound request property goes through, and
binding enforces no non-nullability: a `string Name` in a record is null when the body lacked it.

## A value object over the guard

No value object ships with the guards. One over either is one line, named for the field rather than
for the guard, since `DisplayName` says more than `Text` and two fields of one type can be assigned to
each other. The guard composes with rules of your own:

```csharp
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
```

```csharp
string shown = DisplayName.Create("  Zoe\u0308\u2003Q ").Match(name => name.Value, fault => fault.ToString());   // "Zoë Q"
string refused = DisplayName.Create(new string('x', 51)).Match(name => name.Value, fault => fault.ToString());    // "TooLong"
bool equal = DisplayName.FromKnownGood("Mu\u0308ller") == DisplayName.FromKnownGood("M\u00FCller");               // true, one spelling after NFC
```

Every generated way into `DisplayName`, JSON, a route parameter, `TryFrom`, applies the guard and the
length rule, so a document or a request that carries a hidden character is refused with `Hidden`
before it reaches the domain. Where the guard is the whole rule, the fault type is `TextFault` itself,
as `SubjectId` shows.

## Globalization-invariant mode

`AsText()` composes through the runtime's globalization data. A process in globalization-invariant
mode has none: `<InvariantGlobalization>true</InvariantGlobalization>` in the project, which Native
AOT and container templates often set, or `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. There
`string.Normalize` returns its input unchanged, and a guard that answered anyway would keep one name in
two spellings, without a word. So `AsText()` throws `PlatformNotSupportedException` there, on its first
call whatever the string, rather than on the first decomposed name a client happens to send.

Written text has no guard in that mode. Where the application calls `AsText()`, turn the mode off and
make ICU available to the process (`libicu` on Debian and Ubuntu, `icu-libs` on Alpine); ICU installed
while the mode stays on does nothing. `AsIdentifier()` keeps working there, but only for what it is for,
a value another system issued: it refuses the joiners a Persian name or an emoji sequence needs and
folds no space, so it is no stand-in for `AsText()`.

## Where it does not belong

- **A password or a secret.** Composing or folding it changes what the person typed; compare it as
  given, and never through a guard that would also put it in a fault.
- **A multi-line field**, such as a comment or a message body: a line break is a control character,
  refused as `Hidden`. Guard each line, or keep the field as the `string` it is.
- **Text another system will parse**, such as a path or a URL: it has its own rules, and trimming it
  is already a change.
- **A comparison key.** Confusable letters across scripts and case folding are a comparison's
  concern; the guard decides what is stored, not what is equal.
