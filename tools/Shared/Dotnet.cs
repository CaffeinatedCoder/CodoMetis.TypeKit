using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// The dotnet a tool starts. A shell may find a dotnet first on PATH that cannot satisfy global.json's SDK
/// pin: on macOS, Homebrew's formula (10.0.401 against the 10.0.402 pin) shadows the SDK installer's, and
/// every command it runs fails with exit 155. Then the one the installer puts in its default location is
/// tried, as the verdict tool and the SessionStart hook do; when neither satisfies the pin, the one on PATH
/// stays, so its own error reaches whoever reads the output (design/adr/0105).
/// </summary>
internal static class Dotnet
{
    /// <summary>
    /// Where the SDK installer puts dotnet: /usr/local/share/dotnet, or %ProgramFiles%\dotnet on Windows, unless
    /// TOOLS_DOTNET_FALLBACK_DIR names another directory (an install elsewhere, or a test's stub).
    /// </summary>
    public static string InstallerDirectory =>
        Environment.GetEnvironmentVariable("TOOLS_DOTNET_FALLBACK_DIR") is { Length: > 0 } given ? given
        : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
        : "/usr/local/share/dotnet";

    static string ExecutableIn(string directory) => Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    /// <summary>
    /// The choice, given whether each candidate satisfies the pin. A bare "dotnet" is what a child started by that
    /// name gets, and .NET looks for it beside the running host before it reads PATH, so a caller that falls back
    /// starts the installer's by its full path, never by putting its directory on PATH alone. The choice: the one on PATH when it does, the installer's
    /// when only it does (with the directory to put first on a child's PATH and DOTNET_ROOT), and otherwise the
    /// one on PATH. Starts nothing.
    /// </summary>
    public static (string File, string? Directory) Choose(string installerDirectory, Func<string, bool> satisfies)
    {
        if (satisfies("dotnet")) return ("dotnet", null);
        var fallback = ExecutableIn(installerDirectory);
        return File.Exists(fallback) && satisfies(fallback) ? (fallback, installerDirectory) : ("dotnet", null);
    }

    /// <summary>Whether <c>dotnet --version</c> succeeds in the directory, whose global.json then pins an SDK this dotnet has.</summary>
    public static bool Satisfies(string dotnet, string directory)
    {
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add("--version");
        try
        {
            using var process = Process.Start(start);
            if (process is null) return false;
            var drained = Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
            process.WaitForExit();
            drained.Wait();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    static readonly ConcurrentDictionary<string, (string File, string? Directory)> chosen = new(StringComparer.Ordinal);

    /// <summary>The dotnet to start in a directory, decided once per directory and process.</summary>
    public static (string File, string? Directory) For(string directory) =>
        chosen.GetOrAdd(Path.GetFullPath(directory), d => Choose(InstallerDirectory, dotnet => Satisfies(dotnet, d)));

    /// <summary>Points a child at the chosen dotnet's directory, so what it starts in turn (MSBuild, a test host) uses the same SDK.</summary>
    public static void Apply(string? directory, IDictionary<string, string?> environment)
    {
        if (directory is null) return;
        environment.TryGetValue("PATH", out var path);
        environment["PATH"] = string.IsNullOrEmpty(path) ? directory : directory + Path.PathSeparator + path;
        environment["DOTNET_ROOT"] = directory;
    }
}
