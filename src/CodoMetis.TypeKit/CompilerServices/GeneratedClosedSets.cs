using System.ComponentModel;
using System.Text.Json;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>What the generated closed-set members throw. Public only so generated code can call it; there is nothing to call by hand.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedClosedSets
{
    /// <summary>The refusal of <c>Parse</c>: the set has no member of that name. Names the set, never the input.</summary>
    /// <param name="set">The set's name.</param>
    /// <returns>The exception to throw.</returns>
    public static FormatException NoMember(string set) =>
        new($"{set} has no member of that name.{Refusals.ClosedSetMember}");

    /// <summary>The refusal of the JSON converter: the set has no member of that name, or the token is not a string. Names the set, never the input.</summary>
    /// <param name="set">The set's name.</param>
    /// <returns>The exception to throw.</returns>
    public static JsonException NoMemberInJson(string set) =>
        new($"{set} has no member of that name.{Refusals.ClosedSetMember}");

    /// <summary>
    /// The refusal of <c>Name</c>, <c>ToString()</c> and <c>Match</c> on an instance the set's own code
    /// created beside its members, or on a member read before the set has named it: it has no name, and
    /// a null written as JSON or stored in a column would read back as nothing.
    /// </summary>
    /// <param name="set">The set's name.</param>
    /// <returns>The exception to throw.</returns>
    public static InvalidOperationException NotAMember(string set) =>
        new($"{set}: this instance is not a member of the set, so it has no name.{Refusals.ClosedSetNotAMember}");

    /// <summary>
    /// The refusal of <c>All</c>, <c>Parse</c>, <c>TryParse</c> and the JSON converter while the set's
    /// static fields are still being initialized: <c>All</c> is filled after every member exists, and a
    /// member declared after the reading field is still null, so either would be read as null for the
    /// rest of the process.
    /// </summary>
    /// <param name="set">The set's name.</param>
    /// <returns>The exception to throw.</returns>
    public static InvalidOperationException StillInitializing(string set) =>
        new($"{set} is still being initialized: a static field initializer of the set read All, or a member by name, before every member existed.{Refusals.ClosedSetInitializing}");
}
