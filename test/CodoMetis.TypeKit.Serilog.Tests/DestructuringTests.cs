using CodoMetis.TypeKit.Generators.Probes;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CodoMetis.TypeKit.Serilog.Tests;

/// <summary>
/// With <c>Destructure.WithTypeKit()</c>, a value object is logged as what it prints wherever Serilog
/// destructures it, so a masked one stays masked (spikes/SensitiveValues, findings 4 and 5).
/// </summary>
public sealed class DestructuringTests
{
    private const string Secret = "hunter2-secret";

    private static readonly ProbeSecret Masked = ProbeSecret.From(Secret);
    private static readonly ProbePin Pin = ProbePin.From(271828);
    private static readonly ProbeId Id = ProbeId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee"));
    private static readonly ProbeCount Count = ProbeCount.From(3);

    public sealed record Holder(ProbeSecret Secret, ProbePin Pin, ProbeId Id, string Name);

    public sealed record Plain(string Name, int Age);

    [Fact]
    public void Without_it_a_destructured_value_object_shows_what_it_masks()
    {
        // Why the package exists: Serilog reads the public Value, past the ToString() that masks it.
        Render(configure: null, "{@Secret}", Masked).ShouldContain(Secret);
    }

    [Fact]
    public void A_masked_value_object_is_logged_as_its_mask_when_destructured()
    {
        Render("{@Secret}", Masked).ShouldBe("\"***\"");
        Render("{@Pin}", Pin).ShouldBe("\"****\"");
    }

    [Fact]
    public void Inside_a_destructured_record_list_or_dictionary_it_stays_masked()
    {
        var holder = Render("{@Holder}", new Holder(Masked, Pin, Id, "Ann"));
        holder.ShouldBe("Holder { Secret: \"***\", Pin: \"****\", Id: \"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee\", Name: \"Ann\" }");

        Render("{@List}", new List<ProbeSecret> { Masked, Masked }).ShouldBe("[\"***\", \"***\"]");
        Render("{@Map}", new Dictionary<string, ProbeSecret> { ["a"] = Masked }).ShouldBe("[(\"a\": \"***\")]");
    }

    [Fact]
    public void Any_other_value_object_is_logged_as_its_text_not_as_its_Value_property()
    {
        Render("{@Id}", Id).ShouldBe("\"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee\"");
        Render("{@Count}", Count).ShouldBe("\"3\"");
    }

    [Fact]
    public void A_nullable_value_object_is_destructured_as_the_value_object()
    {
        ProbeSecret? none = null;
        ProbeCount? count = Count;

        Render("{@Count}", count).ShouldBe("\"3\"");
        Render("{@None}", none).ShouldBe("null");
    }

    [Fact]
    public void Scalars_and_other_types_are_left_to_Serilog()
    {
        Render("{Secret}", Masked).ShouldBe("\"***\"");
        Render("{@Plain}", new Plain("Ann", 42)).ShouldBe("Plain { Name: \"Ann\", Age: 42 }");
    }

    private static string Render(string template, object? value) =>
        Render(configure: destructure => destructure.WithTypeKit(), template, value);

    private static string Render(Func<global::Serilog.Configuration.LoggerDestructuringConfiguration, LoggerConfiguration>? configure, string template, object? value)
    {
        var events = new List<LogEvent>();
        var configuration = new LoggerConfiguration().WriteTo.Sink(new ListSink(events));
        if (configure is not null) configuration = configure(configuration.Destructure);

        using (var logger = configuration.CreateLogger())
            logger.Information(template, value);

        return events.ShouldHaveSingleItem().Properties.Single().Value.ToString();
    }

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
