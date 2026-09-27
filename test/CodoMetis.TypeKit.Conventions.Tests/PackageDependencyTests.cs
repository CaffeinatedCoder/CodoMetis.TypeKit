using System.Xml.Linq;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The dependency chain a consumer restores, read from the nuspecs NuGet actually writes.
/// </summary>
/// <remarks>
/// The consumer smoke test cannot see every link: its host references the domain project too, so a
/// satellite that forgot its dependency on the base package would still restore there. These read
/// the packed nuspecs directly, so each package is held to its own declaration.
/// </remarks>
[Collection(PacksCollection.Name)]
public sealed class PackageDependencyTests(AnalyzerPackagingTests.Packs packs)
{
    private const string BasePackage      = "CodoMetis.TypeKit";
    private const string AnalyzerPackage  = "CodoMetis.TypeKit.Analyzers";
    private const string GeneratorPackage = "CodoMetis.TypeKit.Generators";

    public static TheoryData<string> ShippingPackages => [.. Repository.ShippingProjects()];

    /// <summary>Everything that runs next to the base package, so everything but the base and the analyzer.</summary>
    public static TheoryData<string> DependentPackages => [.. Dependents()];

    [Fact]
    public void The_dependent_packages_are_discovered() => Dependents().Count.ShouldBeGreaterThanOrEqualTo(3);

    private static List<string> Dependents() =>
        [.. Repository.ShippingProjects().Where(package => package is not (BasePackage or AnalyzerPackage))];

    [Fact]
    public void Every_package_has_the_one_version()
    {
        var versions = Repository.ShippingProjects().Select(package => (package, version: Version(packs.Nuspec(package)))).ToList();

        versions.Select(entry => entry.version).Distinct().Count()
                .ShouldBe(1, $"The packages carry different versions: {string.Join(", ", versions.Select(entry => $"{entry.package} {entry.version}"))}.");
    }

    [Theory]
    [MemberData(nameof(DependentPackages))]
    public void The_package_depends_on_the_base_package_of_its_own_version(string package)
    {
        var nuspec = packs.Nuspec(package);

        var dependency = Dependencies(nuspec).SingleOrDefault(element => (string?)element.Attribute("id") == BasePackage);

        dependency.ShouldNotBeNull($"{package} does not depend on {BasePackage}, so a consumer that installs only {package} gets no contracts.");
        ((string?)dependency.Attribute("version")).ShouldBe(Version(packs.Nuspec(BasePackage)),
            $"{package} depends on another version of {BasePackage} than the one released with it.");
    }

    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void Only_the_generators_bring_Metalama(string package)
    {
        var metalama = Dependencies(packs.Nuspec(package))
                      .Select(element => (string?)element.Attribute("id") ?? "")
                      .Where(id => id.StartsWith("Metalama.", StringComparison.OrdinalIgnoreCase))
                      .ToList();

        if (package == GeneratorPackage)
            metalama.ShouldContain("Metalama.Framework", $"{GeneratorPackage} does not bring Metalama.Framework, so nothing is generated for its consumers.");
        else
            metalama.ShouldBeEmpty($"{package} brings Metalama to its consumers. Only {GeneratorPackage} may.");
    }

    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void No_package_depends_on_the_generators(string package) =>
        Dependencies(packs.Nuspec(package)).Select(element => (string?)element.Attribute("id"))
                                          .ShouldNotContain(GeneratorPackage,
                                              $"{package} depends on {GeneratorPackage}. The satellites work at run time against the interfaces, and taking Metalama on is the consumer's named choice.");

    private static string Version(XDocument nuspec) =>
        nuspec.Descendants().Single(element => element.Name.LocalName == "version" && element.Parent?.Name.LocalName == "metadata").Value;

    /// <summary>By local name: NuGet picks the nuspec's XML namespace by the features a package uses.</summary>
    private static IEnumerable<XElement> Dependencies(XDocument nuspec) =>
        nuspec.Descendants().Where(element => element.Name.LocalName == "dependency");
}
