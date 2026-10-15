using System.Diagnostics;
using System.Reflection;
using System.Text;

/// <summary>
/// A throwaway repository holding one tool as the checkout does: its CodoMetis.TypeKit.slnx marker, the
/// tool's sources, the shared tooling files, and the tool's build output at
/// tools/&lt;Name&gt;/bin/Release/net10.0, where it is run from. It has no namespace, being linked into each tool's tests. Since the sources are the ones the tool was
/// built from, its fingerprint matches; a test that edits one sees the tool refuse to run stale.
/// </summary>
public sealed class ToolSandbox : IDisposable
{
    private readonly DirectoryInfo _created;

    public ToolSandbox(string tool, string assembly)
    {
        _created = Directory.CreateTempSubdirectory($"{tool}-");
        Work = Physical(_created.FullName);
        Repo = Path.Combine(Work, "repo");

        var source = SourceRoot();
        Directory.CreateDirectory(Repo);
        File.WriteAllText(Path.Combine(Repo, "CodoMetis.TypeKit.slnx"), "");
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            CopyFile(Path.Combine(source, "tools", name), Path.Combine(Repo, "tools", name));
        CopyTree(Path.Combine(source, "tools", "Shared"), Path.Combine(Repo, "tools", "Shared"));
        CopyTree(Path.Combine(source, "tools", tool), Path.Combine(Repo, "tools", tool));

        var output = typeof(ToolSandbox).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ToolOutput").Value!;
        var bin = Path.Combine(Repo, "tools", tool, "bin", "Release", "net10.0");
        CopyTree(output, bin);
        Dll = Path.Combine(bin, $"{assembly}.dll");
    }

    /// <summary>The sandbox's own directory, its physical path: a child's working directory comes back with links resolved (macOS's /var is /private/var).</summary>
    public string Work { get; }

    public string Repo { get; }

    public string Dll { get; }

    /// <summary>Runs `dotnet &lt;assembly&gt;.dll args` from the work directory; stdout and stderr together, in order of arrival.</summary>
    public (int Code, string Output, string Error) Run(IReadOnlyDictionary<string, string?>? environment = null, string? input = null, params string[] arguments) =>
        RunProcess("dotnet", [Dll, .. arguments], Work, environment, input);

    public static (int Code, string Output, string Error) RunProcess(
        string file, string[] arguments, string directory, IReadOnlyDictionary<string, string?>? environment = null, string? input = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, WorkingDirectory = directory,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        // The caller's own settings never reach the tool: a DOTNET_CLI_UI_LANGUAGE=en exported in the shell
        // that runs the tests would make the stub answer in English whether or not the tool pins it.
        foreach (var name in new[] { "BUILD_VERDICT_LOG", "BUILD_VERDICT_EXIT", "BUILD_VERDICT_DOTNET", "STUB_CALLS", "STUB_ROOT", "STUB_BUILD", "DOTNET_CLI_UI_LANGUAGE" })
            psi.Environment.Remove(name);
        foreach (var (k, v) in environment ?? new Dictionary<string, string?>())
        {
            if (v is null) psi.Environment.Remove(k); else psi.Environment[k] = v;
        }

        var merged = new StringBuilder();
        var error = new StringBuilder();
        var gate = new object();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, d) => { if (d.Data is not null) lock (gate) merged.Append(d.Data).Append('\n'); };
        p.ErrorDataReceived += (_, d) =>
        {
            if (d.Data is null) return;
            lock (gate) { merged.Append(d.Data).Append('\n'); error.Append(d.Data).Append('\n'); }
        };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (input is not null)
        {
            p.StandardInput.Write(input);
            p.StandardInput.Close();
        }
        // A tool that has not answered in 20 s has hung, which a hook's caller would sit out until its timeout.
        if (!p.WaitForExit(TimeSpan.FromSeconds(20)))
        {
            p.Kill(entireProcessTree: true);
            return (-1, "timed out", "timed out");
        }
        p.WaitForExit();
        lock (gate) return (p.ExitCode, merged.ToString(), error.ToString());
    }

    public void Dispose()
    {
        try { _created.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>The repository the tests were built in, found by walking up to its solution marker.</summary>
    public static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException($"No CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}.");
    }

    private static void CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
    }

    // Every file below, except a project's own bin and obj.
    private static void CopyTree(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj") continue;
            CopyFile(file, Path.Combine(to, relative));
        }
    }

    private static string Physical(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (new DirectoryInfo(current).LinkTarget is not null) current = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true)!.FullName;
        }
        return current;
    }
}
