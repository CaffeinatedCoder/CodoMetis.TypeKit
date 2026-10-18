using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// Rows are per probe: each probe of a claim runs a rows file of its own, its identity reads that file only, and
/// <c>rows --probe P</c> writes it whole, over the claim's pairs the other probes of the claim have not credited.
/// </summary>
public sealed class RowsPerProbeTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private static Dictionary<string, string> R(string x, string y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };

    /// <summary>C1 gets a second probe, Q, with a rows file of its own; P credits the pairs of rows 11 and 22.</summary>
    public RowsPerProbeTests()
    {
        fx.Write("tools/probe/corpus/registry.json", """{"probes": {"P": {"claim": "C1", "filter": "*P"}, "Q": {"claim": "C1", "filter": "*Q"}}}""");
        QRows("1", "2");
        fx.Commit("a second probe of C1");
        fx.Footprint();
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", fx.Evidence([R("1", "1"), R("2", "2")])).Exit.ShouldBe(0);
    }

    private void QRows(string x, string y) =>
        fx.Write("tools/probe/corpus/rows/Q.json", $$$"""{"claim": "C1", "rows": [{"row": {"x": "{{{x}}}", "y": "{{{y}}}"}}]}""");

    private string Staged(string relative) => Path.Combine(fx.BranchDir, "staging", relative);

    private List<string> StagedRows(string probe)
    {
        var staged = JsonNode.Parse(File.ReadAllText(Staged($"rows/{probe}.json")))!;
        staged["claim"]!.GetValue<string>().ShouldBe("C1", "a probe's rows file keeps its claim inside");
        return [.. staged["rows"]!.AsArray().Select(r => $"{r!["row"]!["x"]}{r["row"]!["y"]}").Order(StringComparer.Ordinal)];
    }

    private string Identity(string probe)
    {
        var (exit, output, error) = fx.Ledger("identity", "--probe", probe);
        exit.ShouldBe(0, error);
        return output.Trim();
    }

    [Fact]
    public void Rows_for_a_probe_leave_out_the_pairs_its_siblings_credited()
    {
        var (exit, output, error) = fx.Ledger("rows", "--probe", "Q");
        exit.ShouldBe(0, output + error);
        StagedRows("Q").ShouldBe(["12", "21"], "P credited the pairs of 11 and 22, so Q's rows cover the other two");
        File.Exists(Staged("rows/P.json")).ShouldBeFalse("writing one probe's rows leaves its sibling's as they are");
    }

    [Fact]
    public void Rows_for_a_probe_do_not_subtract_its_own_credit()
    {
        fx.Ledger("rows", "--probe", "P").Exit.ShouldBe(0);
        StagedRows("P").ShouldBe(["11", "12", "21", "22"], "P's credit came from the rows the file replaces, so it would end with them");
    }

    [Fact]
    public void The_file_is_written_whole_never_appended()
    {
        fx.Write(Path.GetRelativePath(fx.Root, Staged("rows/Q.json")), """{"claim": "C1", "rows": [{"row": {"x": "2", "y": "1"}}, {"row": {"x": "1", "y": "2"}}, {"row": {"x": "1", "y": "2"}}]}""");
        fx.Ledger("rows", "--probe", "Q").Exit.ShouldBe(0);
        StagedRows("Q").ShouldBe(["12", "21"], "the staged rows are regenerated, not kept and added to");
    }

    [Fact]
    public void A_probe_that_is_not_registered_is_refused()
    {
        var (exit, _, error) = fx.Ledger("rows", "--probe", "Nope");
        exit.ShouldBe(2);
        error.ShouldContain("no probe Nope in the registry");
    }

    [Fact]
    public void Publish_moves_the_staged_rows_by_probe_with_their_claim()
    {
        fx.Ledger("rows", "--probe", "Q").Exit.ShouldBe(0);
        fx.Ledger("publish").Exit.ShouldBe(0);
        JsonNode published;
        using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(fx.Root, "tools/probe/corpus/rows/Q.json.gz")), System.IO.Compression.CompressionMode.Decompress))
            published = JsonNode.Parse(gz)!;
        published["claim"]!.GetValue<string>().ShouldBe("C1");
        published["rows"]!.AsArray().Select(r => $"{r!["row"]!["x"]}{r["row"]!["y"]}").Order(StringComparer.Ordinal).ShouldBe(["12", "21"]);
        File.Exists(Path.Combine(fx.Root, "tools/probe/corpus/rows/C1.json.gz")).ShouldBeFalse("rows are published under the probe's name, not the claim's");
    }

    [Fact]
    public void A_sibling_s_rows_changing_leaves_the_probe_s_identity_and_credit()
    {
        var before = Identity("P");
        QRows("2", "1");
        fx.Commit("Q's rows changed");
        Identity("P").ShouldBe(before, "P's identity reads P's rows, not Q's");
        fx.Covered().ShouldBe(2, "P's pass still counts");
    }

    [Fact]
    public void The_overlay_unpacks_each_probe_s_rows_and_drops_a_file_the_corpus_no_longer_has()
    {
        var worktree = fx.Root + "-overlay";
        try
        {
            var ledger = new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), "main");
            var config = new JsonObject { ["probeDir"] = "probes" };
            var rows = Path.Combine(global::Prober.Replay.Overlay(ledger, config, worktree), "rows");
            Directory.EnumerateFiles(rows).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["P.json", "Q.json"]);
            File.Delete(Path.Combine(fx.Root, "tools/probe/corpus/rows/Q.json"));
            global::Prober.Replay.Overlay(ledger, config, worktree);
            Directory.EnumerateFiles(rows).Select(Path.GetFileName).ShouldBe(["P.json"], "a rows file left from an earlier overlay would be read by a probe of that name");
        }
        finally
        {
            try { Directory.Delete(worktree, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_probe_s_own_rows_changing_moves_its_identity()
    {
        var before = Identity("P");
        fx.Rows([("1", "1"), ("2", "2"), ("1", "2")]);
        fx.Commit("P's rows changed");
        Identity("P").ShouldNotBe(before);
        fx.Covered().ShouldBe(0);
    }
}

/// <summary>
/// A registry entry's <c>watched</c> paths stale its footprint: what the probe's result depends on that the footprint,
/// the paths its own process ran under <c>src/</c>, cannot see.
/// </summary>
public sealed class WatchedTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private const string Catalogue = "test/Variants/Catalogue.cs";

    private static Dictionary<string, string> R(string x, string y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };

    private void Registry(bool watched) =>
        fx.Write("tools/probe/corpus/registry.json", watched
            ? $$$$"""{"probes": {"P": {"claim": "C1", "filter": "*P", "watched": ["{{{{Catalogue}}}}"]}}}"""
            : """{"probes": {"P": {"claim": "C1", "filter": "*P"}}}""");

    private void ChangeTheCatalogue()
    {
        fx.Write(Catalogue, "class Catalogue { int Site; }\n");
        fx.Commit("the catalogue gains a site");
    }

    /// <summary>A replay's record of P: the footprint from a coverage report of src/a.cs, through the command the replay runs, and a pass.</summary>
    private void Replayed()
    {
        var report = Path.Combine(fx.Root, "coverage.cobertura.xml");
        File.WriteAllText(report, $"""<coverage><sources><source>{fx.Root}</source></sources><packages><package><classes><class filename="src/a.cs"><lines><line number="1" hits="1" /></lines></class></classes></package></packages></coverage>""");
        var (exit, output, error) = fx.Ledger("footprint", "--probe", "P", "--root", fx.Root, "--cobertura", report);
        exit.ShouldBe(0, output + error);
        File.Delete(report);
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", fx.Evidence([R("1", "1"), R("2", "2")])).Exit.ShouldBe(0);
    }

    private bool Stale()
    {
        var (exit, output, error) = fx.Ledger("stale", "--json");
        exit.ShouldBe(0, error);
        return JsonNode.Parse(output)!.AsArray().Single(s => s!["probe"]!.GetValue<string>() == "P")!["stale"]!.GetValue<bool>();
    }

    private Ledger Ledger() => new(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), "main");

    [Fact]
    public void The_footprint_records_each_watched_path_with_its_id()
    {
        Registry(watched: true);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe watching a catalogue");
        Replayed();
        var line = JsonNode.Parse(File.ReadLines(Path.Combine(fx.BranchDir, "footprints.jsonl")).Last())!;
        line["watched"]![Catalogue]!.GetValue<string>().ShouldBe(fx.Sh("git", "rev-parse", $"HEAD:{Catalogue}").Trim());
        Stale().ShouldBeFalse();
        fx.Covered().ShouldBe(2);
    }

    [Fact]
    public void A_change_to_a_watched_path_stales_the_footprint()
    {
        Registry(watched: true);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe watching a catalogue");
        Replayed();
        ChangeTheCatalogue();
        Stale().ShouldBeTrue("the catalogue P watches changed");
        fx.Covered().ShouldBe(0);
    }

    [Fact]
    public void Without_watched_the_footprint_does_not_see_the_path()
    {
        Registry(watched: false);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe watching nothing");
        Replayed();
        ChangeTheCatalogue();
        Stale().ShouldBeFalse("the control: nothing P ran or watches changed, which is the gap watched closes");
        fx.Covered().ShouldBe(2);
    }

    [Fact]
    public void A_watched_path_the_footprint_did_not_record_is_not_current()
    {
        Registry(watched: true);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe watching a catalogue");
        fx.Footprint();
        var ledger = Ledger();
        ledger.FootprintCurrent("P", ledger.Footprints()).ShouldBeFalse("the footprint line records nothing of the catalogue P watches");
    }

    [Fact]
    public void A_change_to_a_watched_path_changes_the_footprint_since_a_release()
    {
        Registry(watched: true);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe watching a catalogue");
        Replayed();
        fx.Sh("git", "tag", "v1");
        var ledger = Ledger();
        ledger.FootprintChangedSince("P", "v1", ledger.Footprints()).ShouldBeFalse();
        ChangeTheCatalogue();
        ledger = Ledger();
        ledger.FootprintChangedSince("P", "v1", ledger.Footprints()).ShouldBeTrue();
    }

    /// <summary>A replay's record of a probe that runs nothing under src/ in its own process, as C22, which builds its consumer in a child process.</summary>
    private (int Exit, string Output, string Error) RanNothingUnderSrc()
    {
        var report = Path.Combine(fx.Root, "coverage.cobertura.xml");
        File.WriteAllText(report, $"""<coverage><sources><source>{fx.Root}</source></sources><packages><package><classes><class filename="test/Harness.cs"><lines><line number="1" hits="1" /></lines></class></classes></package></packages></coverage>""");
        var result = fx.Ledger("footprint", "--probe", "P", "--root", fx.Root, "--cobertura", report);
        File.Delete(report);
        return result;
    }

    [Fact]
    public void A_probe_that_ran_nothing_under_src_is_recorded_by_what_it_watches()
    {
        Registry(watched: true);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe watching a catalogue, building in a child process");
        var (exit, output, error) = RanNothingUnderSrc();
        exit.ShouldBe(0, output + error);
        var line = JsonNode.Parse(File.ReadLines(Path.Combine(fx.BranchDir, "footprints.jsonl")).Last())!;
        line["footprint"]!.AsObject().Count.ShouldBe(0);
        line["watched"]![Catalogue]!.GetValue<string>().ShouldBe(fx.Sh("git", "rev-parse", $"HEAD:{Catalogue}").Trim());
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", fx.Evidence([R("1", "1"), R("2", "2")])).Exit.ShouldBe(0);
        Stale().ShouldBeFalse("what P watches is unchanged");
        fx.Covered().ShouldBe(2);
        ChangeTheCatalogue();
        Stale().ShouldBeTrue("the catalogue P watches changed");
        fx.Covered().ShouldBe(0);
    }

    [Fact]
    public void A_probe_that_ran_nothing_under_src_and_watches_nothing_is_refused()
    {
        Registry(watched: false);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe watching nothing, building in a child process");
        var (exit, output, error) = RanNothingUnderSrc();
        exit.ShouldBe(1, output + error);
        (output + error).ShouldContain("refusing to record an empty footprint");
        File.Exists(Path.Combine(fx.BranchDir, "footprints.jsonl")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_gate_counts_a_change_to_a_watched_path_as_touching_the_claim(bool watched)
    {
        Registry(watched);
        fx.Write(Catalogue, "class Catalogue {}\n");
        fx.Commit("a probe and a catalogue");
        fx.Footprint();
        fx.Sh("git", "tag", "v1");
        ChangeTheCatalogue();
        var (_, output, _) = fx.Ledger("gate", "--since", "v1", "--json");
        var gate = JsonNode.Parse(output)!;
        static List<string> Names(JsonNode? list) => [.. list!.AsArray().Select(x => x!.GetValue<string>())];
        Names(gate["touched"]).ShouldBe(watched ? ["C1"] : [], "a change to what a probe of the claim watches touches the claim, and nothing else does here");
        // The conservative rule and the footprint agree: neither selects the claim without the other.
        Names(gate["selected_by_footprint_only"]).ShouldBeEmpty();
        Names(gate["missed_by_footprint_alone"]).ShouldBeEmpty();
    }
}
