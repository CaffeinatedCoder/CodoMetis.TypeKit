using System.ComponentModel;

namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// What the JSON converter that CodoMetis.TypeKit.Generators generates calls, kept in ordinary C# so
/// it is written once and testable without a generator.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedJson
{
    /// <summary>
    /// The UTC <see cref="DateTime"/> a value object writes and reads, without ever consulting the
    /// server's time zone for a value that does not name one.
    /// </summary>
    /// <remarks>
    /// A <see cref="DateTimeKind.Local"/> value is an instant in this machine's zone and converts
    /// exactly. An <see cref="DateTimeKind.Unspecified"/> one (an offset-less JSON string, a column
    /// without a time zone) names no zone, and <see cref="DateTime.ToUniversalTime"/> would read it as
    /// server-local, so 12:00 became 10:00Z on a server at +02:00 and stayed 12:00Z in UTC. It is taken
    /// as UTC instead, keeping its digits.
    /// </remarks>
    /// <param name="value">The value to normalise.</param>
    /// <returns><paramref name="value"/> as a <see cref="DateTimeKind.Utc"/> value.</returns>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}
