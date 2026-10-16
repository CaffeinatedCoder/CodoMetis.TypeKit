using System.Text.Json.Nodes;
using Prober;
using Probes.Shapes;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// An obligation declares where it does not apply, as axis values with a citation: <c>Axes.Build</c> holds the
/// declaration to the claim's axes, values and not-promised list; a pair is wanted by an obligation unless one of
/// its declarations matches it, and a pair no obligation wants is declared inapplicable, which `universe`, `gaps`
/// and `status` show with its citations, the gate leaves out as it leaves out an exclusion, and the covering
/// generator never puts in a row. Fixture: C1 over x and y, each 1 and 2, so four pairs.
/// </summary>
public sealed class DeclaredApplicabilityTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private static JsonObject Declared(string cite, params (string Axis, string[] Values)[] axes) => new()
    {
        ["axes"] = new JsonObject(axes.Select(a => KeyValuePair.Create(a.Axis, (JsonNode?)new JsonArray([.. a.Values.Select(v => (JsonNode)v)])))),
        ["cite"] = cite,
    };

    private static JsonObject Obligation(string id, params JsonObject[] inapplicable)
    {
        var o = new JsonObject { ["id"] = id, ["sentence"] = "The claim." };
        if (inapplicable.Length > 0) o["inapplicable"] = new JsonArray([.. inapplicable]);
        return o;
    }

    /// <summary>C1 with the obligations given and one not-promised entry, <c>no-one</c>.</summary>
    private void Claim(params JsonObject[] obligations) =>
        fx.Write("tools/probe/corpus/claims.json", new JsonArray(new JsonObject
        {
            ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
            ["obligations"] = new JsonArray([.. obligations]),
            ["not-promised"] = new JsonArray(new JsonObject { ["id"] = "no-one", ["source"] = "README.md#a", ["sentence"] = "The claim." }),
        }).ToJsonString());

    /// <summary>
    /// keys does not apply where x=1 and y=1 (one declaration over both axes), nulls nowhere x=1: the pair x=1 y=1 is
    /// wanted by neither, x=1 y=2 by keys alone.
    /// </summary>
    private void OnePairWantedByNone() => Claim(
        Obligation("keys", Declared("docs:README.md#a", ("x", ["1"]), ("y", ["1"]))),
        Obligation("nulls", Declared("not-promised:no-one", ("x", ["1"]))));

    private string Refused()
    {
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        return error;
    }

    [Fact]
    public void A_declaration_naming_an_axis_the_claim_does_not_have_is_refused()
    {
        Claim(Obligation("keys", Declared("not-promised:no-one", ("z", ["1"]))));
        Refused().ShouldContain("claim C1: obligation keys: inapplicable[0] names axis z, which the claim does not have");
    }

    [Fact]
    public void A_declaration_naming_a_value_the_claim_does_not_list_is_refused()
    {
        Claim(Obligation("keys", Declared("not-promised:no-one", ("x", ["1"]))), Obligation("nulls", Declared("not-promised:no-one", ("x", ["3"]))));
        Refused().ShouldContain("claim C1: obligation nulls: inapplicable[0] names x=3, a value the claim does not list for x");
    }

    [Theory]
    [InlineData("fact:KB-31", "is not a knowledge entry's id (fact:KB-031)")]
    [InlineData("record:94", "is not a decision record's number (record:0094)")]
    [InlineData("docs:../README.md#a", "is not a docs source (docs:docs/json.md#decimals-in-one-spelling)")]
    [InlineData("not-promised:No-one", "is not a not-promised id (not-promised:closed-set-has-no-text)")]
    public void A_malformed_cite_is_refused(string cite, string why)
    {
        Claim(Obligation("keys", Declared(cite, ("x", ["1"]))));
        Refused().ShouldContain($"claim C1: obligation keys: inapplicable[0]: cite {cite} {why}");
    }

    [Fact]
    public void A_cite_of_a_not_promised_id_the_claim_lacks_is_refused()
    {
        Claim(Obligation("keys", Declared("not-promised:no-such-entry", ("x", ["1"]))));
        Refused().ShouldContain("claim C1: obligation keys: inapplicable[0] cites not-promised:no-such-entry, which the claim's not-promised list does not hold");
    }

    [Fact]
    public void A_field_a_declaration_does_not_have_is_refused_by_the_schema()
    {
        var declaration = Declared("not-promised:no-one", ("x", ["1"]));
        declaration["reason"] = "x=1 has no key";
        Claim(Obligation("keys", declaration));
        Refused().ShouldContain("claim C1: obligations[0].inapplicable[0].reason is not a field the schema has");
    }

    [Fact]
    public void A_pair_wanted_by_no_obligation_is_listed_by_universe_with_its_citations_and_the_claim_keeps_its_pairs()
    {
        OnePairWantedByNone();
        var (exit, output, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(0, error);
        output.ReplaceLineEndings("\n").ShouldContain(
            "C1: 2 axes, 4 pairs\n  inapplicable 1 pair(s), wanted by no obligation, first x=1 y=1: docs:README.md#a, not-promised:no-one\n");
    }

    [Fact]
    public void Gaps_lists_the_pairs_no_obligation_wants_under_their_own_heading()
    {
        OnePairWantedByNone();
        var (exit, output, error) = fx.Ledger("gaps", "--base", "HEAD");
        exit.ShouldBe(0, error);
        output.ReplaceLineEndings("\n").ShouldContain(
            "pairs no obligation wants (declared inapplicable): 1\n  C1: inapplicable 1 pair(s), wanted by no obligation, first x=1 y=1: docs:README.md#a, not-promised:no-one\n");
    }

    private static Dictionary<string, string> R(string x, string y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };

    /// <summary>P passes rows over the three pairs some obligation wants, never x=1 y=1.</summary>
    private void RecordTheWantedPairs()
    {
        fx.Rows([("1", "2"), ("2", "1"), ("2", "2")]);
        fx.Commit("claims and rows");
        fx.Ledger("universe", "--base", "HEAD").Exit.ShouldBe(0);
        fx.Footprint();
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", fx.Evidence([R("1", "2"), R("2", "1"), R("2", "2")])).Exit.ShouldBe(0);
    }

    [Fact]
    public void Status_reports_the_inapplicable_pairs_and_is_done_once_every_wanted_pair_is_current()
    {
        OnePairWantedByNone();
        RecordTheWantedPairs();
        var (exit, output, error) = fx.Ledger("status", "--json");
        exit.ShouldBe(0, error);
        var status = JsonNode.Parse(output)!;
        var c1 = status["claims"]!["C1"]!;
        c1["pairs"]!.GetValue<int>().ShouldBe(4, "the claim's pairs stay its pairs");
        c1["current"]!.GetValue<int>().ShouldBe(3);
        c1["inapplicable_pairs"]!.GetValue<int>().ShouldBe(1);
        status["inapplicable_pairs"]!.GetValue<int>().ShouldBe(1);
        status["done"]!.GetValue<bool>().ShouldBeTrue("the one pair not current is wanted by no obligation");
        fx.Ledger("status").Out.ShouldContain("  C1: 3/4, 1 inapplicable");
    }

    [Fact]
    public void Status_is_not_done_while_a_wanted_pair_is_not_current()
    {
        Claim(Obligation("keys"), Obligation("nulls"));
        RecordTheWantedPairs();
        var status = JsonNode.Parse(fx.Ledger("status", "--json").Out)!;
        status["claims"]!["C1"]!["inapplicable_pairs"]!.GetValue<int>().ShouldBe(0);
        status["done"]!.GetValue<bool>().ShouldBeFalse("x=1 y=1 is wanted and not current");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_gate_leaves_out_a_pair_wanted_by_no_obligation_as_it_leaves_out_an_exclusion(bool declared)
    {
        fx.Sh("git", "tag", "v1");
        if (declared) OnePairWantedByNone(); else Claim(Obligation("keys"), Obligation("nulls"));
        RecordTheWantedPairs();
        fx.Write("README.md", "# a\n\nThe claim.\n\nMore words.\n");
        fx.Commit("touch the claim");

        var (exit, output, _) = fx.Ledger("gate", "--since", "v1", "--json");
        var gate = JsonNode.Parse(output)!;
        gate["touched"]!.AsArray().Select(c => c!.GetValue<string>()).ShouldBe(["C1"]);
        if (declared)
        {
            exit.ShouldBe(0, output);
            gate["uncovered_pairs"]!.GetValue<int>().ShouldBe(0);
            gate["inapplicable_pairs"]!.GetValue<int>().ShouldBe(1);
        }
        else
        {
            exit.ShouldBe(1);
            gate["reasons"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldContain("C1: 1 of 4 pairs uncovered (touched since v1)");
        }
    }

    [Fact]
    public void Rows_for_a_claim_ask_for_no_pair_no_obligation_wants()
    {
        OnePairWantedByNone();
        fx.Commit("claims");
        fx.Ledger("universe", "--base", "HEAD").Exit.ShouldBe(0);
        var rows = JsonNode.Parse(fx.Ledger("rows", "--claim", "C1", "--full").Out)!;
        rows["rows"]!.AsArray().Select(r => $"{r!["row"]!["x"]} {r["row"]!["y"]}").ShouldNotContain("1 1");
        rows["uncovered_after"]!.GetValue<int>().ShouldBe(0, "a pair wanted by no obligation is not left uncovered");
    }

    [Fact]
    public void A_declaration_changes_the_identity_of_its_claim_s_probes()
    {
        Claim(Obligation("keys"), Obligation("nulls"));
        fx.Commit("claims");
        var before = fx.Ledger("identity", "--probe", "P").Out;
        OnePairWantedByNone();
        fx.Commit("declared");
        fx.Ledger("identity", "--probe", "P").Out.ShouldNotBe(before, "Row.Check applies the declarations, so a result under others is not this one");
    }

    [Fact]
    public void The_overlay_stages_the_claims_beside_the_rows_with_the_staging_s_over_the_corpus_s()
    {
        Claim(Obligation("keys"), Obligation("nulls"));
        fx.Commit("claims");
        var ledger = new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), "main");
        var staged = new JsonArray(new JsonObject
        {
            ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
            ["obligations"] = new JsonArray(Obligation("keys", Declared("docs:README.md#a", ("x", ["1"])))),
        });
        fx.Write(Path.GetRelativePath(fx.Root, Path.Combine(ledger.StagingDir, "claims.json")), staged.ToJsonString());
        var worktree = fx.Root + "-overlay";
        try
        {
            var probeDir = global::Prober.Replay.Overlay(ledger, new JsonObject { ["probeDir"] = "probes" }, worktree);
            var claims = JsonNode.Parse(File.ReadAllText(Path.Combine(probeDir, "claims.json")))!.AsArray();
            JsonNode.DeepEquals(claims, staged).ShouldBeTrue(claims.ToJsonString());
            global::Prober.Replay.Overlay(ledger, new JsonObject { ["probeDir"] = "probes" }, worktree);
            File.Exists(Path.Combine(probeDir, "claims.json")).ShouldBeTrue("a second overlay keeps it");
        }
        finally
        {
            try { Directory.Delete(worktree, recursive: true); } catch (IOException) { }
        }
    }
}

/// <summary>The wanted pairs of a claim, computed on the claim itself, and the covering generator over them.</summary>
public sealed class WantedPairsTests
{
    private static Inapplicability Declared(string cite, params (string Axis, string[] Values)[] axes) =>
        new(axes.ToDictionary(a => a.Axis, a => a.Values.ToList(), StringComparer.Ordinal), cite);

    private static global::Prober.Obligation Obligation(string id, params Inapplicability[] inapplicable) =>
        new(id, "The claim.", new SortedDictionary<string, List<string>>(StringComparer.Ordinal)) { Inapplicable = inapplicable };

    private static Claim Over(int axes, params global::Prober.Obligation[] obligations) => new()
    {
        Id = "C1", Source = "README.md#a", Surfaces = ["s"], Watched = [], Excluded = [], Obligations = obligations,
        Axes = new(new[] { "x", "y", "z" }.Take(axes).ToDictionary(a => a, _ => new List<string> { "1", "2" }), StringComparer.Ordinal),
    };

    private static IEnumerable<string> Shown(IEnumerable<Pair> pairs) => pairs.Select(p => p.ToString()).Order(StringComparer.Ordinal);

    [Fact]
    public void An_obligation_wants_the_pairs_none_of_its_declarations_matches_on_either_side()
    {
        // keys: not where x=1 (the first of a pair, ordinally); nulls: not where y=1 (the second); counts: nowhere declared.
        var claim = Over(2, Obligation("keys", Declared("record:0001", ("x", ["1"]))), Obligation("nulls", Declared("record:0002", ("y", ["1"]))), Obligation("counts"));
        Shown(claim.PairsWantedBy("keys")).ShouldBe(["x=2 y=1", "x=2 y=2"]);
        Shown(claim.PairsWantedBy("nulls")).ShouldBe(["x=1 y=2", "x=2 y=2"]);
        Shown(claim.PairsWantedBy("counts")).ShouldBe(["x=1 y=1", "x=1 y=2", "x=2 y=1", "x=2 y=2"]);
        claim.Pairs().Count.ShouldBe(4, "the claim's pairs stay its pairs");
        claim.DeclaredInapplicable().ShouldBeEmpty("counts wants every pair");
    }

    [Fact]
    public void A_pair_no_obligation_wants_is_declared_inapplicable_with_the_citations_that_match_it()
    {
        // keys wants x=2 y=1 alone, nulls x=1 y=2 alone.
        var claim = Over(2,
            Obligation("keys", Declared("record:0001", ("x", ["1"])), Declared("record:0003", ("y", ["2"]))),
            Obligation("nulls", Declared("record:0002", ("y", ["1"])), Declared("record:0004", ("x", ["2"]))));
        Shown(claim.PairsWanted()).ShouldBe(["x=1 y=2", "x=2 y=1"]);
        var dead = claim.DeclaredInapplicable();
        Shown(dead.Keys).ShouldBe(["x=1 y=1", "x=2 y=2"]);
        dead[new Pair("x=1", "y=1")].ShouldBe(["record:0001", "record:0002"]);
        dead[new Pair("x=2", "y=2")].ShouldBe(["record:0003", "record:0004"]);
    }

    [Fact]
    public void A_declaration_over_two_axes_matches_only_the_pair_holding_both()
    {
        var claim = Over(3, Obligation("keys", Declared("record:0001", ("x", ["1"]), ("y", ["1"]))));
        Shown(claim.DeclaredInapplicable().Keys).ShouldBe(["x=1 y=1"], "x=1 with z, or y=1 with z, is in rows where the other axis is 2");
    }

    [Fact]
    public void The_covering_rows_never_hold_a_pair_no_obligation_wants_and_count_each_wanted_pair_once()
    {
        // Both obligations want the same eleven pairs: none of x=1 with y=1.
        var claim = Over(3, Obligation("keys", Declared("record:0001", ("x", ["1"]), ("y", ["1"]))), Obligation("nulls", Declared("record:0002", ("x", ["1"]), ("y", ["1"]))));
        for (var seed = 1; seed <= 20; seed++)
        {
            var rows = Covering.Rows(claim, claim.Pairs(), 2000, seed);
            rows.ShouldNotContain(r => r.Row["x"] == "1" && r.Row["y"] == "1", $"seed {seed}");
            var gained = rows.SelectMany(r => r.Pairs).ToList();
            gained.Count.ShouldBe(11, $"seed {seed}: each wanted pair once");
            Shown(gained).ShouldBe(Shown(claim.PairsWanted()), $"seed {seed}");
        }
    }

    [Fact]
    public void A_seeded_row_fills_each_other_axis_with_a_value_that_puts_no_unwanted_pair_in_it()
    {
        var claim = Over(3, Obligation("keys", Declared("record:0001", ("x", ["1"]), ("z", ["1"]))));
        var row = Covering.Seeded(claim, new Pair("y=1", "z=1"), claim.DeclaredInapplicable());
        row.ShouldBe(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["x"] = "2", ["y"] = "1", ["z"] = "1" });
    }

    [Theory]
    [InlineData("not-promised:closed-set-has-no-text")]
    [InlineData("not-promised:Closed")]
    [InlineData("docs:docs/json.md#decimals-in-one-spelling")]
    [InlineData("docs:README.md")]
    [InlineData("docs:README.md#What TypeKit guarantees")]
    [InlineData("docs:/etc/passwd.md")]
    [InlineData("docs:../README.md#a")]
    [InlineData("fact:KB-031")]
    [InlineData("fact:KB-31")]
    [InlineData("record:0094")]
    [InlineData("record:94")]
    [InlineData("README.md#a")]
    [InlineData("note:x")]
    public void A_cite_the_universe_accepts_is_one_the_harness_can_cite(string cite)
    {
        var harness = Record.Exception(() => Cite.Parse(cite)) is null;
        (Axes.CiteProblem(cite) is null).ShouldBe(harness, harness ? "the harness cites it" : "the harness refuses it");
    }
}

/// <summary>
/// The harness applies the claim's declarations before a check's body runs: where one matches the row's values the
/// check is inapplicable with the declaration's citation, for that check only, and the body does not run. Without a
/// staged claims file, as for a probe these tests run in-process, no claim declares anything.
/// </summary>
[Collection(nameof(ProbeOut))]
public sealed class DeclaredCheckTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "prober-tests", Guid.NewGuid().ToString("N"));
    private readonly string? before = Rows.ClaimsFile;

    public DeclaredCheckTests()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claims.json"), new JsonArray(new JsonObject
        {
            ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
            ["obligations"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "keys", ["sentence"] = "The claim.",
                    ["inapplicable"] = new JsonArray(
                        new JsonObject { ["axes"] = new JsonObject { ["x"] = new JsonArray("1"), ["y"] = new JsonArray("2") }, ["cite"] = "not-promised:no-key-here" },
                        new JsonObject { ["axes"] = new JsonObject { ["y"] = new JsonArray("3") }, ["cite"] = "fact:KB-031" }),
                },
                new JsonObject { ["id"] = "nulls", ["sentence"] = "The claim." }),
        }).ToJsonString());
        Rows.ClaimsFile = Path.Combine(dir, "claims.json");
    }

    public void Dispose()
    {
        Rows.ClaimsFile = before;
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Runs one row of C1 through <c>Rec.Run</c> with a check of keys and one of nulls, synchronous or not; which bodies ran, and the line written.</summary>
    private (List<string> Ran, Exception? Ended, JsonObject Line) Run(string x, string y, bool async = false, string? claim = "C1")
    {
        var ran = new List<string>();
        var outDir = Path.Combine(dir, "out");
        var previous = Environment.GetEnvironmentVariable("PROBE_OUT");
        Environment.SetEnvironmentVariable("PROBE_OUT", outDir);
        Exception? ended = null;
        try
        {
            Rec.Run("P", new Row(0, new Dictionary<string, string> { ["x"] = x, ["y"] = y }, claim), async r =>
            {
                foreach (var obligation in new[] { "keys", "nulls" })
                    _ = async
                        ? await r.Check(obligation, Baseline.Twin, c => { ran.Add(obligation); return Task.FromResult<string?>(c["x"] == "0" ? "differs" : null); })
                        : r.Check(obligation, Baseline.Twin, c => { ran.Add(obligation); return c["x"] == "0" ? "differs" : null; });
                return null;
            }).GetAwaiter().GetResult();
        }
        catch (Exception e) { ended = e; }
        finally { Environment.SetEnvironmentVariable("PROBE_OUT", previous); }
        var line = File.ReadAllLines(Path.Combine(outDir, "P.jsonl")).Select(l => JsonNode.Parse(l)!.AsObject()).Last();
        File.Delete(Path.Combine(outDir, "P.jsonl"));
        return (ran, ended, line);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_check_its_claim_declares_inapplicable_for_the_row_s_values_does_not_run_and_records_the_cite(bool async)
    {
        var (ran, ended, line) = Run("1", "2", async);
        ran.ShouldBe(["nulls"], "keys does not apply where x=1 and y=2");
        ended.ShouldBeNull("nulls compared and passed; keys did not apply, which voids nothing");
        var keys = HarnessRun.Check(line, "keys");
        Files.Str(keys["category"]).ShouldBe("inapplicable");
        Files.Strings(keys["reasons"]).ShouldBe(["inapplicable:not-promised:no-key-here(declared x=1 y=2)"]);
        Files.StringMap(keys["applied"]).ShouldBe(new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" }, "the axes the declaration matched");
        Files.Str(HarnessRun.Check(line, "nulls")["category"]).ShouldBe("pass", "the declaration is keys', for that check only");
    }

    [Fact]
    public void Each_declaration_of_the_obligation_is_applied_and_one_over_two_axes_needs_both()
    {
        Run("1", "1").Ran.ShouldBe(["keys", "nulls"], "x=1 with y=1 is not x=1 with y=2");
        Run("2", "2").Ran.ShouldBe(["keys", "nulls"], "x=2 with y=2 is not x=1 with y=2");
        var (ran, _, line) = Run("2", "3");
        ran.ShouldBe(["nulls"]);
        Files.Strings(HarnessRun.Check(line, "keys")["reasons"]).ShouldBe(["inapplicable:fact:KB-031(declared y=3)"]);
    }

    [Fact]
    public void A_row_in_hand_without_a_claim_or_without_a_staged_claims_file_sees_no_declarations()
    {
        Run("1", "2", claim: null).Ran.ShouldBe(["keys", "nulls"]);
        Rows.ClaimsFile = null;
        Run("1", "2").Ran.ShouldBe(["keys", "nulls"]);
        Rows.ClaimsFile = Path.Combine(dir, "none.json");
        Run("1", "2").Ran.ShouldBe(["keys", "nulls"]);
    }

    [Fact]
    public void A_copy_of_a_row_keeps_its_claim()
    {
        var row = new Row(0, new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" }, "C1");
        row.Fresh().Claim.ShouldBe("C1");
        row.With("y", "1").Claim.ShouldBe("C1");
    }

    [Fact]
    public void A_row_of_a_rows_file_holds_the_file_s_claim()
    {
        var path = Path.Combine(dir, "P.json");
        File.WriteAllText(path, """{"claim": "C1", "rows": [{"row": {"x": "1", "y": "2"}}]}""");
        Rows.Read(path).ShouldHaveSingleItem().Claim.ShouldBe("C1");
    }
}
