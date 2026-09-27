using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>ToString</c> never prints what an option or result holds; the debugger shows it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ToString</c> is what reaches logs and exception messages, and a value object inside an option
/// or result can be personal data. The records' synthesized <c>ToString</c> prints only public
/// members, and the content is in private fields, so it prints <c>Option { }</c> or
/// <c>Result { State = Error }</c>. The guard is against a later "helpful" override, or a
/// positional declaration that turns the content into public properties.
/// </para>
/// <para>
/// The debugger is not a log, and there the content is exactly what someone stepping through wants
/// to see. <c>[DebuggerDisplay]</c> names a private property; if it went missing the debugger would
/// show an evaluation error instead.
/// </para>
/// </remarks>
public sealed partial class TextRepresentationTests
{
    private const string Secret = "s3cr3t-content";

    private static readonly Dictionary<string, (object Instance, string DebuggerText)> Cases = new()
    {
        ["Some"]                    = (Option.Some(Secret), $"Some({Secret})"),
        ["None"]                    = (Option.None<string>(), "None"),
        ["error-only success"]      = (Result<string>.Success(), "Success"),
        ["error-only error"]        = (Result<string>.Error(Secret), $"Error({Secret})"),
        ["error-only uninitialized"] = (default(Result<string>), "Uninitialized"),
        ["valued success"]          = (Result<string, string>.Success(Secret), $"Success({Secret})"),
        ["valued error"]            = (Result<string, string>.Error(Secret), $"Error({Secret})"),
        ["valued uninitialized"]    = (default(Result<string, string>), "Uninitialized"),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ToString_never_contains_the_content(string @case)
    {
        var text = Cases[@case].Instance.ToString();

        text.ShouldNotBeNull().ShouldNotContain(Secret);
    }

    /// <summary>
    /// Pins what <c>ToString</c> does print, so the test above cannot pass on an empty or throwing
    /// representation, including for an uninitialized result.
    /// </summary>
    [Fact]
    public void ToString_prints_the_type_and_the_state()
    {
        Option.Some(Secret).ToString().ShouldBe("Option { }");
        Result<string>.Error(Secret).ToString().ShouldBe("Result { State = Error }");
        Result<string, string>.Success(Secret).ToString().ShouldBe("Result { State = Success }");
        default(Result<string, string>).ToString().ShouldBe("Result { State = Uninitialized }");
    }

    /// <summary>
    /// The <c>Result.Ok(x)</c> and <c>Result.Error(e)</c> markers carry the content only as far as
    /// the implicit conversion, and are just as loggable on the way.
    /// </summary>
    [Fact]
    public void The_ok_and_error_markers_never_print_their_content()
    {
        Result.Ok(Secret).ToString().ShouldBe("Success { }");
        Result.Error(Secret).ToString().ShouldBe("Error { }");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_debugger_shows_the_content(string @case)
    {
        var (instance, expected) = Cases[@case];

        DebuggerText(instance).ShouldBe(expected);
    }

    private static string DebuggerText(object instance)
    {
        var type      = instance.GetType();
        var attribute = type.GetCustomAttribute<DebuggerDisplayAttribute>().ShouldNotBeNull($"{type.Name} has no [DebuggerDisplay]");
        var match     = DebuggerDisplayProperty().Match(attribute.Value);

        match.Success.ShouldBeTrue($"{type.Name}'s [DebuggerDisplay(\"{attribute.Value}\")] does not name a single property");

        var property = type.GetProperty(match.Groups["name"].Value, BindingFlags.Instance | BindingFlags.NonPublic)
                           .ShouldNotBeNull($"{type.Name} has no private property {match.Groups["name"].Value}");

        return property.GetValue(instance).ShouldBeOfType<string>();
    }

    [GeneratedRegex(@"^\{(?<name>\w+),nq\}$")]
    private static partial Regex DebuggerDisplayProperty();
}
