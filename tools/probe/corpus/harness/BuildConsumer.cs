// A throwaway consumer of the generators, built once per probe run with every row's declarations in a file of its own, for what
// only a build can show: the generators' diagnostics (CMTK1xxx). It builds as test/CodoMetis.TypeKit.Generators.Tests' BuildOutcomeTests
// does: under artifacts/, against the projects this run already built (--no-dependencies), outside the repository's build settings.
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
namespace Probes.Build;

public sealed record BuildDiagnostic(string File, string Severity, string Id, string Message);

public sealed record ConsumerOutcome(int ExitCode, string Output, IReadOnlyList<BuildDiagnostic> Diagnostics, IReadOnlyDictionary<string, HashSet<string>> Methods)
{
    /// <summary>The diagnostics reported in one of the consumer's files.</summary>
    public IEnumerable<BuildDiagnostic> In(string file) => Diagnostics.Where(d => Path.GetFileName(d.File) == file);
}

public static partial class Consumer
{
    static readonly Dictionary<string, Lazy<Task<ConsumerOutcome>>> Builds = new();

    /// <summary>Builds the consumer of <paramref name="files"/> once per <paramref name="key"/> in this process.</summary>
    public static Task<ConsumerOutcome> Build(string key, Func<IReadOnlyDictionary<string, string>> files)
    {
        lock (Builds)
        {
            if (!Builds.TryGetValue(key, out var build)) Builds[key] = build = new(() => Run(files()));
            return build.Value;
        }
    }

    public static string RepositoryRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CodoMetis.TypeKit.slnx"))) return d.FullName;
        throw new InvalidOperationException($"no CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}");
    }

    static string Configuration => typeof(Consumer).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                                   ?? throw new InvalidOperationException("the probe assembly carries no AssemblyConfigurationAttribute");

    static async Task<ConsumerOutcome> Run(IReadOnlyDictionary<string, string> files)
    {
        var root = RepositoryRoot();
        var dir = Path.Combine(root, "artifacts", $"probe-consumer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(root, "src");
            await File.WriteAllTextAsync(Path.Combine(dir, "Consumer.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <AssemblyName>ProbeConsumer</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{src}/CodoMetis.TypeKit.Generators/CodoMetis.TypeKit.Generators.csproj" />
                    <Reference Include="NodaTime"><HintPath>{typeof(NodaTime.Instant).Assembly.Location}</HintPath></Reference>
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Build.targets"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
            foreach (var (name, text) in files) await File.WriteAllTextAsync(Path.Combine(dir, name), text);

            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
            var info = new ProcessStartInfo(dotnet, ["build", dir, "-c", Configuration, "--no-dependencies", "-p:RestoreRecursive=false", "-nodeReuse:false", "-clp:NoSummary", "-v:q", "-nologo"])
            {
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir,
            };
            // The run's code coverage instruments this process through its environment; the build is not the probe's code.
            foreach (var k in info.Environment.Keys.ToList())
                if (k.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)
                    || k.Contains("CODECOVERAGE", StringComparison.OrdinalIgnoreCase) || k.StartsWith("MicrosoftInstrumentationEngine", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("DOTNET_STARTUP_HOOKS", StringComparison.OrdinalIgnoreCase))
                    info.Environment.Remove(k);
            info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10))) await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;
            var diagnostics = Line().Matches(output)
                .Select(m => new BuildDiagnostic(m.Groups["file"].Value.Trim(), m.Groups["sev"].Value, m.Groups["id"].Value, m.Groups["msg"].Value.Trim()))
                .Distinct().ToList();
            var dll = Path.Combine(dir, "bin", Configuration, "net10.0", "ProbeConsumer.dll");
            return new ConsumerOutcome(process.ExitCode, output, diagnostics, File.Exists(dll) ? Methods(dll) : new Dictionary<string, HashSet<string>>());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The methods each type of the built assembly declares, by the type's metadata name (nesting with '+'), read without loading it.</summary>
    static Dictionary<string, HashSet<string>> Methods(string dll)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        string Name(TypeDefinition t)
        {
            var name = md.GetString(t.Name);
            var outer = t.GetDeclaringType();
            if (!outer.IsNil) return Name(md.GetTypeDefinition(outer)) + "+" + name;
            var ns = md.GetString(t.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        var result = new Dictionary<string, HashSet<string>>();
        foreach (var h in md.TypeDefinitions)
        {
            var t = md.GetTypeDefinition(h);
            result[Name(t)] = [.. t.GetMethods().Select(m => md.GetString(md.GetMethodDefinition(m).Name))];
        }
        return result;
    }

    [GeneratedRegex(@"^(?<file>[^\r\n(]+)\((?<line>\d+),(?<col>\d+)\): (?<sev>warning|error) (?<id>[A-Z]+\d+): (?<msg>.*?)(?: \[[^\]\r\n]*\])?\r?$", RegexOptions.Multiline)]
    private static partial Regex Line();
}
