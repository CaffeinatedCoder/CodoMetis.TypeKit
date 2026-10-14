using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>AsText()</c> where text cannot be composed. In globalization-invariant mode, which a Native AOT
/// application often turns on with <c>InvariantGlobalization</c>, <c>string.Normalize</c> returns its
/// input unchanged, and <c>AsText()</c> answered <c>Zoe</c> and a combining diaeresis as a success,
/// uncomposed: one name, two spellings, silently. It throws there now.
/// </summary>
/// <remarks>
/// The mode is fixed when a process starts, so the test starts this test assembly again with
/// <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1</c>, filtered to itself, and holds the child to having
/// run it and passed. In that child, or in a whole run started in the mode, the same test makes its
/// assertions directly, after confirming that the mode is really on.
/// </remarks>
public sealed class InvariantGlobalizationTests
{
    private const string InvariantMode = "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT";

    [Fact]
    public async Task AsText_throws_where_text_cannot_be_composed_and_AsIdentifier_still_answers()
    {
        if (Environment.GetEnvironmentVariable(InvariantMode) != "1")
        {
            await RunInInvariantMode(nameof(AsText_throws_where_text_cannot_be_composed_and_AsIdentifier_still_answers));
            return;
        }

        Should.Throw<CultureNotFoundException>(() => CultureInfo.GetCultureInfo("de-DE"), "the process is not in globalization-invariant mode, so this run tests nothing");
        "Zoe\u0308".Normalize(System.Text.NormalizationForm.FormC).ShouldBe("Zoe\u0308", "string.Normalize composes in this process, so this run tests nothing");

        var refusal = Should.Throw<PlatformNotSupportedException>(() => "Zoe\u0308".AsText());
        refusal.Message.ShouldContain("globalization-invariant mode");
        refusal.Message.ShouldContain("InvariantGlobalization");
        refusal.Message.ShouldContain("Turn invariant mode off and make ICU available");
        refusal.Message.ShouldContain("ICU alone does nothing");
        RefusalLinkTests.ShouldEndWithLinkTo(refusal.Message, RefusalLinkTests.TextWithoutComposition);

        Should.Throw<PlatformNotSupportedException>(() => "Zoe".AsText(), "whatever the string, so that the first call finds out");
        Should.Throw<PlatformNotSupportedException>(() => ((string?)null).AsText(), "whatever the string, so that the first call finds out");
        Should.Throw<PlatformNotSupportedException>(() => ((string?)null).AsOptionalText(), "an optional field finds out on its first call too, blank or not");

        " Zoe\u0308 ".AsIdentifier().Match(id => id, fault => fault.ToString()).ShouldBe("Zoe\u0308", "AsIdentifier composes nothing and needs no globalization data");
        "Zoe\u200B".AsIdentifier().Match(id => id, fault => fault.ToString()).ShouldBe(nameof(TextFault.Hidden));
    }

    /// <summary>
    /// Runs one test of this assembly in a process in globalization-invariant mode, with the dotnet host
    /// of the runtime running this one, and fails unless it ran and passed. Started directly, the test
    /// assembly is xunit's own console runner, whose filter and summary these are.
    /// </summary>
    private static async Task RunInInvariantMode(string method)
    {
        using var process = Process.Start(new ProcessStartInfo(
            DotnetHost(),
            ["exec", typeof(InvariantGlobalizationTests).Assembly.Location,
             "-method", $"{typeof(InvariantGlobalizationTests).FullName}.{method}",
             "-noLogo", "-noColor", "-noAutoReporters"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            Environment            = { [InvariantMode] = "1", ["DOTNET_CLI_UI_LANGUAGE"] = "en" },
        })!;

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        var report = await output + await errors;
        process.ExitCode.ShouldBe(0, $"The test failed in globalization-invariant mode:{Environment.NewLine}{report}");
        report.ShouldContain("Total: 1, Errors: 0, Failed: 0, Skipped: 0", Case.Sensitive, $"The child did not run this one test:{Environment.NewLine}{report}");
    }

    /// <summary>The dotnet host beside the shared runtime running this process, else the one on the PATH.</summary>
    private static string DotnetHost()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var host = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        return File.Exists(host) ? host : "dotnet";
    }
}
