using System.Text.Json;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.Generators.Probes;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// The closed-set family, generated for <see cref="ProbeStatus"/> and <see cref="ProbeTier"/>.
/// Measured first in <c>spikes/ClosedSets</c>; each claim there is a test here.
/// </summary>
public sealed class ClosedSetTests
{
    [Fact]
    public void All_holds_the_members_themselves_in_declaration_order()
    {
        ProbeStatus.All.Select(member => member.Name).ShouldBe(["Open", "Shipped", "Cancelled"]);
        ReferenceEquals(ProbeStatus.All[1], ProbeStatus.Shipped).ShouldBeTrue();
        ProbeTier.All.Select(member => member.Name).ShouldBe(["Basic", "Premium"]);
    }

    [Fact]
    public void Name_QualifiedName_and_ToString_come_from_the_field()
    {
        ProbeStatus.Open.Name.ShouldBe("Open");
        ProbeStatus.Cancelled.QualifiedName.ShouldBe("ProbeStatus.Cancelled");
        ProbeStatus.Shipped.ToString().ShouldBe("Shipped");
        $"{ProbeTier.Premium}".ShouldBe("Premium");
    }

    [Fact]
    public void Constant_data_comes_through_the_declared_constructor()
    {
        ProbeTier.Premium.Rank.ShouldBe(2);
        ProbeTier.Premium.Perk.ShouldBe("Priority support.");
        ProbeTier.Basic.Perk.ShouldBeNull();
    }

    [Fact]
    public void Match_takes_one_delegate_per_member_and_picks_the_branch()
    {
        ProbeStatus.Shipped.Match(open: () => "o", shipped: () => "s", cancelled: () => "c").ShouldBe("s");
        ProbeTier.Basic.Match(basic: () => 1, premium: () => 2).ShouldBe(1);
    }

    [Fact]
    public void TryParse_and_Parse_find_a_member_by_name_and_refuse_without_quoting()
    {
        ProbeStatus.TryParse("Shipped", out var shipped).ShouldBeTrue();
        ReferenceEquals(shipped, ProbeStatus.Shipped).ShouldBeTrue();
        ProbeStatus.TryParse("shipped", out _).ShouldBeFalse("names compare ordinally");
        ProbeStatus.TryParse(null, out _).ShouldBeFalse();

        ReferenceEquals(ProbeStatus.Parse("Open"), ProbeStatus.Open).ShouldBeTrue();

        var refusal = Should.Throw<FormatException>(() => ProbeStatus.Parse("Nope"));
        refusal.Message.ShouldContain("ProbeStatus");
        refusal.Message.ShouldNotContain("Nope");
        refusal.Message.ShouldContain("#a-closed-set-has-no-member-of-that-name");
    }

    [Fact]
    public void JSON_is_the_name_and_reads_the_member_itself_back()
    {
        var json = JsonSerializer.Serialize(new Holder(ProbeStatus.Shipped, ProbeTier.Premium));
        json.ShouldBe("""{"Status":"Shipped","Tier":"Premium"}""");

        var back = JsonSerializer.Deserialize<Holder>(json)!;
        ReferenceEquals(back.Status, ProbeStatus.Shipped).ShouldBeTrue();
        ReferenceEquals(back.Tier, ProbeTier.Premium).ShouldBeTrue();

        var refusal = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Holder>("""{"Status":"Nope","Tier":"Basic"}"""));
        refusal.Message.ShouldContain("ProbeStatus");
        refusal.Message.ShouldNotContain("Nope");

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Holder>("""{"Status":3,"Tier":"Basic"}""")).Message.ShouldContain("ProbeStatus");
    }

    [Fact]
    public void Members_are_singletons_and_the_constructor_is_private()
    {
        (ProbeStatus.Open == ProbeStatus.Parse("Open")).ShouldBeTrue();
        typeof(ProbeStatus).GetConstructors().ShouldBeEmpty("no public constructor");
        typeof(ProbeStatus).IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void The_attribute_names_the_members_and_reaches_the_set_typed()
    {
        var attribute = typeof(ProbeTier).GetCustomAttributes(inherit: false).OfType<GeneratedClosedSetAttribute>().ShouldHaveSingleItem();

        attribute.SetType.ShouldBe(typeof(ProbeTier));
        attribute.Names.ShouldBe(["Basic", "Premium"]);
        attribute.Accept(new Counter()).ShouldBe(2);
    }

    [Fact]
    public void Generic_code_reaches_a_set_through_the_interface()
    {
        Names<ProbeStatus>().ShouldBe("Open,Shipped,Cancelled");
        Names<ProbeTier>().ShouldBe("Basic,Premium");
    }

    private static string Names<TSet>() where TSet : class, IClosedSet<TSet> => string.Join(",", TSet.All.Select(member => member.Name));

    private sealed class Counter : IClosedSetVisitor<int>
    {
        public int Visit<TSet>() where TSet : class, IClosedSet<TSet> => TSet.All.Count;
    }

    private sealed record Holder(ProbeStatus Status, ProbeTier Tier);
}
