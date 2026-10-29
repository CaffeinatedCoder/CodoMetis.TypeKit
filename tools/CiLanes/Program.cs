// Which lanes of .github/workflows/dotnet.yml a run needs, and whether the run did what was selected
// (design/adr/0113-ci-runs-the-lanes-a-change-can-affect.md). Three lanes: the Linux test leg, in every run;
// `os`, the Windows and macOS test legs; `pack`, the Pack and Consumer jobs. Each changed path selects lanes by
// the first class of Table whose pattern matches it, and a path no class names selects every lane.
//
// Usage:
//   ci-lanes select --event <name> --ref <ref> [--base <branch>] [--before <sha>] [--output <file>] [--summary <file>]
//       A pull request into release/** is selected by `git diff origin/<base>...HEAD`, a push to release/** by
//       `git diff <before> HEAD`; every other run gets every lane. Appends `legs=<json array>` and `pack=true|false`
//       to --output (the step's GITHUB_OUTPUT), or prints them, and each path's class to --summary.
//   ci-lanes classify <path>...
//       One line per path: the path, its class and its lanes, tab-separated.
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

static int Usage()
{
    Console.Error.WriteLine("Usage: dotnet tools/CiLanes/bin/Release/net10.0/ci-lanes.dll");
    Console.Error.WriteLine("         select --event <name> --ref <ref> [--base <branch>] [--before <sha>] [--output <file>] [--summary <file>]");
    Console.Error.WriteLine("         classify <path>...");
    Console.Error.WriteLine("         gate <needs.json> [--summary <file>]");
    return 2;
}

[Flags]
enum Lane
{
    Linux = 1,
    Os = 2,
    Pack = 4,
    All = Linux | Os | Pack,
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

    // The first class whose pattern matches a path decides its lanes. `**` matches any number of directories,
    // `*` and `?` stay within one. Decision 113 holds this table; ReleaseWiringTests holds it to the files MSBuild
    // evaluates for the packages and the smoke test.
    public static readonly Class[] Classes =
    [
        new("changelog", Lane.Linux | Lane.Pack, ["CHANGELOG.md"],
            "the packages' release notes are its section, read by a property function no evaluation lists"),
        new("package", Lane.All, ["src/**"], "a shipping project"),
        new("smoke test", Lane.All, ["tools/ConsumerSmokeTest/**", "tools/Shared/**", "tools/Directory.Build.*", "tools/CiLanes/**"],
            "the smoke test, what it compiles in, or this selection"),
        new("prose", Lane.Linux, ["docs/**", "design/**", ".github/ISSUE_TEMPLATE/**", "**/*.md"],
            "read by the Linux suite, built into nothing"),
        new("code no package reads", Lane.Linux | Lane.Os, ["test/**", "spikes/**", "tools/**"], "built and tested, never packed"),
    ];

    public static readonly Class Unlisted = new("unlisted", Lane.All, [], "no class names it");

    static readonly Dictionary<string, Regex> Globs = [];

    public static Class Of(string path) => Classes.FirstOrDefault(c => c.Patterns.Any(p => Glob(p).IsMatch(path))) ?? Unlisted;

    public static string Describe(Lane lanes) =>
        string.Join(", ", new[] { (Lane.Linux, "linux"), (Lane.Os, "os"), (Lane.Pack, "pack") }.Where(l => lanes.HasFlag(l.Item1)).Select(l => l.Item2));

    // The test matrix a selection runs: the Linux leg alone, or the three.
    public static readonly string[] LinuxLeg = ["ubuntu-latest"];
    public static readonly string[] EveryLeg = ["ubuntu-latest", "windows-latest", "macos-latest"];

    public static string[] Legs(Lane lanes) => lanes.HasFlag(Lane.Os) ? EveryLeg : LinuxLeg;

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

        Console.WriteLine($"{char.ToUpperInvariant(why[0])}{why[1..]}.");
        foreach (var (path, @class) in classified) Console.WriteLine($"  {path}: {@class.Name} ({Table.Describe(@class.Lanes)})");
        Console.WriteLine($"Lanes: {Table.Describe(lanes)}. Test legs: {string.Join(", ", Table.Legs(lanes))}. Pack and Consumer: {(pack == "true" ? "run" : "skipped")}.");

        var answers = $"legs={legs}\npack={pack}\n";
        if (options.TryGetValue("output", out var file)) File.AppendAllText(file, answers);
        else Console.Write(answers);

        if (options.TryGetValue("summary", out var summary))
        {
            var text = new StringBuilder($"### Lanes: {Table.Describe(lanes)}\n\n{char.ToUpperInvariant(why[0])}{why[1..]}.\n\n");
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

static class Gate
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
                if (legs is null)
                    problems.Add($"The changes job's legs output is {Quote(changes.Outputs.GetValueOrDefault("legs"))}, not one of the two matrices ci-lanes writes.");
                if (pack is not ("true" or "false"))
                    problems.Add($"The changes job's pack output is {Quote(pack)}, not 'true' or 'false'.");
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

    // The lanes a legs output selects, when it is exactly one of the matrices Selection writes; null otherwise.
    static Lane? Legs(string? legs)
    {
        string[]? parsed;
        try { parsed = legs is null ? null : JsonSerializer.Deserialize<string[]>(legs); }
        catch (JsonException) { return null; }

        if (parsed is null) return null;
        if (parsed.SequenceEqual(Table.EveryLeg)) return Lane.Linux | Lane.Os;
        if (parsed.SequenceEqual(Table.LinuxLeg)) return Lane.Linux;
        return null;
    }

    static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    static string Quote(string? value) => value is null ? "missing" : $"'{value}'";
}
