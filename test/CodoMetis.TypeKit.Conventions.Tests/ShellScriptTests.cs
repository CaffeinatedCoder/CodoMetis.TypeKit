using System.Diagnostics;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// No shell or Python script outside <c>spikes/</c>: a contributor needs git and the pinned SDK, on
/// Windows as on macOS and Linux, and repository tooling is C#, a file-based app or a tool project
/// (<c>design/adr/0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md</c>).
/// </summary>
/// <remarks>
/// A script is a tracked file with a <c>.sh</c>, <c>.bash</c>, <c>.py</c> or <c>.ps1</c> extension,
/// or one whose first line is a <c>#!</c> that names no <c>dotnet</c>: a script without an extension (the
/// prober's Python was one) would pass a rule by extension alone silently. The files are what
/// git tracks, so a build output or a scratch file in the working tree is not judged.
/// </remarks>
public sealed class ShellScriptTests
{
    private static readonly string[] ScriptExtensions = [".sh", ".bash", ".py", ".ps1"];

    private static readonly Lazy<IReadOnlyList<string>> Tracked = new(TrackedFiles);

    [Fact]
    public void The_tracked_files_are_listed()
    {
        Tracked.Value.ShouldContain("CodoMetis.TypeKit.slnx");
        Tracked.Value.ShouldContain("test/CodoMetis.TypeKit.Conventions.Tests/ShellScriptTests.cs");
    }

    [Fact]
    public void A_script_outside_spikes_is_refused()
    {
        var scripts = Tracked.Value.Where(IsScript).Where(path => !Allowed(path)).ToArray();

        scripts.ShouldBeEmpty(
            "Repository tooling is C#, a file-based app (`dotnet run tools/x.cs`) or a tool project under tools/; a shell " +
            "script belongs in spikes/ only, and a hook is a one-line shim that execs `dotnet` (design/adr/0092).");
    }

    /// <summary>The positive control: a spike's script is tracked and is seen as one, so an empty finding means none, not a blind check.</summary>
    [Fact]
    public void A_spike_script_is_seen_as_one()
    {
        var spike = Tracked.Value.FirstOrDefault(path => path.StartsWith("spikes/", StringComparison.Ordinal) && path.EndsWith(".sh", StringComparison.Ordinal));

        spike.ShouldNotBeNull("No tracked .sh under spikes/ to hold the check to.");
        IsScript(spike).ShouldBeTrue();
    }

    private static bool Allowed(string path) =>
        path.StartsWith("spikes/", StringComparison.Ordinal);

    private static bool IsScript(string path)
    {
        if (ScriptExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return true;

        var full = Path.Combine(Repository.Root, path);
        if (!File.Exists(full)) return false;

        using var reader = new StreamReader(full);
        var first = new char[200];
        var read = reader.Read(first, 0, first.Length);
        var line = new string(first, 0, read).Split('\n')[0];

        return line.StartsWith("#!", StringComparison.Ordinal) && !line.Contains("dotnet", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> TrackedFiles()
    {
        using var process = Process.Start(new ProcessStartInfo("git", ["-C", Repository.Root, "ls-files", "-z"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        })!;

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.ShouldBe(0, $"git ls-files failed: {errors.Result}");

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
