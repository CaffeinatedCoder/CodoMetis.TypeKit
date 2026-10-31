// Which lanes of .github/workflows/dotnet.yml a run needs, which tests its legs run, and whether the run did what was
// selected (design/adr/0113-ci-runs-the-lanes-a-change-can-affect.md, design/adr/0127-a-change-to-tools-or-tests-runs-the-test-projects-it-reaches.md).
// Four lanes: the Linux test leg, in every run;
// `windows` and `macos`, those test legs; `pack`, the Pack and Consumer jobs. Each changed path selects lanes by
// the first class of Table whose pattern matches it, and a path no class names selects every lane a selection
// can: all but macOS, which only the runs that are not selected get.
//
// Usage:
//   ci-lanes select --event <name> --ref <ref> [--base <branch>] [--before <sha>] [--output <file>] [--summary <file>]
//       A pull request into release/** is selected by `git diff origin/<base>...HEAD`, a push to release/** by
//       `git diff <before> HEAD`; every other run gets every lane, macOS included. Appends `legs=<json array>`,
//       `pack=true|false`, `suite=prose|reach|full` and `projects=<json array>` to --output (the step's GITHUB_OUTPUT),
//       or prints them, and each path's class to --summary. The suite is prose when every changed path is: the Linux
//       leg then builds and runs only the test projects of test/Prose.slnf, the ones that read prose. It is reach when
//       every other path is in a solution project under tools/ or test/, or is the ignore list: the legs then build
//       and run the projects those paths reach (Reach below), which `projects` names, and the whole solution otherwise.
//   ci-lanes classify <path>...
//       One line per path: the path, its class and its lanes, tab-separated.
//   ci-lanes suite <path>...
//       One line per path: the path, the suite a change to it alone runs, and that suite's projects, tab-separated.
//   ci-lanes gate <needs.json> [--summary <file>]
//       Reads the workflow's toJSON(needs) and passes only when the changes job succeeded with answers this tool
//       writes, every job it selected succeeded and every other was skipped.
//
// Exit 0 is a pass, 1 a failed gate or a git command that failed, 2 a usage error, 3 a tool built from other
// sources than the checkout holds, which names the command that rebuilds it.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

Console.Out.NewLine = "\n";
Console.Error.NewLine = "\n";

var root = SourceFingerprint.FindRoot();
var stale = SourceFingerprint.Difference(root);
if (stale is not null)
{
    Console.WriteLine($"TOOL STALE: {SourceFingerprint.Project} was built from other sources than the checkout holds ({stale})");
    Console.WriteLine($"      rebuild it: {SourceFingerprint.RebuildCommand}");
    return SourceFingerprint.StaleExitCode;
}

return args switch
{
    ["select", .. var rest] when Options.Parse(rest, ["event", "ref", "base", "before", "output", "summary"]) is { } options
                                 && options.ContainsKey("event") && options.ContainsKey("ref") => Selection.Run(root!, options),
    ["classify", _, ..] => Classify(args[1..]),
    ["suite", _, ..] => Suite(root!, args[1..]),
    ["gate", var needs, .. var rest] when Options.Parse(rest, ["summary"]) is { } options => Gate.Run(needs, options),
    _ => Usage(),
};

static int Classify(string[] paths)
{
    foreach (var path in paths)
    {
        var @class = Table.Of(path);
        Console.WriteLine($"{path}\t{@class.Name}\t{Table.Describe(@class.Lanes)}");
    }
    return 0;
}

static int Suite(string root, string[] paths)
{
    foreach (var path in paths)
    {
        var suite = Reach.Suite(root, [(path, Table.Of(path))]);
        Console.WriteLine($"{path}\t{suite.Kind}\t{string.Join(", ", suite.Projects)}");
    }
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: dotnet tools/CiLanes/bin/Release/net10.0/ci-lanes.dll");
    Console.Error.WriteLine("         select --event <name> --ref <ref> [--base <branch>] [--before <sha>] [--output <file>] [--summary <file>]");
    Console.Error.WriteLine("         classify <path>...");
    Console.Error.WriteLine("         suite <path>...");
    Console.Error.WriteLine("         gate <needs.json> [--summary <file>]");
    return 2;
}

[Flags]
enum Lane
{
    Linux = 1,
    Windows = 2,
    MacOs = 4,
    Pack = 8,

    // The most a changed path can select: macOS runs only on the release's pull request, main, the nightly and the release.
    Selectable = Linux | Windows | Pack,
    All = Selectable | MacOs,
}

static class Options
{
    // `--name value` pairs, each name one of those allowed and given once; null for anything else.
    public static Dictionary<string, string>? Parse(string[] args, string[] allowed)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal)) return null;
            var name = args[i][2..];
            if (!allowed.Contains(name) || !options.TryAdd(name, args[i + 1])) return null;
        }
        return options;
    }
}

static partial class Table
{
    public sealed record Class(string Name, Lane Lanes, string[] Patterns, string Why);

    // Read by the test projects of test/Prose.slnf alone, which ProseReaderTests holds; built into nothing. The docs
    // site's config (site/) is read by the docs workflow and the conventions tests (decision 116).
    public static readonly Class Prose = new("prose", Lane.Linux, ["docs/**", "design/**", "site/**", ".github/ISSUE_TEMPLATE/**", "**/*.md"],
        "read by the prose suite, built into nothing");

    // Built and tested, never packed. A path of it in a solution project under tools/ or test/ narrows the suite to the
    // projects it reaches; any other (spikes/, test/Directory.Build.props) runs the whole solution.
    public static readonly Class Code = new("code no package reads", Lane.Linux | Lane.Windows, ["test/**", "spikes/**", "tools/**"],
        "built and tested, never packed, and handling paths, line endings and child processes itself");

    // A fresh checkout has nothing it ignores, and the tests list the index (git ls-files), never the working tree: read
    // by git alone, and by the projects that declare they read the root. `.gitattributes` is not this class: it sets the
    // line endings of every file a Windows checkout holds and a package packs.
    public static readonly Class IgnoreList = new("ignore list", Lane.Linux, [".gitignore"],
        "read by git alone; a fresh checkout ignores nothing, and the tests read the index");

    // The first class whose pattern matches a path decides its lanes. `**` matches any number of directories,
    // `*` and `?` stay within one. Decision 113 holds this table; CiLaneTests holds it to the files MSBuild
    // evaluates for the packages and the smoke test.
    public static readonly Class[] Classes =
    [
        new("changelog", Lane.Linux | Lane.Pack, ["CHANGELOG.md"],
            "the packages' release notes are its section, read by a property function no evaluation lists"),
        new("source text", Lane.Selectable, ["src/CodoMetis.TypeKit.Analyzers/**", "src/CodoMetis.TypeKit.Generators/**"],
            "a package that writes or edits source text, whose line endings a Windows checkout changes"),
        new("package", Lane.Linux | Lane.Pack, ["src/**"], "a shipping project"),
        new("smoke test", Lane.Selectable, ["tools/ConsumerSmokeTest/**", "tools/Shared/**", "tools/Directory.Build.*", "tools/CiLanes/**"],
            "the smoke test, what it compiles in, or this selection"),
        Prose,
        Code,
        IgnoreList,
    ];

    public static readonly Class Unlisted = new("unlisted", Lane.Selectable, [], "no class names it");

    static readonly Dictionary<string, Regex> Globs = [];

    public static Class Of(string path) => Classes.FirstOrDefault(c => c.Patterns.Any(p => Glob(p).IsMatch(path))) ?? Unlisted;

    public static string Describe(Lane lanes) =>
        string.Join(", ", new[] { (Lane.Linux, "linux"), (Lane.Windows, "windows"), (Lane.MacOs, "macos"), (Lane.Pack, "pack") }.Where(l => lanes.HasFlag(l.Item1)).Select(l => l.Item2));

    // The test legs and the lane that selects each, in the order the matrix lists them.
    public static readonly (Lane Lane, string Runner)[] Runners = [(Lane.Linux, "ubuntu-latest"), (Lane.Windows, "windows-latest"), (Lane.MacOs, "macos-latest")];

    // The test matrix: one leg per selected test lane. Linux is in every selection.
    public static string[] Legs(Lane lanes) => [.. Runners.Where(r => lanes.HasFlag(r.Lane)).Select(r => r.Runner)];

    static Regex Glob(string pattern)
    {
        if (Globs.TryGetValue(pattern, out var regex)) return regex;

        var builder = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern.AsSpan(i).StartsWith("**/")) { builder.Append("(?:.*/)?"); i += 2; }
            else if (pattern.AsSpan(i).StartsWith("**")) { builder.Append(".*"); i += 1; }
            else if (pattern[i] == '*') builder.Append("[^/]*");
            else if (pattern[i] == '?') builder.Append("[^/]");
            else builder.Append(Regex.Escape(pattern[i].ToString()));
        }
        return Globs[pattern] = new Regex(builder.Append('$').ToString(), RegexOptions.CultureInvariant);
    }
}

static class Selection
{
    public static int Run(string root, Dictionary<string, string> options)
    {
        var @event = options["event"];
        var @ref = options["ref"];
        var target = options.GetValueOrDefault("base", "");
        var before = options.GetValueOrDefault("before", "");

        string? range = null;
        string why;
        if (@event == "pull_request" && target.StartsWith("release/", StringComparison.Ordinal))
        {
            range = $"origin/{target}...HEAD";
            why = $"a pull request into {target}, selected by its diff against origin/{target}";
        }
        else if (@event == "pull_request" && target == "main")
            why = "a pull request into main is the release's, which runs every lane";
        else if (@event == "push" && @ref.StartsWith("refs/heads/release/", StringComparison.Ordinal))
        {
            if (before.Length == 0 || before.All(c => c == '0'))
                why = "the push creates the branch, so there is nothing to diff against";
            else if (Git(root, out _, "cat-file", "-e", $"{before}^{{commit}}") != 0)
                why = $"the clone does not have {before}, the commit the push replaced";
            else
            {
                range = $"{before} HEAD";
                why = $"a push to {@ref["refs/heads/".Length..]}, selected by its diff against {before}";
            }
        }
        else
            why = $"a {@event} of {@ref} runs every lane";

        var lanes = Lane.All;
        var classified = new List<(string Path, Table.Class Class)>();
        if (range is not null)
        {
            var code = Git(root, out var output, ["diff", "--name-only", "--no-renames", "-z", .. range.Split(' '), "--"]);
            if (code != 0)
            {
                Console.WriteLine($"::error::git diff {range} failed with exit {code}, so nothing can be selected: {output.Trim()}");
                return 1;
            }

            var paths = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length == 0)
                why += "; it names no path, so every lane runs";
            else
            {
                classified = [.. paths.Select(path => (path, Table.Of(path)))];
                lanes = classified.Aggregate(Lane.Linux, (all, row) => all | row.Class.Lanes);
            }
        }

        var legs = JsonSerializer.Serialize(Table.Legs(lanes));
        var pack = lanes.HasFlag(Lane.Pack) ? "true" : "false";
        var suite = Reach.Suite(root, classified);
        var projects = JsonSerializer.Serialize(suite.Projects);

        Console.WriteLine($"{char.ToUpperInvariant(why[0])}{why[1..]}.");
        foreach (var (path, @class) in classified) Console.WriteLine($"  {path}: {@class.Name} ({Table.Describe(@class.Lanes)})");
        Console.WriteLine($"Lanes: {Table.Describe(lanes)}. Test legs: {string.Join(", ", Table.Legs(lanes))}. Pack and Consumer: {(pack == "true" ? "run" : "skipped")}. Suite: {suite.Kind}{(suite.Why is null ? "" : $" ({suite.Why})")}.");
        foreach (var project in suite.Projects) Console.WriteLine($"  {project}");

        var answers = $"legs={legs}\npack={pack}\nsuite={suite.Kind}\nprojects={projects}\n";
        if (options.TryGetValue("output", out var file)) File.AppendAllText(file, answers);
        else Console.Write(answers);

        if (options.TryGetValue("summary", out var summary))
        {
            var text = new StringBuilder($"### Lanes: {Table.Describe(lanes)}, suite: {suite.Kind}\n\n{char.ToUpperInvariant(why[0])}{why[1..]}.\n\n");
            if (suite.Why is not null) text.Append($"The suite is the whole solution: {suite.Why}.\n\n");
            if (suite.Projects.Length > 0)
                text.Append("The legs build and test the projects the change reaches:\n\n").AppendJoin("", suite.Projects.Select(p => $"- `{p}`\n")).Append('\n');
            if (classified.Count > 0)
            {
                text.Append("| Path | Class | Lanes |\n|---|---|---|\n");
                foreach (var (path, @class) in classified.Take(200)) text.Append($"| `{path}` | {@class.Name} | {Table.Describe(@class.Lanes)} |\n");
                if (classified.Count > 200) text.Append($"\nand {classified.Count - 200} more paths.\n");
            }
            File.AppendAllText(summary, text.ToString());
        }
        return 0;
    }

    // git in the repository root, its stdout (or stderr, on a failure) as text.
    static int Git(string root, out string output, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        output = process.ExitCode == 0 ? stdout.Result : stderr.Result;
        return process.ExitCode;
    }
}

// The test suite a selected run builds and tests (design/adr/0127). A path reaches a solution project under tools/ or
// test/ when the project reads it: the project's own directory, every path an item or import of its project file names
// outside that directory (a linked source, a declared `<ReadsByPath Include="../" />`), and, through each
// ProjectReference, whatever the referenced project reads. A test that reads a project by path at run time declares
// it with ReadsByPath; TestReachTests (conventions) holds every test project's evaluated inputs and path literals to
// what this reads. Anything that cannot be read, or a path no project holds, runs the whole solution.
static class Reach
{
    public sealed record Answer(string Kind, string[] Projects, string? Why = null);

    const string ProseFilter = "test/Prose.slnf";

    public static Answer Suite(string root, IReadOnlyList<(string Path, Table.Class Class)> classified)
    {
        if (classified.Count == 0) return new("full", [], "no changed path was classified");
        if (classified.All(row => row.Class == Table.Prose)) return new("prose", []);

        var (projects, failure) = Read(root);
        if (projects is null) return new("full", [], failure);

        var suite = new SortedSet<string>(StringComparer.Ordinal);
        if (classified.Any(row => row.Class == Table.Prose))
        {
            var (prose, unread) = ProseProjects(root);
            if (prose is null) return new("full", [], unread);
            suite.UnionWith(prose);
        }

        foreach (var (path, @class) in classified)
        {
            if (@class == Table.Prose) continue;
            if (@class == Table.IgnoreList || (@class == Table.Code && Owner(projects, path) is not null))
            {
                var reached = projects.Where(project => project.Value.Any(read => Under(path, read))).Select(project => project.Key).ToList();
                if (reached.Count == 0) return new("full", [], $"{path} reaches no project");
                suite.UnionWith(reached);
            }
            else
                return new("full", [], @class == Table.Code ? $"{path} is in no solution project under tools/ or test/" : $"{path} is in the class {@class.Name}");
        }
        return new("reach", [.. suite]);
    }

    static string? Owner(Dictionary<string, string[]> projects, string path) =>
        projects.Keys.Where(project => Under(path, DirectoryOf(project))).MaxBy(project => project.Length);

    // The solution's projects under tools/ and test/, each with the paths it reads; null, and why, when a file cannot be read.
    static (Dictionary<string, string[]>?, string?) Read(string root)
    {
        List<string> solution;
        try
        {
            solution = [.. XDocument.Load(Path.Combine(root, "CodoMetis.TypeKit.slnx")).Descendants("Project")
                                    .Select(project => Normalize((string?)project.Attribute("Path") ?? ""))
                                    .Where(path => path.StartsWith("tools/", StringComparison.Ordinal) || path.StartsWith("test/", StringComparison.Ordinal))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return (null, $"CodoMetis.TypeKit.slnx cannot be read ({e.Message})");
        }

        var reads = new Dictionary<string, string[]?>(StringComparer.Ordinal);
        var projects = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var project in solution)
        {
            var read = Reads(root, project, reads, []);
            if (read is null) return (null, $"{project}, or a project it references, cannot be read");
            projects[project] = read;
        }
        return (projects, null);
    }

    // What a project reads: its directory, the paths its project file names outside it, and what each project it references reads.
    static string[]? Reads(string root, string project, Dictionary<string, string[]?> known, HashSet<string> visiting)
    {
        if (known.TryGetValue(project, out var cached)) return cached;
        if (!visiting.Add(project)) return [];

        XDocument document;
        try { document = XDocument.Load(Path.Combine(root, project)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException) { return known[project] = null; }

        var directory = DirectoryOf(project);
        var read = new SortedSet<string>(StringComparer.Ordinal) { directory };
        foreach (var element in document.Descendants())
        {
            var attribute = element.Name.LocalName == "Import" ? "Project" : "Include";
            foreach (var include in ((string?)element.Attribute(attribute) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (include.Contains("$(") || include.Contains("@(") || include.Contains("%(") || include.IndexOfAny(['/', '\\']) < 0) continue;
                if (Resolve(directory, include) is not { } path) continue;
                read.Add(path);

                if (element.Name.LocalName != "ProjectReference") continue;
                if (Reads(root, path, known, visiting) is not { } referenced) return known[project] = null;
                read.UnionWith(referenced);
            }
        }
        visiting.Remove(project);
        return known[project] = [.. read];
    }

    static (string[]?, string?) ProseProjects(string root)
    {
        try
        {
            using var filter = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ProseFilter)));
            return ([.. filter.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray().Select(p => Normalize(p.GetString()!))], null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, $"{ProseFilter} cannot be read ({e.Message})");
        }
    }

    // A path an include names, relative to the root, up to its first wildcard segment; null when it leaves the repository.
    static string? Resolve(string directory, string include)
    {
        var parts = directory.Length == 0 ? new List<string>() : [.. directory.Split('/')];
        foreach (var segment in include.Replace('\\', '/').Split('/'))
        {
            if (segment.IndexOfAny(['*', '?']) >= 0) break;
            if (segment is "" or ".") continue;
            if (segment != "..") parts.Add(segment);
            else if (parts.Count == 0) return null;
            else parts.RemoveAt(parts.Count - 1);
        }
        return string.Join('/', parts);
    }

    static string Normalize(string path) => path.Replace('\\', '/');

    static string DirectoryOf(string project) => project.LastIndexOf('/') is var slash and >= 0 ? project[..slash] : "";

    static bool Under(string path, string read) =>
        read.Length == 0 || path == read || path.StartsWith(read + "/", StringComparison.Ordinal);
}

static partial class Gate
{
    // Every job of dotnet.yml the gate needs, and the lane that selects it: null for the changes job, which
    // selects, and Linux for the test matrix, which runs in every selection.
    static readonly Dictionary<string, Lane?> Jobs = new(StringComparer.Ordinal)
    {
        ["changes"] = null,
        ["test"] = Lane.Linux,
        ["pack"] = Lane.Pack,
        ["consumer"] = Lane.Pack,
    };

    public static int Run(string file, Dictionary<string, string> options)
    {
        Dictionary<string, (string Result, Dictionary<string, string> Outputs)> needs;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            needs = document.RootElement.EnumerateObject().ToDictionary(
                job => job.Name,
                job => (Text(job.Value, "result"),
                        job.Value.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Object
                            ? outputs.EnumerateObject().ToDictionary(o => o.Name, o => o.Value.ValueKind == JsonValueKind.String ? o.Value.GetString()! : o.Value.GetRawText())
                            : []),
                StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine($"{file} cannot be read as the workflow's needs: {e.Message}");
            return 2;
        }

        var problems = new List<string>();
        foreach (var job in needs.Keys.Where(job => !Jobs.ContainsKey(job)))
            problems.Add($"The gate does not know the job '{job}': add it to Gate.Jobs in tools/CiLanes with the lane that selects it.");
        foreach (var job in Jobs.Keys.Where(job => !needs.ContainsKey(job)))
            problems.Add($"The gate does not need the job '{job}', so it cannot judge it: add it to the CI job's needs.");

        Lane? lanes = null;
        var changesResult = needs.TryGetValue("changes", out var changes) ? changes.Result : "missing";
        if (changesResult != "missing")
        {
            if (changes.Result != "success")
                problems.Add($"The changes job ended '{changes.Result}', so nothing it selected can be trusted.");
            else
            {
                var legs = Legs(changes.Outputs.GetValueOrDefault("legs"));
                var pack = changes.Outputs.GetValueOrDefault("pack");
                var suite = changes.Outputs.GetValueOrDefault("suite");
                var projects = Projects(changes.Outputs.GetValueOrDefault("projects"));
                if (legs is null)
                    problems.Add($"The changes job's legs output is {Quote(changes.Outputs.GetValueOrDefault("legs"))}, not one of the three matrices ci-lanes writes.");
                if (pack is not ("true" or "false"))
                    problems.Add($"The changes job's pack output is {Quote(pack)}, not 'true' or 'false'.");
                if (projects is null)
                    problems.Add($"The changes job's projects output is {Quote(changes.Outputs.GetValueOrDefault("projects"))}, not a JSON array of project files.");
                if (suite is not ("prose" or "reach" or "full"))
                    problems.Add($"The changes job's suite output is {Quote(suite)}, not 'prose', 'reach' or 'full'.");
                else if (suite == "prose" && (legs != Lane.Linux || pack != "false"))
                    problems.Add("The changes job chose the prose suite for a run with more than the Linux leg: only a prose-only change runs it.");
                else if (suite == "reach" && (legs is not (Lane.Linux or (Lane.Linux | Lane.Windows)) || pack != "false"))
                    problems.Add("The changes job chose the reached suite for a run with the pack or the macOS leg: only a change to tools, tests, prose or the ignore list runs it.");
                if (projects is not null && suite == "reach" && (projects.Length == 0 || projects.Distinct(StringComparer.Ordinal).Count() != projects.Length || !projects.All(ProjectFile().IsMatch)))
                    problems.Add($"The changes job chose the reached suite with the projects {changes.Outputs["projects"]}, not one or more distinct project files under tools/ or test/: a filter of nothing builds and tests nothing, and passes.");
                if (projects is { Length: > 0 } && suite is "prose" or "full")
                    problems.Add($"The changes job named projects for the {suite} suite, which tests a filter of its own.");
                if (legs is not null && pack is ("true" or "false"))
                    lanes = legs.Value | (pack == "true" ? Lane.Pack : 0);
            }
        }

        var rows = new List<string>();
        foreach (var (job, lane) in Jobs.Where(j => j.Value is not null))
        {
            if (!needs.TryGetValue(job, out var need)) continue;
            var selected = lanes is { } l ? l.HasFlag(lane!.Value) : (bool?)null;
            rows.Add($"| {job} | {(selected is null ? "unknown" : selected.Value ? "yes" : "no")} | {need.Result} |");
            if (selected is null) continue;

            var expected = selected.Value ? "success" : "skipped";
            if (need.Result != expected)
                problems.Add(selected.Value
                    ? $"The job '{job}' was selected and ended '{need.Result}'."
                    : $"The job '{job}' was not selected and ended '{need.Result}', not skipped: its condition no longer reads the selection.");
        }

        if (options.TryGetValue("summary", out var summary))
        {
            var text = new StringBuilder($"### Gate: {(problems.Count == 0 ? "passed" : "failed")}\n\n");
            text.Append($"Changes: {changesResult}, lanes: {(lanes is { } s ? Table.Describe(s) : "unknown")}.\n\n");
            text.Append("| Job | Selected | Result |\n|---|---|---|\n").AppendJoin('\n', rows).Append('\n');
            foreach (var problem in problems) text.Append($"\n- {problem}");
            File.AppendAllText(summary, text.Append('\n').ToString());
        }

        if (problems.Count == 0)
        {
            Console.WriteLine($"Every selected job passed and every other was skipped (lanes: {Table.Describe(lanes!.Value)}).");
            return 0;
        }
        foreach (var problem in problems) Console.WriteLine($"::error::{problem}");
        return 1;
    }

    // The test lanes of every matrix Selection writes: Linux alone, with Windows, and every leg on a run that is not selected.
    static readonly Lane[] Matrices = [Lane.Linux, Lane.Linux | Lane.Windows, Lane.Linux | Lane.Windows | Lane.MacOs];

    // The lanes a legs output selects, when it is exactly one of the matrices Selection writes; null otherwise.
    static Lane? Legs(string? legs)
    {
        string[]? parsed;
        try { parsed = legs is null ? null : JsonSerializer.Deserialize<string[]>(legs); }
        catch (JsonException) { return null; }

        if (parsed is null) return null;
        foreach (var lanes in Matrices)
            if (parsed.SequenceEqual(Table.Legs(lanes))) return lanes;
        return null;
    }

    // The projects output: a JSON array of strings; null for anything else.
    static string[]? Projects(string? projects)
    {
        try { return projects is null ? null : JsonSerializer.Deserialize<string[]>(projects); }
        catch (JsonException) { return null; }
    }

    // tools/<Name>/<Name>.csproj or test/<Project>/<Project>.csproj: one directory deep, no `..`.
    [GeneratedRegex(@"^(?:tools|test)/(?!\.\.?/)[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\.csproj$")]
    private static partial Regex ProjectFile();

    static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    static string Quote(string? value) => value is null ? "missing" : $"'{value}'";
}
