using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// What the generated members add to the wrapped type's own work: nothing on the heap, and no other
/// JSON. Each allocation case allocated before its fix, measured by the benchmarks on 2026-09-28:
/// <c>TryFormat</c> boxed the wrapped value (32 bytes a call), comparing an enum-backed value object
/// boxed both operands (367 KB to sort 1,000), and the JSON converter wrote and read a number through
/// a nested serializer call.
/// </summary>
/// <remarks>
/// These run against a Debug build of the probes, where the JIT never removes a box, so a boxing
/// call fails here even where an optimising JIT would have hidden it on one machine and not another.
/// </remarks>
public sealed class AllocationAndNumberHandlingTests
{
    private const int Calls = 100;

    private static long Allocated(Action action)
    {
        // Twice first: type loading, static initialisation and the JSON contract caches allocate once.
        action();
        action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static bool Utf8<T>(T value, byte[] destination) where T : IUtf8SpanFormattable =>
        value.TryFormat(destination, out _, default, null);

    [Fact]
    public void TryFormat_allocates_nothing()
    {
        var chars = new char[64];
        var bytes = new byte[64];

        var id     = ProbeId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee"));
        var amount = ProbeAmount.From(1.5m);
        var count  = ProbeCount.From(42);

        Allocated(() => id.TryFormat(chars, out _, default, null)).ShouldBe(0, "Guid, characters");
        Allocated(() => amount.TryFormat(chars, out _, "N2", CultureInfo.InvariantCulture)).ShouldBe(0, "decimal, characters");
        Allocated(() => count.TryFormat(chars, out _, default, null)).ShouldBe(0, "int, characters");
        Allocated(() => Utf8(id, bytes)).ShouldBe(0, "Guid, UTF-8");
        Allocated(() => Utf8(amount, bytes)).ShouldBe(0, "decimal, UTF-8");
    }

    /// <summary><c>ToString(format, provider)</c> allocates its string, and nothing besides.</summary>
    [Fact]
    public void ToString_allocates_what_the_wrapped_type_allocates()
    {
        var amount = ProbeAmount.From(1.5m);
        var raw    = 1.5m;

        Allocated(() => amount.ToString("N2", CultureInfo.InvariantCulture))
            .ShouldBe(Allocated(() => raw.ToString("N2", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Comparing_allocates_nothing()
    {
        var monday  = ProbeWeekday.From(DayOfWeek.Monday);
        var friday  = ProbeWeekday.From(DayOfWeek.Friday);
        var small   = ProbeAmount.From(1m);
        var large   = ProbeAmount.From(2m);
        var first   = ProbeId.From(Guid.Parse("00000000-0000-7000-8000-000000000001"));
        var second  = ProbeId.From(Guid.Parse("00000000-0000-7000-8000-000000000002"));

        Allocated(() => monday.CompareTo(friday)).ShouldBe(0, "an enum");
        Allocated(() => _ = monday < friday).ShouldBe(0, "an enum, through an operator");
        Allocated(() => small.CompareTo(large)).ShouldBe(0, "decimal");
        Allocated(() => first.CompareTo(second)).ShouldBe(0, "Guid");

        monday.CompareTo(friday).ShouldBeLessThan(0);
        friday.CompareTo(monday).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_number_is_written_and_read_with_what_the_wrapped_type_allocates()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var buffer  = new ArrayBufferWriter<byte>();
        var writer  = new Utf8JsonWriter(buffer);
        var utf8    = "1.5"u8.ToArray();

        void Write<T>(T value)
        {
            buffer.ResetWrittenCount();
            writer.Reset();
            JsonSerializer.Serialize(writer, value, options);
        }

        Allocated(() => Write(ProbeAmount.From(1.5m))).ShouldBe(Allocated(() => Write(1.5m)), "written");
        Allocated(() => JsonSerializer.Deserialize<ProbeAmount>(utf8, options)).ShouldBe(Allocated(() => JsonSerializer.Deserialize<decimal>(utf8, options)), "read");
    }

    public static TheoryData<JsonNumberHandling> Handlings =>
    [
        JsonNumberHandling.Strict,
        JsonNumberHandling.AllowReadingFromString,
        JsonNumberHandling.WriteAsString,
        JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString,
        JsonNumberHandling.AllowNamedFloatingPointLiterals,
        JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString,
    ];

    /// <summary>
    /// The converter calls the wrapped type's converter directly where that writes and reads exactly
    /// what the serializer would, and the serializer where the number handling applies: every
    /// combination, both directions, the refusals included, must match the wrapped type.
    /// </summary>
    [Theory]
    [MemberData(nameof(Handlings))]
    public void A_number_follows_the_options_number_handling_exactly_as_the_wrapped_type(JsonNumberHandling handling)
    {
        var options = new JsonSerializerOptions { NumberHandling = handling };

        Outcome(() => JsonSerializer.Serialize(ProbeAmount.From(1.5m), options)).ShouldBe(Outcome(() => JsonSerializer.Serialize(1.5m, options)), "decimal written");
        Outcome(() => JsonSerializer.Serialize(ProbeRatio.From(double.NaN), options)).ShouldBe(Outcome(() => JsonSerializer.Serialize(double.NaN, options)), "NaN written");
        Outcome(() => JsonSerializer.Serialize(ProbeRatio.From(0.25), options)).ShouldBe(Outcome(() => JsonSerializer.Serialize(0.25, options)), "double written");

        foreach (var json in new[] { "1.5", "\"1.5\"", "\"NaN\"", "true", "\"x\"" })
        {
            Outcome(() => JsonSerializer.Deserialize<ProbeAmount>(json, options).Value).ShouldBe(Outcome(() => JsonSerializer.Deserialize<decimal>(json, options)), $"decimal read from {json}");
            Outcome(() => JsonSerializer.Deserialize<ProbeRatio>(json, options).Value).ShouldBe(Outcome(() => JsonSerializer.Deserialize<double>(json, options)), $"double read from {json}");
        }
    }

    private static string Outcome<T>(Func<T> action)
    {
        try
        {
            return string.Create(CultureInfo.InvariantCulture, $"{action()}");
        }
        catch (Exception exception)
        {
            return exception.GetType().Name;
        }
    }
}
