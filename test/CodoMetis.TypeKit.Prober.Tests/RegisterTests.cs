using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// `register --obligation O` writes a seed per obligation (decision 104) into the entry's `seeds`, in place of
/// any seed that names O already, and refuses an O the claim does not list; `new` prints that command for each
/// listed obligation, so no seed is written into the registry by hand. C1 lists keys and nulls; C7 lists none.
/// </summary>
public sealed class RegisterTests : IDisposable
{
    private readonly Fixture fx = new();

    public RegisterTests()
    {
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
                    new JsonObject { ["id"] = "keys", ["sentence"] = "The claim." },
                    new JsonObject { ["id"] = "nulls", ["sentence"] = "The claim." }),
            },
            new JsonObject { ["id"] = "C7", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s") }).ToJsonString());
        fx.Commit("claims with obligations");
        fx.Ledger("universe", "--base", "HEAD").Exit.ShouldBe(0);
    }

    public void Dispose() => fx.Dispose();

    private string StagedRegistry => Path.Combine(fx.BranchDir, "staging", "registry.json");

    private JsonObject Entry(string probe = "P") => JsonNode.Parse(File.ReadAllText(StagedRegistry))!["probes"]![probe]!.AsObject();

    private (int Exit, string Out, string Err) Register(params string[] more) =>
        fx.Ledger(["register", "--probe", "P", "--claim", "C1", "--filter", "*P", .. more]);

    private static string Seeds(JsonObject entry) =>
        string.Join("; ", entry["seeds"]!.AsArray().Select(s => $"{s!["obligation"]}={s["patch"]}:{s["defect"]}"));

    [Fact]
    public void An_obligation_s_seed_goes_into_seeds()
    {
        var (exit, output, error) = Register("--obligation", "keys", "--seed", "seeds/C1-keys.patch", "--defect", "A key is written with ToString().");
        exit.ShouldBe(0, output + error);
        var entry = Entry();
        entry["seeds"]!.ToJsonString().ShouldBe("""[{"obligation":"keys","patch":"seeds/C1-keys.patch","defect":"A key is written with ToString()."}]""");
        entry.ContainsKey("seed").ShouldBeFalse("a seed naming an obligation is not the probe's one `seed`");
        output.ShouldContain("seed for obligation keys of P");
    }

    [Fact]
    public void A_seed_for_an_obligation_that_has_one_replaces_it()
    {
        Register("--obligation", "keys", "--seed", "seeds/C1-keys.patch", "--defect", "first").Exit.ShouldBe(0);
        Register("--obligation", "nulls", "--seed", "seeds/C1-nulls.patch", "--defect", "nulls").Exit.ShouldBe(0);
        var (exit, output, _) = Register("--obligation", "keys", "--seed", "seeds/C1-keys-2.patch", "--defect", "second");
        exit.ShouldBe(0);
        output.ShouldContain("seed for obligation keys of P, in place of the 1 it had");
        Seeds(Entry()).ShouldBe("nulls=seeds/C1-nulls.patch:nulls; keys=seeds/C1-keys-2.patch:second");
    }

    [Fact]
    public void The_one_seed_naming_the_obligation_is_replaced_and_one_naming_another_kept()
    {
        fx.Write("tools/probe/corpus/registry.json", """
            {"probes": {
              "P": {"claim": "C1", "filter": "*P", "seed": {"patch": "seeds/C1.patch", "defect": "old", "obligation": "keys"}},
              "Q": {"claim": "C1", "filter": "*Q", "seed": {"patch": "seeds/C1-q.patch", "defect": "kept", "obligation": "nulls"}}}}
            """);
        fx.Commit("probes whose one seed names an obligation");

        Register("--obligation", "keys", "--seed", "seeds/C1-keys.patch", "--defect", "new").Exit.ShouldBe(0);
        Entry().ContainsKey("seed").ShouldBeFalse("the seed that named keys is replaced, not kept beside the new one");
        Seeds(Entry()).ShouldBe("keys=seeds/C1-keys.patch:new");

        fx.Ledger("register", "--probe", "Q", "--claim", "C1", "--filter", "*Q", "--obligation", "keys", "--seed", "seeds/C1-qk.patch", "--defect", "q").Exit.ShouldBe(0);
        Entry("Q")["seed"]!["obligation"]!.GetValue<string>().ShouldBe("nulls");
        Seeds(Entry("Q")).ShouldBe("keys=seeds/C1-qk.patch:q");
    }

    [Theory]
    [InlineData("C1", "kyes", "claim C1 lists no obligation kyes (it lists keys, nulls)")]
    [InlineData("C1", "claim", "claim C1 lists no obligation claim (it lists keys, nulls)")]
    [InlineData("C7", "keys", "claim C7 lists no obligation keys (it lists claim)")]
    public void An_obligation_the_claim_does_not_list_is_refused_and_nothing_staged(string claim, string obligation, string message)
    {
        var (exit, _, error) = fx.Ledger("register", "--probe", "P", "--claim", claim, "--filter", "*P", "--obligation", obligation, "--seed", "seeds/x.patch", "--defect", "d");
        exit.ShouldBe(2);
        error.ShouldContain(message);
        File.Exists(StagedRegistry).ShouldBeFalse();
    }

    [Fact]
    public void A_claim_without_obligations_takes_a_seed_for_its_one()
    {
        fx.Ledger("register", "--probe", "P", "--claim", "C7", "--filter", "*P", "--obligation", "claim", "--seed", "seeds/C7.patch", "--defect", "d").Exit.ShouldBe(0);
        Seeds(Entry()).ShouldBe("claim=seeds/C7.patch:d");
    }

    [Fact]
    public void A_claim_that_is_not_in_the_claims_is_refused()
    {
        var (exit, _, error) = fx.Ledger("register", "--probe", "P", "--claim", "C9", "--filter", "*P", "--obligation", "keys", "--seed", "seeds/x.patch", "--defect", "d");
        exit.ShouldBe(1);
        error.ShouldContain("no claim C9 in claims.json");
        File.Exists(StagedRegistry).ShouldBeFalse();
    }

    [Fact]
    public void An_obligation_without_a_seed_or_a_defect_is_refused()
    {
        var (exit, _, error) = Register("--obligation", "keys", "--defect", "d");
        exit.ShouldBe(2);
        error.ShouldContain("give the seed with --seed PATCH or --seed-env NAME=VALUE");
        Register("--obligation", "keys", "--seed", "seeds/C1-keys.patch").Exit.ShouldBe(2, "a seed without its defect");
        File.Exists(StagedRegistry).ShouldBeFalse();
    }

    [Fact]
    public void An_environment_toggle_is_a_seed_too()
    {
        Register("--obligation", "nulls", "--seed-env", "A=1", "B=x=y", "--defect", "d").Exit.ShouldBe(0);
        Entry()["seeds"]!.ToJsonString().ShouldBe("""[{"obligation":"nulls","env":{"A":"1","B":"x=y"},"defect":"d"}]""");
        Register("--seed-env", "C=2", "--defect", "one").Exit.ShouldBe(0);
        Entry()["seed"]!.ToJsonString().ShouldBe("""{"env":{"C":"2"},"defect":"one"}""");
        Register("--obligation", "nulls", "--seed-env", "=1", "--defect", "d").Exit.ShouldBe(2, "a toggle without a name");
    }

    [Fact]
    public void A_seed_stales_no_result()
    {
        var before = new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null).Identity("P");
        Register("--obligation", "keys", "--seed", "seeds/C1-keys.patch", "--defect", "d").Exit.ShouldBe(0);
        Register("--seed", "seeds/C1.patch", "--defect", "d").Exit.ShouldBe(0);
        Entry().ContainsKey("seeds").ShouldBeTrue();
        new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null).Identity("P").ShouldBe(before);
    }

    [Fact]
    public void The_commands_new_prints_register_each_obligation_s_seed()
    {
        var (exit, output, _) = fx.Ledger("new", "--claim", "C1", "--probe", "C1KeysProbe");
        exit.ShouldBe(0);
        var commands = Regex.Matches(output, @"`prober (register [^`]*)`").Select(m => m.Groups[1].Value).ToList();
        commands.Count.ShouldBe(2, output);
        output.ShouldNotContain("in the entry's `seeds`");
        foreach (var command in commands)
        {
            var args = Regex.Matches(command, @"""([^""]*)""|(\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                            .Select(arg => arg == "..." ? "What the seed breaks." : arg).ToArray();
            var (registered, said, error) = fx.Ledger(args);
            registered.ShouldBe(0, command + ": " + said + error);
        }

        var entry = Entry("C1KeysProbe");
        entry["filter"]!.GetValue<string>().ShouldBe("*C1KeysProbe");
        Seeds(entry).ShouldBe("keys=seeds/C1-keys.patch:What the seed breaks.; nulls=seeds/C1-nulls.patch:What the seed breaks.");
        var problems = global::Prober.Seeds.Problems(new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null));
        problems.ShouldNotContain(p => p.StartsWith("C1KeysProbe: no seed", StringComparison.Ordinal));
        problems.ShouldNotContain(p => p.StartsWith("C1: obligation", StringComparison.Ordinal));
    }
}
