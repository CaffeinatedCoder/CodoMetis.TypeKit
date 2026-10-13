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

/// <summary>
/// A closed set's static field initializers run while its members are still being created. <c>All</c>,
/// filled once every member exists, was null there, and a field that read it held null for the
/// process's life, typed as never null; a parse of a member declared below handed out null, and
/// <c>TryParse</c> answered true with it. Each refuses, naming the set and linking the README.
/// </summary>
public sealed class ClosedSetInitializationTests
{
    public static TheoryData<string> Forms => ["All", "Parse", "TryParse", "JSON"];

    private static readonly Dictionary<string, (string Set, Func<object?> Touch)> Touch = new()
    {
        ["All"]      = ("EarlyAllStatus", () => EarlyAllStatus.Snapshot),
        ["Parse"]    = ("EarlyParseStatus", () => EarlyParseStatus.Defaults),
        ["TryParse"] = ("EarlyTryParseStatus", () => EarlyTryParseStatus.Defaults),
        ["JSON"]     = ("EarlyJsonStatus", () => EarlyJsonStatus.Defaults),
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
