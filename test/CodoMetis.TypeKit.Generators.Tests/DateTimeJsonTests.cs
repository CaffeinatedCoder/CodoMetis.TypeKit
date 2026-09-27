using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>Tests that switch the process's local time zone, and so must not run beside any other.</summary>
[CollectionDefinition(nameof(LocalTimeZoneCollection), DisableParallelization = true)]
public sealed class LocalTimeZoneCollection;

/// <summary>
/// A <c>DateTime</c> value object is written and read as UTC, and the server's own time zone never
/// decides what an instant means.
/// </summary>
/// <remarks>
/// <para>
/// An <c>Unspecified</c> value (an offset-less JSON string, a <c>datetime2</c> column) was run through
/// <c>ToUniversalTime()</c>, which reads it as server-local time: on a server at +02:00, 12:00 became
/// 10:00Z. It is now taken as UTC, keeping its digits. A dictionary key was not normalised at all,
/// so the same instant read as a different key than value.
/// </para>
/// <para>
/// Every test runs in a time zone away from UTC. CI runs in UTC, where the old conversion was a
/// no-op, so a test that ran in the machine's own zone could not see the defect there.
/// </para>
/// </remarks>
[Collection(nameof(LocalTimeZoneCollection))]
public sealed class DateTimeJsonTests
{
    [Fact]
    public void An_unspecified_date_time_is_written_as_UTC_with_its_own_digits() =>
        InTimeZoneAwayFromUtc(() =>
        {
            var noon = ProbeTimestamp.From(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Unspecified));

            JsonSerializer.Serialize(noon).ShouldBe("\"2026-09-27T12:00:00Z\"");
            JsonSerializer.Serialize(new Dictionary<ProbeTimestamp, int> { [noon] = 1 }).ShouldBe("{\"2026-09-27T12:00:00.0000000Z\":1}");
        });

    [Fact]
    public void A_date_time_without_an_offset_is_read_as_UTC() =>
        InTimeZoneAwayFromUtc(() =>
        {
            var read = JsonSerializer.Deserialize<ProbeTimestamp>("\"2026-09-27T12:00:00\"").Value;

            read.ShouldBe(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc));
            read.Kind.ShouldBe(DateTimeKind.Utc);
        });

    [Fact]
    public void A_date_time_key_is_normalised_to_UTC_like_the_value() =>
        InTimeZoneAwayFromUtc(() =>
        {
            const string instant = "2026-09-27T14:00:00.0000000+02:00";

            var asValue = JsonSerializer.Deserialize<ProbeTimestamp>($"\"{instant}\"");
            var asKey   = JsonSerializer.Deserialize<Dictionary<ProbeTimestamp, int>>($"{{\"{instant}\":1}}")!.Keys.ShouldHaveSingleItem();

            asValue.Value.ShouldBe(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc));
            asKey.ShouldBe(asValue);
            asKey.Value.Kind.ShouldBe(DateTimeKind.Utc);
        });

    /// <summary>
    /// Switches the local time zone through <c>TZ</c>, which .NET reads on Linux and macOS, to one
    /// with a fixed offset of +05:30. The assertion on the offset keeps a platform that ignores
    /// <c>TZ</c> from passing vacuously.
    /// </summary>
    private static void InTimeZoneAwayFromUtc(Action test)
    {
        var previous = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", "Asia/Kolkata");
        TimeZoneInfo.ClearCachedData();

        try
        {
            TimeZoneInfo.Local.BaseUtcOffset.ShouldBe(new TimeSpan(5, 30, 0), "The local time zone did not switch, so this test cannot see a conversion through it.");
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", previous);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
