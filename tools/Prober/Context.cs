using System.Diagnostics;
using System.Text;

namespace Prober;

/// <summary>
/// Everything a command reads from its surroundings: the directory it runs in, the environment and where
/// it writes. Commands never touch the process's own current directory or environment, so tests run
/// them in-process, side by side, each in a checkout of its own.
/// </summary>
internal sealed class Context(string workingDirectory, IReadOnlyDictionary<string, string?> environment, TextWriter output, TextWriter error)
{
    public string WorkingDirectory { get; } = workingDirectory;
    public TextWriter Out { get; } = output;
    public TextWriter Error { get; } = error;

    public string? Env(string name) => environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static Context FromProcess() =>
        new(Directory.GetCurrentDirectory(),
            System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                       .ToDictionary(e => (string)e.Key, e => (string?)e.Value, StringComparer.Ordinal),
            Console.Out, Console.Error);

    public IReadOnlyDictionary<string, string?> Environment => environment;
}

/// <summary>A child process's result.</summary>
internal sealed record Ran(int ExitCode, string Stdout, string Stderr);

internal static class Proc
{
    /// <summary>
    /// Runs a program to completion, capturing its output. Never through a shell. `dotnet` is the one that
    /// satisfies the directory's global.json, the installer's when the one on PATH cannot (Dotnet).
    /// </summary>
    public static Ran Run(string fileName, IEnumerable<string> arguments, string workingDirectory,
                          IReadOnlyDictionary<string, string?>? environment = null, TimeSpan? timeout = null)
    {
        string? dotnetDirectory = null;
        if (fileName == "dotnet") (fileName, dotnetDirectory) = Dotnet.For(workingDirectory);
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment)
                if (value is null) start.Environment.Remove(name); else start.Environment[name] = value;
        Dotnet.Apply(dotnetDirectory, start.Environment);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (timeout is { } limit && !process.WaitForExit(limit))
        {
            // The whole tree: a test host or a headless agent starts children of its own. Portable, no signals.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.WaitForExit();
            return new Ran(-1, stdout.Result, stderr.Result + "\n(stopped at the time limit)");
        }
        process.WaitForExit();
        return new Ran(process.ExitCode, stdout.Result, stderr.Result);
    }
}

internal static class Git
{
    public static Ran Run(string workingDirectory, params string[] arguments) => Proc.Run("git", arguments, workingDirectory);

    public static string Out(string workingDirectory, params string[] arguments) => Run(workingDirectory, arguments).Stdout.Trim();

    /// <summary>The object id of a file (blob) or a directory (tree) at a revision, or null when it does not exist there.</summary>
    public static string? ObjectId(string workingDirectory, string path, string revision = "HEAD")
    {
        var ran = Run(workingDirectory, "rev-parse", "-q", "--verify", $"{revision}:{path}");
        return ran.ExitCode == 0 ? ran.Stdout.Trim() : null;
    }
}
