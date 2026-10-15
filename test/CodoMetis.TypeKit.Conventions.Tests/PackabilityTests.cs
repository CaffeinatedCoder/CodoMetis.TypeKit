using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Only the shipping projects under <c>src/</c> pack. CI and the release run <c>dotnet pack</c> on the
/// whole solution and release.yml publishes every <c>.nupkg</c> it writes, so a test project, the
/// benchmarks or a tool under <c>tools/</c> that packs would be pushed to nuget.org beside the packages.
/// </summary>
/// <remarks>
/// Read from MSBuild's evaluation of each project, not from its file: a test project is unpackable
/// through <c>test/Directory.Build.props</c>, which no project file names.
/// </remarks>
public sealed partial class PackabilityTests
{
    private static IReadOnlyList<string> SolutionProjects() =>
        [.. SolutionProject().Matches(File.ReadAllText(Path.Combine(Repository.Root, "CodoMetis.TypeKit.slnx"))).Select(match => match.Groups["path"].Value)];

    public static TheoryData<string> ProjectsOutsideSrc => [.. SolutionProjects().Where(path => !path.StartsWith("src/", StringComparison.Ordinal))];

    [Fact]
    public void The_projects_outside_src_are_found()
    {
        var projects = SolutionProjects().Where(path => !path.StartsWith("src/", StringComparison.Ordinal)).ToList();

        projects.Count.ShouldBeGreaterThanOrEqualTo(10);
        projects.ShouldContain("tools/DocsQuery/DocsQuery.csproj");
    }

    [Theory]
    [MemberData(nameof(ProjectsOutsideSrc))]
    public async Task A_project_outside_src_never_packs(string project)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", ["msbuild", Path.Combine(Repository.Root, project), "-getProperty:IsPackable", "-nodeReuse:false"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        })!;

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.ShouldBe(0, $"dotnet msbuild {project} failed:{Environment.NewLine}{await output}{await errors}");
        (await output).Trim().ShouldBe("false", $"{project} packs, and release.yml would publish it. Set <IsPackable>false</IsPackable>.");
    }

    [GeneratedRegex(@"<Project Path=""(?<path>[^""]+\.csproj)""")]
    private static partial Regex SolutionProject();
}
