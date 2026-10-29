using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The docs site's menu and API pages follow the repository, so a new guide, package README or run-time package
/// reaches the site without anyone remembering it (design/adr/0115). DocFX builds every page whether the menu lists
/// it or not, so a page the menu forgets is reached only by search, and its build passes.
/// </summary>
/// <remarks>
/// The menu is <c>site/toc.yml</c>, read line by line in the one shape it has: top-level entries, the sections among
/// them holding <c>items</c>, each entry a <c>name</c> and an <c>href</c>.
/// </remarks>
public sealed partial class DocsSiteTests
{
    private const string Site = "site";

    /// <summary>The packages with no API pages, and why: their READMEs are their reference.</summary>
    private static readonly Dictionary<string, string> WithoutApiPages = new(StringComparer.Ordinal)
    {
        ["CodoMetis.TypeKit.Analyzers"] = "its public surface is its rules, which its README lists",
        ["CodoMetis.TypeKit.Generators"] = "its aspects are internal, and its surface is what they generate into a consumer",
    };

    private static readonly Lazy<IReadOnlyList<MenuEntry>> Menu = new(() => ReadMenu(File.ReadAllLines(Path.Combine(Repository.Root, Site, "toc.yml"))));

    [Fact]
    public void The_menu_lists_the_guides_in_the_order_of_the_README()
    {
        var guides = Section("Guides").Select(entry => entry.Href);
        var listed = ReadmeSamples.DocumentationLinks(File.ReadAllText(Path.Combine(Repository.Root, ReadmeSamples.RootReadme)));

        guides.ShouldBe(listed.Select(page => $"../{page}"),
            "site/toc.yml's Guides are the pages of the README's Documentation table, in its order.");
    }

    [Fact]
    public void The_menu_lists_every_package_README_as_reference()
    {
        var reference = Section("Reference").Select(entry => entry.Href);

        reference.ShouldBe(Repository.ShippingProjects().Select(package => $"../src/{package}/README.md"), ignoreOrder: true,
            "site/toc.yml's Reference lists every shipping package's README, and nothing else.");
    }

    [Fact]
    public void The_menu_starts_at_the_README_and_ends_with_the_API_and_the_changelog()
    {
        Menu.Value.Select(entry => entry.Name).ShouldBe(["Home", "Guides", "Reference", "API", "Changelog"]);
        Menu.Value[0].Href.ShouldBe("../README.md");
        Menu.Value[^1].Href.ShouldBe("../CHANGELOG.md");
    }

    [Fact]
    public void The_API_pages_are_the_run_time_packages()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, Site, "docfx.json")));
        var assemblies = config.RootElement.GetProperty("metadata").EnumerateArray()
                               .SelectMany(metadata => metadata.GetProperty("src").EnumerateArray())
                               .SelectMany(source => source.GetProperty("files").EnumerateArray())
                               .Select(file => file.GetString()!);

        WithoutApiPages.Keys.ShouldBeSubsetOf(Repository.ShippingProjects(), "An exemption names a package that no longer exists.");
        assemblies.ShouldBe(Repository.ShippingProjects().Where(package => !WithoutApiPages.ContainsKey(package)).Select(package => $"{package}.dll"), ignoreOrder: true,
            "site/docfx.json's metadata reads every run-time package's assembly. A package without API pages goes into WithoutApiPages with the reason.");
    }

    [Fact]
    public void The_menu_is_read()
    {
        Menu.Value.Count.ShouldBeGreaterThanOrEqualTo(5);
        Section("Guides").Count.ShouldBeGreaterThanOrEqualTo(10);
    }

    private static IReadOnlyList<MenuEntry> Section(string name) =>
        Menu.Value.SingleOrDefault(entry => entry.Name == name)?.Items ?? throw new InvalidOperationException($"site/toc.yml has no section '{name}'.");

    private static IReadOnlyList<MenuEntry> ReadMenu(string[] lines)
    {
        var menu = new List<MenuEntry>();
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0 || line.Trim() == "items:") continue;

            var match = Entry().Match(line);
            if (!match.Success) throw new InvalidOperationException($"site/toc.yml has a line this test cannot read: '{line}'.");

            var nested = match.Groups["indent"].Length > 2;
            var value = match.Groups["value"].Value;
            if (match.Groups["key"].Value == "name")
            {
                var entry = new MenuEntry(value);
                if (nested) menu[^1].Items.Add(entry);
                else menu.Add(entry);
            }
            else
            {
                var owner = nested ? menu[^1].Items[^1] : menu[^1];
                owner.Href = value;
            }
        }
        return menu;
    }

    private sealed record MenuEntry(string Name)
    {
        /// <summary>Empty for a section, which has <see cref="Items"/> instead.</summary>
        public string Href { get; set; } = "";

        public List<MenuEntry> Items { get; } = [];
    }

    [GeneratedRegex(@"^(?<indent>[ -]*)(?<key>name|href): (?<value>.+)$")]
    private static partial Regex Entry();
}
