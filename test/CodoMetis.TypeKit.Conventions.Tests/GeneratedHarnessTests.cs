using System.Diagnostics;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every generated file of the probe harness (<c>tools/probe/corpus/harness/*.g.cs</c>) is what its generator writes
/// from the tracked inputs today: the generator is run into a temporary directory and its output compared with the
/// committed file.
/// </summary>
/// <remarks>
/// The generators are file-based apps under <c>tools/probe/generators/</c> (design/adr/0092, form 1), which have no
/// tests of their own; this is their guard. Without it a hand edit of a <c>.g.cs</c>, or a rows file changed by
/// <c>prober rows</c> without regenerating the declarations, would leave the harness built from something nothing can
/// reproduce, as the Python originals did when they left the repository. The comparison ignores line endings only,
/// since a Windows checkout may write the committed file with CRLF and the generators write LF, as git stores it.
/// </remarks>
public sealed class GeneratedHarnessTests
{
    private const string Harness = "tools/probe/corpus/harness";

    private const string Rows = "tools/probe/corpus/rows";

    private const string Axes = ".claude/probe-axes.md";

    /// <summary>
    /// The one place the mapping lives: each generated file, the app that writes it and its arguments, the paths from
    /// the repository root. The test adds <c>--out</c>. The EF catalogue is E01's and E02's first round of rows (the
    /// rows appended since run on EfShapes3's and EfShapes4's complete grids), so it reads only those.
    /// </summary>
    private static readonly (string File, string App, string[] Arguments)[] Generators =
    [
        ("Shapes.g.cs", "shapes.cs", ["--axes", Axes, "--l06", $"{Rows}/L06OffTheWireProbe.json.gz"]),
        ("Containers.g.cs", "containers.cs", ["--axes", Axes]),
        ("EfShapes.g.cs", "ef.cs", ["catalogue", .. EfCatalogue]),
        ("EfShapes3.g.cs", "ef.cs", ["top-level", .. EfCatalogue]),
        ("EfShapes4.g.cs", "ef.cs", ["nested", .. EfCatalogue]),
        ("KeyShapes.g.cs", "ef.cs", ["keys", "--rows", $"{Rows}/E04IntegerKeyProbe.json.gz"]),
        ("JsonFamilyShapes.g.cs", "json-family.cs", ["stand-ins"]),
        ("L05Shapes.g.cs", "json-family.cs", ["l05", "--rows", $"{Rows}/L05EqualityProbe.json.gz"]),
        ("C09Shapes.g.cs", "decls.cs", ["C09", "--rows", $"{Rows}/C09BaseRecordEqualityProbe.json.gz"]),
        ("C11Shapes.g.cs", "decls.cs", ["C11", "--rows", $"{Rows}/C11ParseShapesBuildProbe.json.gz"]),
        ("FaultClaimControllers.g.cs", "faults.cs", []),
        ("RuleControllers.g.cs", "rules.cs", []),
    ];

    private static string[] EfCatalogue =>
    [
        "--rows", $"{Rows}/E01ColumnParityProbe.json.gz", "--first", "90",
        "--rows", $"{Rows}/E02ValueTranslatesProbe.json.gz", "--first", "108",
    ];

    public static TheoryData<string> Files => [.. Generators.Select(generator => generator.File)];

    [Fact]
    public void Every_generated_file_of_the_harness_has_its_generator()
    {
        var committed = Directory.GetFiles(Path.Combine(Repository.Root, Harness), "*.g.cs")
                                 .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();

        committed.Count.ShouldBeGreaterThanOrEqualTo(12, $"{Harness} holds {committed.Count} generated files; it held twelve when this test was written.");
        committed.ShouldBe(
            Generators.Select(generator => generator.File).Order(StringComparer.Ordinal).ToList(), ignoreOrder: false,
            $"Every {Harness}/*.g.cs is written by an app of tools/probe/generators, listed in {nameof(Generators)} with its arguments, and every listed file exists.");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task The_committed_file_is_its_generators_output(string file)
    {
        var (_, app, arguments) = Generators.Single(generator => generator.File == file);
        var directory = Directory.CreateTempSubdirectory("generated-harness-");
        try
        {
            var output = Path.Combine(directory.FullName, file);
            string[] command = ["run", $"tools/probe/generators/{app}", "--", .. arguments, "--out", output];
            using var process = Process.Start(new ProcessStartInfo("dotnet", command)
            {
                WorkingDirectory       = Repository.Root,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            })!;

            var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            var invocation = $"dotnet {string.Join(' ', command[..^1])} {Harness}/{file}";
            process.ExitCode.ShouldBe(0, $"`{invocation}` failed:{Environment.NewLine}{await standardOutput}{await errors}");

            var expected = (await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
            var actual = (await File.ReadAllTextAsync(Path.Combine(Repository.Root, Harness, file), TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
            (actual == expected).ShouldBeTrue(
                $"{Harness}/{file} is not what {app} writes from the tracked inputs ({FirstDifference(actual, expected)}). " +
                $"Regenerate it with `{invocation}` and commit the result; a hand edit belongs in the generator.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string FirstDifference(string committed, string generated)
    {
        var (a, b) = (committed.Split('\n'), generated.Split('\n'));
        for (var line = 0; line < Math.Min(a.Length, b.Length); line++)
        {
            if (a[line] != b[line]) return $"first difference at line {line + 1}: committed `{Cut(a[line])}`, generated `{Cut(b[line])}`";
        }
        return $"the committed file has {a.Length} lines, the generated one {b.Length}";
    }

    private static string Cut(string line) => line.Length > 160 ? line[..160] + "..." : line;
}
