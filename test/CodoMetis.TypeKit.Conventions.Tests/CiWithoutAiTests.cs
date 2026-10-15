using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// CI never depends on an AI being available (<c>design/adr/0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md</c>):
/// no workflow runs an agent or a model's API, or needs a key for one. AI produces committed artifacts,
/// and CI checks them with the .NET SDK alone.
/// </summary>
/// <remarks>
/// A workflow is refused when it names an AI CLI, action or vendor (<c>claude</c>,
/// <c>anthropics/claude-code-action</c>, <c>openai</c>, ...) or a secret whose name says it is a key for
/// one (<c>secrets.ANTHROPIC_API_KEY</c>, <c>secrets.MODEL_TOKEN</c>, <c>secrets.AI_KEY</c>), comments
/// included: a commented-out step is one edit away from running.
/// </remarks>
public sealed partial class CiWithoutAiTests
{
    private static readonly string Workflows = Path.Combine(Repository.Root, ".github", "workflows");

    private static IReadOnlyList<string> Files =>
    [
        .. Directory.EnumerateFiles(Workflows, "*.yml").Concat(Directory.EnumerateFiles(Workflows, "*.yaml")).Order(StringComparer.Ordinal)
    ];

    public static TheoryData<string> WorkflowNames => [.. Files.Select(Path.GetFileName).OfType<string>()];

    [Fact]
    public void The_workflows_are_found()
    {
        Files.Select(Path.GetFileName).ShouldContain("dotnet.yml");
        Files.Select(Path.GetFileName).ShouldContain("release.yml");
    }

    [Theory]
    [MemberData(nameof(WorkflowNames))]
    public void A_workflow_names_no_AI(string workflow)
    {
        var lines = File.ReadAllLines(Path.Combine(Workflows, workflow));

        var found = lines.Select((line, index) => (Line: index + 1, Text: line))
                         .Where(line => AiName().IsMatch(line.Text) || AiSecret().Matches(line.Text).Any(IsAiSecret))
                         .Select(line => $"{workflow}:{line.Line}: {line.Text.Trim()}")
                         .ToArray();

        found.ShouldBeEmpty("CI never depends on an AI: no workflow runs an agent or a model's API, or needs a key for one (design/adr/0092).");
    }

    private static readonly string[] AiWords = ["AI", "LLM", "GPT", "MODEL", "CLAUDE", "ANTHROPIC", "OPENAI", "GEMINI", "COPILOT"];

    private static bool IsAiSecret(Match secret) =>
        secret.Groups["name"].Value.ToUpperInvariant().Split('_').Any(word => AiWords.Contains(word, StringComparer.Ordinal));

    [GeneratedRegex(@"\b(claude|anthropic|openai|gemini|copilot|codex|mistral|ollama|huggingface|aider)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex AiName();

    [GeneratedRegex(@"secrets\.(?<name>[A-Za-z0-9_]+)")]
    private static partial Regex AiSecret();
}
