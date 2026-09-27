using System.Text.Json;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>The repository as the convention tests see it: files on disk, found from the solution marker.</summary>
internal static class Repository
{
    private const string SolutionMarker = "CodoMetis.TypeKit.slnx";

    /// <summary>
    /// The repository root, found by walking up to the solution file, never by a positional
    /// <c>../../</c>: moving a project must not silently retarget a test.
    /// </summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Every shipping project, discovered from <c>src/</c> so a new package needs no edit here.</summary>
    public static IReadOnlyList<string> ShippingProjects() =>
    [
        .. Directory.GetDirectories(Path.Combine(Root, "src"))
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(name => File.Exists(Path.Combine(Root, "src", name, $"{name}.csproj")))
                    .Order(StringComparer.Ordinal)
    ];

    /// <summary>
    /// The ids of every package the project resolves, direct or transitive, read from its restore
    /// output. The assets file is what NuGet actually resolved, so a dependency that arrives
    /// transitively is seen as well as a direct one.
    /// </summary>
    public static IReadOnlyList<string> ResolvedPackages(string project)
    {
        var assets = Path.Combine(Root, "src", project, "obj", "project.assets.json");

        if (!File.Exists(assets))
            throw new InvalidOperationException(
                $"{assets} does not exist. Restore the solution first (`dotnet restore {SolutionMarker}`); " +
                "the convention tests read restore output and do not trigger a restore themselves.");

        using var document = JsonDocument.Parse(File.ReadAllText(assets));

        return
        [
            .. document.RootElement.GetProperty("libraries")
                       .EnumerateObject()
                       .Where(library => library.Value.GetProperty("type").GetString() == "package")
                       .Select(library => library.Name.Split('/')[0])
                       .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionMarker))) return directory.FullName;
        }

        throw new InvalidOperationException($"No {SolutionMarker} above {AppContext.BaseDirectory}.");
    }
}
