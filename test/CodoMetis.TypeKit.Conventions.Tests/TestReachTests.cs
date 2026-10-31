using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// A change to tools or tests runs only the test projects it reaches (design/adr/0127): <c>ci-lanes</c> reads each
/// project file, and a project reaches what its items and imports name and what its references reach. So every input a
/// test project's build reads, and every project its sources read by path, must be one ci-lanes counts, or the test that
/// reads it silently stops running for the change that breaks it.
/// </summary>
/// <remarks>
/// The build's inputs are read from MSBuild's evaluation, as <see cref="CiLaneTests"/> reads the packages', not from the
/// project file ci-lanes reads, so a reference it cannot parse is found. A read at run time is found in the sources, by
/// the string literals that name another project: <c>"tools/BashGuard/Program.cs"</c>, or its directory name in a file
/// that also names <c>"tools"</c>, as <c>Path.Combine(root, "tools", "BashGuard")</c> does, and a bare <c>"test"</c> or
/// <c>"tools"</c> that ends a <c>Path.Combine</c> in a file that enumerates a directory, a read of every project under it. A project declares a read
/// with <c>&lt;ReadsByPath Include="../" /&gt;</c>; a literal that names a project without reading it is waived below,
/// with the reason.
/// </remarks>
public sealed partial class TestReachTests
{
    /// <summary>Files whose project-shaped literals are not reads, each with the reason.</summary>
    private static readonly Dictionary<string, string> Waived = new(StringComparer.Ordinal)
    {
        ["test/CodoMetis.TypeKit.BuildVerdict.Tests/ToolSandbox.cs"] =
            "copies the tool it is given, which each project that compiles this file in references, and tools/Shared, which runs the whole solution",
        ["test/CodoMetis.TypeKit.CiLanes.Tests/SelectionTests.cs"] = "paths handed to the classifier and written into a throwaway repository",
        ["test/CodoMetis.TypeKit.CiLanes.Tests/GateTests.cs"] = "project files the gate judges, never read",
        ["test/CodoMetis.TypeKit.DocsQuery.Tests/FactTests.cs"] = "a fact's test written into a throwaway repository",
        ["test/CodoMetis.TypeKit.DocsQuery.Tests/StoreTests.cs"] = "paths the store's fingerprint is asked about, in a throwaway repository",
        ["test/CodoMetis.TypeKit.DocsQuery.Tests/TempRepo.cs"] = "a sample's source written into a throwaway repository",
    };

    private const string Conventions = "test/CodoMetis.TypeKit.Conventions.Tests/CodoMetis.TypeKit.Conventions.Tests.csproj";

    /// <summary>The projects a change can narrow the suite to: each directory under tools/ and test/ that holds a project file.</summary>
    private static IReadOnlyList<string> Projects() =>
    [
        .. new[] { "tools", "test" }
           .SelectMany(parent => Directory.GetDirectories(Path.Combine(Repository.Root, parent))
                                          .SelectMany(directory => Directory.GetFiles(directory, "*.csproj")))
           .Select(Relative)
           .Order(StringComparer.Ordinal)
    ];

    private static IReadOnlyList<string> TestProjects() => [.. Projects().Where(project => project.StartsWith("test/", StringComparison.Ordinal))];

    [Fact]
    public async Task Every_file_a_test_project_builds_from_reaches_it()
    {
        var projects = Projects().ToHashSet(StringComparer.Ordinal);
        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        using var throttle = new SemaphoreSlim(Environment.ProcessorCount);

        async Task<HashSet<string>> Evaluate(string project)
        {
            await throttle.WaitAsync();
            try
            {
                var full = Path.Combine(Repository.Root, project);
                var imports = await CiLaneTests.Imports(full);
                var items = (await CiLaneTests.Items(full)).ToList();
                return [.. imports.Concat(items.Select(item => item.FullPath)).Where(InRepository).Select(Relative)];
            }
            finally
            {
                throttle.Release();
            }
        }

        var evaluated = await Task.WhenAll(projects.Select(async project => (project, await Evaluate(project))));
        foreach (var (project, files) in evaluated) inputs[project] = files;

        // A test project builds from its own inputs and those of every project under tools/ or test/ it references.
        var builds = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var project in TestProjects())
        {
            var files = new SortedSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>([project]);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out var next))
            {
                if (!seen.Add(next)) continue;
                foreach (var file in inputs[next])
                {
                    if (!file.StartsWith(DirectoryOf(project) + "/", StringComparison.Ordinal)) files.Add(file);
                    if (projects.Contains(file)) pending.Enqueue(file);
                }
            }
            builds[project] = files;
        }

        builds.Values.SelectMany(files => files).ShouldContain(file => file.StartsWith("test/Directory.Build.props", StringComparison.Ordinal),
            "the evaluation found no test project's imports, so the discovery is broken");
        builds["test/CodoMetis.TypeKit.CiLanes.Tests/CodoMetis.TypeKit.CiLanes.Tests.csproj"].ShouldContain("test/CodoMetis.TypeKit.BuildVerdict.Tests/ToolSandbox.cs",
            "the evaluation no longer finds the file the tools' tests compile in, so the discovery is broken");

        var suites = Suites(builds.Values.SelectMany(files => files).Distinct(StringComparer.Ordinal));
        var unreached = builds.SelectMany(build => build.Value.Where(file => !Runs(suites[file], build.Key)).Select(file => $"{build.Key} builds from {file}")).ToList();

        unreached.ShouldBeEmpty(
            "A change to these files alone would not run the test project that builds from them. tools/CiLanes reads a project's " +
            "items and imports from its project file (Reach in Program.cs): name the file there by a relative path, not through a property.");
    }

    [Fact]
    public void No_test_source_reads_a_project_it_does_not_reach()
    {
        foreach (var file in Waived.Keys)
            File.Exists(Path.Combine(Repository.Root, file)).ShouldBeTrue($"TestReachTests.Waived names {file}, which does not exist.");

        var projects = Projects();
        var reads = new List<(string Reader, string File, string Project, string Literal)>();

        foreach (var reader in TestProjects())
        {
            foreach (var file in SourceFiles(Path.Combine(Repository.Root, DirectoryOf(reader))))
            {
                var path = Relative(file);
                if (Waived.ContainsKey(path)) continue;
                foreach (var (project, literal) in Read(File.ReadAllText(file), projects).Where(read => DirectoryOf(read.Project) != DirectoryOf(reader)))
                    reads.Add((reader, path, project, literal));
            }
        }

        var suites = Suites(reads.Select(read => read.Project).Distinct(StringComparer.Ordinal));
        var undeclared = reads.Where(read => !Runs(suites[read.Project], read.Reader)).Select(read => $"{read.File} reads {DirectoryOf(read.Project)} ({read.Literal})").ToList();

        undeclared.ShouldBeEmpty(
            "These test sources read a project by path that a change to it would not run them for. Declare the read in the reader's project file " +
            "(<ReadsByPath Include=\"../../tools/Name/\" />, or a ProjectReference), or waive the file in TestReachTests.Waived with the reason it reads nothing.");
    }

    /// <summary>The conventions read every file of the repository, so every narrowed suite runs them.</summary>
    [Fact]
    public void Every_narrowed_suite_runs_the_conventions()
    {
        var suites = Suites([.. Projects(), ".gitignore"]);

        suites.Values.ShouldContain(suite => suite.Kind == "reach", "no project narrows the suite, so this test checks nothing");
        suites.Where(suite => suite.Value.Kind == "reach" && !suite.Value.Projects.Contains(Conventions))
              .Select(suite => suite.Key)
              .ShouldBeEmpty("A change to these runs a narrowed suite without the conventions: keep <ReadsByPath Include=\"../../\" /> in their project file.");
    }

    [Theory]
    [InlineData("""File.ReadAllText(Path.Combine(root, "tools", "BashGuard", "Program.cs"));""", "tools/BashGuard/BashGuard.csproj")]
    [InlineData("""var source = "tools/PrHygiene/Program.cs";""", "tools/PrHygiene/PrHygiene.csproj")]
    [InlineData("""Path.Combine(root, "test", "CodoMetis.TypeKit.Facts", "FactFixture.cs")""", "test/CodoMetis.TypeKit.Facts/CodoMetis.TypeKit.Facts.csproj")]
    [InlineData("""Directory.EnumerateFiles(Path.Combine(root, "test"), "*.cs", SearchOption.AllDirectories)""", "test/CodoMetis.TypeKit.Tests/CodoMetis.TypeKit.Tests.csproj")]
    [InlineData("""new ToolSandbox("BashGuard", "bash-guard");""", null)]
    [InlineData("""Run("test", "--project", "test/A.Tests"); Directory.EnumerateFiles(work);""", null)]
    [InlineData("""Path.Combine(root, "test", "CodoMetis.TypeKit.Facts"); Directory.EnumerateFiles(AppContext.BaseDirectory);""", "test/CodoMetis.TypeKit.Facts/CodoMetis.TypeKit.Facts.csproj")]
    [InlineData("""HeaderValue(header, "Oracles")""", null)]
    public void A_literal_that_names_a_project_is_found(string source, string? project)
    {
        var read = Read(source, Projects()).Select(read => read.Project).ToList();

        if (project is null) read.ShouldBeEmpty();
        else read.ShouldContain(project);
    }

    // Each project the source reads by path, with the literal that says so.
    private static IEnumerable<(string Project, string Literal)> Read(string source, IReadOnlyList<string> projects)
    {
        var literals = Literal().Matches(source).Select(match => match.Groups["text"].Value).ToHashSet(StringComparer.Ordinal);
        var scanned = Enumeration().IsMatch(source) ? ScannedDirectory().Matches(source).Select(match => match.Groups["parent"].Value).ToHashSet(StringComparer.Ordinal) : [];

        foreach (var project in projects)
        {
            var directory = DirectoryOf(project);
            var parent = directory[..directory.IndexOf('/')];
            var name = directory[(parent.Length + 1)..];

            var named = literals.FirstOrDefault(literal => literal == directory || literal.StartsWith(directory + "/", StringComparison.Ordinal))
                     ?? (literals.Contains(name) && literals.Contains(parent) ? name : null)
                     ?? (scanned.Contains(parent) ? parent : null);
            if (named is not null) yield return (project, $"\"{named}\"");
        }
    }

    private static Dictionary<string, (string Kind, string[] Projects)> Suites(IEnumerable<string> paths)
    {
        var suites = new Dictionary<string, (string Kind, string[] Projects)>(StringComparer.Ordinal);
        foreach (var chunk in paths.Chunk(100))
        {
            var (code, text) = CiLaneTests.Run(CiLaneTests.CiLanes, ["suite", .. chunk]);
            code.ShouldBe(0, text);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                suites[parts[0]] = (parts[1], parts[2].Split(", ", StringSplitOptions.RemoveEmptyEntries));
            }
        }
        return suites;
    }

    private static bool Runs((string Kind, string[] Projects) suite, string project) =>
        suite.Kind == "full" || suite.Projects.Contains(project) || (suite.Kind == "prose" && ProseProjects.Contains(project));

    // The prose suite's projects, test/Prose.slnf's; ProseReaderTests holds which they are.
    private static readonly HashSet<string> ProseProjects =
    [
        .. System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "test", "Prose.slnf")))
              .RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray().Select(project => project.GetString()!.Replace('\\', '/'))
    ];

    private static bool InRepository(string path) =>
        path.StartsWith(Repository.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !Path.GetRelativePath(Repository.Root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static string Relative(string path) => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/');

    private static string DirectoryOf(string project) => project[..project.LastIndexOf('/')];

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
              .Where(file => !Path.GetRelativePath(project, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    // A regular string literal's text; a verbatim or raw one is matched as far as its first quote, which is enough for a path.
    [GeneratedRegex(@"""(?<text>(?:[^""\\\r\n]|\\.)*)""")]
    private static partial Regex Literal();

    [GeneratedRegex(@"\b(?:Enumerate(?:Files|Directories|FileSystemEntries)|Get(?:Files|Directories|FileSystemEntries))\s*\(")]
    private static partial Regex Enumeration();

    // A path that ends at tools/ or test/ itself, the directory a scan of every project under it starts from.
    [GeneratedRegex(@"Path\.Combine\([^()""]*""(?<parent>tools|test)""\s*\)")]
    private static partial Regex ScannedDirectory();
}
