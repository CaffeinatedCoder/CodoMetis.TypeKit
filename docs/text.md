# Written text

`AsText()` and `AsIdentifier()` check a string before you store it. `AsText()` is for a line of text
a person wrote, like a name. `AsIdentifier()` is for a value another system issued, like a session id.

Most applications trim what a person typed and reject a blank. Few reject a zero-width space, a
Hangul filler or NUL, which `IsNullOrWhiteSpace` lets through and `Trim` leaves in place. Fewer still
compose the text. Then a name written with `ü`, and the same name written with `u` and a combining
diaeresis, look alike but are two rows of a unique index. `AsText()` takes care of all of this.
`AsIdentifier()` trims and rejects, and leaves the rest as issued.

Both are extensions on `string` in the `CodoMetis.TypeKit` namespace, and both return a
`Result<string, TextFault>`. This page helps you pick one, use it on a field, and build a value object
on it. The [base README](../src/CodoMetis.TypeKit/README.md#written-text) is their reference and
lists exactly what they reject. The examples import `CodoMetis.TypeKit`.

## Pick a guard

| | `AsText()` | `AsIdentifier()` |
|---|---|---|
| Use it for | one line of text a person wrote: a name, a title, a subject line | a value another system issued: a subject claim, a session id, a key |
| Trims | yes | yes |
| Turns every space separator, such as an em space, into an ordinary space | yes | no |
| Composes to NFC, so each name has one spelling | yes, and only that: a ligature, a superscript or a fullwidth letter stays as written, which NFKC would change | no, because an issued value is compared with what its issuer sends next time |
| Rejects a character that renders as nothing | yes | yes |
| A line break, a tab or another control character | rejected as `Hidden`. A value is one line, so a multi-line field such as a comment doesn't go through the guard | rejected |
| A format character: a soft hyphen, a byte-order mark, a bidirectional mark or override, a tag character | rejected, so an Arabic name that ends in a right-to-left mark is rejected too | rejected |
| The Arabic number sign, the end of ayah and the other prepended concatenation marks | kept. They are format characters by category, but they are drawn, and span the digits after them | kept |
| The two zero-width joiners | kept. They spell Persian, Arabic and Indic scripts, and hold an emoji sequence together | rejected, since no issuer needs one |
| A variation selector | kept after the character it varies (an emoji's presentation, an ideograph's variant). Anywhere else it selects nothing and is rejected | the same |
| A value made only of joiners, variation selectors and combining marks | rejected, since it renders no character of its own | rejected |
| More than 30 combining marks in a row | rejected. No script stacks that many, and composing a run takes time that grows with the square of its length | rejected |
| An unpaired surrogate, or `U+FFFE` | rejected, where `Normalize` would throw | rejected |
| A null string | `Blank` | `Blank` |
| In globalization-invariant mode | throws `PlatformNotSupportedException`, because it can't compose there ([below](#globalization-invariant-mode)) | works, for issued values |

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

**Good to know**

- **No string makes either guard throw.** Both read the text by `Rune`, so a character above the
  Basic Multilingual Plane is seen for what it is. An unpaired surrogate comes back as `Hidden`, where
  `Normalize` and a database driver would both throw.
- **Neither takes more than linear time,** whatever the string.
- **Unassigned code points are kept on purpose.** The runtime's Unicode tables lag behind the
  standard. Rejecting them would reject a name spelled with a newly assigned character until the SDK
  caught up.
- **Look-alike letters from different scripts are kept on purpose.** Telling them apart is a question
  for comparison, not a reason to reject the text.

## Required and optional

`TextFault` has two members, `Blank` and `Hidden`, because only you know what a blank means for your
field. For a required field, `Blank` is an error. For an optional one, a blank means "no value", and
`AsOptionalText()` and `AsOptionalIdentifier()` return a `Result<Option<string>, TextFault>`.
`Hidden` is an error in both cases. A hidden character isn't nothing: it is text the person can't see.

```csharp
public sealed record ProfileRequest(string? Name, string? Note);   // bound from a request: nothing enforces the non-nullability
```

```csharp
string name = request.Name.AsText().Match(text => text, fault => fault.ToString());                                       // "Blank"
string note = request.Note.AsOptionalText().Match(text => text.Match(t => t, () => "none"), fault => fault.ToString());   // "none"
string tagged = "note\u200B".AsOptionalText().Match(text => text.Match(t => t, () => "none"), fault => fault.ToString()); // "Hidden"
```

A null string is `Blank`. A bound request property comes in through the guard, and request binding
doesn't enforce non-nullability. A `string Name` in a record is null when the body left it out.

## Build a value object on a guard

TypeKit ships no value object for the guards. Your own takes one line of `Create`, as `SubjectId`
below shows. Name it for the field, not for the guard: `DisplayName` says more than `Text`, and two fields of one shared type could
be assigned to each other by mistake. The guard combines with rules of your own:

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

Every generated way to create a `DisplayName` runs the guard and the length rule: JSON, a route
parameter, `TryFrom`. A document or a request that carries a hidden character is rejected with
`Hidden` before it reaches your domain. When the guard is the whole rule, use `TextFault` itself as
the error type, as `SubjectId` does.

## Globalization-invariant mode

`AsText()` composes text with the runtime's globalization data. A process in globalization-invariant
mode has none. You turn that mode on with `<InvariantGlobalization>true</InvariantGlobalization>` in
the project, which Native AOT and container templates often set, or with
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

In that mode `string.Normalize` returns its input unchanged. A guard that answered anyway would keep
one name in two spellings without telling you. So `AsText()` throws `PlatformNotSupportedException`
there, on its first call, whatever the string. You find out at once, not on the first decomposed name
a client happens to send.

To use `AsText()`, turn the mode off and make ICU available to the process: `libicu` on Debian and
Ubuntu, `icu-libs` on Alpine. Installing ICU while the mode stays on does nothing. In that mode there
is no guard for written text.

`AsIdentifier()` keeps working in that mode, but only for what it is for, a value another system
issued. It rejects the joiners a Persian name or an emoji sequence needs, and doesn't fold spaces,
so it can't stand in for `AsText()`.

## When not to use them

- **A password or a secret.** Composing or folding it changes what the person typed. Compare it as
  given, and keep it away from a guard that would also put it in a fault.
- **A multi-line field**, such as a comment or a message body. A line break is a control character,
  rejected as `Hidden`. Check each line on its own, or keep the field as a plain `string`.
- **Text another system will parse**, such as a path or a URL. It has its own rules, and even
  trimming it changes it.
- **A comparison key.** Look-alike letters across scripts and case folding are a question for
  comparison. The guard decides what is stored, not what counts as equal.
