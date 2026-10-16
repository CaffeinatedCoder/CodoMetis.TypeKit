using System.Text.Json.Nodes;
using Prober;
using Probes.Shapes;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>The harness writes rows to the process's PROBE_OUT, so its tests run one at a time.</summary>
[CollectionDefinition(nameof(ProbeOut), DisableParallelization = true)]
public sealed class ProbeOut;

/// <summary>
/// A harness error is never a catch (decision 103): the probe corpus's own <c>Rec.Run</c> (harness/Row.cs,
/// compiled in) ends a row whose check throws as a harness error, not as a violation, and `seeds`, reading
/// the lines it writes, counts no such row caught, nor a violation whose reasons carry a harness error.
/// </summary>
[Collection(nameof(ProbeOut))]
public sealed class HarnessErrorTests : IDisposable
{
    private readonly Fixture fx = new();
    private readonly string cache;

    public HarnessErrorTests()
    {
        cache = fx.Root + "-cache";
        fx.Sh("git", "config", "core.autocrlf", "false");
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
            ["replay"] = new JsonObject { ["probeDir"] = "probes", ["include"] = new JsonArray("src") },
        }.ToJsonString() + "\n```\n");
        fx.Write("src/a.cs", "class A { /* DEFECT */ }\n");
        fx.Write("tools/probe/corpus/seeds/C1.patch", fx.Sh("git", "diff", "--", "src/a.cs"));
        fx.Write("src/a.cs", "class A {}\n");
        fx.Write("tools/probe/corpus/registry.json", new JsonObject
        {
            ["probes"] = new JsonObject
            {
                ["P"] = new JsonObject { ["claim"] = "C1", ["filter"] = "*P", ["seed"] = new JsonObject { ["defect"] = "A holds the defect.", ["patch"] = "seeds/C1.patch" } },
            },
        }.ToJsonString());
        fx.Commit("seed");
    }

    public void Dispose()
    {
        fx.Dispose();
        try { Directory.Delete(cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Runs one row through the harness's <c>Rec.Run</c>, writing to <paramref name="outDir"/>; the exception it ends the test with, if any.</summary>
    private static Exception? Harness(string outDir, int idx, Func<Row, Task<string?>> check)
    {
        var previous = Environment.GetEnvironmentVariable("PROBE_OUT");
        Environment.SetEnvironmentVariable("PROBE_OUT", outDir);
        try
        {
            Rec.Run("C1", new Row(idx, new Dictionary<string, string> { ["x"] = "1", ["y"] = "1" }), check).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception ended) { return ended; }
        finally { Environment.SetEnvironmentVariable("PROBE_OUT", previous); }
    }

    private static Task<string?> Throws(Row _) => throw new InvalidOperationException("the check broke");

    [Fact]
    public void A_check_that_throws_ends_as_a_harness_error_and_fails_the_test_naming_the_exception()
    {
        var outDir = Path.Combine(fx.Root, "rows-out");
        var ended = Harness(outDir, 1, Throws);
        ended.ShouldNotBeNull().Message.ShouldContain("UNEXPECTED InvalidOperationException: the check broke");

        var line = JsonNode.Parse(File.ReadAllLines(Path.Combine(outDir, "C1.jsonl")).ShouldHaveSingleItem())!;
        Files.Str(line["category"]).ShouldBe("harness-error", "an exception is the harness breaking, never the claim's check firing");
        Files.Strings(line["reasons"]).ShouldBe(["harness-error:UNEXPECTED InvalidOperationException: the check broke"]);
        Files.Str(line["status"]).ShouldBe("fail");
    }

    [Fact]
    public void A_seed_that_only_makes_a_check_throw_is_missed_not_caught()
    {
        var (exit, output) = Seeds(new HarnessAdapter(Throws));
        exit.ShouldBe(1, output);
        output.ShouldContain("P (C1): missed, no row violates that did not already (2 rows: harness-error 1, pass 1; 1 ended in a harness error, which is no catch;");
        output.ShouldNotContain("caught,");
    }

    [Fact]
    public void A_violation_whose_reasons_carry_a_harness_error_is_no_catch()
    {
        var (exit, output) = Seeds(new HarnessAdapter(r =>
        {
            r.Outcome("harness-error", "the check could not tell");
            return Task.FromResult<string?>("and failed anyway");
        }));
        exit.ShouldBe(1, output);
        output.ShouldContain("P (C1): missed, no row violates that did not already (2 rows: pass 1, violation 1; 1 ended in a harness error, which is no catch;");
    }

    [Fact]
    public void A_violation_through_the_harness_is_a_catch()
    {
        var (exit, output) = Seeds(new HarnessAdapter(_ => Task.FromResult<string?>("the claim does not hold")));
        exit.ShouldBe(0, output);
        output.ShouldContain("P (C1): caught, 1 of 2 rows violate; e.g. #1: violation:the claim does not hold");
    }

    private (int Exit, string Out) Seeds(IProbeAdapter adapter)
    {
        var output = new StringWriter();
        var context = new Context(fx.Root, new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache }, output, output);
        int exit;
        try { exit = global::Prober.Seeds.Run(context, new Args([], new HashSet<string>(StringComparer.Ordinal)), adapter); }
        catch (Refusal refusal) { output.WriteLine(refusal.Message); exit = refusal.ExitCode; }
        return (exit, output.ToString());
    }

    /// <summary>
    /// The probe P over C1's two rows, each run through the real harness: row 0 passes, and row 1 runs
    /// <paramref name="seeded"/> while src/a.cs holds DEFECT.
    /// </summary>
    private sealed class HarnessAdapter(Func<Row, Task<string?>> seeded) : IProbeAdapter
    {
        public Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment) => new(0, "Build: OK", "");

        public Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment)
        {
            var defect = File.ReadAllText(Path.Combine(worktree, "src", "a.cs")).Contains("DEFECT", StringComparison.Ordinal);
            var outDir = environment["PROBE_OUT"]!;
            var failed = false;
            for (var idx = 0; idx < 2; idx++)
                failed |= Harness(outDir, idx, idx == 1 && defect ? seeded : _ => Task.FromResult<string?>(null)) is not null;
            return new Ran(failed ? 1 : 0, "", "");
        }

        public (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory) => (null, "none");

        public IReadOnlyDictionary<string, string?> Environment(string cacheDirectory) => new Dictionary<string, string?>();
    }
}
