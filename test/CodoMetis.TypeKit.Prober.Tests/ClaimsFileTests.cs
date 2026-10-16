using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// claims.json is read against its schema, and each exclusion carries the reason a reviewer reads (decision 102):
/// a field nobody reads is refused, not kept, and a publish rewrites the file only where it changed.
/// </summary>
public sealed class ClaimsFileTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private void Claims(JsonObject claim)
    {
        var c = new JsonObject { ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s") };
        foreach (var (key, value) in claim) c[key] = value?.DeepClone();
        fx.Write("tools/probe/corpus/claims.json", new JsonArray(c).ToJsonString());
    }

    private static JsonArray Pairs(params (string A, string B)[] pairs) => new([.. pairs.Select(p => (JsonNode)new JsonArray(p.A, p.B))]);

    [Theory]
    [InlineData(null, "claim C1: exclude[0].reason is required")]
    [InlineData("", "claim C1: exclude[0].reason does not match")]
    [InlineData("   ", "claim C1: exclude[0].reason does not match")]
    public void An_exclusion_without_a_reason_is_refused_naming_the_claim(string? reason, string expected)
    {
        var exclusion = new JsonObject { ["pairs"] = Pairs(("x=1", "y=2")) };
        if (reason is not null) exclusion["reason"] = reason;
        Claims(new JsonObject { ["exclude"] = new JsonArray(exclusion) });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain(expected);
    }

    [Fact]
    public void A_bare_pair_is_refused_as_an_exclusion_without_its_reason()
    {
        Claims(new JsonObject { ["exclude"] = Pairs(("x=1", "y=2")) });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain("claim C1: exclude[0] is not an object");
    }

    [Theory]
    [InlineData("excludeReasons")]
    [InlineData("excludeWhy")]
    public void A_reason_field_of_its_own_is_refused(string field)
    {
        Claims(new JsonObject
        {
            ["exclude"] = new JsonArray(new JsonObject { ["reason"] = "No row holds both.", ["pairs"] = Pairs(("x=1", "y=2")) }),
            [field] = new JsonArray(new JsonObject { ["match"] = "x=1 x y=2", ["reason"] = "No row holds both." }),
        });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain($"claim C1: {field} is not a field the schema has");
    }

    [Fact]
    public void A_field_of_its_own_in_an_exclusion_is_refused()
    {
        Claims(new JsonObject
        {
            ["exclude"] = new JsonArray(new JsonObject { ["reason"] = "No row holds both.", ["why"] = "twice", ["pairs"] = Pairs(("x=1", "y=2")) }),
        });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain("claim C1: exclude[0].why is not a field the schema has");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void The_universe_shows_each_exclusion_with_its_reason(string newLine)
    {
        Claims(new JsonObject
        {
            ["exclude"] = new JsonArray(new JsonObject { ["reason"] = "No row holds x=1 with y=2.", ["pairs"] = Pairs(("y=2", "x=1")) }),
        });
        var (exit, output, error) = fx.LedgerEnding([], newLine, "universe", "--base", "HEAD");
        exit.ShouldBe(0, error);
        output.ReplaceLineEndings("\n").ShouldContain("C1: 2 axes, 3 pairs\n  exclude 1 pair(s), first x=1 y=2: No row holds x=1 with y=2.\n");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void The_universe_shows_each_narrowing_with_its_quote(string newLine)
    {
        Claims(new JsonObject { ["narrow"] = new JsonObject { ["y"] = "The claim." } });
        var (exit, output, error) = fx.LedgerEnding([], newLine, "universe", "--base", "HEAD");
        exit.ShouldBe(0, error);
        output.ReplaceLineEndings("\n").ShouldContain("C1: 1 axes, 0 pairs\n  narrow y: \"The claim.\"\n");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_publish_that_changes_nothing_keeps_the_file_as_it_is(string staged)
    {
        // The form publish writes, by hand: a reason with a backtick, a plus and angle brackets, all literal, and \n line
        // ends, whatever this source file was checked out with. The staged copy may come with either line end.
        var tracked = """
            [
              {
                "id": "C1",
                "source": "README.md#a",
                "surfaces": [
                  "s"
                ],
                "exclude": [
                  {
                    "reason": "A `+` row and an Option<T> row cannot meet.",
                    "pairs": [
                      [
                        "x=1",
                        "y=2"
                      ]
                    ]
                  }
                ]
              }
            ]

            """.ReplaceLineEndings("\n");
        fx.Write("tools/probe/corpus/claims.json", tracked);
        fx.Commit("claims");
        fx.Write(".claude/agent-memory-local/prober/branches/main/staging/claims.json", tracked.ReplaceLineEndings(staged));
        var (exit, _, error) = fx.Ledger("publish");
        exit.ShouldBe(0, error);
        fx.Sh("git", "diff", "--exit-code", "--", "tools/probe/corpus/claims.json");
        File.ReadAllText(Path.Combine(fx.Root, "tools/probe/corpus/claims.json")).ShouldBe(tracked, "byte for byte: the escapes and the line ends are the ones publish writes");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void The_corpus_claims_match_their_schema_in_the_form_publish_writes(string checkedOut)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CodoMetis.TypeKit.slnx"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("no CodoMetis.TypeKit.slnx above the test output");
        var path = Path.Combine(root, "tools/probe/corpus/claims.json");
        var claims = Ledger.ReadClaims(path, "tools/probe/corpus/claims.json");
        claims.ShouldNotBeEmpty();
        // Git may check the file out with either line end (core.autocrlf on Windows); what it stores, and diffs, has \n.
        var text = File.ReadAllText(path).ReplaceLineEndings(checkedOut);
        Files.Format(JsonNode.Parse(text)!).ShouldBe(text.ReplaceLineEndings("\n"), "claims.json is in the form `prober publish` writes, so a publish diffs only what it changes");
    }

    [Fact]
    public void A_schema_keyword_the_check_does_not_read_is_an_error_not_a_pass()
    {
        var schema = new JsonObject { ["type"] = "string", ["maxLength"] = 3 };
        Should.Throw<InvalidOperationException>(() => SchemaCheck.Problems("abcd", schema)).Message.ShouldContain("maxLength");
    }
}
