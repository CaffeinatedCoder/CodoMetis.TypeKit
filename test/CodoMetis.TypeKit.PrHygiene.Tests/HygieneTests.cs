using System.Text.Json;

namespace CodoMetis.TypeKit.PrHygiene.Tests;

/// <summary>
/// The pull requests the check must pass and the ones it must fail, one rule at a time. Each case writes the
/// event payload GitHub would and runs the built tool on it as its own process, the way the workflow does.
/// Each failing case starts from <see cref="InOrder"/>, which passes, and breaks one thing, so a case that
/// fails proves the rule it names; each rule was also proved by deleting it from the tool and watching its
/// cases fail.
/// </summary>
public sealed class HygieneTests(HygieneTests.Sandbox sandbox) : IClassFixture<HygieneTests.Sandbox>
{
    public sealed class Sandbox : IDisposable
    {
        public ToolSandbox Tool { get; } = new("PrHygiene", "pr-hygiene");

        public void Dispose() => Tool.Dispose();
    }

    private sealed record Pr(
        string[] Labels,
        string? Body = "Closes #12",
        string Head = "fix/serilog-max-length",
        string Base = "release/2.3.0",
        string? Milestone = "2.3.0",
        string Author = "CaffeinatedCoder");

    private static readonly Pr InOrder = new(["bug", "severity/high", "package/serilog"]);

    private (int Code, string Output) Check(Pr pr)
    {
        var payload = new
        {
            action = "opened",
            pull_request = new
            {
                user = new { login = pr.Author },
                labels = pr.Labels.Select(name => new { name }).ToArray(),
                body = pr.Body,
                head = new { @ref = pr.Head },
                @base = new { @ref = pr.Base },
                milestone = pr.Milestone is null ? null : new { title = pr.Milestone },
            },
        };
        var file = Path.Combine(sandbox.Tool.Work, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(payload));
        var (code, output, _) = sandbox.Tool.Run(arguments: file);
        return (code, output);
    }

    private void Passes(Pr pr)
    {
        var (code, output) = Check(pr);
        code.ShouldBe(0, output);
        output.ShouldNotContain("::error::");
    }

    private void Fails(Pr pr, string problem)
    {
        var (code, output) = Check(pr);
        code.ShouldBe(1, output);
        output.ShouldContain($"::error::{problem}", customMessage: output);
    }

    [Fact]
    public void A_pull_request_in_order_passes() => Passes(InOrder);

    [Theory]
    [InlineData("feat/cause-on-fault", "enhancement")]
    [InlineData("docs/plain-voice", "documentation")]
    [InlineData("test/variant-catalogue", "tests")]
    [InlineData("build/pr-hygiene", "chore")]
    [InlineData("refactor/split-plan", "chore")]
    [InlineData("spike/mutation-base", "chore")]
    public void Each_branch_type_passes_with_its_label(string head, string type) => Passes(InOrder with { Head = head, Labels = [type] });

    [Fact]
    public void A_release_into_main_passes_with_its_milestone() =>
        Passes(InOrder with { Head = "release/2.3.0", Base = "main", Labels = ["chore", "area/release"], Body = "Closes #70" });

    [Fact]
    public void Dependabot_is_not_checked() =>
        Passes(new Pr([], Body: null, Head: "dependabot/github_actions/actions-abc", Base: "main", Milestone: null, Author: "dependabot[bot]"));

    [Fact]
    public void No_type_label_fails() => Fails(InOrder with { Labels = ["package/serilog"] }, "Needs exactly one type label");

    [Fact]
    public void Two_type_labels_fail() => Fails(InOrder with { Labels = ["bug", "chore", "severity/low"] }, "Needs exactly one type label (bug, enhancement, documentation, tests, chore); it has 2: bug, chore.");

    [Fact]
    public void A_bug_without_a_severity_fails() => Fails(InOrder with { Labels = ["bug"] }, "A bug needs exactly one severity/* label");

    [Fact]
    public void A_bug_with_two_severities_fails() => Fails(InOrder with { Labels = ["bug", "severity/high", "severity/low"] }, "A bug needs exactly one severity/* label (critical, high, medium, low); it has 2.");

    [Fact]
    public void Silent_on_no_bug_fails() => Fails(InOrder with { Head = "feat/x", Labels = ["enhancement", "silent"] }, "Only a bug is silent");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Refs #12")]
    [InlineData("Closes the gap in #12")]
    [InlineData("Closes #")]
    [InlineData("Discloses #12")]
    public void A_description_without_a_closing_reference_fails(string? body) =>
        Fails(InOrder with { Body = body }, "The description needs a closing reference to its issue");

    [Theory]
    [InlineData("closes #12")]
    [InlineData("Fixes: #3")]
    [InlineData("Problem first.\n\nResolved CaffeinatedCoder/CodoMetis.TypeKit#4")]
    [InlineData("FIXED #5, close #6")]
    public void Each_closing_keyword_passes(string body) => Passes(InOrder with { Body = body });

    [Theory]
    [InlineData("claude/quirky-panini")]
    [InlineData("pr-hygiene")]
    [InlineData("fix/")]
    [InlineData("/x")]
    public void A_branch_not_named_type_slash_slug_fails(string head) => Fails(InOrder with { Head = head }, $"The branch '{head}' is not named <type>/<slug>");

    [Fact]
    public void A_branch_whose_type_disagrees_with_the_label_fails() =>
        Fails(InOrder with { Head = "feat/cause-on-fault" }, "The branch 'feat/cause-on-fault' is a feat branch, which is labelled enhancement, not bug.");

    [Fact]
    public void A_pull_request_into_a_release_without_a_milestone_fails() =>
        Fails(InOrder with { Milestone = null }, "A pull request into 2.3.0 has the milestone 2.3.0; it has none.");

    [Fact]
    public void A_pull_request_into_a_release_with_another_milestone_fails() =>
        Fails(InOrder with { Milestone = "2.4.0" }, "A pull request into 2.3.0 has the milestone 2.3.0; it has '2.4.0'.");

    [Fact]
    public void A_release_into_main_without_its_milestone_fails() =>
        Fails(InOrder with { Head = "release/2.3.0", Base = "main", Labels = ["chore"], Milestone = null }, "A pull request into 2.3.0 has the milestone 2.3.0; it has none.");

    [Fact]
    public void A_topic_into_main_needs_no_milestone() => Passes(InOrder with { Base = "main", Milestone = null });

    [Theory]
    [InlineData("2.3.0")]
    [InlineData("2.2.1")]
    public void A_breaking_change_into_a_minor_or_a_patch_fails(string version) =>
        Fails(InOrder with { Base = $"release/{version}", Milestone = version, Labels = ["bug", "severity/high", "breaking"] },
              $"A breaking change needs a major, and {version} is not one");

    [Fact]
    public void A_breaking_change_into_a_major_passes() =>
        Passes(InOrder with { Base = "release/3.0.0", Milestone = "3.0.0", Labels = ["bug", "severity/high", "breaking"] });

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var (code, output) = Check(new Pr([], Body: null, Head: "claude/x", Milestone: null));

        code.ShouldBe(1);
        output.Split('\n').Count(line => line.StartsWith("::error::", StringComparison.Ordinal)).ShouldBe(4, output);
    }

    [Fact]
    public void A_payload_without_a_pull_request_is_a_usage_error()
    {
        var file = Path.Combine(sandbox.Tool.Work, "push.json");
        File.WriteAllText(file, """{"ref":"refs/heads/main"}""");

        sandbox.Tool.Run(arguments: file).Code.ShouldBe(2);
    }

    [Fact]
    public void A_missing_payload_is_a_usage_error()
    {
        sandbox.Tool.Run(arguments: Path.Combine(sandbox.Tool.Work, "absent.json")).Code.ShouldBe(2);
        sandbox.Tool.Run().Code.ShouldBe(2);
    }
}
