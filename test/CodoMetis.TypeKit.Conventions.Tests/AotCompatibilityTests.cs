namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every package that ships run-time code is built with the trim and AOT analyzers on
/// (<c>IsAotCompatible</c>), so a call that trimming or Native AOT cannot honour fails the build,
/// where warnings are errors, instead of a consumer's publish (docs/plan.md §11).
/// </summary>
/// <remarks>
/// Read from the packed assembly, which carries the SDK's <c>IsAotCompatible</c> metadata when the
/// property was set for its build. The projects are discovered from <c>src/</c>, so a new package is
/// held to this without an edit here, unless it is exempted below with a reason.
/// </remarks>
[Collection(PacksCollection.Name)]
public sealed class AotCompatibilityTests(AnalyzerPackagingTests.Packs packs)
{
    /// <summary>The packages whose own code never runs in a consumer's application.</summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["CodoMetis.TypeKit.Analyzers"] = "runs inside the compiler (netstandard2.0), and packs no lib/ assembly",
        ["CodoMetis.TypeKit.Generators"] =
            "its aspects run inside the compiler; what they weave lands in the consumer's assembly, and the consumer smoke test " +
            "publishes that with Native AOT",
    };

    public static TheoryData<string> RuntimePackages => [.. Repository.ShippingProjects().Where(package => !Exempt.ContainsKey(package))];

    [Fact]
    public void The_runtime_packages_are_discovered() => RuntimePackages.Count.ShouldBeGreaterThanOrEqualTo(3);

    /// <summary>An exemption for a package that no longer exists would exempt nothing and hide the rename.</summary>
    [Fact]
    public void Every_exemption_names_a_shipping_package() =>
        Exempt.Keys.ShouldBeSubsetOf(Repository.ShippingProjects());

    [Theory]
    [MemberData(nameof(RuntimePackages))]
    public void The_package_is_built_AOT_compatible(string package)
    {
        var metadata = packs.AssemblyMetadata(package).ShouldNotBeNull($"{package} packs no lib/net10.0/{package}.dll");

        metadata.GetValueOrDefault("IsAotCompatible").ShouldBe("True", $"{package} is not built with IsAotCompatible, so the trim and AOT analyzers do not run on it.");
        metadata.GetValueOrDefault("IsTrimmable").ShouldBe("True", $"{package} is not marked trimmable.");
    }
}
