namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// Every public member of the shipping libraries is named by a claim's surfaces or waived with a reason, so the API
/// cannot grow without deciding which oracle holds it (design/adr/0112).
/// </summary>
/// <remarks>
/// <see cref="ApiSurface"/> enumerates the members by reflection, <c>test/oracles/claims.json</c> says which a claim
/// covers, and <c>test/oracles/surface-waivers.json</c> says which need no oracle and why. A waiver that no longer
/// matches an unclaimed member is stale and fails too, so the list shrinks as the claims grow.
/// </remarks>
public sealed class OracleSurfaceCoverageTests
{
    /// <summary>A floor against the enumeration going quiet.</summary>
    private const int KnownMembers = 300;

    [Fact]
    public void The_surface_is_enumerated()
    {
        ApiSurface.Members.Count.ShouldBeGreaterThanOrEqualTo(KnownMembers);
        ApiSurface.Members.ShouldContain("Result.CatchAsync");
        ApiSurface.Members.ShouldContain("Option.Some");
        ApiSurface.Members.ShouldContain("FaultResponse.WithType");
    }

    [Fact]
    public void Every_public_member_is_claimed_or_waived()
    {
        var claimed = Claimed();
        var waived = OracleCatalogue.SurfaceWaivers.Keys.SelectMany(ApiSurface.Matching).ToHashSet(StringComparer.Ordinal);

        ApiSurface.Members.Where(member => !claimed.Contains(member) && !waived.Contains(member))
                  .ShouldBeEmpty("These public members are named by no claim and no waiver. Either a document promises something about "
                               + "them, and the claim goes into test/oracles/claims.json, or they need no oracle, and test/oracles/surface-waivers.json says why.");
    }

    [Fact]
    public void No_waiver_is_stale()
    {
        var claimed = Claimed();
        var stale = new List<string>();

        foreach (var (glob, reason) in OracleCatalogue.SurfaceWaivers)
        {
            var members = ApiSurface.Matching(glob);
            if (members.Count == 0)
                stale.Add($"'{glob}' matches no public member");
            else if (members.All(claimed.Contains))
                stale.Add($"'{glob}' matches only members a claim already covers ({members.Count}): remove the waiver");
            if (reason.Trim().Length < 10)
                stale.Add($"'{glob}' needs a reason");
        }

        stale.ShouldBeEmpty("Waivers to clean up.");
    }

    private static HashSet<string> Claimed() =>
        OracleCatalogue.Claims.SelectMany(claim => claim.Surfaces).Distinct().SelectMany(ApiSurface.Matching).ToHashSet(StringComparer.Ordinal);
}
