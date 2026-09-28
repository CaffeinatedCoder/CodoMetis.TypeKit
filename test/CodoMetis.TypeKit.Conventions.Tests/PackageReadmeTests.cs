using System.Text.RegularExpressions;

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
public sealed partial class PackageReadmeTests(AnalyzerPackagingTests.Packs packs)
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

    /// <summary>
    /// What nuget.org shows and searches: a package without tags is found only by its exact id, and
    /// one without a commit in its repository element cannot be traced to the source it was built from.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_package_carries_the_metadata_nuget_org_shows(string package)
    {
        var metadata = packs.Nuspec(package).Descendants().Where(element => element.Parent?.Name.LocalName == "metadata")
                            .GroupBy(element => element.Name.LocalName)
                            .ToDictionary(group => group.Key, group => group.First());

        foreach (var name in new[] { "description", "tags", "copyright", "projectUrl", "releaseNotes" })
            metadata.ShouldContainKey(name, $"{package} has no <{name}> in its nuspec.");

        metadata["tags"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBeGreaterThanOrEqualTo(3, $"{package} has almost no tags.");
        metadata["license"].Value.ShouldBe("MIT");

        var repository = metadata["repository"];
        repository.Attribute("type")?.Value.ShouldBe("git");
        repository.Attribute("commit")?.Value.ShouldNotBeNullOrWhiteSpace($"{package} names no commit, so Source Link cannot tie it to its source.");
    }

    /// <summary>
    /// nuget.org renders the README but shows the description as plain text, in search results and on
    /// the package page, and a description is frozen with the version once pushed: markdown in it
    /// reaches every reader as literal backticks and asterisks.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_description_is_plain_text(string package)
    {
        var description = packs.Nuspec(package).Descendants()
                               .Single(element => element.Name.LocalName == "description" && element.Parent?.Name.LocalName == "metadata")
                               .Value;

        description.ShouldNotBeNullOrWhiteSpace($"{package} has an empty description.");
        Markdown().Matches(description).Select(match => match.Value).ShouldBeEmpty(
            $"{package}'s description contains markdown, which nuget.org shows literally: \"{description}\". Write it as plain text (<Description> in its project file).");
    }

    /// <summary>The repository's README maps every package to its own README, so a new package appears there too.</summary>
    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_root_README_links_to_the_package(string package) =>
        File.ReadAllText(Path.Combine(Repository.Root, "README.md"))
            .ShouldContain($"](src/{package}/README.md)", Case.Sensitive, $"README.md at the repository root has no link to src/{package}/README.md.");

    /// <summary>Inline code, emphasis, a link, or a heading or list marker at the start of a line.</summary>
    [GeneratedRegex(@"(?m)`|\*\*|__|\]\(|^\s*(?:#|[-*] )")]
    private static partial Regex Markdown();
}
