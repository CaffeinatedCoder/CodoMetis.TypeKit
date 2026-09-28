namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// No packed assembly carries types a source generator wrote into it.
/// </summary>
/// <remarks>
/// A package reference brings its source generators into the project that references it, and they
/// run on the shipping assembly whether or not it has any use for their output. Measured 2026-09-28:
/// <c>Microsoft.AspNetCore.OpenApi</c>'s XML-comment generator ran on
/// <c>CodoMetis.TypeKit.AspNetCore</c> and baked the XML documentation of its project references into
/// ten types nothing calls: the assembly was 229 KB, 37 KB without them. The satellite's project
/// file removes that generator; this reads what the packages actually ship. Microsoft's source
/// generators mark the types they emit with <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/>,
/// so the check needs no generator's name and catches one a future package reference brings.
/// </remarks>
[Collection(PacksCollection.Name)]
public sealed class GeneratedCodeTests(AnalyzerPackagingTests.Packs packs)
{
    public static TheoryData<string> ShippingPackages => [.. Repository.ShippingProjects()];

    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_package_ships_no_source_generated_types(string package)
    {
        var assemblies = packs.Entries(package).Where(entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();

        assemblies.ShouldNotBeEmpty($"{package} packs no assembly, so there is nothing to read.");

        foreach (var assembly in assemblies)
        {
            packs.GeneratedTypes(package, assembly)
                 .GroupBy(type => type.Tool)
                 .Select(tool => $"{tool.Count()} types from {tool.Key}")
                 .ShouldBeEmpty(
                      $"{assembly} in {package} ships types a source generator wrote into it. If a package reference's generator ran on " +
                      "the shipping project (Microsoft.AspNetCore.OpenApi's XML-comment generator did), remove it from the Analyzer items " +
                      "before CoreCompile, as CodoMetis.TypeKit.AspNetCore.csproj does; if the package uses the generator on purpose, " +
                      "exempt that tool here with the reason.");
        }
    }
}
