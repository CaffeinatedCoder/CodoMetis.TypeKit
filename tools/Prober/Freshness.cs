using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Prober;

/// <summary>
/// Whether this dll was built from the sources beside it. The build hashes every source, the schemas
/// included, into <see cref="BuildStamp.Sources"/>; at startup the dll hashes them again. A difference means
/// the dll would answer with code nobody can read any more, so it rebuilds itself and hands over to the new
/// build, or, under <c>PROBER_NO_REBUILD=1</c>, refuses. Content hashes, never timestamps.
/// </summary>
internal static class Freshness
{
    public const string SourceDirectory = "tools/Prober";
    public const string ProjectFile = "Prober.csproj";

    private const string RebuiltVariable = "PROBER_REBUILT";
    private const string NoRebuildVariable = "PROBER_NO_REBUILD";

    /// <summary>The embedded manifest: one <c>path|SHA256</c> per source, paths with <c>/</c>, in ordinal order.</summary>
    public static IReadOnlyList<string> Embedded { get; } = Manifest(BuildStamp.Sources.Split('\n'));

    /// <summary>The tool's version, part of every result's identity: the hash of the embedded manifest.</summary>
    public static string ToolVersion { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Embedded))))[..16];

    public static IReadOnlyList<string> Current(string projectDirectory) =>
        Manifest(Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                          .Concat(Directory.Exists(Path.Combine(projectDirectory, "Schemas")) ? Directory.EnumerateFiles(Path.Combine(projectDirectory, "Schemas"), "*.json") : [])
                          .Append(Path.Combine(projectDirectory, ProjectFile))
                          .Where(File.Exists)
                          .Select(path => Path.GetRelativePath(projectDirectory, path).Replace('\\', '/'))
                          .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
                          .Select(path => $"{path}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(projectDirectory, path))))}"));

    public static IReadOnlyList<string> Differences(IReadOnlyList<string> embedded, IReadOnlyList<string> current)
    {
        static Dictionary<string, string> ByPath(IEnumerable<string> manifest) =>
            manifest.Select(line => line.Split('|', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.Ordinal);
        var before = ByPath(embedded); var after = ByPath(current);
        return [.. before.Keys.Union(after.Keys, StringComparer.Ordinal)
                     .Where(path => !before.TryGetValue(path, out var a) || !after.TryGetValue(path, out var b) || !string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal)];
    }

    /// <summary>Null when current (or no sources beside it); otherwise the exit code of the rebuilt dll, or of the refusal.</summary>
    public static int? Ensure(string root, string[] args)
    {
        var directory = Path.Combine(root, SourceDirectory);
        if (!File.Exists(Path.Combine(directory, ProjectFile))) return null;
        var changed = Differences(Embedded, Current(directory));
        if (changed.Count == 0) return null;

        var project = Path.Combine(directory, ProjectFile);
        var command = $"dotnet build {SourceDirectory}/{ProjectFile} -c {BuildStamp.Configuration}";
        var what = string.Join(", ", changed.Take(5)) + (changed.Count > 5 ? $" and {changed.Count - 5} more" : "");
        if (Environment.GetEnvironmentVariable(RebuiltVariable) == "1" || Environment.GetEnvironmentVariable(NoRebuildVariable) == "1")
        {
            Console.Error.WriteLine($"prober: this dll was built from other sources than {SourceDirectory} holds ({what}). Run `{command}`.");
            return 3;
        }
        Console.Error.WriteLine($"prober: sources changed since this dll was built ({what}); rebuilding.");

        // The running dll is renamed, not overwritten: renaming a loaded file is allowed on every platform.
        var self = typeof(Freshness).Assembly.Location;
        var aside = $"{self}.{Environment.ProcessId}.old";
        File.Move(self, aside);
        var built = Proc.Run(Environment.ProcessPath ?? "dotnet", ["build", project, "-c", BuildStamp.Configuration, "-nologo", "-v:q"], root);
        if (built.ExitCode != 0 || !File.Exists(self))
        {
            if (!File.Exists(self)) File.Move(aside, self);
            Console.Error.WriteLine(built.Stdout + built.Stderr);
            Console.Error.WriteLine($"prober: the rebuild failed; fix it and run `{command}`.");
            return 3;
        }
        try { File.Delete(aside); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        var start = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet") { UseShellExecute = false };
        start.ArgumentList.Add(self);
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment[RebuiltVariable] = "1";
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static IReadOnlyList<string> Manifest(IEnumerable<string> lines) =>
        [.. lines.Select(line => line.Trim().Replace('\\', '/')).Where(line => line.Length > 0).Order(StringComparer.Ordinal)];
}
