using System.Diagnostics;

namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// Every registered oracle ships a seed, and every seed still plants its defect (design/adr/0112).
/// </summary>
/// <remarks>
/// <para>
/// A test that passes with and without a defect is not a guard. The rule has always been to prove a guard by
/// reverting its fix (<c>.claude/skills/verify-the-guard</c>); a seed is that revert kept as a file.
/// <c>test/oracles/seeds/&lt;defect&gt;.patch</c> changes <c>src/</c> only, names the oracle classes it fails in an
/// <c>Oracles:</c> header line, and was shown to fail them by <c>oracles seeds</c>.
/// </para>
/// <para>
/// These tests build nothing: an oracle without a seed, a seed that names no oracle, a seed that touches a test, and a
/// seed a refactor has stranded all fail here. Whether a seed is still caught is the local tool's job, a copy, a build
/// of one project and a run of one class per seed, before a release.
/// </para>
/// </remarks>
public sealed class OracleSeedTests
{
    private const int KnownOracles = 5;

    [Fact]
    public void Every_oracle_is_a_test_class_in_its_project()
    {
        OracleCatalogue.Oracles.Count.ShouldBeGreaterThanOrEqualTo(KnownOracles);

        OracleCatalogue.Oracles
                       .Where(oracle => !OracleCatalogue.TestClasses.TryGetValue(oracle.Class, out var project) || project != oracle.Project)
                       .Select(oracle => $"{oracle.Class} (registered in {oracle.Project}, found in {(OracleCatalogue.TestClasses.TryGetValue(oracle.Class, out var found) ? found : "no project")})")
                       .ShouldBeEmpty("oracles.json names a class that is not a test class in that project.");
    }

    [Fact]
    public void Every_oracle_has_a_seed()
    {
        var seeded = OracleCatalogue.Seeds.SelectMany(seed => seed.Oracles).ToHashSet(StringComparer.Ordinal);

        OracleCatalogue.Oracles.Select(oracle => oracle.Class).Where(oracle => !seeded.Contains(oracle))
                       .ShouldBeEmpty("An oracle has no seed: add test/oracles/seeds/<defect>.patch with an 'Oracles:' line naming it, and show it caught with `oracles seeds`.");
    }

    [Fact]
    public void Every_seed_names_a_defect_and_registered_oracles()
    {
        OracleCatalogue.Seeds.ShouldNotBeEmpty("No seeds under test/oracles/seeds/.");

        var oracles = OracleCatalogue.Oracles.Select(oracle => oracle.Class).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var seed in OracleCatalogue.Seeds)
        {
            if (seed.Defect.Length == 0) problems.Add($"{seed.File.Name}: no 'Seed:' header line describing the defect");
            if (seed.Oracles.Count == 0) problems.Add($"{seed.File.Name}: no 'Oracles:' header line naming the classes it fails");
            problems.AddRange(seed.Oracles.Where(oracle => !oracles.Contains(oracle)).Select(oracle => $"{seed.File.Name}: names '{oracle}', which oracles.json does not register"));
            if (seed.Body.Length == 0) problems.Add($"{seed.File.Name}: holds no diff");
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Every_seed_touches_only_source() =>
        OracleCatalogue.Seeds.SelectMany(seed => seed.ChangedFiles.Select(file => (seed.File.Name, file)))
                       .Where(pair => !pair.file.StartsWith("src/", StringComparison.Ordinal))
                       .Select(pair => $"{pair.Name} changes '{pair.file}'")
                       .ShouldBeEmpty("A seed plants a defect in the library, never in a test.");

    [Fact]
    public void Every_seed_applies_to_the_current_tree()
    {
        var problems = new List<string>();
        foreach (var seed in OracleCatalogue.Seeds)
        {
            var (exit, output) = Git("apply", "--check", seed.File.FullName);
            if (exit != 0) problems.Add($"{seed.File.Name} no longer applies; refresh it against src/: {output.Trim()}");
        }

        problems.ShouldBeEmpty();
    }

    private static (int Exit, string Output) Git(params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = OracleCatalogue.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var git = Process.Start(info) ?? throw new InvalidOperationException("git could not be started; the seed checks need it on PATH.");
        var error = git.StandardError.ReadToEndAsync();
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        return (git.ExitCode, output + error.Result);
    }
}
