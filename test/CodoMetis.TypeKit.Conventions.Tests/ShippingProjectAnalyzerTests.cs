using System.Diagnostics;
using System.Text.Json;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every shipping project runs the CMTK analyzers on its own code, as a consumer's project does.
/// </summary>
/// <remarks>
/// <para>
/// A project reference does not bring a project's analyzers along (dotnet/roslyn#79272), so the
/// shipping projects built without them, and a breach in the library's own code compiled. Measured: the
/// Testing and AspNetCore packages forwarded <c>T1</c> and <c>T2</c> into <c>Fault&lt;T1, T2&gt;</c>'s
/// <c>[Distinct]</c> parameters without carrying it, 38 CMTK0010 errors found only by adding the analyzer
/// by hand (1a9d7f2).
/// </para>
/// <para>
/// Read from MSBuild's evaluation of each project, which is where <c>src/Directory.Build.targets</c>
/// adds the reference, and not from the project files. The analyzer projects are left out by what they
/// are (<c>IsRoslynComponent</c>), not by name, so a new package is held to this with no edit here.
/// </para>
/// </remarks>
public sealed class ShippingProjectAnalyzerTests
{
    private const string AnalyzerProject = "CodoMetis.TypeKit.Analyzers";

    public static TheoryData<string> Projects => [.. Repository.ShippingProjects()];

    [Theory]
    [MemberData(nameof(Projects))]
    public async Task A_shipping_project_runs_the_CMTK_analyzers_on_its_own_code(string project)
    {
        var path = Path.Combine(Repository.Root, "src", project, $"{project}.csproj");

        using var process = Process.Start(new ProcessStartInfo("dotnet", ["msbuild", path, "-getItem:ProjectReference", "-getProperty:IsRoslynComponent", "-nodeReuse:false"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        })!;

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.ShouldBe(0, $"dotnet msbuild {project} failed:{Environment.NewLine}{await output}{await errors}");

        using var evaluation = JsonDocument.Parse(await output);
        if (evaluation.RootElement.GetProperty("Properties").GetProperty("IsRoslynComponent").GetString() == "true") return;

        var references = evaluation.RootElement.GetProperty("Items").TryGetProperty("ProjectReference", out var items)
            ? [.. items.EnumerateArray()]
            : new List<JsonElement>();

        var analyzed = references.Any(reference => reference.GetProperty("Filename").GetString() == AnalyzerProject
            && reference.TryGetProperty("OutputItemType", out var type) && type.GetString() == "Analyzer");

        analyzed.ShouldBeTrue(
            $"{project} does not load {AnalyzerProject} as an analyzer, so a CMTK breach in its own code compiles. " +
            "src/Directory.Build.targets adds the reference to every project that has none; a project that references " +
            "the analyzer itself sets OutputItemType=\"Analyzer\" on that reference.");
    }
}
