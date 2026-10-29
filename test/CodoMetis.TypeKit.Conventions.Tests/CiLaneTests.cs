using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The selection of <c>tools/CiLanes</c> never lets a change to a package skip the pack: every file MSBuild evaluates
/// for a shipping project or the consumer smoke test, items and imports both, is classified into a class that
/// selects the <c>pack</c> lane (design/adr/0113, #70).
/// </summary>
/// <remarks>
/// Read from MSBuild's evaluation, not from a list: before decision 113 the changes job listed its paths by hand,
/// and missed <c>tools/Shared/*.cs</c>, which <c>tools/Directory.Build.props</c> compiles into the smoke test. The
/// imports are read from the preprocessed project (<c>-pp</c>), since <c>MSBuildAllProjects</c> lists only the
/// files that append themselves to it. A file a target reads by a property function is in no evaluation: the one
/// such file, the changelog the release notes come from, is held by name.
/// </remarks>
public sealed class CiLaneTests
{
    /// <summary>The shipping projects, with the ones they reference, and the smoke test, which packs them.</summary>
    public static TheoryData<string> Projects =>
    [
        .. Repository.ShippingProjects().Select(project => $"src/{project}/{project}.csproj"),
        "tools/ConsumerSmokeTest/ConsumerSmokeTest.csproj",
    ];

    [Theory, MemberData(nameof(Projects))]
    public async Task Every_file_a_package_or_the_smoke_test_reads_selects_the_pack(string project)
    {
        var inputs = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([Path.GetFullPath(Path.Combine(Repository.Root, project))]);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (pending.TryDequeue(out var path))
        {
            if (!seen.Add(path)) continue;
            inputs.Add(path);

            foreach (var import in await Imports(path)) inputs.Add(import);
            foreach (var (item, full) in await Items(path))
            {
                inputs.Add(full);
                if (item == "ProjectReference") pending.Enqueue(full);
            }
        }

        var relative = inputs.Where(InRepository).Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')).ToList();
        relative.ShouldContain(path => path.StartsWith("tools/Directory.Build.props", StringComparison.Ordinal) || path.StartsWith("src/", StringComparison.Ordinal),
            "the evaluation found none of the project's own inputs, so the discovery is broken");

        var unpacked = Classify(relative).Where(row => !row.Lanes.Contains("pack", StringComparer.Ordinal)).ToList();

        unpacked.ShouldBeEmpty(
            $"{project} reads these files, and a pull request changing one alone would skip the pack: " +
            string.Join(", ", unpacked.Select(row => $"{row.Path} ({row.Class})")) +
            ". Give them a class that selects pack in tools/CiLanes (decision 113).");
    }

    /// <summary>
    /// src/Directory.Build.targets reads the changelog into the release notes with a property function inside a
    /// target, which no evaluation lists (#75). Held by name, beside the evidence that the target still reads it.
    /// </summary>
    [Fact]
    public void The_changelog_the_release_notes_come_from_selects_the_pack()
    {
        File.ReadAllText(Path.Combine(Repository.Root, "src", "Directory.Build.targets"))
            .ShouldContain("'CodoMetis.TypeKit.slnx'))/CHANGELOG.md", Case.Sensitive,
                "src/Directory.Build.targets no longer reads CHANGELOG.md at the root; find what it reads and hold that here");

        Classify(["CHANGELOG.md"]).Single().Lanes.ShouldContain("pack");
    }

    private static bool InRepository(string path) =>
        path.StartsWith(Repository.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !Path.GetRelativePath(Repository.Root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static List<(string Path, string Class, string[] Lanes)> Classify(IReadOnlyList<string> paths)
    {
        var output = typeof(CiLaneTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "CiLanesOutput").Value!;
        var (code, text) = Run(Path.Combine(output, "ci-lanes.dll"), ["classify", .. paths]);

        code.ShouldBe(0, text);
        return
        [
            .. text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                   .Select(line => line.Split('\t'))
                   .Select(parts => (parts[0], parts[1], parts[2].Split(", ")))
        ];
    }

    // The project and every file it imports, from the preprocessed project: each import is named there by its full path, on a line of its own.
    private static async Task<IEnumerable<string>> Imports(string project)
    {
        var file = Path.Combine(Path.GetTempPath(), $"cilanes-{Guid.NewGuid():N}.xml");
        try
        {
            var (code, text) = await RunAsync(["msbuild", project, $"-pp:{file}", "-nodeReuse:false"]);
            code.ShouldBe(0, $"dotnet msbuild -pp {project} failed: {text}");
            return [.. File.ReadAllLines(file).Select(line => line.Trim()).Where(line => Path.IsPathFullyQualified(line) && File.Exists(line))];
        }
        finally
        {
            File.Delete(file);
        }
    }

    // Every item that names a file: what is compiled, embedded, packed or analysed, and the projects referenced.
    private static async Task<IEnumerable<(string Item, string FullPath)>> Items(string project)
    {
        string[] kinds = ["Compile", "None", "Content", "EmbeddedResource", "AdditionalFiles", "ProjectReference"];
        var (code, text) = await RunAsync(["msbuild", project, .. kinds.Select(kind => $"-getItem:{kind}"), "-nodeReuse:false"]);
        code.ShouldBe(0, $"dotnet msbuild {project} failed: {text}");

        using var evaluation = JsonDocument.Parse(text);
        return
        [
            .. evaluation.RootElement.GetProperty("Items").EnumerateObject()
                         .SelectMany(kind => kind.Value.EnumerateArray().Select(item => (kind.Name, item.GetProperty("FullPath").GetString()!)))
                         .Where(item => File.Exists(item.Item2))
        ];
    }

    private static Task<(int Code, string Output)> RunAsync(string[] arguments) => Task.Run(() => Run(null, arguments));

    // dotnet with the arguments, or the dll with them when one is given; stdout, and stderr after it on a failure.
    private static (int Code, string Output) Run(string? dll, string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", dll is null ? arguments : [dll, .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        })!;

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, process.ExitCode == 0 ? output : output + errors.Result);
    }
}
