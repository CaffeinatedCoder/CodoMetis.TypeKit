using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// `new` stages what a probe writer used to do by hand: the skeleton in the harness's form with the claim's
/// obligations quoted and one throwing check each, the claim's rows, and the registry entry with no seed yet,
/// so the seeds every obligation owes are named. C1 lists two obligations; C7-two-words lists none.
/// </summary>
public sealed class NewProbeTests : IDisposable
{
    private readonly Fixture fx = new();

    public NewProbeTests()
    {
        fx.Write("README.md", "# a\n\nThe claim.\n\nA key is written as the wrapped type writes it.\n\nA null is refused.\n");
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
            ["replay"] = new JsonObject { ["probeDir"] = "probes", ["include"] = new JsonArray("src"), ["families"] = new JsonObject { ["f"] = new JsonArray("s") } },
        }.ToJsonString() + "\n```\n");
        fx.Write("tools/probe/corpus/claims.json", new JsonArray(
            new JsonObject
            {
                ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
                ["obligations"] = new JsonArray(
                    new JsonObject { ["id"] = "keys", ["sentence"] = "A key is written as the wrapped type writes it." },
                    new JsonObject { ["id"] = "nulls", ["sentence"] = "A null is refused." }),
            },
            new JsonObject { ["id"] = "C7-two-words", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s") }).ToJsonString());
        fx.Commit("claims with obligations");
        fx.Ledger("universe", "--base", "HEAD").Exit.ShouldBe(0);
    }

    public void Dispose() => fx.Dispose();

    private string Staged(string relative) => Path.Combine(fx.BranchDir, "staging", relative);

    [Fact]
    public void It_stages_the_skeleton_the_rows_and_the_registry_entry()
    {
        var (exit, output, error) = fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe");
        exit.ShouldBe(0, output + error);

        var source = File.ReadAllText(Staged("probes/C1KeysProbe.cs"));
        source.ShouldContain("// Claim C1 (README.md#a), surfaces s, family f.");
        source.ShouldContain("//   keys: \"A key is written as the wrapped type writes it.\"");
        source.ShouldContain("//   nulls: \"A null is refused.\"");
        source.ShouldContain("// TODO keys: \"A key is written as the wrapped type writes it.\"");
        source.ShouldContain("// TODO nulls: \"A null is refused.\"");

        // The probe's own rows, C1's other probe P's untouched: P has covered nothing, so a covering array over all four pairs.
        var staged = JsonNode.Parse(File.ReadAllText(Staged("rows/C1KeysProbe.json")))!;
        staged["claim"]!.GetValue<string>().ShouldBe("C1");
        staged["rows"]!.AsArray().Select(r => $"{r!["row"]!["x"]}{r["row"]!["y"]}").Order().ShouldBe(["11", "12", "21", "22"]);
        File.Exists(Staged("rows/P.json")).ShouldBeFalse("a new probe leaves its sibling's rows as they are");

        var entry = JsonNode.Parse(File.ReadAllText(Staged("registry.json")))!["probes"]!["C1KeysProbe"]!.AsObject();
        entry["claim"]!.GetValue<string>().ShouldBe("C1");
        entry["filter"]!.GetValue<string>().ShouldBe("*C1KeysProbe");
        entry["seeds"]!.AsArray().ShouldBeEmpty();
        entry.ContainsKey("seed").ShouldBeFalse();

        output.ShouldContain("still owed, per obligation:");
        output.ShouldContain("  keys: the check CheckKeys");
        output.ShouldContain("  nulls: the check CheckNulls");
    }

    [Fact]
    public void The_seeds_each_obligation_owes_are_named()
    {
        fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe").Exit.ShouldBe(0);
        var problems = global::Prober.Seeds.Problems(new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null));
        problems.ShouldContain("C1KeysProbe: no seed, its registry entry names none");
        problems.ShouldContain("C1: obligation keys has no seed in any of its probes");
        problems.ShouldContain("C1: obligation nulls has no seed in any of its probes");

        var (exit, output, _) = fx.Ledger("seeds", "--dry-run");
        exit.ShouldBe(0);
        output.ShouldContain("  C1KeysProbe/keys: (none)");
        output.ShouldContain("  C1KeysProbe/nulls: (none)");
    }

    [Fact]
    public void A_claim_not_in_the_claims_is_refused_and_nothing_staged()
    {
        var (exit, _, error) = fx.Ledger("new", "--claim", "C9");
        exit.ShouldBe(1);
        error.ShouldContain("no claim C9 in claims.json");
        Directory.Exists(Path.Combine(fx.BranchDir, "staging")).ShouldBeFalse();
    }

    [Fact]
    public void A_name_a_probe_has_is_refused_and_nothing_changes()
    {
        fx.Write("tools/probe/corpus/registry.json", """{"probes": {"P": {"claim": "C1", "filter": "*P"}, "C1OldProbe": {"claim": "C1", "filter": "*C1OldProbe"}}}""");
        fx.Commit("a second probe of C1");
        var (exit, _, error) = fx.Ledger("new", "--claim", "C1", "--probe", "C1OldProbe");
        exit.ShouldBe(1);
        error.ShouldContain("a probe C1OldProbe exists already (in the registry)");
        fx.Ledger("new", "--claim", "C1", "--probe", "P").Exit.ShouldBe(2, "a name that does not end in Probe");
        Directory.Exists(Path.Combine(fx.BranchDir, "staging")).ShouldBeFalse();

        fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe").Exit.ShouldBe(0);
        var registry = File.ReadAllText(Staged("registry.json"));
        var source = File.ReadAllText(Staged("probes/C1KeysProbe.cs"));
        fx.Ledger("new", "--claim", "C7-two-words", "--probe", "C1KeysProbe").Exit.ShouldBe(1, "a second `new` of one name");
        File.ReadAllText(Staged("registry.json")).ShouldBe(registry);
        File.ReadAllText(Staged("probes/C1KeysProbe.cs")).ShouldBe(source);
    }

    [Theory]
    [InlineData("C20-testing-shouldhold", "C20TestingShouldholdProbe")]
    [InlineData("E02-value-translates-to-column", "E02ValueTranslatesToColumnProbe")]
    [InlineData("L01-json-bytes", "L01JsonBytesProbe")]
    public void The_name_defaults_to_the_claim_s_prefix_and_words(string claim, string probe) => NewProbe.DefaultName(claim).ShouldBe(probe);

    [Fact]
    public void Without_a_name_the_default_is_staged()
    {
        fx.Ledger("new", "--claim", "C7-two-words").Exit.ShouldBe(0);
        // The registry's key, not the file: a file system that ignores case finds the file under another spelling.
        JsonNode.Parse(File.ReadAllText(Staged("registry.json")))!["probes"]!.AsObject().Select(kv => kv.Key).ShouldBe(["C7TwoWordsProbe"]);
        File.ReadAllText(Staged("probes/C7TwoWordsProbe.cs")).ShouldContain("//   claim: the claim as a whole");
    }

    [Fact]
    public void A_family_that_is_not_the_claim_s_is_refused()
    {
        var (exit, _, error) = fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe", "--family", "g");
        exit.ShouldBe(2);
        error.ShouldContain("claim C1 is of family f (its first surface, s), not g");
        fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe", "--family", "f").Exit.ShouldBe(0);
    }

    /// <summary>
    /// The lines that make a probe a probe of this harness, with its own names taken out: the namespace, the
    /// class, the rows as member data, the theory and the row's run through Rec.Run with a check of the row.
    /// </summary>
    private static List<string> Form(string source) =>
        [.. source.Split('\n').Select(line => line.Trim()).Select(line => line switch
        {
            _ when line.StartsWith("namespace ", StringComparison.Ordinal) => line,
            _ when Regex.IsMatch(line, @"^public sealed class \w+Probe$") => "public sealed class {probe}",
            _ when line.StartsWith("public static TheoryData<string, int> Data()", StringComparison.Ordinal) =>
                Regex.Replace(line, @"Rows\.Data\(""[^""]+""\)", "Rows.Data({probe})"),
            _ when line.StartsWith("[Theory", StringComparison.Ordinal) => line,
            _ when line.StartsWith("public Task ", StringComparison.Ordinal) => Regex.Replace(line, @"Rec\.Run\(probe, idx, [^;]+\);$", "Rec.Run(probe, idx, {check});"),
            _ => null,
        }).OfType<string>()];

    private static readonly string Corpus = Path.Combine(RepositoryRoot(), "tools", "probe", "corpus", "probes");

    [Theory]
    [InlineData("C08SameNameWarningProbe")]
    [InlineData("C12DescribedConverterProbe")]
    [InlineData("C20ShouldHoldProbe")]
    public void The_skeleton_has_the_form_of_the_corpus_s_probes(string probe)
    {
        fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe").Exit.ShouldBe(0);
        var skeleton = File.ReadAllText(Staged("probes/C1KeysProbe.cs"));
        var corpus = File.ReadAllText(Path.Combine(Corpus, probe + ".cs"));
        Form(skeleton).ShouldBe(Form(corpus));
        Form(skeleton).Count.ShouldBe(5, "namespace, class, rows, theory and run");
        var usings = corpus.Split('\n').Where(l => l.StartsWith("using ", StringComparison.Ordinal)).Select(l => l.Trim()).ToList();
        skeleton.Split('\n').Where(l => l.StartsWith("using ", StringComparison.Ordinal)).ShouldAllBe(l => usings.Contains(l.Trim()));
    }

    [Fact]
    public void Every_obligation_s_check_throws_until_it_is_written()
    {
        fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe").Exit.ShouldBe(0);
        var skeleton = File.ReadAllText(Staged("probes/C1KeysProbe.cs"));
        foreach (var method in new[] { "CheckKeys", "CheckNulls" })
        {
            skeleton.ShouldContain($"static Task<string?> {method}(Row r) => throw new NotImplementedException(");
            Regex.IsMatch(skeleton, $@"static async Task<string\?> Check\(Row r\) =>[^;]*await {method}\(r\)").ShouldBeTrue($"Check runs {method}");
        }
        Regex.Matches(skeleton, @"=> throw new NotImplementedException\(").Count.ShouldBe(2);
        skeleton.ShouldNotContain("return null");
        skeleton.ShouldNotContain("FromResult");
    }

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CodoMetis.TypeKit.slnx"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("no CodoMetis.TypeKit.slnx above the test output");
        return root;
    }
}
