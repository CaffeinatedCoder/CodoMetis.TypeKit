// Checks a pull_request event payload against CONTRIBUTING's "Issues and labels"
// (design/adr/0109-work-is-tracked-as-labelled-issues-and-ci-checks-each-pull-request.md):
//   - exactly one type label: bug, enhancement, documentation, tests or chore;
//   - a bug carries exactly one severity/* label, and `silent` is only ever a bug;
//   - the description closes its issue with one of GitHub's closing keywords ("Closes #12");
//   - the head branch is named <type>/<slug> (decision 94), and its type agrees with the type label;
//   - a pull request into release/<version>, or of release/<version> into main, has the milestone <version>;
//   - a `breaking` pull request goes into a major (x.0.0): ApiCompat holds every other version to the
//     surface of the one before.
// It reads the payload alone: no token, no network, nothing of the pull request's code run. Dependabot's
// pull requests carry their own labels and close no issue, so they are not checked.
//
// Usage:
//   dotnet tools/PrHygiene/bin/Release/net10.0/pr-hygiene.dll <event.json>
//
// Each problem is printed as a GitHub annotation (::error::), so the run's page lists them. Exit 0 is a
// pass, 1 a pull request with a problem, 2 a usage error or a payload without a pull request, 3 a tool built
// from other sources than the checkout holds, which names the command that rebuilds it.
using System.Text.Json;
using System.Text.RegularExpressions;

Console.Out.NewLine = "\n";
Console.Error.NewLine = "\n";

var stale = SourceFingerprint.Difference(SourceFingerprint.FindRoot());
if (stale is not null)
{
    Console.WriteLine($"TOOL STALE: {SourceFingerprint.Project} was built from other sources than the checkout holds ({stale})");
    Console.WriteLine($"      rebuild it: {SourceFingerprint.RebuildCommand}");
    return SourceFingerprint.StaleExitCode;
}

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet tools/PrHygiene/bin/Release/net10.0/pr-hygiene.dll <event.json>");
    return 2;
}

PullRequest pr;
try
{
    using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
    if (!document.RootElement.TryGetProperty("pull_request", out var element) || element.ValueKind != JsonValueKind.Object)
    {
        Console.Error.WriteLine($"{args[0]} is no pull_request event: it has no pull_request object.");
        return 2;
    }
    pr = PullRequest.Read(element);
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine($"{args[0]} cannot be read as an event payload: {e.Message}");
    return 2;
}

if (pr.Author == "dependabot[bot]")
{
    Console.WriteLine("Dependabot pull request, not checked.");
    return 0;
}

var problems = Hygiene.Problems(pr);
if (problems.Count == 0)
{
    Console.WriteLine("Labels, milestone, branch and issue link are in order.");
    return 0;
}
foreach (var problem in problems) Console.WriteLine($"::error::{problem}");
Console.WriteLine("See CONTRIBUTING.md, \"Issues and labels\".");
return 1;

sealed record PullRequest(string Author, IReadOnlyList<string> Labels, string Body, string Head, string Base, string? Milestone)
{
    public static PullRequest Read(JsonElement pr) => new(
        Author: Text(pr, "user", "login") ?? "",
        Labels: pr.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
            ? [.. labels.EnumerateArray().Select(label => Text(label, "name")).OfType<string>()]
            : [],
        Body: Text(pr, "body") ?? "",
        Head: Text(pr, "head", "ref") ?? "",
        Base: Text(pr, "base", "ref") ?? "",
        Milestone: Text(pr, "milestone", "title"));

    // The string at the path, or null where a step is missing or null (GitHub writes "body": null for an empty description).
    static string? Text(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}

static partial class Hygiene
{
    static readonly string[] Types = ["bug", "enhancement", "documentation", "tests", "chore"];

    // A branch's <type>, as decision 94 names it after the main commit's conventional type, and the type label that goes with it.
    static readonly Dictionary<string, string> TypeOfBranch = new(StringComparer.Ordinal)
    {
        ["feat"] = "enhancement",
        ["fix"] = "bug",
        ["docs"] = "documentation",
        ["test"] = "tests",
        ["build"] = "chore",
        ["refactor"] = "chore",
        ["spike"] = "chore",
        ["release"] = "chore",
    };

    // GitHub's closing keywords, before #n or owner/repo#n. Only these link a pull request to the issue it closes.
    [GeneratedRegex(@"\b(close[sd]?|fix(e[sd])?|resolve[sd]?):? +([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)?#[0-9]+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Closing();

    [GeneratedRegex(@"^release/(?<version>(?<major>[0-9]+)\.(?<minor>[0-9]+)\.(?<patch>[0-9]+))$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseBranch();

    public static List<string> Problems(PullRequest pr)
    {
        var problems = new List<string>();
        var labels = pr.Labels.ToHashSet(StringComparer.Ordinal);

        var types = Types.Where(labels.Contains).ToList();
        if (types.Count != 1)
            problems.Add($"Needs exactly one type label ({string.Join(", ", Types)}); it has {types.Count}{(types.Count > 1 ? $": {string.Join(", ", types)}" : "")}.");

        var severities = pr.Labels.Count(label => label.StartsWith("severity/", StringComparison.Ordinal));
        if (labels.Contains("bug") && severities != 1)
            problems.Add($"A bug needs exactly one severity/* label (critical, high, medium, low); it has {severities}.");
        if (labels.Contains("silent") && !labels.Contains("bug"))
            problems.Add("Only a bug is silent: label it bug, or take silent off.");

        if (!Closing().IsMatch(pr.Body))
            problems.Add("The description needs a closing reference to its issue, such as 'Closes #12'.");

        var slash = pr.Head.IndexOf('/', StringComparison.Ordinal);
        var branchType = slash > 0 ? pr.Head[..slash] : null;
        if (branchType is null || slash == pr.Head.Length - 1 || !TypeOfBranch.TryGetValue(branchType, out var expected))
            problems.Add($"The branch '{pr.Head}' is not named <type>/<slug>, where the type is one of {string.Join(", ", TypeOfBranch.Keys)} (decision 94).");
        else if (types.Count == 1 && types[0] != expected)
            problems.Add($"The branch '{pr.Head}' is a {branchType} branch, which is labelled {expected}, not {types[0]}.");

        var release = ReleaseBranch().Match(pr.Base);
        if (!release.Success && pr.Base == "main") release = ReleaseBranch().Match(pr.Head);
        if (release.Success)
        {
            var version = release.Groups["version"].Value;
            if (pr.Milestone != version)
                problems.Add($"A pull request into {version} has the milestone {version}; it has {(pr.Milestone is null ? "none" : $"'{pr.Milestone}'")}.");
            if (labels.Contains("breaking") && (release.Groups["minor"].Value != "0" || release.Groups["patch"].Value != "0"))
                problems.Add($"A breaking change needs a major, and {version} is not one: it waits for the next x.0.0.");
        }

        return problems;
    }
}
