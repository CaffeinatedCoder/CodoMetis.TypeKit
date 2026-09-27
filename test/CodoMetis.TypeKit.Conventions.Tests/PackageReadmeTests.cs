namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every shipping package carries its README, and the package says so: nuget.org shows the README
/// named in the nuspec, and nothing when the file is packed but not named, or named but not packed.
/// </summary>
/// <remarks>
/// The projects are discovered from <c>src/</c>, so a new package is held to this without an edit
/// here, and the floor below keeps the theory from passing on an empty list.
/// </remarks>
[Collection(PacksCollection.Name)]
public sealed class PackageReadmeTests(AnalyzerPackagingTests.Packs packs)
{
    public static TheoryData<string> ShippingPackages => [.. Repository.ShippingProjects()];

    [Fact]
    public void The_shipping_packages_are_discovered() => Repository.ShippingProjects().Count.ShouldBeGreaterThanOrEqualTo(5);

    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_package_names_its_README(string package)
    {
        var readme = packs.Nuspec(package).Descendants().SingleOrDefault(element => element.Name.LocalName == "readme");

        readme.ShouldNotBeNull($"{package} names no README in its nuspec, so nuget.org shows none. Set PackageReadmeFile (src/Directory.Build.props).");
        readme.Value.ShouldBe("README.md");
    }

    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_package_contains_its_README(string package) =>
        packs.Entries(package).ShouldContain("README.md", $"{package} does not pack its README.md.");

    /// <summary>The README is the package's own: its title is the package id, not a copy of another package's README.</summary>
    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_README_is_titled_with_the_package_id(string package)
    {
        var firstLine = File.ReadLines(Path.Combine(Repository.Root, "src", package, "README.md")).First();

        firstLine.ShouldBe($"# {package}");
    }

    /// <summary>The repository's README maps every package to its own README, so a new package appears there too.</summary>
    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_root_README_links_to_the_package(string package) =>
        File.ReadAllText(Path.Combine(Repository.Root, "README.md"))
            .ShouldContain($"](src/{package}/README.md)", Case.Sensitive, $"README.md at the repository root has no link to src/{package}/README.md.");
}
