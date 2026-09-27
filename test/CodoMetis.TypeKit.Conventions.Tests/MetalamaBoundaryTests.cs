namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Only <c>CodoMetis.TypeKit.Generators</c> may bring Metalama into a consumer's build.
/// </summary>
/// <remarks>
/// <para>
/// The base package is Metalama-free so that someone who only wants <c>Option</c>/<c>Result</c>
/// never receives a compiler framework by accident, and the EF Core and ASP.NET Core satellites
/// work against the interfaces at run time. A Metalama reference arriving anywhere else, even
/// transitively, breaks that promise without breaking any build. Hence this test.
/// </para>
/// <para>
/// The positive control is what keeps the negative ones honest: if the assets file stopped being
/// read correctly, every "no Metalama here" assertion would pass on an empty list.
/// </para>
/// </remarks>
public sealed class MetalamaBoundaryTests
{
    private const string GeneratorsProject = "CodoMetis.TypeKit.Generators";

    public static TheoryData<string> ProjectsThatMustNotResolveMetalama =>
        [.. Repository.ShippingProjects().Where(project => project != GeneratorsProject)];

    [Fact]
    public void The_shipping_projects_are_discovered()
    {
        var projects = Repository.ShippingProjects();

        projects.Count.ShouldBeGreaterThanOrEqualTo(5, string.Join(", ", projects));
        projects.ShouldContain("CodoMetis.TypeKit");
        projects.ShouldContain(GeneratorsProject);
    }

    [Fact]
    public void The_generators_package_resolves_Metalama()
    {
        Repository.ResolvedPackages(GeneratorsProject).ShouldContain("Metalama.Framework");
    }

    [Theory]
    [MemberData(nameof(ProjectsThatMustNotResolveMetalama))]
    public void Only_the_generators_package_resolves_Metalama(string project)
    {
        var metalama = Repository.ResolvedPackages(project)
                                 .Where(id => id.StartsWith("Metalama.", StringComparison.OrdinalIgnoreCase))
                                 .ToArray();

        metalama.ShouldBeEmpty($"{project} resolves {string.Join(", ", metalama)}; only {GeneratorsProject} may bring Metalama in.");
    }
}
