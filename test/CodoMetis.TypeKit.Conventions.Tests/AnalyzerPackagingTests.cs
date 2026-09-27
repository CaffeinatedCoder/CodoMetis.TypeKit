using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The analyzers reach consumers because <c>CodoMetis.TypeKit</c> depends on the analyzer package,
/// and the analyzer package ships its assembly under <c>analyzers/dotnet/cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both halves fail silently. Measured 2026-09-27: a project reference with
/// <c>ReferenceOutputAssembly="false"</c> emits no dependency at all, and the build stays green
/// while every consumer loses CMTK0001 and CMTK0002. So these tests read what NuGet actually
/// emits, from a real pack, rather than the project files.
/// </para>
/// <para>
/// The dependency must not exclude <c>Analyzers</c> either. On SDK 10.0.401 an excluded analyzer
/// still loads, because the SDK takes analyzers from every package in the restore graph, but the
/// dependency should say what is intended rather than lean on that.
/// </para>
/// </remarks>
[Collection(PacksCollection.Name)]
public sealed class AnalyzerPackagingTests(AnalyzerPackagingTests.Packs packs)
{
    private const string BasePackage     = "CodoMetis.TypeKit";
    private const string AnalyzerPackage = "CodoMetis.TypeKit.Analyzers";

    [Fact]
    public void The_base_package_depends_on_the_analyzer_package_for_every_framework()
    {
        var groups = Named(packs.Nuspec(BasePackage), "group").ToList();

        groups.ShouldNotBeEmpty("the base package declares no dependency groups");

        foreach (var group in groups)
        {
            var dependency = group.Elements().Where(element => element.Name.LocalName == "dependency")
                                  .SingleOrDefault(element => (string?)element.Attribute("id") == AnalyzerPackage);

            dependency.ShouldNotBeNull(
                $"{BasePackage} ({group.Attribute("targetFramework")?.Value}) has no dependency on {AnalyzerPackage}, so no consumer gets the analyzers.");

            var excluded = ((string?)dependency.Attribute("exclude") ?? "").Split(',', StringSplitOptions.TrimEntries);
            excluded.ShouldNotContain("Analyzers", StringComparer.OrdinalIgnoreCase,
                $"The dependency on {AnalyzerPackage} excludes its analyzers. Set PrivateAssets=\"none\" on the reference.");
        }
    }

    [Fact]
    public void The_analyzer_package_ships_the_analyzer_and_nothing_to_compile_against()
    {
        var entries = packs.Entries(AnalyzerPackage);

        entries.ShouldContain($"analyzers/dotnet/cs/{AnalyzerPackage}.dll");
        entries.Where(entry => entry.StartsWith("lib/", StringComparison.Ordinal)).ShouldBeEmpty(
            "The analyzer package has a lib/ folder, so every consumer would compile against the analyzer assembly.");
    }

    [Fact]
    public void The_analyzer_package_is_not_a_development_dependency()
    {
        var flag = Named(packs.Nuspec(AnalyzerPackage), "developmentDependency").SingleOrDefault()?.Value;

        (flag ?? "false").ShouldBe("false", "A development dependency does not flow to the consumers of CodoMetis.TypeKit.");
    }

    /// <summary>
    /// Elements by local name. NuGet picks the nuspec's XML namespace by the features a package
    /// uses (a development dependency is written against the 2010/07 schema, a plain package against
    /// 2013/05), so a query for one namespace finds nothing in the other and passes vacuously.
    /// </summary>
    private static IEnumerable<XElement> Named(XDocument nuspec, string localName) =>
        nuspec.Descendants().Where(element => element.Name.LocalName == localName);

    /// <summary>
    /// Every shipping package, packed once per test run from the current sources and shared by the
    /// packaging test classes through <see cref="PacksCollection"/>: two fixtures packing the same
    /// project at once would race on its build output.
    /// </summary>
    public sealed class Packs : IAsyncLifetime
    {
        private readonly string _output = Path.Combine(Path.GetTempPath(), $"codometis-typekit-pack-{Guid.NewGuid():N}");

        public async ValueTask InitializeAsync()
        {
            foreach (var project in Repository.ShippingProjects())
                await Pack(project);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_output)) Directory.Delete(_output, recursive: true);
            return ValueTask.CompletedTask;
        }

        public XDocument Nuspec(string package)
        {
            using var archive = ZipFile.OpenRead(Package(package));
            using var stream  = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();

            return XDocument.Load(stream);
        }

        public IReadOnlyList<string> Entries(string package)
        {
            using var archive = ZipFile.OpenRead(Package(package));

            return [.. archive.Entries.Select(entry => entry.FullName)];
        }

        private string Package(string package) =>
            Directory.GetFiles(_output, $"{package}.*.nupkg")
                     .Where(path => !path.EndsWith(".snupkg", StringComparison.Ordinal))
                     .Single(path => char.IsDigit(Path.GetFileName(path)[package.Length + 1]));

        /// <summary>
        /// <c>--no-restore</c>: the convention tests read restore output, and a restore here could
        /// rewrite it while another test reads it.
        /// </summary>
        private async Task Pack(string project)
        {
            var csproj = Path.Combine(Repository.Root, "src", project, $"{project}.csproj");

            using var process = Process.Start(new ProcessStartInfo("dotnet", ["pack", csproj, "--no-restore", "-c", "Release", "-o", _output, "-nodeReuse:false"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            })!;

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            process.ExitCode.ShouldBe(0, $"dotnet pack {project} failed:{Environment.NewLine}{await output}{await errors}");
        }
    }
}

/// <summary>The one <see cref="AnalyzerPackagingTests.Packs"/> the packaging test classes share.</summary>
[CollectionDefinition(Name)]
public sealed class PacksCollection : ICollectionFixture<AnalyzerPackagingTests.Packs>
{
    public const string Name = "Packs";
}
