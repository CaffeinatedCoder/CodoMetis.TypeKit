using System.Text.Json.Nodes;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// `prober gaps` prints what the corpus leaves out, each under its heading with its recorded reason, and says
/// none under a heading with nothing: a report from the tracked corpus and the generated axes that decides
/// nothing (decision 104).
/// </summary>
public sealed class GapsTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    /// <summary>The output after its first line, which names the branch and the commit.</summary>
    private string Gaps(string newLine)
    {
        var (exit, output, error) = fx.LedgerEnding([], newLine, "gaps", "--base", "HEAD");
        exit.ShouldBe(0, "a report decides nothing: " + error);
        var text = output.ReplaceLineEndings("\n");
        text.ShouldStartWith("prober gaps: main at ");
        return text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..];
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Each_gap_is_listed_under_its_heading_with_its_reason(string newLine)
    {
        fx.Write("design/adr/0001-limits.md", "# 1. Limits\n\nAn ordering of keys is not promised.\n\nNo culture but the invariant one is promised.\n");
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject
            {
                ["x"] = new JsonArray("1", "2", "3"), ["y"] = new JsonArray("1", "2"),
                ["shape.refused-kind"] = new JsonArray("in-generic-type"),
            },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
            ["refused"] = new JsonObject { ["x"] = new JsonObject { ["3"] = "the framework refuses 3 on its own (KB-001)" } },
            ["out-of-reach"] = new JsonObject { ["postgresql"] = "needs a server" },
        }.ToJsonString() + "\n```\n");
        File.WriteAllText(Path.Combine(fx.Memory, "axes-generated.json"), new JsonObject
        {
            ["axes"] = new JsonObject { ["json.z"] = new JsonArray("true", "false") },
            ["needsValues"] = new JsonArray("json.w"),
            ["versions"] = new JsonObject { ["System.Text.Json"] = "10.0.0" },
        }.ToJsonString());
        fx.Write("tools/probe/corpus/claims.json", new JsonArray(new JsonObject
        {
            ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
            ["narrow"] = new JsonObject { ["y"] = "The claim." },
            ["not-promised"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "order", ["source"] = "design/adr/0001-limits.md#1-limits", ["sentence"] = "An ordering of keys is not promised.",
                    ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2") },
                },
                new JsonObject { ["id"] = "culture", ["source"] = "design/adr/0001-limits.md#1-limits", ["sentence"] = "No culture but the invariant one is promised." }),
        }).ToJsonString());

        Gaps(newLine).ShouldBe("""
            settings no surface reaches and nothing ignores (enumerated at System.Text.Json 10.0.0): 1
              json.z (no surface reaches it and it is not ignored)
            settings without values: 1
              json.w (no hand values and not ignored)
            declarations the generators refuse (shape.refused-kind): 1
              in-generic-type
            values the frameworks refuse on their own: 1
              x=3: the framework refuses 3 on its own (KB-001)
            not promised, by what it concerns: 3
              the whole claim: C1/culture "No culture but the invariant one is promised." (design/adr/0001-limits.md#1-limits)
              x=1|2: C1/order "An ordering of keys is not promised." (design/adr/0001-limits.md#1-limits)
              y: C1 narrows it, "The claim." (README.md#a)
            pairs no obligation wants (declared inapplicable): none
            pairs no passing check read both axes of (the last replay's evidence): none
            out of reach: 1
              postgresql: needs a server

            """.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_corpus_without_gaps_says_none_under_each_heading(string newLine) =>
        Gaps(newLine).ShouldBe("""
            settings no surface reaches and nothing ignores: none
            settings without values: none
            declarations the generators refuse (shape.refused-kind): none
            values the frameworks refuse on their own: none
            not promised, by what it concerns: none
            pairs no obligation wants (declared inapplicable): none
            pairs no passing check read both axes of (the last replay's evidence): none
            out of reach: none

            """.ReplaceLineEndings("\n"));

    /// <summary>
    /// C1 over x, y and z: shape wants every pair and its passing checks read x and y; parse does not apply where x=1 and
    /// z=1 and its passing checks read x and z. x=1 z=1 is wanted by shape alone, which never read z, and no passing check
    /// read y with z: those five pairs are listed. A check that did not pass reads nothing for the listing.
    /// </summary>
    [Fact]
    public void The_pairs_no_obligation_wanting_them_read_both_axes_of_are_listed()
    {
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2"), ["z"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y", "z"), ["watched"] = new JsonArray("src") } },
        }.ToJsonString() + "\n```\n");
        fx.Write("tools/probe/corpus/claims.json", new JsonArray(new JsonObject
        {
            ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
            ["obligations"] = new JsonArray(
                new JsonObject { ["id"] = "shape", ["sentence"] = "The claim." },
                new JsonObject
                {
                    ["id"] = "parse", ["sentence"] = "The claim.",
                    ["inapplicable"] = new JsonArray(new JsonObject { ["axes"] = new JsonObject { ["x"] = new JsonArray("1"), ["z"] = new JsonArray("1") }, ["cite"] = "docs:README.md#a" }),
                }),
        }).ToJsonString());
        fx.Commit("three axes");
        fx.Ledger("universe", "--base", "HEAD").Exit.ShouldBe(0);
        Gaps("\n").ShouldContain("pairs no passing check read both axes of (the last replay's evidence): none\n", Case.Sensitive, "no evidence yet: nothing to say");

        static JsonObject Check(string obligation, string category, params (string Axis, string Value)[] read) => new()
        {
            ["obligation"] = obligation, ["category"] = category, ["baseline"] = "twin",
            ["applied"] = new JsonObject(read.Select(r => KeyValuePair.Create(r.Axis, (JsonNode?)r.Value))),
        };
        var evidence = Path.Combine(fx.Root, "evidence.json");
        File.WriteAllText(evidence, new JsonObject
        {
            ["variants"] = new JsonArray(new JsonObject { ["env"] = new JsonObject(), ["expected"] = new JsonArray(0), ["executed"] = new JsonArray(0) }),
            ["rows"] = new JsonObject
            {
                ["0"] = new JsonObject
                {
                    ["category"] = "pass", ["applied"] = new JsonObject(), ["findings"] = new JsonArray(),
                    ["checks"] = new JsonArray(
                        Check("shape", "pass", ("x", "1"), ("y", "1")),
                        Check("parse", "pass", ("x", "2"), ("z", "1")),
                        Check("parse", "inapplicable", ("y", "2"), ("z", "2"))),
                },
            },
        }.ToJsonString());
        fx.Footprint();
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", evidence).Exit.ShouldBe(0);

        Gaps("\n").ShouldContain("""
            pairs no passing check read both axes of (the last replay's evidence): 5
              C1: x=1 z=1
              C1: y=1 z=1
              C1: y=1 z=2
              C1: y=2 z=1
              C1: y=2 z=2

            """.ReplaceLineEndings("\n"));
    }
}
