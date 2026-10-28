using System.Text.Json;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every tool an agent runs is built when its session starts, in the checkout it works in, with a dotnet that has the
/// SDK global.json pins, and an agent is told when none could (<c>design/adr/0105-every-tool-is-built-in-the-session-s-checkout.md</c>).
/// </summary>
/// <remarks>
/// Measured over the sessions of 2026-10-15 and 2026-10-16: 70 of about 100 session starts failed their builds, because
/// Homebrew's dotnet (10.0.401) was first on the hook's PATH, and every session in a worktree found a tool missing,
/// because the hook built in <c>$CLAUDE_PROJECT_DIR</c>, which in such a session is the main checkout.
/// </remarks>
public sealed class SessionStartHookTests
{
    private const string Filter = "tools/AgentTools.slnf";

    private static IReadOnlyList<string> Commands()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, ".claude", "settings.json")));
        return
        [
            .. settings.RootElement.GetProperty("hooks").GetProperty("SessionStart").EnumerateArray()
                       .SelectMany(group => group.GetProperty("hooks").EnumerateArray())
                       .Select(hook => hook.GetProperty("command").GetString() ?? "")
        ];
    }

    /// <summary>Every tool project but the guard, discovered from tools/ so a new tool needs no edit here.</summary>
    public static TheoryData<string> Tools =>
    [
        .. Directory.GetDirectories(Path.Combine(Repository.Root, "tools"))
                    .Select(Path.GetFileName).OfType<string>()
                    .Where(name => File.Exists(Path.Combine(Repository.Root, "tools", name, $"{name}.csproj")) && name != "BashGuard")
                    .Order(StringComparer.Ordinal)
    ];

    [Fact]
    public void The_tools_are_found() => Tools.Select(row => row.Data).ShouldContain("Oracles");

    [Theory, MemberData(nameof(Tools))]
    public void Every_tool_is_in_the_session_start_filter(string tool)
    {
        using var filter = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, Filter)));
        filter.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray().Select(p => p.GetString())
              .ShouldContain($"tools/{tool}/{tool}.csproj", $"{Filter} lists every tool an agent runs, so a session starts with it built");
    }

    [Fact]
    public void The_tools_are_built_in_the_sessions_checkout()
    {
        var command = Commands().SingleOrDefault(c => c.Contains(Filter, StringComparison.Ordinal));
        command.ShouldNotBeNull($"a SessionStart hook builds {Filter}");
        command.ShouldNotContain("$CLAUDE_PROJECT_DIR/" + Filter, Case.Sensitive,
            "relative to the session's directory: in a worktree session $CLAUDE_PROJECT_DIR is the main checkout");
    }

    [Fact]
    public void The_guard_is_built_where_the_hook_runs_it() =>
        Commands().ShouldContain(c => c.StartsWith("dotnet build \"$CLAUDE_PROJECT_DIR/tools/BashGuard/", StringComparison.Ordinal),
                                 "the PreToolUse hook runs the guard from $CLAUDE_PROJECT_DIR");

    [Fact]
    public void Every_build_falls_back_to_the_installers_dotnet_and_says_when_none_builds()
    {
        foreach (var command in Commands())
        {
            var steps = command.Split(" || ");
            steps.Length.ShouldBe(3, command);
            steps[0].ShouldStartWith("dotnet build ", Case.Sensitive, command);
            steps[1].ShouldBe($"PATH=\"/usr/local/share/dotnet:$PATH\" {steps[0].Replace(" >/dev/null 2>&1", "", StringComparison.Ordinal)}",
                              "the same build with the SDK installer's dotnet first, as the verdict tool falls back");
            steps[2].ShouldStartWith("echo \"Session start: ", Case.Sensitive, "said on stdout, which the agent reads, with the hook exiting 0");
        }
    }
}
