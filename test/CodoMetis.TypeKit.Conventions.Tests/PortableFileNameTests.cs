using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every tracked path checks out on Windows and macOS as it does on Linux. A prose-only change runs the Linux leg
/// alone (design/adr/0113), so a file name that Windows refuses, or two names a case-insensitive file system takes
/// for one, would otherwise first fail on the release's pull request (#73).
/// </summary>
/// <remarks>
/// Read from git's index (<c>git ls-files</c>), which can hold names this file system cannot: a test that walked the
/// directory would never see the second of two names that differ only in case.
/// </remarks>
public sealed partial class PortableFileNameTests
{
    /// <summary>
    /// The longest repository-relative path allowed. Windows' MAX_PATH is 260; a checkout's own directory takes
    /// about 40 characters on a hosted runner and up to about 90 in a worktree under <c>.claude/worktrees/</c>, and
    /// the longest tracked path was 102 on 2026-10-29.
    /// </summary>
    private const int LongestPath = 160;

    [Fact]
    public void Every_tracked_path_is_a_name_Windows_and_macOS_accept() =>
        Problems(TrackedPaths()).ShouldBeEmpty("These tracked paths do not check out on every platform the tests run on.");

    [Fact]
    public void The_index_is_read() => TrackedPaths().ShouldContain("CodoMetis.TypeKit.slnx");

    [Theory]
    [InlineData("docs/a:b.md", "a character Windows refuses")]
    [InlineData("docs/what?.md", "a character Windows refuses")]
    [InlineData("docs/tab\there.md", "a character Windows refuses")]
    [InlineData("docs/back\\slash.md", "a character Windows refuses")]
    [InlineData("docs/trailing.", "ends in a dot or a space")]
    [InlineData("docs/trailing /x.md", "ends in a dot or a space")]
    [InlineData("docs/con.md", "a device name Windows reserves")]
    [InlineData("src/LPT1/x.cs", "a device name Windows reserves")]
    [InlineData("docs/Aux", "a device name Windows reserves")]
    public void A_name_Windows_refuses_is_found(string path, string problem) =>
        Problems([path]).ShouldContain(found => found.Contains(problem, StringComparison.Ordinal));

    [Fact]
    public void Two_names_that_differ_only_in_case_are_found() =>
        Problems(["docs/Guide.md", "docs/guide.md"]).ShouldContain(found => found.Contains("the same name to a case-insensitive", StringComparison.Ordinal));

    [Fact]
    public void Two_directories_that_differ_only_in_case_are_found() =>
        Problems(["Docs/a.md", "docs/b.md"]).ShouldContain(found => found.Contains("Docs", StringComparison.Ordinal) && found.Contains("docs", StringComparison.Ordinal));

    [Fact]
    public void Two_names_that_differ_only_in_normalization_are_found() =>
        Problems(["docs/caf\u00e9.md", "docs/cafe\u0301.md"]).ShouldNotBeEmpty();

    [Fact]
    public void A_path_too_long_is_found() =>
        Problems([$"docs/{new string('a', LongestPath)}.md"]).ShouldContain(found => found.Contains("characters long", StringComparison.Ordinal));

    [Theory]
    [InlineData("docs/console.md")]
    [InlineData("docs/a.b.c.md")]
    [InlineData(".github/workflows/dotnet.yml")]
    [InlineData("design/adr/0113-ci-runs-the-lanes-a-change-can-affect.md")]
    public void An_ordinary_name_passes(string path) => Problems([path]).ShouldBeEmpty();

    private static List<string> Problems(IReadOnlyList<string> paths)
    {
        var problems = new List<string>();
        foreach (var path in paths)
        {
            if (path.Length > LongestPath)
                problems.Add($"{path} is {path.Length} characters long, over {LongestPath}: with a checkout's own directory it passes Windows' MAX_PATH");

            foreach (var segment in path.Split('/'))
            {
                if (segment.Any(c => c < ' ' || "<>:\"|?*\\".Contains(c)))
                    problems.Add($"{path} holds a character Windows refuses in a file name (<>:\"|?*\\ or a control character)");
                if (segment.EndsWith('.') || segment.EndsWith(' '))
                    problems.Add($"{path}: '{segment}' ends in a dot or a space, which Windows drops");
                if (DeviceName().IsMatch(segment))
                    problems.Add($"{path}: '{segment}' is a device name Windows reserves, with any extension");
            }
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in paths.SelectMany(Prefixes).Distinct(StringComparer.Ordinal))
        {
            var folded = name.Normalize(NormalizationForm.FormC).ToUpperInvariant();
            if (names.TryGetValue(folded, out var other))
                problems.Add($"{other} and {name} are the same name to a case-insensitive or normalizing file system (Windows, macOS)");
            else
                names[folded] = name;
        }

        return problems;
    }

    // The path and each directory above it: two directories that differ in case collide as two files do.
    private static IEnumerable<string> Prefixes(string path)
    {
        for (var slash = path.IndexOf('/'); slash > 0; slash = path.IndexOf('/', slash + 1)) yield return path[..slash];
        yield return path;
    }

    private static List<string> TrackedPaths()
    {
        using var process = Process.Start(new ProcessStartInfo("git", ["-C", Repository.Root, "-c", "core.quotePath=false", "ls-files", "-z"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
        })!;

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.ShouldBe(0, $"git ls-files failed: {errors.Result}");
        return [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceName();
}
