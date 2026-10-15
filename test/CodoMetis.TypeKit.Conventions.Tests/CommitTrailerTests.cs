using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every <c>fix:</c> commit since v2.1.0 names its defect class and its finder in two trailers,
/// <c>Defect-Class:</c> and <c>Found-By:</c>, from the vocabulary the fix history counted with
/// (<c>design/adr/0094-topic-branches-into-a-release-branch-and-fix-trailers.md</c>; <c>VA</c>, the variant step of
/// another fix, from <c>design/adr/0095-a-fix-is-tested-across-its-input-class.md</c>).
/// </summary>
/// <remarks>
/// The trailers are read by git's own trailer parser, so a line git does not take for a trailer (one
/// outside the message's last paragraph) does not count. A shallow clone, or one without the tag, is
/// refused rather than read: it would hold no fix commit, and the check would pass on nothing.
/// </remarks>
public sealed partial class CommitTrailerTests
{
    /// <summary>The last release before the trailers: the 2.2.0 cycle is the first that carries them.</summary>
    private const string Since = "v2.1.0";

    private static readonly HashSet<string> DefectClasses =
        new(["FCI", "GTS", "AIS", "EFT", "API", "POS", "DOC", "TXT", "AOT", "SDL", "CRP", "TOOL"], StringComparer.Ordinal);

    private static readonly HashSet<string> Finders =
        new(["RS", "CA", "PR", "ST", "OR", "US", "MS", "FR", "SF", "VA", "UN"], StringComparer.Ordinal);

    /// <summary>
    /// Fix commits that reached a pushed branch without their trailers, which is never rewritten: the full
    /// hash, and the trailers the commit should have carried.
    /// </summary>
    private static readonly Dictionary<string, (string DefectClass, string FoundBy)> Exempt = new(StringComparer.Ordinal);

    [Fact]
    public void The_history_is_whole() => RequireWholeHistory();

    [Fact]
    public void Every_fix_since_2_1_0_names_its_defect_class_and_its_finder()
    {
        RequireWholeHistory();

        var problems = FixCommits(Repository.Root, $"{Since}..HEAD")
            .Where(commit => !Exempt.ContainsKey(commit.Hash))
            .SelectMany(Problems)
            .ToArray();

        problems.ShouldBeEmpty(
            "A fix: commit carries `Defect-Class:` and `Found-By:` in its trailer block, one of each, from the tables of " +
            "design/adr/0094 and 0095. Amend it before the branch is pushed; once pushed, add its hash to Exempt with the " +
            "trailers it should have carried.");
    }

    [Fact]
    public void The_exemptions_use_the_vocabulary()
    {
        foreach (var (hash, (defectClass, foundBy)) in Exempt)
        {
            DefectClasses.ShouldContain(defectClass, $"Exempt {hash}");
            Finders.ShouldContain(foundBy, $"Exempt {hash}");
        }
    }

    /// <summary>
    /// The positive control: a throwaway repository with a fix commit of each kind, so an empty finding on
    /// the real history means none, not a reader that sees nothing.
    /// </summary>
    [Fact]
    public void The_reader_finds_each_kind_of_fix_commit()
    {
        var repository = Directory.CreateTempSubdirectory("codometis-typekit-trailers-").FullName;

        try
        {
            Git(repository, "init", "-q");
            Commit(repository, "fix: both trailers\n\nWhat was wrong.\n\nDefect-Class: GTS\nFound-By: CA\nCo-Authored-By: A <a@example.com>\n");
            Commit(repository, "fix(ef): no finder\n\nDefect-Class: EFT\n");
            Commit(repository, "feat: not a fix\n");
            Commit(repository, "fix!: an unknown class and a finder twice\n\nDefect-Class: XYZ\nFound-By: RS\nFound-By: CA\n");
            Commit(repository, "fix: trailers outside the last paragraph\n\nDefect-Class: GTS\nFound-By: CA\n\nA closing paragraph.\n");

            var commits = FixCommits(repository, "HEAD").Reverse().ToArray();

            commits.Select(commit => commit.Subject).ShouldBe(
            [
                "fix: both trailers",
                "fix(ef): no finder",
                "fix!: an unknown class and a finder twice",
                "fix: trailers outside the last paragraph",
            ]);

            Problems(commits[0]).ShouldBeEmpty();
            Problems(commits[1]).ShouldHaveSingleItem().ShouldContain("no Found-By");
            Problems(commits[2]).Length.ShouldBe(2);
            Problems(commits[2]).ShouldContain(problem => problem.Contains("Defect-Class XYZ"));
            Problems(commits[2]).ShouldContain(problem => problem.Contains("Found-By twice"));
            Problems(commits[3]).Length.ShouldBe(2, "A trailer outside the last paragraph is no trailer.");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal); // git's objects are read-only, which Windows will not delete
            Directory.Delete(repository, recursive: true);
        }
    }

    private sealed record FixCommit(string Hash, string Subject, string[] DefectClass, string[] FoundBy);

    private static string[] Problems(FixCommit commit)
    {
        var name = $"{commit.Hash[..7]} {commit.Subject}";

        return [.. Check(name, "Defect-Class", commit.DefectClass, DefectClasses), .. Check(name, "Found-By", commit.FoundBy, Finders)];

        static IEnumerable<string> Check(string name, string key, string[] values, HashSet<string> vocabulary)
        {
            if (values.Length == 0) yield return $"{name}: no {key}";
            else if (values.Length > 1) yield return $"{name}: {key} twice or more ({string.Join(", ", values)})";
            else if (!vocabulary.Contains(values[0])) yield return $"{name}: {key} {values[0]} is none of {string.Join(", ", vocabulary)}";
        }
    }

    /// <summary>The non-merge commits of <paramref name="range"/> whose subject is a conventional <c>fix:</c>, newest first.</summary>
    private static FixCommit[] FixCommits(string repository, string range)
    {
        const string format =
            "--format=%H%x1f%s%x1f%(trailers:key=Defect-Class,valueonly,separator=%x1d)%x1f%(trailers:key=Found-By,valueonly,separator=%x1d)%x1e";

        return
        [
            .. Git(repository, "log", "--no-merges", format, range)
               .Split('\x1e')
               .Select(record => record.Trim('\n', '\r'))
               .Where(record => record.Length > 0)
               .Select(record => record.Split('\x1f'))
               .Where(fields => FixSubject().IsMatch(fields[1]))
               .Select(fields => new FixCommit(fields[0], fields[1], Values(fields[2]), Values(fields[3])))
        ];

        static string[] Values(string field) => [.. field.Split('\x1d').Select(value => value.Trim()).Where(value => value.Length > 0)];
    }

    [GeneratedRegex(@"^fix(\([^)]*\))?!?:")]
    private static partial Regex FixSubject();

    private static void RequireWholeHistory()
    {
        Git(Repository.Root, "rev-parse", "--is-shallow-repository").Trim().ShouldBe(
            "false",
            "A shallow clone holds no history to check: run `git fetch --unshallow`, or check out with fetch-depth: 0 in CI.");

        Run(Repository.Root, "rev-parse", "--verify", "--quiet", $"{Since}^{{commit}}").ExitCode.ShouldBe(
            0,
            $"The tag {Since} is missing: run `git fetch --tags`.");
    }

    private static void Commit(string repository, string message)
    {
        var file = Path.Combine(repository, ".git", "MESSAGE");
        File.WriteAllText(file, message);

        // No hook of the machine's runs here, and no signing prompt.
        Git(repository, "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false",
            "-c", $"core.hooksPath={Path.Combine(repository, "no-hooks")}", "commit", "-q", "--allow-empty", "-F", file);
    }

    private static string Git(string repository, params string[] arguments)
    {
        var (exitCode, output, errors) = Run(repository, arguments);
        exitCode.ShouldBe(0, $"git {string.Join(' ', arguments)} failed: {errors}");
        return output;
    }

    private static (int ExitCode, string Output, string Errors) Run(string repository, params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", ["-C", repository, "-c", "i18n.logOutputEncoding=UTF-8", .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
        })!;

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, errors.Result);
    }
}
