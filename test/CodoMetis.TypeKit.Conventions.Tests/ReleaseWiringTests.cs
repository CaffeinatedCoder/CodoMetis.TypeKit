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

        jobs.Keys.ShouldBe(["ci", "verify", "publish", "release"], ignoreOrder: true, "The release workflow no longer has the ci/verify/publish/release split these tests assume.");

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
    /// The changes job asks tools/CiLanes which lanes the run needs (design/adr/0113), and hands its two answers to the
    /// jobs that read them: the test matrix and the pack lane. Before decision 113 the job decided in a shell `case`
    /// whose list missed inputs and failed open (#70). The checkout is the whole history, since the diff is against
    /// the base branch or the commit a push replaced.
    /// </summary>
    [Fact]
    public void The_changes_job_selects_the_lanes_with_ci_lanes()
    {
        var changes = Job(BuildWorkflow, "changes");

        foreach (var output in new[] { "legs", "pack", "suite", "projects" })
            changes.ShouldMatch($@"(?m)^      {output}:\s*\$\{{\{{ steps\.select\.outputs\.{output} \}}\}}\s*$",
                $"dotnet.yml:changes has no `{output}` output taken from the select step's, so the jobs it selects have nothing to read.");

        var select = Steps(changes).SingleOrDefault(step => Regex.IsMatch(step, @"(?m)^\s+id:\s*select\s*$"));
        select.ShouldNotBeNull("dotnet.yml:changes has no step with id select.");
        select.ShouldContain("dotnet tools/CiLanes/bin/Release/net10.0/ci-lanes.dll select", Case.Sensitive, "dotnet.yml:changes does not run the selection's built dll.");
        foreach (var argument in new[] { @"--event ""$GITHUB_EVENT_NAME""", @"--ref ""$GITHUB_REF""", @"--base ""$BASE_REF""", @"--before ""$BEFORE""", @"--output ""$GITHUB_OUTPUT""" })
            select.ShouldContain(argument, Case.Sensitive, $"dotnet.yml:changes does not pass {argument} to the selection.");
        select.ShouldContain("BASE_REF: ${{ github.base_ref }}", Case.Sensitive);
        select.ShouldContain("BEFORE: ${{ github.event.before }}", Case.Sensitive);

        changes.ShouldMatch(@"(?m)^\s+run:\s*dotnet build tools/CiLanes -c Release\s*$", "dotnet.yml:changes runs the selection without building it in Release first.");
        changes.ShouldMatch(@"(?m)^\s+fetch-depth:\s*0\s*$", "dotnet.yml:changes checks out a shallow clone, which has neither the base branch nor the commit a push replaced.");
    }

    [Fact]
    public void The_test_matrix_is_the_selection()
    {
        var test = Job(BuildWorkflow, "test");

        test.ShouldMatch(JobKey("needs", @"(?:changes|\[\s*changes\s*\])"), "dotnet.yml:test does not need changes, so it cannot read the selected legs.");
        test.ShouldMatch(@"(?m)^\s+os:\s*\$\{\{ fromJSON\(needs\.changes\.outputs\.legs\) \}\}\s*$",
            "dotnet.yml:test's matrix is not the changes job's legs, so the selection no longer decides which legs run.");

        // A prose-only change runs test/Prose.slnf, a reached suite test/Reach.slnf (decision 127); any other answer, a
        // missing one included, the whole solution.
        test.ShouldMatch(@"(?m)^      SOLUTION:\s*\$\{\{ needs\.changes\.outputs\.suite == 'prose' && 'test/Prose\.slnf' \|\| needs\.changes\.outputs\.suite == 'reach' && 'test/Reach\.slnf' \|\| 'CodoMetis\.TypeKit\.slnx' \}\}\s*$",
            "dotnet.yml:test does not choose its solution from the changes job's suite, falling back to the whole solution.");

        // The reached suite is written from the changes job's projects before anything restores it.
        var write = Steps(test).SingleOrDefault(step => step.Contains("> test/Reach.slnf", StringComparison.Ordinal));
        write.ShouldNotBeNull("dotnet.yml:test does not write test/Reach.slnf, so a reached suite builds a filter nobody wrote.");
        write.ShouldMatch(@"(?m)^\s+if:\s*needs\.changes\.outputs\.suite == 'reach'\s*$", "dotnet.yml:test writes test/Reach.slnf for a run that is not a reached suite.");
        write.ShouldContain("PROJECTS: ${{ needs.changes.outputs.projects }}", Case.Sensitive, "dotnet.yml:test does not write test/Reach.slnf from the changes job's projects.");
        write.ShouldContain("""printf '{"solution":{"path":"../CodoMetis.TypeKit.slnx","projects":%s}}\n' "$PROJECTS" > test/Reach.slnf""", Case.Sensitive,
            "dotnet.yml:test does not write test/Reach.slnf as a filter of the solution holding the changes job's projects.");
        test.IndexOf("> test/Reach.slnf", StringComparison.Ordinal).ShouldBeLessThan(test.IndexOf(@"dotnet restore ""$SOLUTION""", StringComparison.Ordinal),
            "dotnet.yml:test restores before it writes test/Reach.slnf.");
        foreach (var command in new[] { @"dotnet restore ""$SOLUTION""", @"dotnet build ""$SOLUTION"" -c Release", @"dotnet test --solution ""$SOLUTION"" -c Release" })
            test.ShouldContain(command, Case.Sensitive, $"dotnet.yml:test does not run `{command}`, so the suite the changes job chose is not what it builds or tests.");
    }

    /// <summary>Pack and Consumer start beside the test legs (#117) and run when the pack lane is selected.</summary>
    [Fact]
    public void Pack_and_Consumer_run_when_the_pack_lane_is_selected()
    {
        foreach (var name in new[] { "pack", "consumer" })
        {
            var job = Job(BuildWorkflow, name);

            job.ShouldMatch(JobKey("needs", @"\[\s*changes\s*\]"),
                $"dotnet.yml:{name} does not need [ changes ] alone: it reads the selection, and waits for nothing else.");
            job.ShouldMatch(JobKey("if", Regex.Escape("needs.changes.outputs.pack == 'true'")),
                $"dotnet.yml:{name} is not conditioned on `needs.changes.outputs.pack == 'true'`, so it runs on every pull request again or never.");
        }
    }

    /// <summary>
    /// The CI job is the required build check: it judges every other job, so it needs each of them, and it runs under
    /// <c>always()</c>, since a skipped job reads as a passing required check (KB-043). A job added without joining its
    /// needs would go unjudged; the gate itself fails a job it does not know.
    /// </summary>
    [Fact]
    public void The_CI_job_judges_every_other_job()
    {
        var jobs = Jobs(BuildWorkflow);
        var ci = Job(BuildWorkflow, "ci");

        var needs = Regex.Match(ci, @"(?m)^    needs:\s*\[(?<jobs>[^\]]*)\]\s*$");
        needs.Success.ShouldBeTrue("dotnet.yml:ci has no `needs: [ ... ]` list.");
        needs.Groups["jobs"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
             .ShouldBe(jobs.Keys.Where(name => name != "ci"), ignoreOrder: true, "dotnet.yml:ci does not need exactly every other job, so it cannot judge them all.");

        ci.ShouldMatch(JobKey("if", Regex.Escape("always()")), "dotnet.yml:ci does not run under always(), so a failed or cancelled job skips it, and a skipped CI reads as passing.");
        ci.ShouldMatch(@"(?m)^\s+NEEDS:\s*\$\{\{ toJSON\(needs\) \}\}\s*$", "dotnet.yml:ci does not hand the gate toJSON(needs).");
        ci.ShouldContain("dotnet tools/CiLanes/bin/Release/net10.0/ci-lanes.dll gate", Case.Sensitive, "dotnet.yml:ci does not run the gate.");
        ci.ShouldMatch(@"(?m)^\s+name:\s*CI\s*$", "dotnet.yml:ci is not named CI, the required check's name.");
    }

    /// <summary>
    /// The release runs every lane of the build workflow before it verifies (#74): the Windows and macOS legs and the
    /// smoke test's own-feed mode, which a pull request may have skipped.
    /// </summary>
    [Fact]
    public void The_release_runs_the_build_workflow_before_it_verifies()
    {
        BuildWorkflow.ShouldMatch(@"(?m)^  workflow_call:\s*$", "dotnet.yml cannot be called: it has no workflow_call trigger.");
        Job(ReleaseWorkflow, "ci").ShouldMatch(JobKey("uses", Regex.Escape("./.github/workflows/dotnet.yml")), "release.yml:ci does not call dotnet.yml.");
        Job(ReleaseWorkflow, "verify").ShouldMatch(JobKey("needs", @"(?:ci|\[\s*ci\s*\])"), "release.yml:verify does not need ci, so it packs without the full matrix.");
    }

    /// <summary>
    /// A called workflow's <c>github.workflow</c> is its caller's name, and a concurrency group ignores case (KB-043).
    /// The build workflow's group under the release's call must differ from the release's own, or the call waits for its
    /// caller and the release never finishes.
    /// </summary>
    [Fact]
    public void A_call_from_the_release_never_shares_its_callers_concurrency_group()
    {
        var caller = Regex.Match(ReleaseWorkflow, @"(?m)^name:\s*(?<name>.+?)\s*$").Groups["name"].Value;
        string Group(string workflow) =>
            Regex.Match(workflow, @"(?m)^concurrency:\s*\n\s+group:\s*(?<group>.+?)\s*$").Groups["group"].Value.Replace("${{ github.workflow }}", caller, StringComparison.Ordinal);

        var release = Group(ReleaseWorkflow);
        var called = Group(BuildWorkflow);

        release.ShouldNotBeEmpty("release.yml has no workflow-level concurrency group.");
        called.ShouldNotBeEmpty("dotnet.yml has no workflow-level concurrency group.");
        string.Equals(release, called, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
            $"Under release.yml's call, dotnet.yml's concurrency group is {called}, the release's own ({release}) as GitHub compares groups: the call would wait for its caller.");
    }

    [Fact]
    public void The_nightly_run_dispatches_the_build_workflow()
    {
        var nightly = Workflow("nightly.yml");

        nightly.ShouldMatch(@"(?m)^\s+- cron:", "nightly.yml has no schedule.");
        nightly.ShouldContain("gh workflow run dotnet.yml --ref", Case.Sensitive, "nightly.yml does not dispatch dotnet.yml.");
        nightly.ShouldMatch(@"(?m)^\s+actions:\s*write\b", "nightly.yml cannot dispatch without actions: write.");
    }

    /// <summary>
    /// The release packs and smoke-tests always: the artifacts it publishes are the artifacts it tested, whatever
    /// the tag's diff. Neither its verify job nor the pack or smoke test step carries a condition.
    /// </summary>
    [Fact]
    public void The_release_packs_and_smoke_tests_unconditionally()
    {
        var job = Job(ReleaseWorkflow, "verify");
        var steps = Steps(job).ToList();

        job.ShouldNotMatch(JobKey("if", ".*"), "release.yml:verify carries a job-level condition; the release packs and smoke-tests every tag.");

        foreach (var (what, step) in new[] { ("pack", steps.Single(candidate => candidate.Contains("dotnet pack", StringComparison.Ordinal))), ("smoke test", steps.Single(candidate => SmokeTestStep().IsMatch(candidate))) })
            step.ShouldNotMatch(@"(?m)^\s+if:", $"release.yml:verify's {what} step carries a condition; the release packs and smoke-tests every tag.");
    }

    /// <summary>A key of the job itself, at the job's indentation (four spaces), never a step's key of the same name.</summary>
    private static string JobKey(string key, string value) => $@"(?m)^    {key}:\s*{value}\s*$";


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

    [GeneratedRegex(@"(?m)^\s+run:.*\bapt-get install\b.*\bclang\b.*\bzlib1g-dev\b")]
    private static partial Regex AotPrerequisites();

    [GeneratedRegex(@"(?m)^\s+id-token:\s*write")]
    private static partial Regex IdTokenWrite();

    [GeneratedRegex(@"(?m)^\s+contents:\s*write")]
    private static partial Regex ContentsWrite();

    [GeneratedRegex(@"--exclude-filter\s+(?<ids>[^\s\\]+)")]
    private static partial Regex ExcludeFilter();
}
