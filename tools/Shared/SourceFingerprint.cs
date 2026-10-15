using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Whether a tool still matches the sources it was built from. tools/Directory.Build.targets writes each
/// source's path and SHA-256 into <see cref="ToolStamp.Sources"/>; this hashes the checkout's files the same
/// way and names what differs: a source changed, added or removed. A stale tool says so and stops, with the
/// command that rebuilds it (exit 3). It never rebuilds itself: a build it started would race another
/// call of the same tool, and nobody asked for it (design/adr/0092).
/// </summary>
internal static class SourceFingerprint
{
    /// <summary>The exit code of a tool that refuses to run stale.</summary>
    public const int StaleExitCode = 3;

    /// <summary>The tool's project directory, relative to the repository root.</summary>
    public static string Project => ToolStamp.Project;

    /// <summary>The command that rebuilds this tool where it runs from.</summary>
    public static string RebuildCommand => $"dotnet build {ToolStamp.Project} -c {ToolStamp.Configuration}";

    /// <summary>The embedded manifest: one <c>path|SHA256</c> per source, paths from the root with <c>/</c>, in ordinal order.</summary>
    public static IReadOnlyList<string> Embedded { get; } = Manifest(ToolStamp.Sources.Split('\n'));

    /// <summary>The hash of the embedded manifest, lower-case hex: changes with any source, for a cache key or a result's identity.</summary>
    public static string ToolVersion { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Embedded))));

    /// <summary>The repository root, found by walking up from the tool's own directory to the solution marker.</summary>
    public static string? FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx"))) return directory.FullName;
        }
        return null;
    }

    /// <summary>The manifest of the checkout's sources: the set the build hashes, by the same patterns.</summary>
    public static IReadOnlyList<string> Current(string root)
    {
        var project = Path.Combine(root, ToolStamp.Project);
        var tools = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(project))!;
        var files = ToolStamp.Patterns.Split('|', StringSplitOptions.RemoveEmptyEntries)
                             .SelectMany(pattern => Matching(project, pattern))
                             .Where(file => !Path.GetRelativePath(project, file).Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"))
                             .Concat(Directory.EnumerateFiles(project, "*.csproj", SearchOption.TopDirectoryOnly))
                             .Concat(Directory.Exists(Path.Combine(tools, "Shared")) ? Directory.EnumerateFiles(Path.Combine(tools, "Shared"), "*.cs") : [])
                             .Append(Path.Combine(tools, "Directory.Build.props"))
                             .Append(Path.Combine(tools, "Directory.Build.targets"))
                             .Where(File.Exists)
                             .Distinct(StringComparer.Ordinal);
        return Manifest(files.Select(file => $"{Path.GetRelativePath(root, file).Replace('\\', '/')}|{Hash(file)}"));
    }

    /// <summary>The sources whose hash differs, or that one side has and the other lacks, in ordinal order.</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyList<string> embedded, IReadOnlyList<string> current)
    {
        static Dictionary<string, string> ByPath(IEnumerable<string> manifest) =>
            manifest.Select(line => line.Split('|', 2)).ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.Ordinal);

        var before = ByPath(embedded);
        var after = ByPath(current);
        return [.. before.Keys.Union(after.Keys, StringComparer.Ordinal)
                     .Where(path => !before.TryGetValue(path, out var a) || !after.TryGetValue(path, out var b) || !string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal)];
    }

    /// <summary>Null when the checkout holds the sources the tool was built from; otherwise what differs, said in a few words.</summary>
    public static string? Difference(string? root)
    {
        if (root is null) return "no CodoMetis.TypeKit.slnx above the tool, so its sources cannot be read";
        if (!Directory.Exists(Path.Combine(root, ToolStamp.Project))) return $"{ToolStamp.Project} is not in the checkout";

        var changed = Differences(Embedded, Current(root));
        if (changed.Count == 0) return null;
        return string.Join(", ", changed.Take(5)) + (changed.Count > 5 ? $" and {changed.Count - 5} more" : "") + (changed.Count == 1 ? " differs" : " differ");
    }

    // "**/*.cs" or "Consumers/**/*" below a directory, "Schemas/*.json" in one directory, or one file.
    private static IEnumerable<string> Matching(string project, string pattern)
    {
        var deep = pattern.IndexOf("**/", StringComparison.Ordinal);
        if (deep >= 0)
        {
            var below = Path.Combine(project, pattern[..deep]);
            return Directory.Exists(below) ? Directory.EnumerateFiles(below, pattern[(deep + 3)..], SearchOption.AllDirectories) : [];
        }
        var directory = Path.Combine(project, Path.GetDirectoryName(pattern) ?? "");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, Path.GetFileName(pattern), SearchOption.TopDirectoryOnly) : [];
    }

    private static IReadOnlyList<string> Manifest(IEnumerable<string> lines) =>
        [.. lines.Select(line => line.Trim().Replace('\\', '/')).Where(line => line.Length > 0).Order(StringComparer.Ordinal)];

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
