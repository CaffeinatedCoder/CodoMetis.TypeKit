using System.Diagnostics;
using System.Text;

namespace DocsQuery;

/// <summary>
/// Whether this dll was built from the sources beside it. The build hashes every source into
/// <see cref="BuildStamp.Sources"/>; at startup the dll hashes them again. A difference means the dll
/// answers with code nobody can read any more, so it rebuilds itself and hands over to the new build,
/// or, under <c>DOCS_QUERY_NO_REBUILD=1</c>, refuses. Content hashes, never timestamps.
/// </summary>
internal static class Freshness
{
    public const string SourceDirectory = "tools/DocsQuery";
    public const string ProjectFile     = "DocsQuery.csproj";

    private const string RebuiltVariable   = "DOCS_QUERY_REBUILT";
    private const string NoRebuildVariable = "DOCS_QUERY_NO_REBUILD";

    /// <summary>The embedded manifest: one <c>path|SHA256</c> per source, paths with <c>/</c>, in ordinal order.</summary>
    public static IReadOnlyList<string> Embedded { get; } = Manifest(BuildStamp.Sources.Split('\n'));

    /// <summary>The tool's version for the cache fingerprint: the hash of the embedded manifest, so any change to the code drops the cache.</summary>
    public static string ToolVersion { get; } = Corpus.Hash(Encoding.UTF8.GetBytes(string.Join('\n', Embedded)));

    /// <summary>The manifest of the sources as they are now: the same glob the build hashes.</summary>
    public static IReadOnlyList<string> Current(string projectDirectory) =>
        Manifest(Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                          .Append(Path.Combine(projectDirectory, ProjectFile))
                          .Where(File.Exists)
                          .Select(path => Corpus.Relative(projectDirectory, path))
                          .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
                          .Select(path => $"{path}|{Corpus.Hash(File.ReadAllBytes(Path.Combine(projectDirectory, path)))}"));

    /// <summary>The sources whose hash differs, or that one side has and the other lacks.</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyList<string> embedded, IReadOnlyList<string> current)
    {
        static Dictionary<string, string> ByPath(IEnumerable<string> manifest) =>
            manifest.Select(line => line.Split('|', 2)).ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.Ordinal);

        var before = ByPath(embedded);
        var after  = ByPath(current);

        return [.. before.Keys.Union(after.Keys, StringComparer.Ordinal)
                     .Where(path => !before.TryGetValue(path, out var a) || !after.TryGetValue(path, out var b) || !string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Null when the dll is current, or has no sources beside it to compare with (a copy elsewhere);
    /// otherwise the exit code of the rebuilt dll that answered instead, or of the refusal.
    /// </summary>
    public static int? Ensure(string root, string[] args)
    {
        var directory = Path.Combine(root, SourceDirectory);
        if (!File.Exists(Path.Combine(directory, ProjectFile))) return null;

        var changed = Differences(Embedded, Current(directory));
        if (changed.Count == 0) return null;

        var project = Path.Combine(directory, ProjectFile);
        var command = $"dotnet build {Corpus.Relative(root, project)} -c {BuildStamp.Configuration}";
        var what    = string.Join(", ", changed.Take(5)) + (changed.Count > 5 ? $" and {changed.Count - 5} more" : "");

        if (Environment.GetEnvironmentVariable(RebuiltVariable) == "1" || Environment.GetEnvironmentVariable(NoRebuildVariable) == "1")
        {
            Console.Error.WriteLine($"docs-query: this dll was built from other sources than {SourceDirectory} holds ({what}). Run `{command}`.");
            return 3;
        }

        Console.Error.WriteLine($"docs-query: sources changed since this dll was built ({what}); rebuilding.");

        // The running dll is renamed, not overwritten: the build writes a new file at its path, and the
        // pages this process has mapped stay valid. Renaming a loaded file is allowed on every platform.
        var self  = typeof(Freshness).Assembly.Location;
        var aside = $"{self}.{Environment.ProcessId}.old";
        File.Move(self, aside);

        var (built, output) = Run(Environment.ProcessPath ?? "dotnet", ["build", project, "-c", BuildStamp.Configuration, "-nologo", "-v:q"], redirect: true);

        if (built != 0 || !File.Exists(self))
        {
            if (!File.Exists(self)) File.Move(aside, self);
            Console.Error.WriteLine(output);
            Console.Error.WriteLine($"docs-query: the rebuild failed; fix it and run `{command}`.");
            return 3;
        }

        TryDelete(aside);

        var (exitCode, _) = Run(Environment.ProcessPath ?? "dotnet", [self, .. args], redirect: false, (RebuiltVariable, "1"));
        return exitCode;
    }

    private static IReadOnlyList<string> Manifest(IEnumerable<string> lines) =>
        [.. lines.Select(line => line.Trim().Replace('\\', '/')).Where(line => line.Length > 0).Order(StringComparer.Ordinal)];

    private static (int ExitCode, string Output) Run(string fileName, string[] arguments, bool redirect, params (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = redirect, RedirectStandardError = redirect, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment) start.Environment[name] = value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = redirect ? process.StandardOutput.ReadToEndAsync() : Task.FromResult("");
        var error  = redirect ? process.StandardError.ReadToEndAsync() : Task.FromResult("");
        process.WaitForExit();

        return (process.ExitCode, output.Result + error.Result);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Windows keeps a loaded file; the next build overwrites nothing of it. */ }
    }
}
