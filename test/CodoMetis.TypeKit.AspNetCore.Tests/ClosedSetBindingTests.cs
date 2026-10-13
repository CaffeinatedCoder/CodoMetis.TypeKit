using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.Extensions.Configuration;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A closed set in configuration: the reflection-based binder reads it through its type converter.
/// Without one, a set property bound to null with no error, the setting silently ignored.
/// </summary>
public sealed class ClosedSetBindingTests
{
    public sealed class Settings
    {
        public ProbeStatus? Status { get; set; }

        public ProbeTier Tier { get; set; } = ProbeTier.Basic;

        public List<ProbeStatus> Allowed { get; set; } = [];
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value))).Build();

    [Fact]
    public void A_set_binds_by_its_member_s_name()
    {
        var settings = Configuration(("Status", "Shipped"), ("Tier", "Premium"), ("Allowed:0", "Open"), ("Allowed:1", "Cancelled")).Get<Settings>()!;

        ReferenceEquals(settings.Status, ProbeStatus.Shipped).ShouldBeTrue();
        ReferenceEquals(settings.Tier, ProbeTier.Premium).ShouldBeTrue();
        settings.Allowed.ShouldBe([ProbeStatus.Open, ProbeStatus.Cancelled]);
    }

    /// <summary>An unknown name fails the binding, as it fails Parse, and the message does not quote the setting.</summary>
    [Fact]
    public void An_unknown_name_fails_the_binding_without_quoting_it()
    {
        var refusal = Should.Throw<InvalidOperationException>(() => Configuration(("Status", "Nope")).Get<Settings>());

        refusal.InnerException.ShouldBeOfType<FormatException>().Message.ShouldContain("ProbeStatus");
        refusal.InnerException!.Message.ShouldNotContain("Nope");
    }
}
