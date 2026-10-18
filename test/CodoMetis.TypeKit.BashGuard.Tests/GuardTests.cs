using System.Text;
using System.Text.Json;

namespace CodoMetis.TypeKit.BashGuard.Tests;

/// <summary>
/// The commands the guard must refuse and the ones it must let through. Each case runs the built guard the
/// way .claude/settings.json does, as its own `dotnet bash-guard.dll` process with the payload on stdin.
/// Pointed at a guard with one rule broken (a heredoc read as commands, quotes ignored, a prefix not
/// stripped), the cases here fail; that is how each one was proved.
/// </summary>
public sealed class GuardTests(GuardTests.Sandbox sandbox) : IClassFixture<GuardTests.Sandbox>
{
    public sealed class Sandbox : IDisposable
    {
        public ToolSandbox Tool { get; } = new("BashGuard", "bash-guard");

        public void Dispose() => Tool.Dispose();
    }

    internal static string Payload(string command)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("tool_name", "Bash");
            w.WriteStartObject("tool_input");
            w.WriteString("command", command);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private (int Code, string Error) Guard(string payload)
    {
        var (code, _, error) = sandbox.Tool.Run(input: payload);
        return (code, error);
    }

    // ---- the raw toolchain, in every shape a command takes ----------------------------------------------

    [Theory]
    [InlineData("dotnet build", "dotnet build")]
    [InlineData("dotnet test", "dotnet test --solution CodoMetis.TypeKit.slnx")]
    [InlineData("dotnet msbuild", "dotnet msbuild -t:Pack")]
    [InlineData("dotnet by its path", "/usr/local/share/dotnet/dotnet build")]
    [InlineData("dotnet by its Windows path", "\"C:/Program Files/dotnet/dotnet.exe\" test")]
    [InlineData("after assignments", "PATH=/usr/local/share/dotnet:$PATH DOTNET_ROOT=/usr/local/share/dotnet dotnet test")]
    [InlineData("after env and its options", "env -i DOTNET_CLI_UI_LANGUAGE=en dotnet test")]
    [InlineData("after time", "time dotnet build")]
    [InlineData("after timeout and its duration", "timeout -s KILL 600 dotnet test")]
    [InlineData("after nice -n", "nice -n 5 dotnet build")]
    [InlineData("after cd and &&", "cd test && dotnet build")]
    [InlineData("first in a pipeline, redirected", "dotnet build CodoMetis.TypeKit.slnx 2>&1 | tail -20")]
    [InlineData("after ;", "echo start; dotnet test")]
    [InlineData("after ||", "false || dotnet build")]
    [InlineData("in the background", "dotnet build &")]
    [InlineData("on the second line", "echo start\ndotnet test")]
    [InlineData("in a subshell", "(cd test; dotnet build)")]
    [InlineData("in a group", "{ dotnet build; }")]
    [InlineData("in an if", "if true; then dotnet test; fi")]
    [InlineData("in a loop", "for p in a b; do dotnet test --project \"$p\"; done")]
    [InlineData("in a command substitution", "out=$(dotnet build)")]
    [InlineData("in a substitution inside double quotes", "echo \"result: $(dotnet test)\"")]
    [InlineData("in backticks", "echo `dotnet build`")]
    [InlineData("in bash -c", "bash -c \"dotnet build\"")]
    [InlineData("in bash -lc, after &&", "bash -lc 'cd x && dotnet test'")]
    [InlineData("after a heredoc has ended", "cat > notes.txt <<EOF\nnothing here\nEOF\ndotnet test")]
    [InlineData("after a tab-stripped heredoc has ended", "cat <<-EOF\n\tnothing here\n\tEOF\ndotnet test")]
    [InlineData("after a background job", "sleep 1 & dotnet build")]
    [InlineData("after a leading redirection", "2>/dev/null dotnet build")]
    [InlineData("after a comment has ended", "ls # a note; with a semicolon\ndotnet build")]
    [InlineData("a test of a tool's tests", "dotnet test --project tools/BuildVerdict.Tests")]
    [InlineData("the verdict piped into tail", "dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test | tail -5")]
    [InlineData("the verdict piped into head, redirected", "dotnet ./tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll build 2>&1 | head")]
    [InlineData("the verdict by dotnet's path, piped into less", "/usr/local/share/dotnet/dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test |& less")]
    [InlineData("a query that runs a target", "dotnet msbuild src/A/A.csproj -getProperty:TargetPath -t:Build")]
    [InlineData("a query that runs a target, spelled out", "dotnet msbuild src/A/A.csproj --target:Pack -getItem:Compile")]
    [InlineData("a query that runs a target, Windows-style", "dotnet msbuild src/A/A.csproj /t:Build /getProperty:TargetPath")]
    [InlineData("a target result, which builds", "dotnet msbuild src/A/A.csproj -getTargetResult:Build")]
    [InlineData("a query that restores", "dotnet msbuild src/A/A.csproj -restore -getProperty:Version")]
    [InlineData("a query that restores, short", "dotnet msbuild src/A/A.csproj -r -getItem:PackageReference")]
    [InlineData("a property set, not queried", "dotnet msbuild src/A/A.csproj -property:Configuration=Release")]
    [InlineData("a build that queries", "dotnet build src/A/A.csproj -getProperty:TargetPath")]
    public void Refused(string name, string command) => Guard(Payload(command)).Code.ShouldBe(2, name);

    // ---- what passes ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("dotnet --version", "dotnet --version")]
    [InlineData("dotnet restore", "dotnet restore CodoMetis.TypeKit.slnx")]
    [InlineData("dotnet pack", "dotnet pack src/CodoMetis.TypeKit -o dist")]
    [InlineData("dotnet tool", "dotnet tool restore")]
    [InlineData("dotnet ef", "dotnet ef migrations list")]
    [InlineData("dotnet build-server is another verb", "dotnet build-server shutdown")]
    [InlineData("help for test", "dotnet test --project test/CodoMetis.TypeKit.Tests --no-build --help")]
    [InlineData("help for build", "dotnet build -h")]
    [InlineData("a build of a tool", "dotnet build tools/BuildVerdict -c Release")]
    [InlineData("a build of a tool by its project file", "cd x && dotnet build ./tools/BashGuard/BashGuard.csproj -c Release")]
    [InlineData("the verdict tool", "dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test --project test/CodoMetis.TypeKit.Tests --filter-class \"*OptionTests\"")]
    [InlineData("the verdict tool redirected to a file", "dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll build > /tmp/v.txt 2>&1")]
    [InlineData("the verdict tool into grep", "dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test | grep FAIL")]
    [InlineData("the smoke test", "dotnet tools/ConsumerSmokeTest/bin/Release/net10.0/consumer-smoke-test.dll dist")]
    [InlineData("a property query", "dotnet msbuild src/A/A.csproj -getProperty:IsRoslynComponent -getItem:ProjectReference -nodeReuse:false")]
    [InlineData("an item query, spelled out", "dotnet msbuild src/A/A.csproj --getItem:Compile")]
    [InlineData("a property query, Windows-style and any case", "dotnet msbuild src/A/A.csproj /GetProperty:Version")]
    [InlineData("a property query with a property set", "dotnet msbuild src/A/A.csproj -p:Configuration=Release -getProperty:OutputPath")]
    [InlineData("a property query in a loop", "for p in A B; do dotnet msbuild src/$p/$p.csproj -getItem:ProjectReference -getProperty:IsRoslynComponent; done")]
    [InlineData("dotnet as an argument", "echo dotnet build")]
    [InlineData("inside double quotes", "git commit -m \"dotnet build is refused\"")]
    [InlineData("inside double quotes with operators", "echo \"x && dotnet build && y\"")]
    [InlineData("inside single quotes with operators", "echo 'x; dotnet test | tail'")]
    [InlineData("in a search pattern", "grep -n \"dotnet test\" AGENTS.md")]
    [InlineData("in a comment", "ls # dotnet build")]
    [InlineData("in a comment that holds an operator", "ls # then; dotnet build")]
    [InlineData("in a heredoc body", "cat > probe.sh <<'EOF'\ndotnet build\ncd x && dotnet test | tail\nEOF\necho written")]
    [InlineData("in a tab-stripped heredoc body", "cat <<-EOF\n\tdotnet test\n\tEOF\necho done")]
    [InlineData("a here-string", "grep -c x <<< \"dotnet build\"")]
    [InlineData("a tail that reads a log", "tail -20 \"$(git rev-parse --absolute-git-dir)/build-verdict-last.log\"")]
    // The Python original looped forever on these, until the hook's timeout.
    [InlineData("a command ending in a redirection", "ls 2>")]
    [InlineData("a command ending in a heredoc operator", "cat <<")]
    [InlineData("a command ending in a heredoc operator and blanks", "cat <<- ")]
    public void Allowed(string name, string command)
    {
        var (code, error) = Guard(Payload(command));
        code.ShouldBe(0, $"{name}: {error.Split('\n')[0]}");
    }

    // ---- what the refusal says ------------------------------------------------------------------------

    [Fact]
    public void The_pipe_refusal_names_the_pager() =>
        Guard(Payload("dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test | tail -5")).Error.ShouldContain("piped into tail");

    [Fact]
    public void A_test_refusal_gives_the_verdict_form()
    {
        var (code, error) = Guard(Payload("cd x && dotnet test --solution CodoMetis.TypeKit.slnx --filter-class \"*OptionTests\" --output Detailed -- -nodeReuse:false 2>&1 | tail"));

        code.ShouldBe(2);
        error.ShouldContain("Refused: raw `dotnet test`", customMessage: "it names the command it refused");
        error.ShouldContain("Run: dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test --filter-class *OptionTests -- --output Detailed", customMessage: "filters kept");
        error.ShouldContain("Dropped from the test step: -nodeReuse:false", customMessage: "it drops an MSBuild switch from a test");
        error.ShouldContain("An MSBuild switch belongs on `build -- ...`", customMessage: "it says where MSBuild switches go");
    }

    [Fact]
    public void A_build_refusal_keeps_its_target_and_flags()
    {
        var (code, error) = Guard(Payload("dotnet build src/A/A.csproj -c Release"));

        code.ShouldBe(2);
        error.ShouldContain("Run: dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll build src/A/A.csproj -- -c Release");
    }

    // ---- payloads it cannot read are allowed ----------------------------------------------------------

    [Theory]
    [InlineData("a payload that is not JSON is allowed", "not json")]
    [InlineData("a payload without a command is allowed", "{\"tool_input\": {}}")]
    [InlineData("a session-start payload is allowed", "{\"session_id\":\"x\",\"hook_event_name\":\"SessionStart\",\"source\":\"startup\"}")]
    public void Unreadable(string name, string payload) => Guard(payload).Code.ShouldBe(0, name);
}

/// <summary>
/// A guard built from other sources than the checkout holds refuses every command but the build of a tool,
/// and names the command that rebuilds it: a stale guard is never silent, and never in the way of its fix.
/// </summary>
public sealed class StaleGuardTests
{
    private static ToolSandbox Stale()
    {
        var sandbox = new ToolSandbox("BashGuard", "bash-guard");
        File.AppendAllText(Path.Combine(sandbox.Repo, "tools", "BashGuard", "Program.cs"), "\n// edited\n");
        return sandbox;
    }

    [Fact]
    public void A_stale_guard_refuses_and_names_the_rebuild()
    {
        using var sandbox = Stale();

        var (code, _, error) = sandbox.Run(input: GuardTests.Payload("ls"));

        code.ShouldBe(2);
        error.ShouldContain("(tools/BashGuard/Program.cs differs)");
        error.ShouldContain("Rebuild it first: dotnet build tools/BashGuard -c ");
    }

    [Fact]
    public void A_stale_guard_lets_the_rebuild_through()
    {
        using var sandbox = Stale();

        sandbox.Run(input: GuardTests.Payload("dotnet build tools/BashGuard -c Release")).Code.ShouldBe(0);
    }
}
