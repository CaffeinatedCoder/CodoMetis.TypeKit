using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// A probe proves it can fail (decision 100): `seeds` applies the defect its registry entry names in a scratch
/// worktree, runs the probe there, and counts the seed caught only for a violation the unseeded probe does
/// not have. The probe here is a fake adapter: row 1 of C1 violates while the product holds DEFECT.
/// </summary>
public sealed class SeedsTests : IDisposable
{
    private readonly Fixture fx = new();
    private readonly string cache;
    private readonly FakeAdapter adapter = new();

    public SeedsTests()
    {
        cache = fx.Root + "-cache";
        fx.Sh("git", "config", "core.autocrlf", "false");
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
            ["replay"] = new JsonObject { ["probeDir"] = "probes", ["include"] = new JsonArray("src") },
        }.ToJsonString() + "\n```\n");
        fx.Commit("replay configuration");
    }

    public void Dispose()
    {
        fx.Dispose();
        try { Directory.Delete(cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Writes the seed for P: a patch made by editing the file and taking the diff, and the registry entry naming it.</summary>
    private void Seed(string? file = "src/a.cs", string? edit = "class A { /* DEFECT */ }\n", JsonObject? env = null)
    {
        var seed = new JsonObject { ["defect"] = "A holds the defect." };
        if (file is not null)
        {
            var original = File.ReadAllText(Path.Combine(fx.Root, file));
            fx.Write(file, edit!);
            fx.Write("tools/probe/corpus/seeds/C1.patch", fx.Sh("git", "diff", "--", file));
            fx.Write(file, original);
            seed["patch"] = "seeds/C1.patch";
        }
        if (env is not null) seed["env"] = env;
        fx.Write("tools/probe/corpus/registry.json", new JsonObject
        {
            ["probes"] = new JsonObject { ["P"] = new JsonObject { ["claim"] = "C1", ["filter"] = "*P", ["seed"] = seed } },
        }.ToJsonString());
        fx.Commit("seed");
    }

    private (int Exit, string Out, string Err) Seeds(params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var context = new Context(fx.Root, new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache }, output, error);
        int exit;
        try { exit = global::Prober.Seeds.Run(context, new Args(args, new HashSet<string>(StringComparer.Ordinal) { "dry-run" }), adapter); }
        catch (Refusal refusal) { error.WriteLine(refusal.Message); exit = refusal.ExitCode; }
        return (exit, output.ToString(), error.ToString());
    }

    private string Worktree => Directory.GetDirectories(cache).Single() is var home ? Path.Combine(home, "seeds") : "";

    [Fact]
    public void A_seed_the_probe_catches_passes()
    {
        Seed();
        var (exit, output, error) = Seeds();
        exit.ShouldBe(0, output + error);
        output.ShouldContain("P (C1): caught, 1 of 2 rows violate; e.g. #1: violation:");
        output.ShouldContain("1 caught");
    }

    [Fact]
    public void A_seed_the_probe_misses_fails_the_command()
    {
        Seed(edit: "class A { /* harmless */ }\n");
        var (exit, output, _) = Seeds();
        exit.ShouldBe(1);
        output.ShouldContain("P (C1): missed, no row violates that did not already (2 rows: pass 2;");
    }

    [Fact]
    public void A_violation_the_probe_has_without_the_seed_is_not_the_seeds()
    {
        adapter.AlwaysViolates = 1;
        Seed();
        var (exit, output, _) = Seeds();
        exit.ShouldBe(1, "row 1 fails unseeded too, so the seed proved nothing");
        output.ShouldContain("1 violations unseeded");
        output.ShouldContain("P (C1): missed");
    }

    [Fact]
    public void A_probe_without_a_seed_fails_the_command()
    {
        var (exit, output, _) = Seeds();
        exit.ShouldBe(1);
        output.ShouldContain("P (C1): no seed, its registry entry names none");
    }

    [Fact]
    public void An_environment_toggle_is_a_seed()
    {
        Seed(file: null, env: new JsonObject { ["SEED_DEFECT"] = "1" });
        var (exit, output, error) = Seeds();
        exit.ShouldBe(0, output + error);
        output.ShouldContain("P (C1): caught");
    }

    [Fact]
    public void A_seed_outside_the_product_files_is_refused()
    {
        Seed(file: "README.md", edit: "# a\n\nThe claim, DEFECT.\n");
        var (exit, output, _) = Seeds();
        exit.ShouldBe(1);
        output.ShouldContain("P (C1): does not apply, it patches README.md, outside the product files (src)");
    }

    [Fact]
    public void A_seed_that_no_longer_applies_fails_the_command()
    {
        Seed();
        fx.Write("src/a.cs", "class B {}\n");
        fx.Commit("the product moved on");
        var (exit, output, _) = Seeds();
        exit.ShouldBe(1);
        output.ShouldContain("P (C1): does not apply");
    }

    [Fact]
    public void A_seed_that_breaks_the_build_proves_nothing()
    {
        adapter.BreaksOn = "DEFECT";
        Seed();
        var (exit, output, _) = Seeds();
        exit.ShouldBe(1);
        output.ShouldContain("P (C1): build failed");
    }

    [Fact]
    public void The_seed_is_applied_only_in_the_scratch_worktree_and_undone()
    {
        Seed();
        Seeds().Exit.ShouldBe(0);
        File.ReadAllText(Path.Combine(fx.Root, "src/a.cs")).ShouldBe("class A {}\n");
        File.ReadAllText(Path.Combine(Worktree, "src/a.cs")).ShouldBe("class A {}\n");
        adapter.Worktrees.ShouldAllBe(w => w == Worktree);
    }

    [Fact]
    public void A_seeded_run_is_never_evidence()
    {
        Seed();
        Seeds().Exit.ShouldBe(0);
        File.Exists(Path.Combine(fx.BranchDir, "runs.jsonl")).ShouldBeFalse();
        Directory.Exists(Path.Combine(fx.BranchDir, "evidence")).ShouldBeFalse();
        File.Exists(Path.Combine(fx.BranchDir, "seeds-last.json")).ShouldBeTrue();
    }

    [Fact]
    public void The_seed_is_not_part_of_the_probe_s_identity()
    {
        var before = fx.Ledger("identity", "--probe", "P").Out;
        Seed();
        fx.Ledger("identity", "--probe", "P").Out.ShouldBe(before, "a seed proves the probe; adding or changing one must not stale its results");
    }

    private List<string> Problems() => global::Prober.Seeds.Problems(new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null));

    [Fact]
    public void The_check_without_a_build_names_a_probe_without_a_seed()
    {
        Problems().ShouldBe(["P: no seed, its registry entry names none"]);
        Seed();
        Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_check_without_a_build_names_a_seed_the_product_left_behind()
    {
        Seed();
        fx.Write("src/a.cs", "class B {}\n");
        fx.Commit("the product moved on");
        Problems().ShouldHaveSingleItem().ShouldStartWith("P: does not apply, ");
    }

    [Fact]
    public void Every_probe_in_the_repository_has_a_seed_that_applies()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CodoMetis.TypeKit.slnx"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("no CodoMetis.TypeKit.slnx above the test output");
        global::Prober.Seeds.Problems(new Ledger(new Context(root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null)).ShouldBeEmpty();
    }

    /// <summary>The probe P over C1's two rows, without a build: row 1 violates where src/a.cs holds DEFECT or SEED_DEFECT is set.</summary>
    private sealed class FakeAdapter : IProbeAdapter
    {
        public int? AlwaysViolates { get; set; }
        public string? BreaksOn { get; set; }
        public List<string> Worktrees { get; } = [];

        public Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment)
        {
            Worktrees.Add(worktree);
            return BreaksOn is { } marker && File.ReadAllText(Path.Combine(worktree, "src", "a.cs")).Contains(marker, StringComparison.Ordinal)
                       ? new Ran(1, "Build: FAILED", "")
                       : new Ran(0, "Build: OK", "");
        }

        public Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment)
        {
            Worktrees.Add(worktree);
            var defect = File.ReadAllText(Path.Combine(worktree, "src", "a.cs")).Contains("DEFECT", StringComparison.Ordinal)
                      || environment.GetValueOrDefault("SEED_DEFECT") == "1";
            var outDir = environment["PROBE_OUT"]!;
            Directory.CreateDirectory(outDir);
            var lines = Enumerable.Range(0, 2).Select(idx =>
            {
                var violates = idx == AlwaysViolates || (defect && idx == 1);
                return new JsonObject
                {
                    ["idx"] = idx, ["category"] = violates ? "violation" : "pass",
                    ["reasons"] = violates ? new JsonArray("violation:A holds the defect") : new JsonArray(),
                }.ToJsonString();
            });
            File.WriteAllText(Path.Combine(outDir, "C1.jsonl"), string.Join('\n', lines) + "\n");
            return new Ran(lines.Any(l => l.Contains("violation", StringComparison.Ordinal)) ? 1 : 0, "", "");
        }

        public (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory) => (null, "none");

        public IReadOnlyDictionary<string, string?> Environment(string cacheDirectory) => new Dictionary<string, string?>();
    }
}
