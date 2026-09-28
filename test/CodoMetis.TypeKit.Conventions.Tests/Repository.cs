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
        using var document = Assets(project);

        return
        [
            .. document.RootElement.GetProperty("libraries")
                       .EnumerateObject()
                       .Where(library => library.Value.GetProperty("type").GetString() == "package")
                       .Select(library => library.Name.Split('/')[0])
                       .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// The packages the project references directly and hands to no consumer, read from its restore
    /// output: those restore records as <c>suppressParent: All</c>, for which pack writes no
    /// dependency. That covers a <c>PrivateAssets="all"</c> written in the project file and a
    /// reference the SDK adds on its own (<c>AutoReferenced</c>), such as the ILLink tasks
    /// <c>IsAotCompatible</c> brings, which no project file names.
    /// </summary>
    public static IReadOnlyList<(string Id, bool AutoReferenced)> BuildOnlyPackages(string project)
    {
        using var document = Assets(project);

        var packages = new List<(string Id, bool AutoReferenced)>();

        foreach (var framework in document.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("dependencies", out var dependencies)) continue;

            foreach (var dependency in dependencies.EnumerateObject())
            {
                var suppressed = dependency.Value.TryGetProperty("suppressParent", out var suppressParent)
                              && string.Equals(suppressParent.GetString()?.Trim(), "All", StringComparison.OrdinalIgnoreCase);
                if (!suppressed) continue;

                var autoReferenced = dependency.Value.TryGetProperty("autoReferenced", out var flag) && flag.ValueKind == JsonValueKind.True;
                packages.Add((dependency.Name, autoReferenced));
            }
        }

        return packages;
    }

    private static JsonDocument Assets(string project)
    {
        var assets = Path.Combine(Root, "src", project, "obj", "project.assets.json");

        if (!File.Exists(assets))
            throw new InvalidOperationException(
                $"{assets} does not exist. Restore the solution first (`dotnet restore {SolutionMarker}`); " +
                "the convention tests read restore output and do not trigger a restore themselves.");

        return JsonDocument.Parse(File.ReadAllText(assets));
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
