// The oracle kit's local tool (design/adr/0112, test/oracles/README.md). Never part of CI.
//
//   dotnet tools/Oracles/bin/Release/net10.0/oracles.dll seeds [name filter]   show that every seed is caught
//   dotnet tools/Oracles/bin/Release/net10.0/oracles.dll status                the catalogue's worklist
//
// `seeds`: first a baseline, a copy of the working tree with no seed (bin/, obj/, .git/ and the agents' worktrees left
// out, so uncommitted work is included) under the temp directory, each oracle the selected seeds name built in Release
// and run alone; then, for each test/oracles/seeds/<defect>.patch whose file name holds the filter, another copy, the
// patch applied, each oracle project its `Oracles:` line names built once and each named class run alone, with the
// same filter. An oracle catches a seed when it passes without it and fails with it in a test of its own: a test
// that fails with a row that is not a known candidate, never one xunit could not set up, a test host that crashed or
// a run that wrote no results, which are ERROR (SeedVerdict, design/adr/0117). One line per baseline and one per
// (seed, oracle), CAUGHT, MISSED or ERROR.
//
// Exit 0 is a pass, 1 a seed not caught (or a status that could not read the catalogue), 2 a usage error, 3 a tool
// built from other sources than the checkout holds, which names the command that rebuilds it, 4 an oracle that could
// not be judged: its baseline does not pass, or a run with the seed did not reach the tests' own code.
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
                         .Select(seed => (Path: seed, Name: Path.GetFileName(seed), Named: Header(File.ReadAllText(seed), "Oracles").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
                         .ToList();
    if (seeds.Count == 0)
    {
        Console.WriteLine($"no seed matches '{filter}' in test/oracles/seeds");
        return 1;
    }

    // The baseline: every registered oracle a seed names, run once on a copy with no seed, built and filtered as the
    // seeded runs are. A seed is judged against it (SeedVerdict.Judge), so an oracle that cannot pass here, a dead
    // fixture or a failing test, makes its pairs ERROR instead of crediting every seed with its failures.
    var started = Stopwatch.StartNew();
    var baselines = RunOracles(seed: null, [.. seeds.SelectMany(seed => seed.Named).Distinct(StringComparer.Ordinal).Where(oracles.ContainsKey).Select(oracle => oracles[oracle])]);
    foreach (var (oracle, run) in baselines.OrderBy(pair => pair.Key, StringComparer.Ordinal))
    {
        Console.WriteLine(run.Error is null && run.Caught == 0
            ? $"BASELINE {oracle}: passes without a seed, {run.Executed} tests ({started.Elapsed.TotalSeconds:F0} s)"
            : $"BASELINE {oracle}: DOES NOT PASS without a seed, {run.Error ?? $"{run.Caught} of {run.Executed} tests fail, e.g. {run.Example}"}");
    }

    var notRun = new SeedVerdict.Run(0, 0, 0, "", "not run, since it does not pass without the seed");
    var missed = false;
    var errors = false;
    foreach (var (seed, name, named) in seeds)
    {
        started.Restart();
        try
        {
            var registered = named.Select(oracle => oracles.TryGetValue(oracle, out var found) ? found : throw new SeedError($"'{oracle}' is not in oracles.json")).ToList();
            var seeded = RunOracles(seed, [.. registered.Where(oracle => baselines[oracle.Class] is { Error: null, Caught: 0 })]);
            foreach (var oracle in registered)
            {
                var verdict = SeedVerdict.Judge(baselines[oracle.Class], seeded.GetValueOrDefault(oracle.Class) ?? notRun);
                missed |= verdict.StartsWith("MISSED", StringComparison.Ordinal);
                errors |= verdict.StartsWith("ERROR", StringComparison.Ordinal);
                Console.WriteLine($"{verdict[..8]}{name} -> {oracle.Class}: {verdict[8..]} ({started.Elapsed.TotalSeconds:F0} s)");
            }
        }
        catch (SeedError error)
        {
            errors = true;
            Console.WriteLine($"ERROR   {name}: {error.Message}");
        }
    }

    if (missed) Console.WriteLine("A SEED IS NOT CAUGHT: the oracle it names cannot fail for that defect");
    if (errors) Console.WriteLine("AN ORACLE COULD NOT BE JUDGED: its baseline or its run did not reach the tests' own code; fix that and run again");
    if (!missed && !errors) Console.WriteLine("every seed is caught");
    return errors ? 4 : missed ? 1 : 0;
}

// A copy of the working tree with the seed applied, or none, each oracle's project built once in Release and each
// class run alone. A project that does not build is an error run for each of its oracles.
Dictionary<string, SeedVerdict.Run> RunOracles(string? seed, IReadOnlyList<Oracle> selected)
{
    var runs = new Dictionary<string, SeedVerdict.Run>(StringComparer.Ordinal);
    if (selected.Count == 0) return runs;

    var scratch = Path.Combine(Path.GetTempPath(), $"typekit-seed-{Guid.NewGuid():N}");
    try
    {
        CopyTree(root!, scratch);
        if (seed is not null) Require(RunProcess(scratch, "git", null, ["apply", "--whitespace=nowarn", seed]), "apply the patch");

        foreach (var group in selected.GroupBy(oracle => oracle.Project))
        {
            var project = Path.Combine("test", group.Key);
            if (Failure(RunDotnet(scratch, "build", project, "-c", "Release", "-nologo", "-v", "q"), $"build {group.Key}") is { } failure)
            {
                foreach (var oracle in group) runs[oracle.Class] = new SeedVerdict.Run(0, 0, 0, "", failure);
                continue;
            }

            foreach (var oracle in group)
            {
                var results = Path.Combine(scratch, "SeedResults", oracle.Class);
                var (exit, _) = RunDotnet(scratch, "test", "--project", project, "-c", "Release", "--no-build",
                                          "--filter-class", $"*.{oracle.Class}", "--report-xunit-trx", "--report-xunit-trx-filename", $"{oracle.Class}.trx",
                                          "--results-directory", results);
                var trx = Path.Combine(results, $"{oracle.Class}.trx");
                runs[oracle.Class] = SeedVerdict.Read(exit, File.Exists(trx) ? XDocument.Load(trx) : null, oracle.Class);
            }
        }

        return runs;
    }
    finally
    {
        try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
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
    if (Failure(result, step) is { } failure) throw new SeedError(failure);
}

static string? Failure((int Exit, string Output) result, string step) =>
    result.Exit == 0 ? null : $"{step} failed (exit {result.Exit}): {string.Join(" | ", result.Output.Split('\n').Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(3).Select(line => line.Trim()))}";

internal sealed record Claim(string Id, string Source, IReadOnlyList<string>? HeldBy);

internal sealed record Oracle(string Class, string Project, string Kind, string Against);

internal sealed record Candidate(string Oracle, string Row, string Family, string Issue);

internal sealed class SeedError(string message) : Exception(message);
