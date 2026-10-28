// The oracle kit's local tool (design/adr/0112, test/oracles/README.md). Never part of CI.
//
//   dotnet tools/Oracles/bin/Release/net10.0/oracles.dll seeds [name filter]   show that every seed is caught
//   dotnet tools/Oracles/bin/Release/net10.0/oracles.dll status                the catalogue's worklist
//
// `seeds`: for each test/oracles/seeds/<defect>.patch whose file name holds the filter, a copy of the working tree
// (bin/, obj/, .git/ and the agents' worktrees left out, so uncommitted work is included) under the temp directory,
// the patch applied, each oracle project its `Oracles:` line names built once in Release, and each named class run
// alone. An oracle catches a seed when one of its tests fails with a row that is not a known candidate
// (SeedVerdict). One line per (seed, oracle), CAUGHT, MISSED or ERROR, and exit code 1 unless every pair is caught.
//
// Exit 0 is a pass, 1 a seed not caught (or a status that could not read the catalogue), 2 a usage error, 3 a tool
// built from other sources than the checkout holds, which names the command that rebuilds it.
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodoMetis.TypeKit.Oracles;

Console.Out.NewLine = "\n";

var root = SourceFingerprint.FindRoot();
var stale = SourceFingerprint.Difference(root);
if (stale is not null)
{
    Console.WriteLine($"TOOL STALE: {SourceFingerprint.Project} was built from other sources than the checkout holds ({stale})");
    Console.WriteLine($"      rebuild it: {SourceFingerprint.RebuildCommand}");
    return SourceFingerprint.StaleExitCode;
}

var oraclesDirectory = Path.Combine(root!, "test", "oracles");
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

return args switch
{
    ["seeds"] => Seeds(""),
    ["seeds", var filter] => Seeds(filter),
    ["status"] or [] => Status(),
    _ => Usage(),
};

int Usage()
{
    Console.Error.WriteLine("usage: dotnet tools/Oracles/bin/Release/net10.0/oracles.dll seeds [filter] | status");
    return 2;
}

int Seeds(string filter)
{
    var oracles = Read<List<Oracle>>("oracles.json").ToDictionary(oracle => oracle.Class, StringComparer.Ordinal);
    var seeds = Directory.GetFiles(Path.Combine(oraclesDirectory, "seeds"), "*.patch")
                         .Where(seed => Path.GetFileName(seed).Contains(filter, StringComparison.OrdinalIgnoreCase))
                         .Order(StringComparer.Ordinal)
                         .ToList();
    if (seeds.Count == 0)
    {
        Console.WriteLine($"no seed matches '{filter}' in test/oracles/seeds");
        return 1;
    }

    var allCaught = true;
    foreach (var seed in seeds)
    {
        var name = Path.GetFileName(seed);
        var named = Header(File.ReadAllText(seed), "Oracles").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var scratch = Path.Combine(Path.GetTempPath(), $"typekit-seed-{Guid.NewGuid():N}");
        var started = Stopwatch.StartNew();
        try
        {
            CopyTree(root!, scratch);
            Require(RunProcess(scratch, "git", null, ["apply", "--whitespace=nowarn", seed]), "apply the patch");

            foreach (var group in named.Select(oracle => oracles.TryGetValue(oracle, out var registered) ? registered : throw new SeedError($"'{oracle}' is not in oracles.json"))
                                       .GroupBy(oracle => oracle.Project))
            {
                var project = Path.Combine("test", group.Key);
                Require(RunDotnet(scratch, "build", project, "-c", "Release", "-nologo", "-v", "q"), $"build {group.Key}");
                foreach (var oracle in group)
                {
                    var results = Path.Combine(scratch, "SeedResults", oracle.Class);
                    RunDotnet(scratch, "test", "--project", project, "-c", "Release", "--no-build",
                              "--filter-class", $"*.{oracle.Class}", "--report-xunit-trx", "--report-xunit-trx-filename", $"{oracle.Class}.trx",
                              "--results-directory", results);
                    var verdict = Verdict(Path.Combine(results, $"{oracle.Class}.trx"), oracle.Class);
                    allCaught &= verdict.StartsWith("CAUGHT", StringComparison.Ordinal);
                    Console.WriteLine($"{verdict[..8]}{name} -> {oracle.Class}: {verdict[8..]} ({started.Elapsed.TotalSeconds:F0} s)");
                }
            }
        }
        catch (SeedError error)
        {
            allCaught = false;
            Console.WriteLine($"ERROR   {name}: {error.Message}");
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    Console.WriteLine(allCaught ? "every seed is caught" : "A SEED IS NOT CAUGHT: the oracle it names cannot fail for that defect");
    return allCaught ? 0 : 1;
}

string Verdict(string trx, string oracle)
{
    if (!File.Exists(trx)) return "ERROR   no test results written (did the class run?)";

    XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    var results = XDocument.Load(trx).Descendants(ns + "UnitTestResult").ToList();
    if (results.Count == 0) return $"ERROR   no test of {oracle} ran";

    var failed = results.Where(result => (string?)result.Attribute("outcome") == "Failed")
                        .Select(result => (Test: (string?)result.Attribute("testName") ?? "?", Message: result.Descendants(ns + "Message").FirstOrDefault()?.Value ?? ""))
                        .ToList();
    var caught = failed.Where(failure => SeedVerdict.Catches(failure.Message)).ToList();
    if (caught.Count > 0) return $"CAUGHT  {caught.Count} of {results.Count} tests fail, e.g. {caught[0].Test}";
    return failed.Count == 0
        ? $"MISSED  all {results.Count} tests pass with the defect planted"
        : $"MISSED  {failed.Count} of {results.Count} tests fail, every row a known candidate";
}

int Status()
{
    var claims = Read<List<Claim>>("claims.json");
    var oracles = Read<List<Oracle>>("oracles.json");
    var waivers = Read<Dictionary<string, string>>("surface-waivers.json");
    var candidates = Read<List<Candidate>>("candidates.json");
    var seeds = Directory.GetFiles(Path.Combine(oraclesDirectory, "seeds"), "*.patch")
                         .Select(seed => Header(File.ReadAllText(seed), "Oracles").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                         .ToList();

    var unheld = claims.Where(claim => claim.HeldBy is not { Count: > 0 }).ToList();
    Console.WriteLine($"claims: {claims.Count}, held by a test: {claims.Count - unheld.Count}, unheld (the worklist): {unheld.Count}");
    foreach (var claim in unheld) Console.WriteLine($"  UNHELD   {claim.Id,-40} {claim.Source}");

    var unread = waivers.Where(waiver => waiver.Value.StartsWith("unread:", StringComparison.Ordinal)).ToList();
    Console.WriteLine($"surface waivers: {waivers.Count}, unread (members whose document has no claims yet): {unread.Count}");
    foreach (var waiver in unread) Console.WriteLine($"  UNREAD   {waiver.Key}");

    var seeded = seeds.SelectMany(names => names).ToHashSet(StringComparer.Ordinal);
    Console.WriteLine($"oracles: {oracles.Count}, seeds: {seeds.Count}, oracles without a seed: {oracles.Count(oracle => !seeded.Contains(oracle.Class))}");
    foreach (var oracle in oracles.Where(oracle => !seeded.Contains(oracle.Class))) Console.WriteLine($"  UNSEEDED {oracle.Class}");

    Console.WriteLine($"candidates: {candidates.Count} ({candidates.Count(candidate => candidate.Issue.Length == 0)} without an issue)");
    foreach (var candidate in candidates) Console.WriteLine($"  {candidate.Oracle}: '{candidate.Row}' [{candidate.Family}] {(candidate.Issue.Length == 0 ? "NOT FILED" : candidate.Issue)}");

    return 0;
}

T Read<T>(string file)
{
    using var stream = File.OpenRead(Path.Combine(oraclesDirectory, file));
    return JsonSerializer.Deserialize<T>(stream, json) ?? throw new InvalidOperationException($"test/oracles/{file} is empty.");
}

static string Header(string text, string key) =>
    Regex.Match(text, $@"^{key}:\s*(?<value>.+?)\s*$", RegexOptions.Multiline) is { Success: true } match ? match.Groups["value"].Value : "";

static void CopyTree(string from, string to)
{
    string[] skipped = ["bin", "obj", ".git", "SeedResults", "TestResults", "dist", ".codebase-memory"];
    foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.TopDirectoryOnly))
    {
        var name = Path.GetFileName(directory);
        if (skipped.Contains(name, StringComparer.Ordinal)) continue;
        if (name == ".claude") { CopyClaude(directory, Path.Combine(to, name)); continue; }
        CopyTree(directory, Path.Combine(to, name));
    }

    Directory.CreateDirectory(to);
    foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.TopDirectoryOnly))
        File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
}

// The agents' worktrees under .claude/worktrees are whole checkouts; nothing a seed builds reads them.
static void CopyClaude(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    foreach (var directory in Directory.EnumerateDirectories(from).Where(directory => Path.GetFileName(directory) != "worktrees"))
        CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
}

static (int Exit, string Output) RunDotnet(string directory, params string[] arguments)
{
    var (dotnet, home) = Dotnet.For(directory);
    return RunProcess(directory, dotnet, home, arguments);
}

static (int Exit, string Output) RunProcess(string directory, string file, string? dotnetHome, string[] arguments)
{
    var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    Dotnet.Apply(dotnetHome, start.Environment);

    using var process = Process.Start(start) ?? throw new SeedError($"{file} could not be started");
    var error = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output + error.Result);
}

static void Require((int Exit, string Output) result, string step)
{
    if (result.Exit != 0) throw new SeedError($"{step} failed (exit {result.Exit}): {string.Join(" | ", result.Output.Split('\n').Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(3).Select(line => line.Trim()))}");
}

internal sealed record Claim(string Id, string Source, IReadOnlyList<string>? HeldBy);

internal sealed record Oracle(string Class, string Project, string Kind, string Against);

internal sealed record Candidate(string Oracle, string Row, string Family, string Issue);

internal sealed class SeedError(string message) : Exception(message);
