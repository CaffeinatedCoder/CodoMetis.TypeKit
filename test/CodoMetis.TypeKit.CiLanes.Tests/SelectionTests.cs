namespace CodoMetis.TypeKit.CiLanes.Tests;

/// <summary>
/// The lanes a run gets, from the event and the paths it changes. Each case runs the built tool as its own process
/// against a throwaway git repository, the way the changes job runs it against the checkout, and reads the answers
/// it writes to the step's output file (design/adr/0113).
/// </summary>
public sealed class SelectionTests : IDisposable
{
    private const string LinuxOnly = """legs=["ubuntu-latest"]""";
    private const string EveryLeg = """legs=["ubuntu-latest","windows-latest","macos-latest"]""";

    private readonly ToolSandbox _tool = new("CiLanes", "ci-lanes");

    public SelectionTests()
    {
        Write("src/CodoMetis.TypeKit/Option.cs", "// a package's source\n");
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
        { "CHANGELOG.md", "changelog", "linux, pack" },
        { "src/CodoMetis.TypeKit/README.md", "package", "linux, os, pack" },
        { "src/Directory.Build.targets", "package", "linux, os, pack" },
        { "tools/ConsumerSmokeTest/Consumers/Aot/Program.cs", "smoke test", "linux, os, pack" },
        { "tools/ConsumerSmokeTest/README.md", "smoke test", "linux, os, pack" },
        { "tools/Shared/SharedFile.cs", "smoke test", "linux, os, pack" },
        { "tools/Directory.Build.props", "smoke test", "linux, os, pack" },
        { "tools/CiLanes/Program.cs", "smoke test", "linux, os, pack" },
        { "tools/PrHygiene/Program.cs", "code no package reads", "linux, os" },
        { "test/CodoMetis.TypeKit.Tests/OptionTests.cs", "code no package reads", "linux, os" },
        { "test/Directory.Build.props", "code no package reads", "linux, os" },
        { "spikes/FabricSpike/Program.cs", "code no package reads", "linux, os" },
        { "global.json", "unlisted", "linux, os, pack" },
        { "Directory.Build.props", "unlisted", "linux, os, pack" },
        { "Directory.Packages.props", "unlisted", "linux, os, pack" },
        { ".config/dotnet-tools.json", "unlisted", "linux, os, pack" },
        { ".github/workflows/dotnet.yml", "unlisted", "linux, os, pack" },
        { "benchmarks/CodoMetis.TypeKit.Benchmarks/Program.cs", "unlisted", "linux, os, pack" },
        { "a-file-nobody-listed", "unlisted", "linux, os, pack" },
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

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([LinuxOnly, "pack=false"]);
    }

    [Fact]
    public void A_package_change_runs_every_lane()
    {
        Commit("docs/guide.md", "# A guide, reworded\n");
        Commit("src/CodoMetis.TypeKit/Option.cs", "// changed\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([EveryLeg, "pack=true"]);
    }

    [Fact]
    public void A_test_change_skips_the_pack_only()
    {
        Commit("test/CodoMetis.TypeKit.Tests/OptionTests.cs", "// a test\n");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([EveryLeg, "pack=false"]);
    }

    /// <summary>Without <c>--no-renames</c>, git lists a rename's new path alone, and a move out of src/ reads as prose (#70).</summary>
    [Fact]
    public void A_move_out_of_a_package_counts_its_old_path()
    {
        Git("mv", "src/CodoMetis.TypeKit/Option.cs", "docs/Option.cs.md");
        Git("commit", "-q", "-m", "move");

        Answers(PullRequestInto("release/9.9.0")).ShouldBe([EveryLeg, "pack=true"]);
    }

    [Fact]
    public void The_release_pull_request_into_main_runs_every_lane()
    {
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers(PullRequestInto("main")).ShouldBe([EveryLeg, "pack=true"]);
    }

    [Fact]
    public void A_push_to_a_release_branch_is_selected_by_what_it_replaced()
    {
        var before = Git("rev-parse", "HEAD").Trim();
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers("--event", "push", "--ref", "refs/heads/release/9.9.0", "--before", before).ShouldBe([LinuxOnly, "pack=false"]);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("1234567890abcdef1234567890abcdef12345678")]
    [InlineData("")]
    public void A_push_without_a_commit_to_diff_against_runs_every_lane(string before)
    {
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers("--event", "push", "--ref", "refs/heads/release/9.9.0", "--before", before).ShouldBe([EveryLeg, "pack=true"]);
    }

    [Theory]
    [InlineData("push", "refs/heads/main")]
    [InlineData("push", "refs/tags/v9.9.0")]
    [InlineData("workflow_dispatch", "refs/heads/release/9.9.0")]
    [InlineData("schedule", "refs/heads/main")]
    public void Every_other_run_gets_every_lane(string @event, string @ref)
    {
        Commit("docs/guide.md", "# A guide, reworded\n");

        Answers("--event", @event, "--ref", @ref, "--before", Git("rev-parse", "HEAD~1").Trim()).ShouldBe([EveryLeg, "pack=true"]);
    }

    [Fact]
    public void A_diff_that_names_no_path_runs_every_lane() =>
        Answers(PullRequestInto("release/9.9.0")).ShouldBe([EveryLeg, "pack=true"]);

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
    [InlineData("gate")]
    [InlineData("lanes")]
    public void A_usage_error_is_exit_2(params string[] arguments) =>
        _tool.Run(arguments: arguments).Code.ShouldBe(2);

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
