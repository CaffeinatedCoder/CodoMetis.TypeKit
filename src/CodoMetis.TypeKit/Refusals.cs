namespace CodoMetis.TypeKit;

/// <summary>
/// The end of every deliberate run-time refusal's message: a link to the subsection of the package
/// README's "Why does this throw?" that explains it.
/// </summary>
/// <remarks>
/// An analyzer rule carries a help link that the IDE opens; an exception has only its message, so the
/// message is the landing page. The link comes last, where the serializer's path or an argument's
/// parameter name follows it, and the subsection names what to do instead. Each anchor is a heading of
/// the README, as GitHub derives it; RefusalLinkTests holds both ends together. Nothing of the input
/// is ever added here.
/// </remarks>
internal static class Refusals
{
    private const string Readme = "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit/README.md#";

    /// <summary>An <c>Option</c>, a <c>Result</c> or a marker read or written as JSON.</summary>
    public const string NotAWireType = " See " + Readme + "option-or-result-in-json";

    /// <summary>A null given to an <c>Option</c> or a <c>Result</c> as its value or error.</summary>
    public const string NullContent = " See " + Readme + "a-null-in-an-option-or-a-result";

    /// <summary>A branch picked on a <c>default</c> result.</summary>
    public const string UninitializedResult = " See " + Readme + "an-uninitialized-result";

    /// <summary>A value object's <c>Create</c> refused JSON or text.</summary>
    public const string RefusedValue = " See " + Readme + "a-value-object-refused-a-value";

    /// <summary>A value object's <c>Create</c> refused what <c>FromKnownGood</c> was given.</summary>
    public const string RefusedKnownGood = " See " + Readme + "fromknowngood-refused-a-value";

    /// <summary>A null given to a value object: <c>From(null)</c>, or a JSON null.</summary>
    public const string NullToValueObject = " See " + Readme + "a-null-given-to-a-value-object";

    /// <summary>The wrapped type could not read JSON or text at all.</summary>
    public const string UnreadableInput = " See " + Readme + "a-value-object-could-not-read-the-input";
}
