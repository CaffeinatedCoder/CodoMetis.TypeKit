using System.Text.Json;
using CodoMetis.TypeKit.ClosedSets;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>A set whose static field reads <c>All</c>: <c>All</c> is filled after every member exists.</summary>
public sealed partial class EarlyAllStatus : IClosedSet<EarlyAllStatus>
{
    public static readonly EarlyAllStatus Open = new();
    public static readonly IReadOnlyList<EarlyAllStatus> Snapshot = All;
}

/// <summary>
/// A set whose static field parses the name of a member declared below it, still null there. A field of
/// the set's own type must be created with <c>new</c> (CMTK1019), so the parse is held in a list.
/// </summary>
public sealed partial class EarlyParseStatus : IClosedSet<EarlyParseStatus>
{
    public static readonly IReadOnlyList<EarlyParseStatus> Defaults = [Parse("Closed")];
    public static readonly EarlyParseStatus Closed = new();
}

/// <summary>See <see cref="EarlyParseStatus"/>, through <c>TryParse</c>.</summary>
public sealed partial class EarlyTryParseStatus : IClosedSet<EarlyTryParseStatus>
{
    public static readonly IReadOnlyList<EarlyTryParseStatus> Defaults = TryParse("Closed", out var closed) ? [closed] : [];
    public static readonly EarlyTryParseStatus Closed = new();
}

/// <summary>See <see cref="EarlyParseStatus"/>, through the JSON converter.</summary>
public sealed partial class EarlyJsonStatus : IClosedSet<EarlyJsonStatus>
{
    public static readonly IReadOnlyList<EarlyJsonStatus> Defaults = JsonSerializer.Deserialize<List<EarlyJsonStatus>>("""["Closed"]""")!;
    public static readonly EarlyJsonStatus Closed = new();
}

/// <summary>See <see cref="EarlyParseStatus"/>, through <c>Parse(name, ignoreCase)</c>.</summary>
public sealed partial class EarlyIgnoreCaseParseStatus : IClosedSet<EarlyIgnoreCaseParseStatus>
{
    public static readonly IReadOnlyList<EarlyIgnoreCaseParseStatus> Defaults = [Parse("closed", ignoreCase: true)];
    public static readonly EarlyIgnoreCaseParseStatus Closed = new();
}

/// <summary>See <see cref="EarlyParseStatus"/>, through <c>TryParse(name, ignoreCase, out member)</c>.</summary>
public sealed partial class EarlyIgnoreCaseTryParseStatus : IClosedSet<EarlyIgnoreCaseTryParseStatus>
{
    public static readonly IReadOnlyList<EarlyIgnoreCaseTryParseStatus> Defaults = TryParse("closed", ignoreCase: true, out var closed) ? [closed] : [];
    public static readonly EarlyIgnoreCaseTryParseStatus Closed = new();
}

/// <summary>See <see cref="EarlyParseStatus"/>, through the explicit <c>IParsable&lt;TSelf&gt;.TryParse</c>.</summary>
public sealed partial class EarlyParsableStatus : IClosedSet<EarlyParsableStatus>
{
    public static readonly IReadOnlyList<EarlyParsableStatus> Defaults = EarlyReads.Parsable<EarlyParsableStatus>("Closed");
    public static readonly EarlyParsableStatus Closed = new();
}

/// <summary>See <see cref="EarlyParseStatus"/>, through the JSON converter's dictionary-key read.</summary>
public sealed partial class EarlyJsonKeyStatus : IClosedSet<EarlyJsonKeyStatus>
{
    public static readonly IReadOnlyList<EarlyJsonKeyStatus> Defaults = [.. JsonSerializer.Deserialize<Dictionary<EarlyJsonKeyStatus, int>>("{\"Closed\":1}")!.Keys];
    public static readonly EarlyJsonKeyStatus Closed = new();
}

/// <summary>Generic code that reads a set through <c>IParsable&lt;TSelf&gt;</c>, as a binder does.</summary>
public static class EarlyReads
{
    public static IReadOnlyList<T> Parsable<T>(string name) where T : IParsable<T> => T.TryParse(name, null, out var member) ? [member] : [];
}

/// <summary>
/// A closed set's static field initializers run while its members are still being created. <c>All</c>,
/// filled once every member exists, was null there, and a field that read it held null for the
/// process's life, typed as never null; a parse of a member declared below handed out null, and
/// <c>TryParse</c> answered true with it, as did the ignore-case overloads and <c>IParsable</c>, and a
/// dictionary-key read threw <see cref="ArgumentNullException"/>. Each refuses, naming the set and
/// linking the README.
/// </summary>
public sealed class ClosedSetInitializationTests
{
    public static TheoryData<string> Forms => ["All", "Parse", "TryParse", "JSON", "Parse ignoring case", "TryParse ignoring case", "IParsable", "JSON key"];

    private static readonly Dictionary<string, (string Set, Func<object?> Touch)> Touch = new()
    {
        ["All"]                    = ("EarlyAllStatus", () => EarlyAllStatus.Snapshot),
        ["Parse"]                  = ("EarlyParseStatus", () => EarlyParseStatus.Defaults),
        ["TryParse"]               = ("EarlyTryParseStatus", () => EarlyTryParseStatus.Defaults),
        ["JSON"]                   = ("EarlyJsonStatus", () => EarlyJsonStatus.Defaults),
        ["Parse ignoring case"]    = ("EarlyIgnoreCaseParseStatus", () => EarlyIgnoreCaseParseStatus.Defaults),
        ["TryParse ignoring case"] = ("EarlyIgnoreCaseTryParseStatus", () => EarlyIgnoreCaseTryParseStatus.Defaults),
        ["IParsable"]              = ("EarlyParsableStatus", () => EarlyParsableStatus.Defaults),
        ["JSON key"]               = ("EarlyJsonKeyStatus", () => EarlyJsonKeyStatus.Defaults),
    };

    [Theory]
    [MemberData(nameof(Forms))]
    public void Reading_the_set_in_its_own_static_initializer_is_refused(string form)
    {
        var (set, touch) = Touch[form];

        var refusal = Should.Throw<TypeInitializationException>(touch).InnerException.ShouldBeOfType<InvalidOperationException>();

        refusal.Message.ShouldStartWith($"{set} is still being initialized");
        refusal.Message.ShouldEndWith("#a-closed-set-read-before-it-is-initialized");
    }
}
