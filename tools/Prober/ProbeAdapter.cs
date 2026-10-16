using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Prober;

/// <summary>
/// What the core needs from a repository's toolchain, and nothing more: build the probes, run one probe in
/// one process with an environment (writing its rows' outcomes and a coverage report), and derive the
/// generated axes. The core holds nothing of .NET's; a repository on another stack plugs in its own.
/// </summary>
internal interface IProbeAdapter
{
    /// <summary>Builds what the probes need in the worktree. The first line of the output is shown.</summary>
    Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment);

    /// <summary>Runs one probe (by its filter) with the environment given; the coverage report goes to <paramref name="coverageFile"/> under <paramref name="results"/>.</summary>
    Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment);

    /// <summary>The generated axes (settings of the option types the features read), or null with a reason.</summary>
    (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory);

    /// <summary>The environment every build and run of the probes needs (for .NET: the coverage extension).</summary>
    IReadOnlyDictionary<string, string?> Environment(string cacheDirectory);
}

/// <summary>
/// The .NET adapter, configured by the "replay" object of the axes file: argv templates for the build and
/// the test run (through the repository's verdict tool), the commands that prepare the worktree first (building
/// that tool), the coverage extension referenced from a targets file outside the worktree, and the reflection
/// enumerator run with the repository's package versions.
/// </summary>
internal sealed class DotNetAdapter(JsonObject config) : IProbeAdapter
{
    private string Project => Files.Str(config["project"]);

    private static string[] Expand(JsonNode? template, Dictionary<string, string> values) =>
        [.. Files.Strings(template).Select(arg => Regex.Replace(arg, @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value))];

    /// <summary>
    /// The program a template starts: a bare name (no path separator, <c>dotnet</c>) is found on PATH; a path is
    /// relative to the replay worktree and must exist there, so a template naming a file the repository no longer
    /// has fails by name, never as a puzzling exit code.
    /// </summary>
    internal static string Executable(string worktree, string program)
    {
        if (program.IndexOfAny(['/', '\\']) < 0) return program;
        var path = Path.Combine(worktree, program);
        return File.Exists(path) ? path : throw new Refusal($"the replay template names {program}, which the worktree does not have");
    }

    private static Ran Start(string worktree, string[] argv, IReadOnlyDictionary<string, string?> environment) =>
        Proc.Run(Executable(worktree, argv[0]), argv[1..], worktree, environment);

    /// <summary>Prepares the worktree (the "prepare" commands: building the verdict tool, which a fresh or moved worktree lacks or holds stale), then builds the probes.</summary>
    public Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var command in (config["prepare"] as JsonArray ?? []).OfType<JsonArray>())
        {
            var prepared = Start(worktree, Expand(command, new() { ["project"] = Project }), environment);
            if (prepared.ExitCode != 0)
                return prepared with { Stdout = $"PREPARE FAILED: {string.Join(' ', Files.Strings(command))}\n" + prepared.Stdout + prepared.Stderr };
        }
        return Start(worktree, Expand(config["build"], new() { ["project"] = Project }), environment);
    }

    /// <summary>
    /// The "test" template, then the "coverage" arguments when a coverage file is asked for. A replay without a
    /// footprint asks for none, so a template that carries the coverage arguments itself is refused rather than
    /// run with an empty file name.
    /// </summary>
    public Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment)
    {
        var values = new Dictionary<string, string> { ["project"] = Project, ["filter"] = filter, ["results"] = results, ["cobertura"] = coverageFile };
        if (coverageFile.Length == 0 && Files.Strings(config["test"]).Any(arg => arg.Contains("{cobertura}", StringComparison.Ordinal)))
            throw new Refusal("the replay's test template carries the coverage arguments itself; move them to a \"coverage\" array of the replay configuration, so a run without a footprint can leave them out");
        string[] argv = coverageFile.Length > 0 ? [.. Expand(config["test"], values), .. Expand(config["coverage"], values)] : Expand(config["test"], values);
        return Start(worktree, argv, environment);
    }

    public IReadOnlyDictionary<string, string?> Environment(string cacheDirectory)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Files.Str(config["coverageTargets"]) is { Length: > 0 } targets)
        {
            var path = Path.Combine(cacheDirectory, "coverage.targets");
            if (!File.Exists(path) || SharedFile.ReadAllText(path) != targets) Files.WriteAtomically(path, targets);
            result["CustomAfterMicrosoftCommonTargets"] = path;
        }
        return result;
    }

    public (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory)
    {
        if (config["enumerate"] is not JsonObject enumerate) return (null, "no enumerator configured");
        // The enumerator pins its packages in #:package directives; each version comes from the repository's
        // central package versions, so the axes follow a dependency update without anyone editing the script.
        var script = File.ReadAllText(Path.Combine(checkout, Files.Str(enumerate["script"])));
        var props = Path.Combine(checkout, "Directory.Packages.props");
        var versions = File.Exists(props)
            ? Regex.Matches(File.ReadAllText(props), "<PackageVersion\\s+Include=\"([^\"]+)\"\\s+Version=\"([^\"]+)\"")
                   .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        script = Regex.Replace(script, @"^#:package ([^@\s]+)@(\S+)", m => $"#:package {m.Groups[1].Value}@{(versions.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Groups[2].Value)}", RegexOptions.Multiline);
        Directory.CreateDirectory(workDirectory);
        var file = Path.Combine(workDirectory, Path.GetFileName(Files.Str(enumerate["script"])));
        Files.WriteAtomically(file, script);
        var ran = Proc.Run("dotnet", ["run", file], workDirectory);
        if (ran.ExitCode != 0) return (null, "the enumerator failed: " + ran.Stderr.Trim().Split('\n')[^1]);
        var json = ran.Stdout[ran.Stdout.IndexOf('{')..];
        return (JsonNode.Parse(json) as JsonObject, null);
    }
}
