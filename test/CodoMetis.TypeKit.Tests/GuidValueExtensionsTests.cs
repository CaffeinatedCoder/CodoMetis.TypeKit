using System.Globalization;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>New()</c> on Guid-backed identifiers. <see cref="ProbeId"/> implements the two interfaces by
/// hand, which is all the extension needs; the generated implementation is tested with the generators.
/// </summary>
public sealed class GuidValueExtensionsTests
{
    private readonly record struct ProbeId : IValueObject<ProbeId, Guid>, IPlainValueObject<ProbeId, Guid>
    {
        private ProbeId(Guid value) => Value = value;

        public Guid Value { get; }

        public static ProbeId From(Guid value) => new(value);
    }

    [Fact]
    public void New_creates_distinct_version_7_identifiers()
    {
        var first  = ProbeId.New();
        var second = ProbeId.New();

        first.Value.Version.ShouldBe(7);
        second.Value.Version.ShouldBe(7);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void New_with_a_timestamp_embeds_its_milliseconds()
    {
        var timestamp = new DateTimeOffset(2026, 9, 27, 12, 0, 0, 123, TimeSpan.Zero);

        var id = ProbeId.New(timestamp);

        EmbeddedMilliseconds(id.Value).ShouldBe(timestamp.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Identifiers_a_millisecond_apart_sort_in_creation_order()
    {
        var start = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

        var ids = Enumerable.Range(0, 5).Select(offset => ProbeId.New(start.AddMilliseconds(offset))).ToList();

        ids.Select(id => id.Value.ToString("N")).ShouldBe(ids.Select(id => id.Value.ToString("N")).Order(StringComparer.Ordinal));
    }

    /// <summary>A version 7 Guid starts with 48 bits of Unix milliseconds, the first 12 hex digits.</summary>
    private static long EmbeddedMilliseconds(Guid id) =>
        long.Parse(id.ToString("N")[..12], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
