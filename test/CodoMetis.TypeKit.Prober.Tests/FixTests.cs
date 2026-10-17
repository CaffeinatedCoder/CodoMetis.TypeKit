using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// <c>prober fix F --commit SHA</c> writes the fixed line of findings.jsonl as finding-line.schema.json wants it, where a
/// fixed finding was a line written by hand (F3, #45): the open line's claim and probe, the commit's full id.
/// </summary>
public sealed class FixTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private string Findings => Path.Combine(fx.BranchDir, "findings.jsonl");

    private void Open(string id) =>
        File.AppendAllText(Findings, new JsonObject
        {
            ["schemaVersion"] = 1, ["id"] = id, ["claim"] = "C1", ["status"] = "open", ["probe"] = "P", ["reason"] = "row 3 differs",
        }.ToJsonString() + "\n");

    private JsonObject Last() => JsonNode.Parse(File.ReadAllLines(Findings)[^1])!.AsObject();

    private Ledger Ledger() => new(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null);

    [Fact]
    public void It_writes_the_fixed_line_with_the_open_lines_claim_and_probe_and_the_full_commit()
    {
        Open("F9");
        var head = fx.Sh("git", "rev-parse", "HEAD").Trim();
        var (exit, output, error) = fx.Ledger("fix", "F9", "--commit", head[..7], "--reason", "fixed by the converter's null");
        exit.ShouldBe(0, error);
        output.ShouldBe($"F9 fixed at {head[..7]}{Environment.NewLine}");
        var line = Last();
        line.Select(kv => kv.Key).ShouldBe(["schemaVersion", "id", "claim", "status", "commit", "probe", "reason"]);
        line["schemaVersion"]!.GetValue<int>().ShouldBe(1);
        line["id"]!.GetValue<string>().ShouldBe("F9");
        line["claim"]!.GetValue<string>().ShouldBe("C1");
        line["status"]!.GetValue<string>().ShouldBe("fixed");
        line["commit"]!.GetValue<string>().ShouldBe(head);
        line["probe"]!.GetValue<string>().ShouldBe("P");
        line["reason"]!.GetValue<string>().ShouldBe("fixed by the converter's null");
        Ledger().OpenFindings().ShouldBeEmpty();
        Ledger().Findings()["F9"]["status"]!.GetValue<string>().ShouldBe("fixed");
    }

    [Fact]
    public void A_commit_outside_heads_history_is_recorded_and_counts_once_it_is_in_it()
    {
        Open("F9");
        fx.Sh("git", "checkout", "-q", "-b", "fix");
        fx.Write("src/a.cs", "class A { }\n");
        fx.Sh("git", "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qam", "the fix");   // the state stays untracked
        var fix = fx.Sh("git", "rev-parse", "HEAD").Trim();
        fx.Sh("git", "checkout", "-q", "main");
        var (exit, output, error) = fx.Ledger("fix", "F9", "--commit", fix);
        exit.ShouldBe(0, error);
        output.ShouldContain($"F9 fixed at {fix[..7]}; it counts once that commit is in HEAD's history");
        Ledger().OpenFindings().ShouldBe(["F9"]);
        fx.Sh("git", "merge", "-q", "--ff-only", "fix");
        Ledger().OpenFindings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("F8", "HEAD", 1, "F8 is no finding of this ledger")]
    [InlineData("F9", "0123456789abcdef0123456789abcdef01234567", 1, "--commit 0123456789abcdef0123456789abcdef01234567 is no commit of this repository")]
    [InlineData("F9", "", 2, "--commit is required")]
    public void It_refuses_a_finding_the_ledger_does_not_hold_and_a_commit_the_repository_lacks(string id, string commit, int code, string message)
    {
        Open("F9");
        var before = File.ReadAllText(Findings);
        var (exit, _, error) = commit.Length > 0 ? fx.Ledger("fix", id, "--commit", commit) : fx.Ledger("fix", id);
        exit.ShouldBe(code);
        error.ShouldContain(message);
        File.ReadAllText(Findings).ShouldBe(before);
    }

    [Fact]
    public void It_refuses_a_finding_already_fixed_and_a_call_without_one_id()
    {
        Open("F9");
        fx.Ledger("fix", "F9", "--commit", "HEAD").Exit.ShouldBe(0);
        var (exit, _, error) = fx.Ledger("fix", "F9", "--commit", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain("F9 is fixed, not open");
        fx.Ledger("fix", "--commit", "HEAD").Exit.ShouldBe(2);
        fx.Ledger("fix", "F9", "F10", "--commit", "HEAD").Exit.ShouldBe(2);
    }
}
