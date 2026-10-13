using System.ComponentModel;
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

    /// <summary>
    /// <c>All</c> cannot be written through a cast: a member replaced there would be listed, offered
    /// and documented while <c>Parse</c> still answered the original. A write that succeeds is undone
    /// before the test fails, so the set other tests read stays whole.
    /// </summary>
    [Fact]
    public void All_cannot_be_written_through_a_cast()
    {
        ProbeStatus.All.ShouldNotBeAssignableTo<ProbeStatus[]>();

        var list = (IList<ProbeStatus>)ProbeStatus.All;
        var replaced = false;
        try
        {
            list[0] = ProbeStatus.Cancelled;
            replaced = true;
        }
        catch (NotSupportedException)
        {
        }
        finally
        {
            if (replaced) list[0] = ProbeStatus.Open;
        }

        replaced.ShouldBeFalse("All was written through IList<T>");
        Should.Throw<NotSupportedException>(() => list.Clear());
        ProbeStatus.All[0].ShouldBeSameAs(ProbeStatus.Open);
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

    /// <summary>
    /// A found member is not null to the caller's nullable analysis: the generated <c>TryParse</c> says
    /// so, as the interface does. Without it this test does not compile (CS8602, an error here).
    /// </summary>
    [Fact]
    public void A_member_TryParse_found_is_not_null_to_the_compiler()
    {
        if (ProbeStatus.TryParse("Shipped", out var shipped)) shipped.Name.ShouldBe("Shipped");
        else throw new InvalidOperationException("Shipped is a member.");
    }

    /// <summary>The one seam: a hand-written <c>ToString()</c> is kept, and the name is still what JSON and parsing use.</summary>
    [Fact]
    public void A_hand_written_ToString_is_kept_and_the_name_still_travels()
    {
        ProbeLabelled.High.ToString().ShouldBe("high priority");
        $"{ProbeLabelled.Low}".ShouldBe("low priority");

        ProbeLabelled.High.Name.ShouldBe("High");
        JsonSerializer.Serialize(ProbeLabelled.High).ShouldBe("\"High\"");
        ReferenceEquals(ProbeLabelled.Parse("High"), ProbeLabelled.High).ShouldBeTrue();
    }

    /// <summary>
    /// An instance the set's own code made beside its members has no name. Before, its name was null:
    /// JSON wrote <c>null</c>, a nullable column stored one, and both read back as nothing.
    /// </summary>
    [Fact]
    public void An_instance_that_is_not_a_member_is_refused_wherever_its_name_is_read()
    {
        var unlisted = ProbeLevel.Unlisted();
        const string link = "#an-instance-that-is-not-a-member-of-its-set";

        Should.Throw<InvalidOperationException>(() => unlisted.Name).Message.ShouldEndWith(link);
        Should.Throw<InvalidOperationException>(() => unlisted.QualifiedName).Message.ShouldEndWith(link);
        Should.Throw<InvalidOperationException>(() => unlisted.ToString()).Message.ShouldEndWith(link);
        Should.Throw<InvalidOperationException>(() => unlisted.Match(low: () => 1, high: () => 2)).Message.ShouldEndWith(link);
        Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(unlisted)).Message.ShouldEndWith(link);

        ProbeLevel.High.Name.ShouldBe("High");
    }

    /// <summary>
    /// Every form of <c>new</c> CMTK1019 accepts is generated, and an overload of a generated method
    /// with another signature is the set's own: kept beside the generated one, not CMTK1011.
    /// </summary>
    [Fact]
    public void Members_created_in_any_form_and_overloads_of_another_signature_are_kept()
    {
        ProbePriority.All.Select(member => member.Name).ShouldBe(["Low", "Normal", "High"]);
        ProbePriority.Normal.Label.ShouldBe("normal (the default)");

        ReferenceEquals(ProbePriority.Parse(2), ProbePriority.Normal).ShouldBeTrue();
        ReferenceEquals(ProbePriority.Parse("High"), ProbePriority.High).ShouldBeTrue();

        var picked = "";
        ProbePriority.Low.Match(low: () => picked = "low", normal: () => picked = "normal", high: () => picked = "high");
        picked.ShouldBe("low");
        ProbePriority.High.Match(low: () => 1, normal: () => 2, high: () => 3).ShouldBe(3);
    }

    /// <summary>
    /// A set is a class, so a JSON null is the serializer's to read, as for any class: null, unless
    /// <c>RespectNullableAnnotations</c> refuses it for a property that is not nullable. The converter
    /// cannot refuse it itself without refusing it for an <c>OrderStatus?</c> property too.
    /// </summary>
    [Fact]
    public void A_JSON_null_is_the_serializer_s_to_read()
    {
        const string json = """{"Status":null,"Tier":"Basic"}""";

        JsonSerializer.Deserialize<Holder>(json)!.Status.ShouldBeNull();
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Holder>(json, new JsonSerializerOptions { RespectNullableAnnotations = true }));
        JsonSerializer.Deserialize<OptionalHolder>(json, new JsonSerializerOptions { RespectNullableAnnotations = true })!.Status.ShouldBeNull();
    }

    /// <summary>
    /// Every delegate is checked before a branch is picked, as Option's and Result's are: a null for a
    /// member not matched passed until that member first arrived.
    /// </summary>
    [Fact]
    public void Match_refuses_a_null_delegate_whichever_member_it_is_called_on()
    {
        foreach (var member in ProbeStatus.All)
        {
            Should.Throw<ArgumentNullException>(() => member.Match(open: null!, shipped: () => 2, cancelled: () => 3)).ParamName.ShouldBe("open");
            Should.Throw<ArgumentNullException>(() => member.Match(open: () => 1, shipped: null!, cancelled: () => 3)).ParamName.ShouldBe("shipped");
            Should.Throw<ArgumentNullException>(() => member.Match(open: () => 1, shipped: () => 2, cancelled: null!)).ParamName.ShouldBe("cancelled");
        }
    }

    /// <summary>A dictionary key is the member's name, as the value is; the serializer refused a set as a key both ways before.</summary>
    [Fact]
    public void A_dictionary_key_is_the_name_and_reads_the_member_itself_back()
    {
        var counts = new Dictionary<ProbeStatus, int> { [ProbeStatus.Open] = 1, [ProbeStatus.Cancelled] = 3 };

        var json = JsonSerializer.Serialize(counts);
        json.ShouldBe("""{"Open":1,"Cancelled":3}""");

        var back = JsonSerializer.Deserialize<Dictionary<ProbeStatus, int>>(json)!;
        back.Keys.ShouldBe([ProbeStatus.Open, ProbeStatus.Cancelled]);
        back[ProbeStatus.Cancelled].ShouldBe(3);

        // As the value: no naming policy renames a key, which would not read back.
        JsonSerializer.Serialize(counts, new JsonSerializerOptions { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase }).ShouldBe(json);

        var refusal = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Dictionary<ProbeStatus, int>>("""{"Nope":1}"""));
        refusal.Message.ShouldContain("ProbeStatus");
        refusal.Message.ShouldNotContain("Nope");

        Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(new Dictionary<ProbeLevel, int> { [ProbeLevel.Unlisted()] = 1 }))
              .Message.ShouldEndWith("#an-instance-that-is-not-a-member-of-its-set");
    }

    /// <summary>A member's name lower-cased may be a keyword; the parameter is still that name, escaped where it is written.</summary>
    [Fact]
    public void A_member_whose_parameter_is_a_keyword_is_matched_by_the_escaped_name()
    {
        ProbeKeyword.Default.Match(@new: () => "new", @default: () => "default").ShouldBe("default");
        ProbeKeyword.New.Name.ShouldBe("New");
        typeof(ProbeKeyword).GetMethod("Match")!.GetParameters().Select(parameter => parameter.Name).ShouldBe(["new", "default"]);
    }

    /// <summary>Generic code that parses through <see cref="IParsable{TSelf}"/> reaches a set, implemented explicitly beside its own <c>Parse</c>.</summary>
    [Fact]
    public void Generic_code_parses_a_set_through_IParsable()
    {
        ReferenceEquals(ParseAs<ProbeStatus>("Shipped"), ProbeStatus.Shipped).ShouldBeTrue();
        TryParseAs<ProbeStatus>("Cancelled", out var cancelled).ShouldBeTrue();
        ReferenceEquals(cancelled, ProbeStatus.Cancelled).ShouldBeTrue();
        TryParseAs<ProbeStatus>("shipped", out _).ShouldBeFalse("names compare ordinally");
        TryParseAs<ProbeStatus>(null, out _).ShouldBeFalse();

        var refusal = Should.Throw<FormatException>(() => ParseAs<ProbeStatus>("Nope"));
        refusal.Message.ShouldContain("ProbeStatus");
        refusal.Message.ShouldNotContain("Nope");
        Should.Throw<ArgumentNullException>(() => ParseAs<ProbeStatus>(null!));

        typeof(ProbeStatus).GetMethods().Where(method => method.Name == "Parse").ShouldHaveSingleItem("the provider overload is explicit");
    }

    /// <summary>
    /// What reads a string through <see cref="TypeDescriptor"/>, the reflection-based configuration
    /// binder and MVC among them, finds a set by its type converter alone: without one, a set bound to
    /// null with no error.
    /// </summary>
    [Fact]
    public void The_type_converter_reads_and_writes_the_name()
    {
        var converter = TypeDescriptor.GetConverter(typeof(ProbeStatus));

        converter.CanConvertFrom(typeof(string)).ShouldBeTrue();
        converter.CanConvertTo(typeof(string)).ShouldBeTrue();
        ReferenceEquals(converter.ConvertFromInvariantString("Shipped"), ProbeStatus.Shipped).ShouldBeTrue();
        converter.ConvertToInvariantString(ProbeStatus.Cancelled).ShouldBe("Cancelled");

        var refusal = Should.Throw<FormatException>(() => converter.ConvertFromInvariantString("Nope"));
        refusal.Message.ShouldNotContain("Nope");
        Should.Throw<InvalidOperationException>(() => TypeDescriptor.GetConverter(typeof(ProbeLevel)).ConvertToInvariantString(ProbeLevel.Unlisted()))
              .Message.ShouldEndWith("#an-instance-that-is-not-a-member-of-its-set");
    }

    private static T ParseAs<T>(string s) where T : IParsable<T> => T.Parse(s, null);

    private static bool TryParseAs<T>(string? s, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T result) where T : IParsable<T> => T.TryParse(s, null, out result);

    private static string Names<TSet>() where TSet : class, IClosedSet<TSet> => string.Join(",", TSet.All.Select(member => member.Name));

    private sealed class Counter : IClosedSetVisitor<int>
    {
        public int Visit<TSet>() where TSet : class, IClosedSet<TSet> => TSet.All.Count;
    }

    private sealed record Holder(ProbeStatus Status, ProbeTier Tier);

    private sealed record OptionalHolder(ProbeStatus? Status, ProbeTier Tier);
}
