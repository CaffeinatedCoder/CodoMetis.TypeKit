using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit.CompilerServices;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// Every deliberate run-time refusal of the package, and of the code the generators weave, which
/// builds its messages here, ends its message with a link to the subsection of the package README's
/// "Why does this throw?" that explains it; and every such link's anchor is a heading of that README.
/// </summary>
/// <remarks>
/// <para>
/// An analyzer rule carries a help link the IDE opens. An exception has only its message, so the
/// message is the landing page for "why does this throw?". A link to a heading that does not exist
/// opens the top of the page, silently, so the anchor is derived from the README's headings as GitHub
/// derives it (lowercase, punctuation dropped, spaces to hyphens) and a renamed heading fails here as
/// a missing link does.
/// </para>
/// <para>
/// Each refusal is driven through the call a consumer makes. The serializer wraps a converter's
/// <see cref="NotSupportedException"/> in one of its own that appends the path, and an
/// <see cref="ArgumentException"/> appends its parameter name, so the link ends the refusal's own
/// message and stands in the one the caller sees.
/// </para>
/// </remarks>
public sealed partial class RefusalLinkTests
{
    private const string WhyDoesThisThrow = "Why does this throw?";

    /// <summary>The one subsection about a build error, which no run-time refusal links to.</summary>
    private const string BuildErrorOnly = "Materialize and CMTK0004";

    private const string InJson = "Option or Result in JSON";
    private const string NullContent = "A null in an Option or a Result";
    private const string Uninitialized = "An uninitialized Result";
    private const string Refused = "A value object refused a value";
    private const string KnownGood = "FromKnownGood refused a value";
    private const string Unreadable = "A value object could not read the input";

    private enum Fault { TooLong }

    private readonly record struct Code;

    private static readonly Dictionary<string, (string Heading, Func<object?> Refusal)> Refusals = new()
    {
        ["Option written as JSON"]          = (InJson, () => JsonSerializer.Serialize(Option.Some(1))),
        ["Option read from JSON"]           = (InJson, () => JsonSerializer.Deserialize<Option<int>>("1")),
        ["Option? written as a JSON null"]  = (InJson, () => JsonSerializer.Serialize<Option<int>?>(null)),
        ["Option as a dictionary key"]      = (InJson, () => JsonSerializer.Serialize(new Dictionary<Option<string>, int> { [Option.Some("key")] = 1 })),
        ["None marker as JSON"]             = (InJson, () => JsonSerializer.Serialize(Option.None())),
        ["Result<TError> as JSON"]          = (InJson, () => JsonSerializer.Serialize(Result<string>.Success())),
        ["Result<T, TError> from JSON"]     = (InJson, () => JsonSerializer.Deserialize<Result<int, string>>("{}")),
        ["Success marker as JSON"]          = (InJson, () => JsonSerializer.Serialize(Result.Success())),
        ["Success(value) marker as JSON"]   = (InJson, () => JsonSerializer.Serialize(Result.Success(1))),
        ["Error marker as JSON"]            = (InJson, () => JsonSerializer.Serialize(Result.Error("fault"))),

        ["Option.Some(null)"]               = (NullContent, () => Option.Some<string>(null!)),
        ["Map to null"]                     = (NullContent, () => Option.Some("x").Map(_ => (string)null!)),
        ["FirstOrNone over a null"]         = (NullContent, () => new string[] { null! }.FirstOrNone()),
        ["ToResult(null)"]                  = (NullContent, () => Option.Some(1).ToResult((string)null!)),
        ["Result.Success(null)"]            = (NullContent, () => Result.Success<string>(null!)),
        ["Result.Error(null)"]              = (NullContent, () => Result.Error<string>(null!)),
        ["Result<T, TError>.Success(null)"] = (NullContent, () => Result<string, string>.Success(null!)),
        ["Result<T, TError>.Error(null)"]   = (NullContent, () => Result<int, string>.Error(null!)),
        ["Result<TError>.Error(null)"]      = (NullContent, () => Result<string>.Error(null!)),
        ["MapError to null"]                = (NullContent, () => Result<string>.Error("e").MapError(_ => (string)null!)),
        ["Ensure(predicate, null)"]         = (NullContent, () => Result<int, string>.Success(1).Ensure(_ => true, null!)),
        ["EnsureAsync(predicate, null)"]    = (NullContent, () => Task.FromResult(Result<int, string>.Success(1)).EnsureAsync(_ => true, null!).GetAwaiter().GetResult()),
        ["FirstOrError(predicate, null)"]   = (NullContent, () => new[] { 1 }.FirstOrError(_ => true, (string)null!)),
        ["LastOrError(predicate, null)"]    = (NullContent, () => new[] { 1 }.LastOrError(_ => true, (string)null!)),

        ["Match on a default Result<T, TError>"] = (Uninitialized, () => default(Result<int, string>).Match(x => x, _ => 0)),
        ["bool of a default Result<TError>"]     = (Uninitialized, () => (bool)default(Result<string>)),
        ["Zip with a default Result"]            = (Uninitialized, () => Result<int, string>.Success(1).Zip(default(Result<int, string>), (a, b) => a + b)),

        ["refused JSON value"]              = (Refused, () => GeneratedFactories.OrJsonException(Result<Code, Fault>.Error(Fault.TooLong))),
        ["refused text"]                    = (Refused, () => GeneratedFactories.OrFormatException(Result<Code, Fault>.Error(Fault.TooLong))),
        ["refused known-good expression"]   = (KnownGood, () => GeneratedFactories.OrInvalidOperationException(Result<Code, Fault>.Error(Fault.TooLong), "request.Code")),
        ["refused known-good value"]        = (KnownGood, () => GeneratedFactories.OrInvalidOperationException(Result<Code, Fault>.Error(Fault.TooLong), null)),

        ["unreadable JSON value"]           = (Unreadable, () => ReadJson("\"x\"", asKey: false)),
        ["unreadable JSON key"]             = (Unreadable, () => ReadJson("""{"x":1}""", asKey: true)),
        ["unreadable text, Parse"]          = (Unreadable, () => throw GeneratedParsing.Unreadable<Code, int>()),
        ["unreadable text, static Parse"]   = (Unreadable, () => GeneratedParsing.Guarded<Code, int>("x", static text => int.Parse(text, CultureInfo.InvariantCulture))),
        ["unreadable text, type converter"] = (Unreadable, () => GeneratedParsing.ConvertFromString<Code, int>("x", null)),
    };

    private static readonly Lazy<IReadOnlyList<(int Level, string Text)>> Headings = new(ReadmeHeadings);

    public static TheoryData<string> RefusalNames => [.. Refusals.Keys];

    [Fact]
    public void The_refusals_are_enumerated() => Refusals.Count.ShouldBeGreaterThanOrEqualTo(30);

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_refusal_ends_its_message_with_a_link_to_its_subsection(string refusal)
    {
        var (heading, attempt) = Refusals[refusal];

        var thrown = Record.Exception(attempt).ShouldNotBeNull($"{refusal} threw nothing.");
        var own = thrown.GetBaseException();
        var message = OwnMessage(own);

        var link = LinkAtTheEnd().Match(message);
        link.Success.ShouldBeTrue($"{refusal}: the message does not end with a link to the package README: {message}");
        link.Groups["anchor"].Value.ShouldBe(Slug(heading), $"{refusal} links to another subsection than '{heading}'.");
        Anchors().ShouldContain(link.Groups["anchor"].Value, $"{refusal} links to #{link.Groups["anchor"].Value}, which no heading of the README produces, so the link opens the top of the page.");

        thrown.Message.ShouldContain(link.Value, Case.Sensitive, $"{refusal}: the message the caller sees lost the link.");
    }

    /// <summary>A subsection nothing links to is either stale or a refusal that lost its link.</summary>
    [Fact]
    public void Every_subsection_of_Why_does_this_throw_is_what_a_refusal_links_to()
    {
        var subsections = Subsections(WhyDoesThisThrow);
        subsections.Count.ShouldBeGreaterThanOrEqualTo(7, $"The README has no '## {WhyDoesThisThrow}' with its subsections.");

        subsections.Select(Slug).Except([Slug(BuildErrorOnly)]).Order()
                   .ShouldBe(Refusals.Values.Select(entry => Slug(entry.Heading)).Distinct().Order(),
                             "The subsections of the README and the headings the refusals link to differ.");
    }

    /// <summary>A link inside the README to one of its own sections resolves too.</summary>
    [Fact]
    public void Every_link_within_the_README_names_one_of_its_headings()
    {
        var links = InPageLink().Matches(File.ReadAllText(ReadmePath())).Select(match => match.Groups["anchor"].Value).ToList();

        links.ShouldNotBeEmpty();
        links.Where(anchor => !Anchors().Contains(anchor)).ShouldBeEmpty("These links in the README name no heading.");
    }

    [Fact]
    public void The_slug_is_GitHub_s()
    {
        Slug("Why does this throw?").ShouldBe("why-does-this-throw");
        Slug("`FromKnownGood` refused a value").ShouldBe("fromknowngood-refused-a-value");
        Slug("Option&lt;T&gt;").ShouldBe("optiont");
        Slug("Materialize and CMTK0004").ShouldBe("materialize-and-cmtk0004");
    }

    /// <summary>A refusal's message as it built it: an <see cref="ArgumentException"/> appends its parameter name.</summary>
    private static string OwnMessage(Exception exception) =>
        exception is ArgumentException { ParamName: { } parameter } && exception.Message.EndsWith($" (Parameter '{parameter}')", StringComparison.Ordinal)
            ? exception.Message[..^$" (Parameter '{parameter}')".Length]
            : exception.Message;

    /// <summary>What a value object's generated converter does with JSON its wrapped type cannot read.</summary>
    private static object? ReadJson(string json, bool asKey)
    {
        var options = JsonSerializerOptions.Default;
        var plan = GeneratedJsonPlan<int>.For(options, builtIn: null);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));

        reader.Read();
        if (!asKey) return plan.Read<Code>(ref reader, options);

        reader.Read();
        return plan.ReadKey<Code>(ref reader, options);
    }

    /// <summary>GitHub's anchor for a heading: entities decoded, lowercase, punctuation dropped, spaces to hyphens.</summary>
    private static string Slug(string heading) =>
        NotInASlug().Replace(WebUtility.HtmlDecode(heading).ToLowerInvariant(), "").Replace(' ', '-');

    private static HashSet<string> Anchors()
    {
        var anchors = Headings.Value.Select(heading => Slug(heading.Text)).ToList();
        anchors.Count.ShouldBe(anchors.Distinct().Count(), "Two headings of the README have one anchor, and GitHub numbers the second.");

        return [.. anchors];
    }

    private static List<string> Subsections(string section) =>
        [.. Headings.Value.SkipWhile(heading => !(heading.Level == 2 && heading.Text == section))
                          .Skip(1)
                          .TakeWhile(heading => heading.Level > 2)
                          .Where(heading => heading.Level == 3)
                          .Select(heading => heading.Text)];

    /// <summary>The README's headings, outside code fences.</summary>
    private static IReadOnlyList<(int Level, string Text)> ReadmeHeadings()
    {
        var headings = new List<(int, string)>();
        var fenced = false;

        foreach (var line in File.ReadLines(ReadmePath()))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            else if (!fenced && Heading().Match(line) is { Success: true } heading)
                headings.Add((heading.Groups["level"].Length, heading.Groups["text"].Value.Trim()));
        }

        return headings;
    }

    private static string ReadmePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx")))
                return Path.Combine(directory.FullName, "src", "CodoMetis.TypeKit", "README.md");
        }

        throw new InvalidOperationException($"No CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@" See (?<readme>https://github\.com/CaffeinatedCoder/CodoMetis\.TypeKit/blob/main/src/CodoMetis\.TypeKit/README\.md)#(?<anchor>[^\s#]+)$")]
    private static partial Regex LinkAtTheEnd();

    [GeneratedRegex(@"\]\(#(?<anchor>[^)\s]+)\)")]
    private static partial Regex InPageLink();

    [GeneratedRegex(@"^(?<level>#{1,6}) (?<text>.+)$")]
    private static partial Regex Heading();

    /// <summary>What GitHub drops from a heading: everything but letters, marks, digits, the underscore, spaces and hyphens.</summary>
    [GeneratedRegex(@"[^\p{L}\p{M}\p{Nd}\p{Pc} -]")]
    private static partial Regex NotInASlug();
}
