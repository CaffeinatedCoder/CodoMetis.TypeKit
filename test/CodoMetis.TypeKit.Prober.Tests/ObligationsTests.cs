using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// A claim lists what it promises, each obligation a sentence quoted from its source, and what it does not
/// promise, each with the sentence that makes it intended; every obligation of a claim with probes has a seed
/// that names it (decision 104). The probe here is a fake adapter: row N of C1 violates while SEED_ROW is N.
/// </summary>
public sealed class ObligationsTests : IDisposable
{
    private readonly Fixture fx = new();
    private readonly string cache;
    private readonly FakeAdapter adapter = new();

    public ObligationsTests()
    {
        cache = fx.Root + "-cache";
        fx.Sh("git", "config", "core.autocrlf", "false");
        fx.Write("README.md", "# a\n\nThe claim.\n\nA key is written as the wrapped type writes it.\n\nA null is refused.\n");
        fx.Write("design/adr/0001-limits.md", "# 1. Limits\n\nAn ordering of keys is not promised.\n");
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
            ["replay"] = new JsonObject { ["probeDir"] = "probes", ["include"] = new JsonArray("src") },
        }.ToJsonString() + "\n```\n");
        fx.Commit("sources and replay configuration");
    }

    public void Dispose()
    {
        fx.Dispose();
        try { Directory.Delete(cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static JsonObject Obligation(string id, string sentence) => new() { ["id"] = id, ["sentence"] = sentence };

    private static JsonArray TwoObligations() => new(
        Obligation("keys", "A key is written as the wrapped type writes it."),
        Obligation("nulls", "A null is refused."));

    private void Claims(JsonObject claim)
    {
        var c = new JsonObject { ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s") };
        foreach (var (key, value) in claim) c[key] = value?.DeepClone();
        fx.Write("tools/probe/corpus/claims.json", new JsonArray(c).ToJsonString());
    }

    /// <summary>Registers P for C1 with the seeds given, each an environment toggle that makes row <c>row</c> violate.</summary>
    private void Register(JsonObject? seed = null, params (string Obligation, string Row)[] seeds)
    {
        var entry = new JsonObject { ["claim"] = "C1", ["filter"] = "*P" };
        if (seed is not null) entry["seed"] = seed;
        if (seeds.Length > 0)
            entry["seeds"] = new JsonArray([.. seeds.Select(s => (JsonNode)new JsonObject
            {
                ["obligation"] = s.Obligation, ["env"] = new JsonObject { ["SEED_ROW"] = s.Row }, ["defect"] = $"{s.Obligation} is broken.",
            })]);
        fx.Write("tools/probe/corpus/registry.json", new JsonObject { ["probes"] = new JsonObject { ["P"] = entry } }.ToJsonString());
        fx.Commit("register");
    }

    private static JsonObject EnvSeed() => new() { ["env"] = new JsonObject { ["SEED_ROW"] = "1" }, ["defect"] = "Row 1 is broken." };

    private List<string> Problems() => Seeds.Problems(new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null));

    private JsonObject Status()
    {
        var (exit, output, error) = fx.Ledger("status", "--json");
        exit.ShouldBe(0, error);
        return JsonNode.Parse(output)!["claims"]!["C1"]!.AsObject();
    }

    /// <summary>Each obligation of the claim in `status --json`, as "id: the probes whose seeds break it".</summary>
    private static List<string> ObligationsOf(JsonObject claim) =>
        [.. claim["obligations"]!.AsArray().Select(o => $"{o!["id"]!.GetValue<string>()}: {string.Join(", ", o["seeded_by"]!.AsArray().Select(p => p!.GetValue<string>()))}")];

    [Fact]
    public void An_obligation_quoting_a_sentence_its_source_does_not_hold_is_refused()
    {
        Claims(new JsonObject { ["obligations"] = new JsonArray(Obligation("keys", "A key is written by ToString().")) });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain("claim C1: obligation keys quotes a sentence not found in README.md");
    }

    [Fact]
    public void A_not_promised_entry_is_checked_against_its_own_source()
    {
        // The sentence is in the claim's README, not in the record the entry names, so it is refused.
        Claims(new JsonObject
        {
            ["not-promised"] = new JsonArray(new JsonObject { ["id"] = "order", ["source"] = "design/adr/0001-limits.md#1-limits", ["sentence"] = "A null is refused." }),
        });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain("claim C1: not-promised order quotes a sentence not found in design/adr/0001-limits.md");
    }

    [Fact]
    public void A_not_promised_entry_its_source_holds_is_kept()
    {
        Claims(new JsonObject
        {
            ["not-promised"] = new JsonArray(new JsonObject { ["id"] = "order", ["source"] = "design/adr/0001-limits.md#1-limits", ["sentence"] = "An ordering of keys is not promised." }),
        });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(0, error);
    }

    [Fact]
    public void A_duplicate_obligation_id_is_refused()
    {
        Claims(new JsonObject
        {
            ["obligations"] = new JsonArray(Obligation("keys", "A key is written as the wrapped type writes it."), Obligation("keys", "A null is refused.")),
        });
        var (exit, _, error) = fx.Ledger("universe", "--base", "HEAD");
        exit.ShouldBe(1);
        error.ShouldContain("claim C1: obligation keys is listed twice");
    }

    [Fact]
    public void A_claim_without_obligations_has_one_the_claim_seeded_by_a_seed_naming_none()
    {
        Register(EnvSeed());
        ObligationsOf(Status()).ShouldBe(["claim: P"]);
        Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Status_shows_the_obligations_no_seed_breaks(string newLine)
    {
        Claims(new JsonObject { ["obligations"] = TwoObligations() });
        Register(null, ("keys", "1"));
        ObligationsOf(Status()).ShouldBe(["keys: P", "nulls: "]);
        var (exit, output, error) = fx.LedgerEnding([], newLine, "status");
        exit.ShouldBe(0, error);
        output.ReplaceLineEndings("\n").ShouldContain("\n  C1: 0/4  obligations 2 (1 without a seed: nulls)\n");

        Register(null, ("keys", "1"), ("nulls", "0"));
        fx.LedgerEnding([], newLine, "status").Out.ReplaceLineEndings("\n").ShouldContain("\n  C1: 0/4  obligations 2, each seeded\n");
    }

    [Fact]
    public void The_check_without_a_build_names_an_obligation_no_seed_breaks()
    {
        Claims(new JsonObject { ["obligations"] = TwoObligations() });
        Register(EnvSeed(), ("keys", "1"));
        Problems().ShouldBe(["C1: obligation nulls has no seed in any of its probes"], "a seed naming no obligation proves the probe, not an obligation the claim lists");
        Register(null, ("keys", "1"), ("nulls", "0"));
        Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_check_without_a_build_names_a_seed_naming_an_obligation_the_claim_does_not_list(bool listed)
    {
        if (listed) Claims(new JsonObject { ["obligations"] = new JsonArray(Obligation("keys", "A key is written as the wrapped type writes it.")) });
        Register(null, ("keys", "1"), ("kyes", "0"));
        Problems().ShouldBe(listed
            ? ["P/kyes: names obligation kyes, which claim C1 does not list"]
            : ["P/keys: names obligation keys, which claim C1 does not list", "P/kyes: names obligation kyes, which claim C1 does not list"]);
    }

    [Fact]
    public void Seeds_runs_each_seed_under_its_obligation()
    {
        Claims(new JsonObject { ["obligations"] = TwoObligations() });
        Register(null, ("keys", "1"), ("nulls", "7"));
        var output = new StringWriter(); var error = new StringWriter();
        var context = new Context(fx.Root, new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache }, output, error);
        var exit = Seeds.Run(context, new Args([], new HashSet<string>(StringComparer.Ordinal) { "dry-run" }), adapter);

        exit.ShouldBe(1, "the nulls seed makes no row violate, so it is missed and fails the command");
        var text = output.ToString().ReplaceLineEndings("\n");
        text.ShouldContain(": 2 seeds of 1 probes, in ");
        text.ShouldContain("\n  P/keys (C1): caught, 1 of 2 rows violate; e.g. #1: violation:row 1 is broken\n");
        text.ShouldContain("\n  P/nulls (C1): missed, no row violates that did not already (2 rows: pass 2;");
        var last = JsonNode.Parse(File.ReadAllText(Path.Combine(fx.BranchDir, "seeds-last.json")))!["seeds"]!.AsArray()
                           .Select(s => (s!["probe"]!.GetValue<string>(), s["obligation"]!.GetValue<string>(), s["seed"]!.GetValue<string>(), s["result"]!.GetValue<string>()));
        last.ShouldBe([("P", "keys", "SEED_ROW=1", "caught"), ("P", "nulls", "SEED_ROW=7", "missed")]);
    }

    /// <summary>The probe P over C1's two rows, without a build: row N violates where SEED_ROW is N.</summary>
    private sealed class FakeAdapter : IProbeAdapter
    {
        public Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment) => new(0, "Build: OK", "");

        public Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment)
        {
            var broken = environment.GetValueOrDefault("SEED_ROW");
            var outDir = environment["PROBE_OUT"]!;
            Directory.CreateDirectory(outDir);
            var lines = Enumerable.Range(0, 2).Select(idx =>
            {
                var violates = broken == idx.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new JsonObject
                {
                    ["idx"] = idx, ["category"] = violates ? "violation" : "pass",
                    ["reasons"] = violates ? new JsonArray($"violation:row {idx} is broken") : new JsonArray(),
                }.ToJsonString();
            }).ToList();
            File.WriteAllText(Path.Combine(outDir, "C1.jsonl"), string.Join('\n', lines) + "\n");
            return new Ran(lines.Any(l => l.Contains("\"violation\"", StringComparison.Ordinal)) ? 1 : 0, "", "");
        }

        public (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory) => (null, "none");

        public IReadOnlyDictionary<string, string?> Environment(string cacheDirectory) => new Dictionary<string, string?>();
    }
}
