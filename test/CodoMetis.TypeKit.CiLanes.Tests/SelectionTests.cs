namespace CodoMetis.TypeKit.CiLanes.Tests;

/// <summary>
/// The lanes a run gets, from the event and the paths it changes. Each case runs the built tool as its own process
/// against a throwaway git repository, the way the changes job runs it against the checkout, and reads the answers
/// it writes to the step's output file (design/adr/0113).
/// </summary>
public sealed class SelectionTests : IDisposable
{
    private const string LinuxOnly = """legs=["ubuntu-latest"]""";
    private const string LinuxAndWindows = """legs=["ubuntu-latest","windows-latest"]""";
    private const string EveryLeg = """legs=["ubuntu-latest","windows-latest","macos-latest"]""";

    private const string Conventions = "test/CodoMetis.TypeKit.Conventions.Tests/CodoMetis.TypeKit.Conventions.Tests.csproj";
    private const string Oracles = "test/CodoMetis.TypeKit.Oracles.Tests/CodoMetis.TypeKit.Oracles.Tests.csproj";
    private const string Samples = "test/CodoMetis.TypeKit.Samples/CodoMetis.TypeKit.Samples.csproj";
    private const string CoreTests = "test/CodoMetis.TypeKit.Tests/CodoMetis.TypeKit.Tests.csproj";
    private const string HygieneTests = "test/CodoMetis.TypeKit.PrHygiene.Tests/CodoMetis.TypeKit.PrHygiene.Tests.csproj";
    private const string GuardTests = "test/CodoMetis.TypeKit.BashGuard.Tests/CodoMetis.TypeKit.BashGuard.Tests.csproj";
    private const string WorkflowTests = "test/CodoMetis.TypeKit.Workflow.Tests/CodoMetis.TypeKit.Workflow.Tests.csproj";
    private const string Hygiene = "tools/PrHygiene/PrHygiene.csproj";

    private readonly ToolSandbox _tool = new("CiLanes", "ci-lanes");

    /// <summary>
    /// The base is a small solution shaped like the repository's: a package, two tools and their tests, one test
    /// project that compiles in a file of another, one that references a test project, and the two that read the
    /// repository by path and declare it, the conventions (the whole tree) and the oracles (test/).
    /// </summary>
    public SelectionTests()
    {
        Write("CodoMetis.TypeKit.slnx", $"""
            <Solution>
              <Project Path="src/CodoMetis.TypeKit/CodoMetis.TypeKit.csproj" />
              <Project Path="tools/CiLanes/CiLanes.csproj" />
              <Project Path="{Hygiene}" />
              <Project Path="tools/BashGuard/BashGuard.csproj" />
              <Project Path="{CoreTests}" />
              <Project Path="{HygieneTests}" />
              <Project Path="{GuardTests}" />
              <Project Path="{WorkflowTests}" />
              <Project Path="{Conventions}" />
              <Project Path="{Oracles}" />
              <Project Path="{Samples}" />
            </Solution>
            """);
        Write("test/Prose.slnf", $$"""{ "solution": { "path": "../CodoMetis.TypeKit.slnx", "projects": [ "{{Conventions}}", "{{Oracles}}", "{{Samples}}" ] } }""");
        Write("src/CodoMetis.TypeKit/CodoMetis.TypeKit.csproj", Project());
        Write(Hygiene, Project());
        Write("tools/BashGuard/BashGuard.csproj", Project());
        Write(CoreTests, Project("""<ProjectReference Include="..\..\src\CodoMetis.TypeKit\CodoMetis.TypeKit.csproj" />"""));
        Write(HygieneTests, Project("""<ProjectReference Include="../../tools/PrHygiene/PrHygiene.csproj" ReferenceOutputAssembly="false" />""",
                                    """<Compile Include="../CodoMetis.TypeKit.BashGuard.Tests/ToolSandbox.cs" Link="ToolSandbox.cs" />"""));
        Write(GuardTests, Project("""<ProjectReference Include="../../tools/BashGuard/BashGuard.csproj" ReferenceOutputAssembly="false" />"""));
        Write(WorkflowTests, Project("""<ProjectReference Include="../CodoMetis.TypeKit.PrHygiene.Tests/CodoMetis.TypeKit.PrHygiene.Tests.csproj" />"""));
        Write(Conventions, Project("""<ReadsByPath Include="../../" />"""));
        Write(Oracles, Project("""<ReadsByPath Include="../" />"""));
        Write(Samples, Project("""<ProjectReference Include="..\..\src\CodoMetis.TypeKit\CodoMetis.TypeKit.csproj" />"""));
        Write("tools/PrHygiene/Program.cs", "// a tool\n");
        Write("test/CodoMetis.TypeKit.BashGuard.Tests/ToolSandbox.cs", "// shared by the tools' tests\n");
        Write("test/CodoMetis.TypeKit.Tests/OptionTests.cs", "// a test\n");
        Write("src/CodoMetis.TypeKit/Option.cs", "// a package's source\n");
        Write("src/CodoMetis.TypeKit.Analyzers/CodeFixes/AddCreate.cs", "// a code fix's source\n");
        Write("docs/guide.md", "# A guide\n");
        Git("init", "-q", "-b", "release/9.9.0");
        Git("add", "-A");
        Git("commit", "-q", "-m", "base");
        Git("update-ref", "refs/remotes/origin/release/9.9.0", "HEAD");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
    }

    public void Dispose() => _tool.Dispose();

    public static TheoryData<string, string, string> Paths => new()
    {
        { "docs/guide.md", "prose", "linux" },
        { "design/adr/0113-ci-runs-the-lanes-a-change-can-affect.md", "prose", "linux" },
        { "README.md", "prose", "linux" },
        { "test/oracles/README.md", "prose", "linux" },
        { ".github/ISSUE_TEMPLATE/bug.yml", "prose", "linux" },
        { "site/docfx.json", "prose", "linux" },
        { "site/toc.yml", "prose", "linux" },
        { "site/template/conceptual.extension.js", "prose", "linux" },
        { "CHANGELOG.md", "changelog", "linux, pack" },
        { "src/CodoMetis.TypeKit.Analyzers/CodeFixes/AddCreateCodeFix.cs", "source text", "linux, windows, pack" },
        { "src/CodoMetis.TypeKit.Analyzers/AnalyzerReleases.Unshipped.md", "source text", "linux, windows, pack" },
        { "src/CodoMetis.TypeKit.Generators/README.md", "source text", "linux, windows, pack" },
        { "src/CodoMetis.TypeKit/README.md", "package", "linux, pack" },
        { "src/CodoMetis.TypeKit.EntityFrameworkCore/ValueObjectConverter.cs", "package", "linux, pack" },
        { "src/CodoMetis.TypeKit.AnalyzersAndMore/X.cs", "package", "linux, pack" },
        { "src/Directory.Build.targets", "package", "linux, pack" },
        { "tools/ConsumerSmokeTest/Consumers/Aot/Program.cs", "smoke test", "linux, windows, pack" },
        { "tools/ConsumerSmokeTest/README.md", "smoke test", "linux, windows, pack" },
        { "tools/Shared/SharedFile.cs", "smoke test", "linux, windows, pack" },
        { "tools/Directory.Build.props", "smoke test", "linux, windows, pack" },
        { "tools/CiLanes/Program.cs", "smoke test", "linux, windows, pack" },
        { "tools/PrHygiene/Program.cs", "code no package reads", "linux, windows" },
        { "test/CodoMetis.TypeKit.Tests/OptionTests.cs", "code no package reads", "linux, windows" },
        { "test/Directory.Build.props", "code no package reads", "linux, windows" },
        { "spikes/FabricSpike/Program.cs", "code no package reads", "linux, windows" },
        { ".gitignore", "ignore list", "linux" },
        { ".gitattributes", "unlisted", "linux, windows, pack" },
        { "global.json", "unlisted", "linux, windows, pack" },
        { "Directory.Build.props", "unlisted", "linux, windows, pack" },
        { "Directory.Packages.props", "unlisted", "linux, windows, pack" },
        { ".config/dotnet-tools.json", "unlisted", "linux, windows, pack" },
        { ".github/workflows/dotnet.yml", "unlisted", "linux, windows, pack" },
        { ".github/workflows/docs.yml", "unlisted", "linux, windows, pack" },
        { "benchmarks/CodoMetis.TypeKit.Benchmarks/Program.cs", "unlisted", "linux, windows, pack" },
        { "a-file-nobody-listed", "unlisted", "linux, windows, pack" },
    };

    [Theory, MemberData(nameof(Paths))]
    public void A_path_selects_the_lanes_of_its_class(string path, string @class, string lanes)
    {
        var (code, output, _) = _tool.Run(arguments: ["classify", path]);

        code.ShouldBe(0, output);
        output.ShouldBe($"{path}\t{@class}\t{lanes}\n");
    }

    [Fact]
    public void A_prose_change_runs_the_Linux_leg_alone()
    {
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxOnly, "pack=false", "suite=prose", "projects=[]"]);
    }

    /// <summary>The prose suite runs only for a change that is prose throughout; beside a test change it joins the projects reached.</summary>
    [Fact]
    public void Prose_beside_a_test_change_adds_the_prose_suite_to_what_the_test_reaches()
    {
        Commit("docs/guide.md", "# A guide, reworded\n");
        Commit("test/CodoMetis.TypeKit.Tests/OptionTests.cs", "// a test, changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=false", "suite=reach", Projects(Conventions, Oracles, Samples, CoreTests)]);
    }

    [Fact]
    public void A_changelog_change_runs_the_whole_suite()
    {
        Commit("CHANGELOG.md", "## 9.9.0\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxOnly, "pack=true", "suite=full", "projects=[]"]);
    }

    [Fact]
    public void A_package_change_runs_Linux_and_the_pack()
    {
        Commit("docs/guide.md", "# A guide, reworded\n");
        Commit("src/CodoMetis.TypeKit/Option.cs", "// changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxOnly, "pack=true", "suite=full", "projects=[]"]);
    }

    /// <summary>The one src/ defect a Windows leg alone caught in 150 runs was a code fix's line endings (f9f64e5).</summary>
    [Fact]
    public void An_analyzer_change_adds_the_Windows_leg()
    {
        Commit("src/CodoMetis.TypeKit/Option.cs", "// changed\n");
        Commit("src/CodoMetis.TypeKit.Analyzers/CodeFixes/AddCreate.cs", "// changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=true", "suite=full", "projects=[]"]);
    }

    /// <summary>
    /// A test project's change runs that project, the conventions and the oracles, which read every test project's
    /// sources (decision 127), on Linux and Windows, without the pack.
    /// </summary>
    [Fact]
    public void A_test_project_change_runs_that_project_and_the_repository_readers()
    {
        Commit("test/CodoMetis.TypeKit.Tests/OptionTests.cs", "// a test, changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=false", "suite=reach", Projects(Conventions, Oracles, CoreTests)]);
    }

    /// <summary>
    /// A tool's change runs the tool's build, its tests, a test project that reaches it through another
    /// (ProjectReference, transitively), and the conventions; not the oracles, which read test/ alone (#268).
    /// </summary>
    [Fact]
    public void A_tool_change_runs_the_test_projects_that_reach_it()
    {
        Commit("tools/PrHygiene/Program.cs", "// a tool, changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=false", "suite=reach", Projects(Conventions, HygieneTests, WorkflowTests, Hygiene)]);
    }

    /// <summary>A file another test project compiles in reaches that project, and what references it.</summary>
    [Fact]
    public void A_linked_file_reaches_the_project_that_compiles_it_in()
    {
        Commit("test/CodoMetis.TypeKit.BashGuard.Tests/ToolSandbox.cs", "// changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=false", "suite=reach", Projects(GuardTests, Conventions, Oracles, HygieneTests, WorkflowTests)]);
    }

    /// <summary>The shape of #266: one tool, its tests, the docs and the ignore list.</summary>
    [Fact]
    public void A_tool_its_tests_and_docs_run_a_narrowed_suite_without_the_pack()
    {
        Commit("tools/PrHygiene/Program.cs", "// a tool, changed\n");
        Commit("test/CodoMetis.TypeKit.PrHygiene.Tests/HygieneTests.cs", "// its tests\n");
        Commit("docs/guide.md", "# A guide, reworded\n");
        Commit(".gitignore", "bin/\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=false", "suite=reach", Projects(Conventions, Oracles, Samples, HygieneTests, WorkflowTests, Hygiene)]);
    }

    /// <summary>A fresh checkout ignores nothing, so the ignore list is read by git alone and the conventions that list the index.</summary>
    [Fact]
    public void An_ignore_list_change_runs_the_conventions_on_Linux()
    {
        Commit(".gitignore", "bin/\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxOnly, "pack=false", "suite=reach", Projects(Conventions)]);
    }

    /// <summary>
    /// tools/Shared is compiled into every tool, the smoke test among them: the whole solution, and the pack. (A file
    /// the fingerprint does not hash, since an edited source would make the sandbox's tool refuse to run stale.)
    /// </summary>
    [Fact]
    public void A_shared_tooling_change_runs_the_whole_suite()
    {
        Commit("tools/PrHygiene/Program.cs", "// a tool, changed\n");
        Commit("tools/Shared/notes.txt", "changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=true", "suite=full", "projects=[]"]);
    }

    [Fact]
    public void A_tool_change_beside_a_package_change_runs_the_whole_suite()
    {
        Commit("tools/PrHygiene/Program.cs", "// a tool, changed\n");
        Commit("src/CodoMetis.TypeKit/Option.cs", "// changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=true", "suite=full", "projects=[]"]);
    }

    /// <summary>A path under test/ or tools/ that no solution project holds reaches projects nobody can name: the whole suite.</summary>
    [Theory]
    [InlineData("test/Directory.Build.props")]
    [InlineData("test/oracles/claims.json")]
    [InlineData("test/CodoMetis.TypeKit.Unlisted.Tests/Tests.cs")]
    [InlineData("tools/AgentTools.slnf")]
    public void A_path_outside_every_solution_project_runs_the_whole_suite(string path)
    {
        Commit("tools/PrHygiene/Program.cs", "// a tool, changed\n");
        Commit(path, "// changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=false", "suite=full", "projects=[]"]);
    }

    /// <summary>A project file that cannot be read hides what that project reaches, so nothing is narrowed.</summary>
    [Fact]
    public void A_project_file_that_cannot_be_read_runs_the_whole_suite()
    {
        Commit(WorkflowTests, "<Project");
        Commit("tools/PrHygiene/Program.cs", "// a tool, changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=false", "suite=full", "projects=[]"]);
    }

    [Fact]
    public void Suite_answers_each_path_alone()
    {
        var (code, output, _) = _tool.Run(arguments: ["suite", "tools/PrHygiene/Program.cs", "docs/guide.md", "src/CodoMetis.TypeKit/Option.cs"]);

        code.ShouldBe(0, output);
        output.ShouldBe(
            $"tools/PrHygiene/Program.cs\treach\t{Conventions}, {HygieneTests}, {WorkflowTests}, {Hygiene}\n" +
            "docs/guide.md\tprose\t\n" +
            "src/CodoMetis.TypeKit/Option.cs\tfull\t\n");
    }

    /// <summary>A selection never runs macOS: a path no class names gets every lane but that one.</summary>
    [Fact]
    public void An_unlisted_change_runs_every_lane_but_macOS()
    {
        Commit("global.json", "{}\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxAndWindows, "pack=true", "suite=full", "projects=[]"]);
    }

    /// <summary>Without <c>--no-renames</c>, git lists a rename's new path alone, and a move out of src/ reads as prose (#70).</summary>
    [Fact]
    public void A_move_out_of_a_package_counts_its_old_path()
    {
        Git("mv", "src/CodoMetis.TypeKit/Option.cs", "docs/Option.cs.md");
        Git("commit", "-q", "-m", "move");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxOnly, "pack=true", "suite=full", "projects=[]"]);
    }

    [Fact]
    public void The_release_pull_request_into_main_runs_every_lane()
    {
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers(PullRequestInto("main")).ShouldBe([EveryLeg, "pack=true", "suite=full", "projects=[]"]);
    }

    [Fact]
    public void A_push_to_a_release_branch_is_selected_by_what_it_replaced()
    {
        var before = Git("rev-parse", "HEAD").Trim();
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers("--event", "push", "--ref", "refs/heads/release/9.9.0", "--before", before).ShouldBe([LinuxOnly, "pack=false", "suite=prose", "projects=[]"]);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("1234567890abcdef1234567890abcdef12345678")]
    [InlineData("")]
    public void A_push_without_a_commit_to_diff_against_runs_every_lane(string before)
    {
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers("--event", "push", "--ref", "refs/heads/release/9.9.0", "--before", before).ShouldBe([EveryLeg, "pack=true", "suite=full", "projects=[]"]);
    }

    [Theory]
    [InlineData("push", "refs/heads/main")]
    [InlineData("push", "refs/tags/v9.9.0")]
    [InlineData("workflow_dispatch", "refs/heads/release/9.9.0")]
    [InlineData("schedule", "refs/heads/main")]
    public void Every_other_run_gets_every_lane(string @event, string @ref)
    {
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers("--event", @event, "--ref", @ref, "--before", Git("rev-parse", "HEAD~1").Trim()).ShouldBe([EveryLeg, "pack=true", "suite=full", "projects=[]"]);
    }

    [Fact]
    public void A_diff_that_names_no_path_runs_every_lane() =>
        Answers(PullRequestInto("release/9.9.0")).ShouldBe([EveryLeg, "pack=true", "suite=full", "projects=[]"]);

    /// <summary>A diff that cannot be taken fails the job, never answers "nothing changed" (#70).</summary>
    [Fact]
    public void A_diff_that_fails_fails_the_selection()
    {
        var output = Path.Combine(_tool.Work, "output");

        var (code, text, _) = _tool.Run(arguments: ["select", .. PullRequestInto("release/9.9.1"), "--output", output]);

        code.ShouldBe(1, text);
        text.ShouldContain("::error::git diff origin/release/9.9.1...HEAD failed");
        File.Exists(output).ShouldBeFalse("a failed selection writes no answer");
    }

    [Fact]
    public void The_summary_names_each_path_and_its_class()
    {
        Commit("CHANGELOG.md", "## 9.9.0\n");
        var summary = Path.Combine(_tool.Work, "summary.md");

        _tool.Run(arguments: ["select", .. PullRequestInto("release/9.9.0"), "--summary", summary]).Code.ShouldBe(0);

        File.ReadAllText(summary).ShouldContain("| `CHANGELOG.md` | changelog | linux, pack |");
    }

    [Theory]
    [InlineData]
    [InlineData("select")]
    [InlineData("select", "--event", "push")]
    [InlineData("select", "--event", "push", "--ref", "refs/heads/main", "--colour", "red")]
    [InlineData("classify")]
    [InlineData("suite")]
    [InlineData("gate")]
    [InlineData("lanes")]
    public void A_usage_error_is_exit_2(params string[] arguments) =>
        _tool.Run(arguments: arguments).Code.ShouldBe(2);

    private static string Project(params string[] items) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n{string.Concat(items.Select(item => $"    {item}\n"))}  </ItemGroup>\n</Project>\n";

    // The projects answer: a JSON array, in ordinal order.
    private static string Projects(params string[] projects) =>
        $"projects={System.Text.Json.JsonSerializer.Serialize(projects.Order(StringComparer.Ordinal))}";

    private static string[] PullRequestInto(string target) => ["--event", "pull_request", "--ref", "refs/pull/1/merge", "--base", target];

    private string[] Answers(params string[] options)
    {
        var output = Path.Combine(_tool.Work, $"{Guid.NewGuid():N}.output");
        var (code, text, _) = _tool.Run(arguments: ["select", .. options, "--output", output]);

        code.ShouldBe(0, text);
        return File.ReadAllLines(output);
    }

    private void Commit(string path, string content)
    {
        Write(path, content);
        Git("add", "-A");
        Git("commit", "-q", "-m", $"change {path}");
    }

    private void Write(string path, string content)
    {
        var file = Path.Combine(_tool.Repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    // git in the sandbox's repository, with no configuration of the machine's: its user, signing and hooks stay out.
    private string Git(params string[] arguments)
    {
        var empty = Path.Combine(_tool.Work, "gitconfig");
        File.WriteAllText(empty, "");
        var (code, output, error) = ToolSandbox.RunProcess("git", ["-c", "user.name=CI Lanes", "-c", "user.email=ci@example.invalid", "-c", "commit.gpgsign=false", .. arguments],
            _tool.Repo, new Dictionary<string, string?> { ["GIT_CONFIG_GLOBAL"] = empty, ["GIT_CONFIG_NOSYSTEM"] = "1" });

        code.ShouldBe(0, $"git {string.Join(' ', arguments)}: {error}");
        return output;
    }
}
