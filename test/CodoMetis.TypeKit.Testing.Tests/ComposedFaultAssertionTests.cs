namespace CodoMetis.TypeKit.Testing.Tests;

/// <summary>
/// A composed fault has no public content, so <c>ShouldHold(reason)</c> hands back the fault it holds with
/// that reason, detail and rule intact, and otherwise fails naming the fault it holds.
/// </summary>
public sealed class ComposedFaultAssertionTests
{
    public enum TeamFault { NotFound, NameTaken }

    public enum ConcurrencyFault { Conflict, Stale }

    public enum RemovalFault { StillReferenced }

    public enum StaleRule { VersionBehind }

    private const string Composed = "Fault<ComposedFaultAssertionTests.TeamFault, ComposedFaultAssertionTests.ConcurrencyFault>";

    private static Result<Fault<TeamFault, ConcurrencyFault>> Stale =>
        Fault<TeamFault, ConcurrencyFault>.Of(Fault.Of(ConcurrencyFault.Stale, "It is at version 4, not 3.").WithRule(StaleRule.VersionBehind));

    [Fact]
    public void The_held_fault_comes_back_whole()
    {
        var fault = Stale.ShouldBeError().ShouldHold(ConcurrencyFault.Stale);

        fault.Detail.ShouldBe("It is at version 4, not 3.");
        fault.Rule.ShouldBe("ComposedFaultAssertionTests.StaleRule.VersionBehind");
    }

    [Fact]
    public void A_fault_of_the_first_type_comes_back() =>
        Fault<TeamFault, ConcurrencyFault>.Of(TeamFault.NameTaken).ShouldHold(TeamFault.NameTaken).Reason.ShouldBe(TeamFault.NameTaken);

    [Fact]
    public void A_fault_of_the_other_type_fails_naming_it() =>
        Should.Throw<TypeKitAssertionException>(() => Stale.ShouldBeError().ShouldHold(TeamFault.NotFound))
              .Message.ShouldBe($"Expected the fault NotFound, but the {Composed} is the fault " +
                                "Fault { Reason = Stale, Detail = It is at version 4, not 3., Rule = ComposedFaultAssertionTests.StaleRule.VersionBehind }.");

    [Fact]
    public void Another_reason_of_the_same_type_fails_naming_it() =>
        Should.Throw<TypeKitAssertionException>(() => Stale.ShouldBeError().ShouldHold(ConcurrencyFault.Conflict))
              .Message.ShouldStartWith($"Expected the fault Conflict, but the {Composed} is the fault Fault {{ Reason = Stale,");

    [Fact]
    public void An_uninitialized_fault_fails_as_one() =>
        Should.Throw<TypeKitAssertionException>(() => (new Fault<TeamFault, ConcurrencyFault>[1])[0].ShouldHold(TeamFault.NotFound))
              .Message.ShouldBe($"Expected the fault NotFound, but the {Composed} is uninitialized: a default instance, which holds no fault.");

    [Fact]
    public void Each_of_three_types_comes_back()
    {
        Fault<TeamFault, ConcurrencyFault, RemovalFault> team = Fault.Of(TeamFault.NotFound);
        Fault<TeamFault, ConcurrencyFault, RemovalFault> concurrency = Fault.Of(ConcurrencyFault.Conflict, "Raced.");
        Fault<TeamFault, ConcurrencyFault, RemovalFault> removal = Fault.Of(RemovalFault.StillReferenced);

        team.ShouldHold(TeamFault.NotFound).Detail.ShouldBeNull();
        concurrency.ShouldHold(ConcurrencyFault.Conflict).Detail.ShouldBe("Raced.");
        removal.ShouldHold(RemovalFault.StillReferenced).Reason.ShouldBe(RemovalFault.StillReferenced);

        Should.Throw<TypeKitAssertionException>(() => removal.ShouldHold(TeamFault.NotFound));
        Should.Throw<TypeKitAssertionException>(() => team.ShouldHold(RemovalFault.StillReferenced));
    }

    [Fact]
    public void An_uninitialized_fault_of_three_types_fails_as_one() =>
        Should.Throw<TypeKitAssertionException>(() => (new Fault<TeamFault, ConcurrencyFault, RemovalFault>[1])[0].ShouldHold(RemovalFault.StillReferenced))
              .Message.ShouldEndWith("is uninitialized: a default instance, which holds no fault.");
}
