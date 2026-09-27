using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The parts of the release path that fail silently, or on release day, rather than on a PR.
/// </summary>
/// <remarks>
/// Trusted Publishing matches on the workflow's <em>file name</em>, the environment and the
/// repository, none of which the build sees: rename one and the next tag fails with an
/// authentication error that says nothing about the rename. The SBOM has the opposite shape: a
/// build-only reference added later would silently widen it into a document that contradicts the
/// nuspec. The workflows are matched by YAML structure (a key at its indentation), never by a phrase
/// that a comment explaining the rule could also contain.
/// </remarks>
public sealed partial class ReleaseWiringTests
{
    /// <summary>The workflow file name the nuget.org trusted-publishing policy names.</summary>
    private const string ReleaseWorkflowFileName = "release.yml";

    /// <summary>The environment the policy expects, and the approval gate.</summary>
    private const string PublishEnvironment = "nuget";

    private const string SmokeTest = "./test/consumer-smoke-test.sh";

    private static string ReleaseWorkflow => Workflow(ReleaseWorkflowFileName);

    private static string BuildWorkflow => Workflow("dotnet.yml");

    [Fact]
    public void The_release_workflow_has_the_file_name_the_policy_names() =>
        File.Exists(Path.Combine(Repository.Root, ".github", "workflows", ReleaseWorkflowFileName))
            .ShouldBeTrue($"No .github/workflows/{ReleaseWorkflowFileName}. The trusted-publishing policy names the workflow by file name, " +
                          "so renaming it stops publishing with an authentication error that does not mention the rename.");

    [Fact]
    public void The_publish_job_is_gated_on_the_environment_the_policy_expects() =>
        Job(ReleaseWorkflow, "publish").ShouldMatch($@"(?m)^\s+environment:\s*{PublishEnvironment}\s*$",
            $"The publish job is not gated on the '{PublishEnvironment}' environment. The OIDC token carries it as a claim, and it is the approval gate.");

    /// <summary>The split exists so that nothing running unattended can mint a publishing token, and no job can both publish and write to the repository.</summary>
    [Fact]
    public void Only_the_publish_job_may_mint_a_token()
    {
        var jobs = Jobs(ReleaseWorkflow);

        jobs.Keys.ShouldBe(["verify", "publish", "release"], ignoreOrder: true, "The release workflow no longer has the verify/publish/release split these tests assume.");

        foreach (var (name, job) in jobs)
        {
            var mintsToken = IdTokenWrite().IsMatch(job);

            mintsToken.ShouldBe(name == "publish",
                name == "publish" ? "The publish job does not request id-token: write, so OIDC login cannot work." : $"The {name} job may mint an OIDC token.");

            if (mintsToken)
                ContentsWrite().IsMatch(job).ShouldBeFalse("The publish job can both mint a token and write to the repository.");
        }
    }

    [Fact]
    public void The_release_checks_the_tag_against_the_version_property() =>
        Job(ReleaseWorkflow, "verify").ShouldContain("does not match Directory.Build.props",
            Case.Sensitive, "The release does not verify the pushed tag against the Version property, so it would publish whatever was typed.");

    [Fact]
    public void The_release_refuses_a_version_the_changelog_has_not_dated() =>
        Job(ReleaseWorkflow, "verify").ShouldMatch(@"(?m)^\s+- name: Verify the changelog dates this version\s*$",
            "The release no longer checks that CHANGELOG.md dates the version it publishes.");

    [Fact]
    public void Both_workflows_run_the_consumer_smoke_test_against_the_packed_feed()
    {
        File.Exists(Path.Combine(Repository.Root, SmokeTest)).ShouldBeTrue($"{SmokeTest} does not exist.");

        foreach (var (workflow, text) in new[] { ("release.yml", ReleaseWorkflow), ("dotnet.yml", BuildWorkflow) })
            text.ShouldMatch($@"(?m)^\s+run:\s*{Regex.Escape(SmokeTest)} dist\s*$", $"{workflow} does not run the smoke test against its packed feed.");

        BuildWorkflow.ShouldMatch($@"(?m)^\s+run:\s*{Regex.Escape(SmokeTest)}\s*$",
            "dotnet.yml does not run the smoke test in its other mode, packing its own feed.");
    }

    [Fact]
    public void The_smoke_test_is_executable()
    {
        if (OperatingSystem.IsWindows()) return;

        File.GetUnixFileMode(Path.Combine(Repository.Root, SmokeTest)).HasFlag(UnixFileMode.UserExecute)
            .ShouldBeTrue($"{SmokeTest} is not executable, and the workflows run it directly.");
    }

    /// <summary>
    /// Measured 2026-09-27: without the filter, the analyzer package's SBOM lists 13 Roslyn
    /// components while its nuspec declares no dependency at all.
    /// </summary>
    [Fact]
    public void Every_build_only_reference_is_excluded_from_both_SBOM_steps()
    {
        var buildOnly = Repository.ShippingProjects()
                                  .SelectMany(BuildOnlyReferences)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .Order(StringComparer.Ordinal)
                                  .ToList();

        buildOnly.ShouldNotBeEmpty("No PrivateAssets=all reference found; the analyzer's Roslyn references are, so the discovery is broken.");

        foreach (var (workflow, text) in new[] { ("release.yml", ReleaseWorkflow), ("dotnet.yml", BuildWorkflow) })
        {
            var excluded = ExcludeFilter().Matches(text)
                                          .SelectMany(match => match.Groups["ids"].Value.Split(','))
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

            buildOnly.Where(reference => !excluded.Contains(reference)).ShouldBeEmpty(
                $"{workflow}'s SBOM step does not exclude these PrivateAssets=all references, which no consumer receives. " +
                "Add them to its comma-separated --exclude-filter.");
        }
    }

    [Fact]
    public void The_SBOM_tool_is_pinned_in_the_tool_manifest()
    {
        var manifest = Path.Combine(Repository.Root, ".config", "dotnet-tools.json");

        File.Exists(manifest).ShouldBeTrue($"No tool manifest at {manifest}, and both workflows run `dotnet tool restore`, which then does nothing.");
        File.ReadAllText(manifest).ShouldContain("\"cyclonedx\"", Case.Sensitive, "The tool manifest does not pin cyclonedx.");
    }

    /// <summary>One publishing path: a second one, holding an API key, is the one that skips a check.</summary>
    [Fact]
    public void Nothing_else_pushes_packages()
    {
        var pushers = SourceFiles(Repository.Root)
                               .Where(path => Path.GetExtension(path) is ".sh" or ".ps1" or ".yml" or ".yaml" or ".cmd")
                               .Where(path => Path.GetFileName(path) != ReleaseWorkflowFileName)
                               .Where(path => File.ReadAllText(path).Contains("nuget push", StringComparison.OrdinalIgnoreCase))
                               .Select(path => Path.GetRelativePath(Repository.Root, path))
                               .ToList();

        pushers.ShouldBeEmpty("These push packages beside release.yml. Publishing goes through release.yml and Trusted Publishing only.");
    }

    /// <summary>The repository's own files: no build output, no VCS or IDE state.</summary>
    private static IEnumerable<string> SourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory)) yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child) is "bin" or "obj" or ".git" or ".idea" or "artifacts" or "TestResults" or "out") continue;

            foreach (var file in SourceFiles(child)) yield return file;
        }
    }

    private static IEnumerable<string> BuildOnlyReferences(string project) =>
        XDocument.Load(Path.Combine(Repository.Root, "src", project, $"{project}.csproj"))
                 .Descendants("PackageReference")
                 .Where(reference => string.Equals(
                      ((string?)reference.Attribute("PrivateAssets") ?? reference.Element("PrivateAssets")?.Value)?.Trim(),
                      "all", StringComparison.OrdinalIgnoreCase))
                 .Select(reference => (string?)reference.Attribute("Include"))
                 .OfType<string>();

    private static string Workflow(string fileName) =>
        File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", fileName));

    private static string Job(string workflow, string name) =>
        Jobs(workflow).TryGetValue(name, out var job) ? job : throw new ShouldAssertException($"The workflow has no '{name}' job.");

    /// <summary>Each job's text, from its key under <c>jobs:</c> to the next job's.</summary>
    private static Dictionary<string, string> Jobs(string workflow)
    {
        var jobs = workflow[(workflow.IndexOf("\njobs:", StringComparison.Ordinal) + 1)..];
        var keys = JobKey().Matches(jobs).ToList();

        return keys.Select((key, index) => (
                        name: key.Groups["name"].Value,
                        text: jobs[key.Index..(index + 1 < keys.Count ? keys[index + 1].Index : jobs.Length)]))
                   .ToDictionary(job => job.name, job => job.text);
    }

    [GeneratedRegex(@"(?m)^  (?<name>[A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobKey();

    [GeneratedRegex(@"(?m)^\s+id-token:\s*write")]
    private static partial Regex IdTokenWrite();

    [GeneratedRegex(@"(?m)^\s+contents:\s*write")]
    private static partial Regex ContentsWrite();

    [GeneratedRegex(@"--exclude-filter\s+(?<ids>[^\s\\]+)")]
    private static partial Regex ExcludeFilter();
}
