using System.Text.Json.Nodes;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// A result is bound to what the axes file's toolchain commands print. A command that cannot say it refuses, by
/// name: hashed into the identity, its error text made every result read as stale, so `status` showed 0% current
/// when the dotnet first on PATH had no SDK global.json accepts.
/// </summary>
public sealed class ToolchainTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private static Dictionary<string, string> R(string x, string y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };

    private void Toolchain(params string[] command)
    {
        var path = Path.Combine(fx.Root, ".claude/probe-axes.md");
        var text = File.ReadAllText(path);
        var block = text[(text.IndexOf('\n') + 1)..text.LastIndexOf("```", StringComparison.Ordinal)];
        var axes = JsonNode.Parse(block)!.AsObject();
        axes["replay"] = new JsonObject { ["toolchain"] = new JsonObject { ["sdk"] = new JsonArray([.. command.Select(c => (JsonNode)c)]) } };
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + axes.ToJsonString() + "\n```\n");
        fx.Commit("toolchain");
    }

    /// <summary>A pass recorded under a toolchain that names itself, then read under one that cannot.</summary>
    private void RecordedThenBroken(params string[] command)
    {
        Toolchain("git", "--version");
        RecordPass();
        Toolchain(command);
    }

    private void RecordPass()
    {
        fx.Footprint();
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", fx.Evidence([R("1", "1"), R("2", "2")])).Exit.ShouldBe(0);
    }

    /// <summary>The ways a toolchain command fails to name the toolchain, each with what the refusal must say.</summary>
    public static TheoryData<string[], string> Failing() => new()
    {
        { ["git", "no-such-subcommand"], "exited 1" },
        { ["cmtk-no-such-program"], "did not start" },
        { ["git", "diff", "--quiet"], "printed nothing" },
        { [], "is empty" },
    };

    [Theory, MemberData(nameof(Failing))]
    public void A_toolchain_command_that_cannot_name_the_toolchain_refuses(string[] command, string reason)
    {
        RecordedThenBroken(command);
        var (exit, output, error) = fx.Ledger("status");
        exit.ShouldBe(1, output);
        error.ShouldContain("the toolchain command sdk");
        error.ShouldContain(reason);
        output.ShouldNotContain("pairs current", Case.Sensitive, "a refusal, never a status of 0% current");
    }

    /// <summary>
    /// The case that was found: a dotnet with no SDK global.json accepts lists the installed SDKs on stdout, which
    /// reads like a version, and says why on stderr. The refusal quotes the why.
    /// </summary>
    [Fact]
    public void A_dotnet_without_the_pinned_sdk_refuses_with_its_error_not_its_sdk_list()
    {
        fx.Write("global.json", """{"sdk": {"version": "0.0.1"}}""");
        RecordedThenBroken("dotnet", "--version");
        var (exit, output, error) = fx.Ledger("status");
        exit.ShouldBe(1, output);
        error.ShouldContain("the toolchain command sdk (`dotnet --version`) exited");
        error.ShouldNotContain("sdk]", Case.Insensitive, "the installed SDKs on stdout are not the reason");
    }

    /// <summary>Every command that reads an identity, not only status.</summary>
    [Theory]
    [InlineData("status")]
    [InlineData("gate")]
    [InlineData("stale")]
    [InlineData("rows", "--claim", "C1")]
    [InlineData("identity", "--probe", "P")]
    public void Every_reader_of_an_identity_refuses(params string[] command)
    {
        RecordedThenBroken("git", "no-such-subcommand");
        var (exit, _, error) = fx.Ledger(command);
        exit.ShouldBe(1, error);
        error.ShouldContain("the toolchain command sdk");
    }

    /// <summary>
    /// The case that was found next: Homebrew's dotnet first on PATH (10.0.401 against the 10.0.402 pin) made every
    /// `dotnet` the prober starts fail, though the SDK installer's had the pinned SDK. Here the dotnet that runs the
    /// built prober has no SDK the pin accepts, and a stub in the installer's place (TOOLS_DOTNET_FALLBACK_DIR) answers:
    /// the prober falls back to it, records the pass under what it prints, and keeps the pass current.
    /// </summary>
    [Fact]
    public void A_dotnet_without_the_pinned_sdk_falls_back_to_the_installers()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the stub installer is a shell script");
        Toolchain("dotnet", "--version");
        fx.Write("global.json", """{"sdk": {"version": "0.0.1", "rollForward": "disable"}}""");
        fx.Commit("a pin no dotnet here has");
        fx.Footprint();
        var evidence = fx.Evidence([R("1", "1"), R("2", "2")]);

        var installer = Directory.CreateTempSubdirectory("prober-installer-").FullName;
        try
        {
            var stub = Path.Combine(installer, "dotnet");
            File.WriteAllText(stub, "#!/bin/sh\necho 0.0.1\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var sandbox = new ToolSandbox("Prober", "prober");
            // The prober runs as its own process, so the caller's state settings would reach it: under a PROBE_STATE_DIR it
            // recorded the run there, not in this repository's state, and status found no claim (#58). None reaches it.
            var environment = new Dictionary<string, string?>
            {
                ["TOOLS_DOTNET_FALLBACK_DIR"] = installer, ["PROBE_STATE_DIR"] = null, ["PROBE_CACHE_DIR"] = null, ["PROBE_HOLDER"] = null,
            };
            (int, string, string) Prober(params string[] arguments) => ToolSandbox.RunProcess("dotnet", [sandbox.Dll, .. arguments], fx.Root, environment);

            var (recorded, _, why) = Prober("record-run", "--probe", "P", "--result", "pass", "--evidence", evidence);
            recorded.ShouldBe(0, why);
            var (exit, output, error) = Prober("status", "--json");
            exit.ShouldBe(0, error);
            ((int)JsonNode.Parse(output)!["claims"]!["C1"]!["current"]!.GetValue<double>()).ShouldBe(2, "the pass, recorded under the fallback, is current");
        }
        finally
        {
            Directory.Delete(installer, recursive: true);
        }
    }

    [Fact]
    public void A_toolchain_that_names_itself_keeps_the_result_current()
    {
        Toolchain("git", "--version");
        RecordPass();
        fx.Covered().ShouldBe(2);
    }
}
