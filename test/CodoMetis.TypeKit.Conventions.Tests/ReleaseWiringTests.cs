using System.Text.RegularExpressions;

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

    /// <summary>The smoke test, a tool project (design/adr/0092), and the dll the workflows run.</summary>
    private const string SmokeTestProject = "tools/ConsumerSmokeTest/ConsumerSmokeTest.csproj";
    private const string SmokeTest = "dotnet tools/ConsumerSmokeTest/bin/Release/net10.0/consumer-smoke-test.dll";

    /// <summary>The prober, a tool project run as its built dll (design/adr/0092), which the workflows run with no model (design/adr/0095).</summary>
    private const string Prober = "dotnet tools/Prober/bin/Release/net10.0/prober.dll";

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

    /// <summary>
    /// A re-run of the publish job is the answer to a partial failure, so it must complete what is
    /// missing. Measured 2026-09-28 with the SDK's own push against a stand-in feed: a .nupkg the feed
    /// already has is skipped together with its .snupkg, so symbols pushed alongside their package
    /// are never retried, and the re-run goes green without them. The symbol packages are pushed in a
    /// step of their own; with <c>--no-symbols</c>, or without a symbol source NuGet can resolve,
    /// that push skips every .snupkg without an error.
    /// </summary>
    [Fact]
    public void A_re_run_of_the_publish_job_completes_missing_symbols()
    {
        var pushes = Steps(Job(ReleaseWorkflow, "publish")).Where(step => step.Contains("dotnet nuget push", StringComparison.Ordinal)).ToList();

        pushes.Count.ShouldBeGreaterThanOrEqualTo(2, "The publish job no longer pushes the packages and the symbol packages in steps of their own.");
        pushes.Where(step => !step.Contains("--skip-duplicate", StringComparison.Ordinal)).ShouldBeEmpty(
            "A push without --skip-duplicate fails a re-run on the first package nuget.org already has.");

        var symbols = pushes.Where(step => SymbolPackageGlob().IsMatch(step)).ToList();

        symbols.Count.ShouldBe(1,
            "No step of the publish job pushes the .snupkg files on their own. NuGet skips a .snupkg together with a duplicate .nupkg, " +
            "so a re-run would go green with a failed symbol upload still missing.");
        symbols[0].ShouldNotContain("--no-symbols", Case.Sensitive, "The symbol push passes --no-symbols, so NuGet skips every .snupkg without an error.");
        symbols[0].ShouldContain("--symbol-source", Case.Sensitive,
            "The symbol push names no --symbol-source, so a .snupkg NuGet resolves no symbol endpoint for is skipped without an error.");
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
        File.Exists(Path.Combine(Repository.Root, SmokeTestProject)).ShouldBeTrue($"{SmokeTestProject} does not exist.");

        foreach (var (workflow, text) in new[] { ("release.yml", ReleaseWorkflow), ("dotnet.yml", BuildWorkflow) })
            text.ShouldMatch($@"(?m)^\s+run:\s*{Regex.Escape(SmokeTest)} dist\s*$", $"{workflow} does not run the smoke test against its packed feed.");

        BuildWorkflow.ShouldMatch($@"(?m)^\s+run:\s*{Regex.Escape(SmokeTest)}\s*$",
            "dotnet.yml does not run the smoke test in its other mode, packing its own feed.");
    }

    /// <summary>
    /// The smoke test publishes a consumer with Native AOT, which needs clang and zlib: every job that
    /// runs it installs them first.
    /// </summary>
    [Fact]
    public void Every_job_that_runs_the_smoke_test_installs_the_Native_AOT_prerequisites_first()
    {
        var jobs = new[] { ("release.yml", ReleaseWorkflow), ("dotnet.yml", BuildWorkflow) }
                   .SelectMany(workflow => Jobs(workflow.Item2).Select(job => (Name: $"{workflow.Item1}:{job.Key}", Text: job.Value)))
                   .Where(job => SmokeTestStep().IsMatch(job.Text))
                   .ToList();

        jobs.Count.ShouldBeGreaterThanOrEqualTo(3, "the release's verify job and the build workflow's pack and consumer jobs");

        foreach (var (name, text) in jobs)
        {
            var install = AotPrerequisites().Match(text);

            install.Success.ShouldBeTrue($"{name} runs the smoke test without installing clang and zlib1g-dev.");
            install.Index.ShouldBeLessThan(SmokeTestStep().Match(text).Index, $"{name} installs the Native AOT prerequisites after the smoke test.");
        }
    }

    /// <summary>
    /// The workflows run the smoke test's built dll from bin/Release, so every job that runs it builds it in
    /// Release first: the solution's build, or the tool's own. Without one, the step fails on a dll that is
    /// not there, or runs one a cache left behind.
    /// </summary>
    [Fact]
    public void Every_job_that_runs_the_smoke_test_builds_it_first()
    {
        var jobs = new[] { ("release.yml", ReleaseWorkflow), ("dotnet.yml", BuildWorkflow) }
                   .SelectMany(workflow => Jobs(workflow.Item2).Select(job => (Name: $"{workflow.Item1}:{job.Key}", Text: job.Value)))
                   .Where(job => SmokeTestStep().IsMatch(job.Text))
                   .ToList();

        jobs.Count.ShouldBeGreaterThanOrEqualTo(3, "the release's verify job and the build workflow's pack and consumer jobs");

        foreach (var (name, text) in jobs)
        {
            var build = SmokeTestBuild().Match(text);

            build.Success.ShouldBeTrue($"{name} runs the smoke test without building it in Release: build the solution or {SmokeTestProject} with -c Release first.");
            build.Index.ShouldBeLessThan(SmokeTestStep().Match(text).Index, $"{name} builds the smoke test after running it.");
        }
    }

    /// <summary>
    /// CI replays the whole probe corpus on every push and pull request, with no model (design/adr/0095). The
    /// replay had been documented as running in CI while no workflow called it, and a dropped step fails nothing:
    /// a regression the corpus would catch merges instead.
    /// </summary>
    [Fact]
    public void The_build_workflow_replays_every_probe_and_reports_the_status()
    {
        var (name, steps) = ProberJob("dotnet.yml", BuildWorkflow);

        var replay = StrictReplay(name, steps);
        var status = steps.FindIndex(step => ProberStep("status").IsMatch(step));

        status.ShouldBeGreaterThan(replay, $"{name} does not run `prober status` after the replay, so the run reports no coverage.");
    }

    /// <summary>
    /// The release replays the corpus and runs the gate before it packs (design/adr/0095). The gate is advisory
    /// until a binding release is named (AGENTS.md, "Releasing"), so only its presence and its place are held
    /// here, and the comparison with the previous release: on a tag push, <c>git describe</c> alone names the tag
    /// being released, and the gate would compare the commit with itself and find nothing touched.
    /// </summary>
    [Fact]
    public void The_release_replays_every_probe_and_runs_the_gate_before_packing()
    {
        var job = Job(ReleaseWorkflow, "verify");
        var steps = Steps(job).ToList();

        var replay = StrictReplay("release.yml:verify", steps);
        var gate = steps.FindIndex(step => ProberStep("gate").IsMatch(step));
        var pack = steps.FindIndex(step => step.Contains("dotnet pack", StringComparison.Ordinal));

        gate.ShouldBeGreaterThan(replay, "release.yml:verify does not run `prober gate` after `prober replay --all`; the gate reads the replay's evidence.");
        pack.ShouldBeGreaterThan(gate, "release.yml:verify packs before the probe gate has run.");
        steps[gate].ShouldMatch(@"\bgate --since ""?\$", "The gate runs without --since, so on a tag push it compares the release with itself.");
        job.ShouldMatch(StateDirectory, "release.yml:verify does not set PROBE_STATE_DIR, so the prober writes into a developer's state directory.");
    }

    /// <summary>The one job of a workflow whose steps run the prober.</summary>
    private static (string Name, List<string> Steps) ProberJob(string workflow, string text)
    {
        var jobs = Jobs(text).Where(job => Steps(job.Value).Any(step => ProberStep("replay").IsMatch(step))).ToList();

        jobs.Count.ShouldBe(1, $"{workflow} has no job that runs `prober replay`, or more than one.");
        jobs[0].Value.ShouldMatch(StateDirectory, $"{workflow}:{jobs[0].Key} does not set PROBE_STATE_DIR, so the prober writes into a developer's state directory.");

        return ($"{workflow}:{jobs[0].Key}", Steps(jobs[0].Value).ToList());
    }

    /// <summary>
    /// The replay of every probe, built first, and able to fail the job: its exit code is the step's own, with no
    /// <c>|| true</c> and no <c>continue-on-error</c>.
    /// </summary>
    private static int StrictReplay(string name, List<string> steps)
    {
        var replay = steps.FindIndex(step => ProberStep("replay").IsMatch(step));
        var build = steps.FindIndex(step => ProberBuild().IsMatch(step));

        replay.ShouldBeGreaterThanOrEqualTo(0, $"{name} does not run `prober replay`.");
        steps[replay].ShouldMatch($@"(?m)^\s+run:\s*{Regex.Escape(Prober)} replay --all\s*$",
            $"{name} replays the probes some other way than `{Prober} replay --all` alone on its run line, so a failing probe may not fail the job.");
        steps[replay].ShouldNotMatch(@"(?m)^\s+continue-on-error:", $"{name} lets the replay fail without failing the job.");
        build.ShouldBeInRange(0, replay - 1, $"{name} runs the prober without building it in Release first: build the solution or tools/Prober with -c Release.");

        return replay;
    }

    /// <summary>PROBE_STATE_DIR set for the job: as an env key, or written to <c>$GITHUB_ENV</c> (the runner context is not available to a job's env).</summary>
    private const string StateDirectory = @"(?m)^\s+(PROBE_STATE_DIR:\s*\S|echo ""PROBE_STATE_DIR=\S+"" >> ""\$GITHUB_ENV"")";

    /// <summary>A step that runs a prober command; <see cref="Steps"/> has already dropped the comment lines.</summary>
    private static Regex ProberStep(string command) => new($@"{Regex.Escape(Prober)} {command}\b");

    /// <summary>
    /// Measured 2026-09-27: without the filter, the analyzer package's SBOM lists 13 Roslyn
    /// components while its nuspec declares no dependency at all. Measured 2026-09-28: the SDK adds
    /// <c>Microsoft.NET.ILLink.Tasks</c> to every <c>IsAotCompatible</c> project, build-only, and it
    /// reached the three run-time SBOMs while no nuspec names it. No project file declares it, so the
    /// build-only set is read from restore output, not from <c>PrivateAssets</c> in the project files.
    /// </summary>
    [Fact]
    public void Every_build_only_reference_is_excluded_from_both_SBOM_steps()
    {
        var found = Repository.ShippingProjects().SelectMany(Repository.BuildOnlyPackages).ToList();

        found.Where(package => !package.AutoReferenced).ShouldNotBeEmpty(
            "No PrivateAssets=all reference found in restore output; the analyzer's Roslyn references are, so the discovery is broken.");
        found.Where(package => package.AutoReferenced).ShouldNotBeEmpty(
            "No build-only reference added by the SDK found in restore output; IsAotCompatible adds one, so the discovery is broken.");

        var buildOnly = found.Select(package => package.Id)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Order(StringComparer.Ordinal)
                             .ToList();

        foreach (var (workflow, text) in new[] { ("release.yml", ReleaseWorkflow), ("dotnet.yml", BuildWorkflow) })
        {
            var excluded = ExcludeFilter().Matches(text)
                                          .SelectMany(match => match.Groups["ids"].Value.Split(','))
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

            buildOnly.Where(reference => !excluded.Contains(reference)).ShouldBeEmpty(
                $"{workflow}'s SBOM step does not exclude these build-only references (suppressParent All in restore output, " +
                "written as PrivateAssets=all or added by the SDK), which no consumer receives. Add them to its comma-separated --exclude-filter.");
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

    /// <summary>Each step's text, without its comment lines, so a step is judged by what it runs, not by what its comment explains.</summary>
    private static IEnumerable<string> Steps(string job) =>
        StepStart().Split(job)
                   .Skip(1)
                   .Select(step => string.Join('\n', step.Split('\n').Where(line => !line.TrimStart().StartsWith('#'))));

    [GeneratedRegex(@"(?m)^  (?<name>[A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobKey();

    [GeneratedRegex(@"(?m)^\s+- (?=name:|uses:)")]
    private static partial Regex StepStart();

    [GeneratedRegex(@"\*\.\S*\.snupkg\b")]
    private static partial Regex SymbolPackageGlob();

    [GeneratedRegex(@"(?m)^\s+run:\s*dotnet tools/ConsumerSmokeTest/bin/Release/net10\.0/consumer-smoke-test\.dll")]
    private static partial Regex SmokeTestStep();

    [GeneratedRegex(@"(?m)^\s+run:\s*dotnet build (CodoMetis\.TypeKit\.slnx|tools/ConsumerSmokeTest)\s+-c Release\s*$")]
    private static partial Regex SmokeTestBuild();

    [GeneratedRegex(@"(?m)^\s+run:\s*dotnet build (CodoMetis\.TypeKit\.slnx|tools/Prober)\s+-c Release\s*$")]
    private static partial Regex ProberBuild();

    [GeneratedRegex(@"(?m)^\s+run:.*\bapt-get install\b.*\bclang\b.*\bzlib1g-dev\b")]
    private static partial Regex AotPrerequisites();

    [GeneratedRegex(@"(?m)^\s+id-token:\s*write")]
    private static partial Regex IdTokenWrite();

    [GeneratedRegex(@"(?m)^\s+contents:\s*write")]
    private static partial Regex ContentsWrite();

    [GeneratedRegex(@"--exclude-filter\s+(?<ids>[^\s\\]+)")]
    private static partial Regex ExcludeFilter();
}
