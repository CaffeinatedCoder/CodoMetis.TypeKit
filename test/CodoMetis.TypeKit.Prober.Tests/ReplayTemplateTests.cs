using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// The replay's templates name only what the repository has. A tool that moves (a script ported to a tool
/// project) must not leave the replay starting a file that is gone: that fails only on the next replay, far
/// from the commit that caused it.
/// </summary>
public sealed class ReplayTemplateTests
{
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CodoMetis.TypeKit.slnx"))) return dir.FullName;
        throw new InvalidOperationException("no CodoMetis.TypeKit.slnx above the test output");
    }

    /// <summary>
    /// Every path a template names: a file must be tracked in the repository; a dll under a project's bin/ must
    /// come from a project there that a "prepare" command builds (a fresh worktree has no build output).
    /// </summary>
    internal static List<string> Problems(string root, JsonObject replay)
    {
        var problems = new List<string>();
        var prepared = (replay["prepare"] as JsonArray ?? []).OfType<JsonArray>()
            .SelectMany(c => c.Select(a => a?.GetValue<string>() ?? "")).ToHashSet(StringComparer.Ordinal);
        var templates = new List<(string Name, IEnumerable<string> Args)>();
        foreach (var name in new[] { "build", "test" })
            templates.Add((name, (replay[name] as JsonArray ?? []).Select(a => a?.GetValue<string>() ?? "")));
        foreach (var (i, command) in (replay["prepare"] as JsonArray ?? []).OfType<JsonArray>().Index())
            templates.Add(($"prepare[{i}]", command.Select(a => a?.GetValue<string>() ?? "")));
        if (replay["enumerate"]?["script"]?.GetValue<string>() is { } script) templates.Add(("enumerate", [script]));

        foreach (var (name, args) in templates)
            foreach (var arg in args.Where(a => a.Contains('/') && !a.Contains('{')))
            {
                var bin = arg.IndexOf("/bin/", StringComparison.Ordinal);
                if (bin >= 0)
                {
                    var project = arg[..bin];
                    if (!Directory.Exists(Path.Combine(root, project)) || !Directory.EnumerateFiles(Path.Combine(root, project), "*.csproj").Any())
                        problems.Add($"{name}: {arg} comes from {project}, which is no project in the repository");
                    else if (!prepared.Contains(project))
                        problems.Add($"{name}: {arg} is build output of {project}, which no \"prepare\" command builds");
                }
                else if (!File.Exists(Path.Combine(root, arg)) && !Directory.Exists(Path.Combine(root, arg)))
                    problems.Add($"{name}: {arg} is not in the repository");
            }
        return problems;
    }

    private static JsonObject RepositoryReplay(string root) =>
        Axes.Block(Path.Combine(root, ".claude", "probe-axes.md"))["replay"] as JsonObject ?? throw new InvalidOperationException("no replay object");

    [Fact]
    public void The_replay_templates_name_only_what_the_repository_has()
    {
        var root = RepositoryRoot();
        Problems(root, RepositoryReplay(root)).ShouldBeEmpty();
    }

    [Fact]
    public void A_template_naming_a_removed_script_is_caught()
    {
        // The seed: the templates as they were before the verdict script became a tool project.
        var root = RepositoryRoot();
        var replay = (JsonObject)RepositoryReplay(root).DeepClone();
        replay["build"] = new JsonArray("tools/build-verdict.sh", "build", "{project}");
        Problems(root, replay).ShouldContain(p => p.Contains("tools/build-verdict.sh is not in the repository", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tool_dll_no_prepare_command_builds_is_caught()
    {
        var root = RepositoryRoot();
        var replay = (JsonObject)RepositoryReplay(root).DeepClone();
        replay.Remove("prepare");
        Problems(root, replay).ShouldContain(p => p.Contains("which no \"prepare\" command builds", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bare_command_is_found_on_PATH_and_a_path_must_exist_in_the_worktree()
    {
        var worktree = Path.Combine(Path.GetTempPath(), "prober-exe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(worktree, "tools"));
        try
        {
            DotNetAdapter.Executable(worktree, "dotnet").ShouldBe("dotnet");
            File.WriteAllText(Path.Combine(worktree, "tools", "run.sh"), "");
            DotNetAdapter.Executable(worktree, "tools/run.sh").ShouldBe(Path.Combine(worktree, "tools/run.sh"));
            Should.Throw<Refusal>(() => DotNetAdapter.Executable(worktree, "tools/build-verdict.sh"))
                  .Message.ShouldContain("tools/build-verdict.sh");
        }
        finally { Directory.Delete(worktree, recursive: true); }
    }
}
