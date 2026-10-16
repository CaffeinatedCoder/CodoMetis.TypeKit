using System.Text;

namespace CodoMetis.TypeKit.BuildVerdict.Tests;

/// <summary>
/// The tool's parsers and verdicts, against fixture logs and a stub dotnet. The fixtures are the English
/// output the tool pins with DOTNET_CLI_UI_LANGUAGE=en, cut from real runs on this repository
/// (2026-10-15), and beside each parser one fixture it must reject: the German output of the same run, a
/// summary with no total, a failure block the parser cannot read. Each was proved by seeding its defect.
/// </summary>
/// <remarks>
/// The tool runs as its built dll from a <see cref="ToolSandbox"/>: a throwaway repository with its own
/// solution marker and git repository, so paths, the log location and the stub's record of its calls are
/// all checkable. Each fact collects every check it makes and fails with all that did not hold.
/// </remarks>
public sealed class VerdictTests(VerdictTests.Sandbox sandbox) : IClassFixture<VerdictTests.Sandbox>
{
    private readonly List<string> _failures = [];
    private string _output = "";
    private string R => sandbox.Tool.Repo;

    public sealed class Sandbox : IDisposable
    {
        public ToolSandbox Tool { get; } = new("BuildVerdict", "build-verdict");

        public Sandbox()
        {
            ToolSandbox.RunProcess("git", ["init", "-q", Tool.Repo], Tool.Work);
            Directory.CreateDirectory(Path.Combine(Tool.Repo, "test", "A.Tests"));
            Directory.CreateDirectory(Path.Combine(Tool.Repo, "test", "B.Tests"));
        }

        public void Dispose() => Tool.Dispose();
    }

    private void Check(bool holds, string name, string why)
    {
        if (!holds) _failures.Add($"{name} ({why})");
    }

    private string[] Lines() => _output.Replace("\r\n", "\n").Split('\n');
    private void Has(string name, string text) => Check(_output.Contains(text, StringComparison.Ordinal), name, $"missing: {text}");
    private void Hasnt(string name, string text) => Check(!_output.Contains(text, StringComparison.Ordinal), name, $"present: {text}");
    private void First(string name, string line) => Check(Lines()[0] == line, name, $"first line: {Lines()[0]}");
    private void Exited(string name, int wanted, int got) => Check(wanted == got, name, $"wanted exit {wanted}, got {got}");
    private void Holds() => _failures.ShouldBeEmpty($"output:\n{_output}");

    private int Run(IReadOnlyDictionary<string, string?> environment, params string[] arguments)
    {
        var (code, output, _) = sandbox.Tool.Run(environment, null, arguments);
        _output = output;
        return code;
    }

    // The tool in parse-only mode, on a fixture.
    private int Verdict(int exit, string fixture, params string[] arguments)
    {
        var log = Path.Combine(sandbox.Tool.Work, "fixture");
        File.WriteAllText(log, fixture, new UTF8Encoding(false));
        return Run(new Dictionary<string, string?> { ["BUILD_VERDICT_LOG"] = log, ["BUILD_VERDICT_EXIT"] = exit.ToString(System.Globalization.CultureInfo.InvariantCulture) }, arguments);
    }

    // ---- build: pass ----------------------------------------------------------------------------------

    [Fact]
    public void A_clean_build()
    {
        var f = $"""
              Determining projects to restore...
              All projects are up-to-date for restore.
              CodoMetis.TypeKit.Analyzers -> {R}/src/CodoMetis.TypeKit.Analyzers/bin/Debug/netstandard2.0/CodoMetis.TypeKit.Analyzers.dll
              CodoMetis.TypeKit -> {R}/src/CodoMetis.TypeKit/bin/Debug/net10.0/CodoMetis.TypeKit.dll
              CodoMetis.TypeKit.Tests -> {R}/test/CodoMetis.TypeKit.Tests/bin/Debug/net10.0/CodoMetis.TypeKit.Tests.dll

            Build succeeded.
                0 Warning(s)
                0 Error(s)

            Time Elapsed 00:00:12.14

            """;
        Exited("a clean build exits 0", 0, Verdict(0, f, "build"));
        First("a clean build counts projects and time", "BUILD OK: 3 projects, 00:00:12.14");
        Hasnt("a clean build prints no warnings line", "warnings:");
        Holds();
    }

    // The same build in German, as this machine prints it unpinned: exit 0, and no line the parser knows.
    [Fact]
    public void An_unreadable_clean_build()
    {
        var f = $"""
              CodoMetis.TypeKit -> {R}/src/CodoMetis.TypeKit/bin/Debug/net10.0/CodoMetis.TypeKit.dll

            Der Buildvorgang wurde erfolgreich ausgeführt.
                0 Warnung(en)
                0 Fehler

            Verstrichene Zeit 00:00:12.14

            """;
        Exited("an unreadable clean build fails closed", 1, Verdict(0, f, "build"));
        Has("an unreadable clean build is named", "BUILD DID NOT REPORT");
        Holds();
    }

    // A clean build with a warning that stays a warning (Directory.Build.props).
    [Fact]
    public void A_build_with_warnings()
    {
        var f = $"""
            {R}/test/A.Tests/A.Tests.csproj : warning NU1903: Package 'X' 1.0.0 has a known high severity vulnerability [{R}/test/A.Tests/A.Tests.csproj]
            Build succeeded.
            Time Elapsed 00:00:01.00

            """;
        Exited("a build with warnings passes", 0, Verdict(0, f, "build"));
        Has("warnings are summarised per code", "warnings: NU1903 x1");
        Has("a warning's remedy comes from a prefix row", "NU1903: NU1901-NU1904 stay warnings on purpose");
        Holds();
    }

    // ---- build: failures ------------------------------------------------------------------------------

    // The real failing build: a value object not partial, a default Option, a type error; MSBuild prints
    // each once inline and once in its summary. B.Tests reports the CS0029 too, as a referencing project
    // that compiles the same file would; CMTK1002 shares CMTK1000's row, to exercise "(as above)". The
    // lines put LAMA and CS before CMTK in the file, so only the ranking gives CMTK-first.
    [Fact]
    public void A_failed_build()
    {
        var diag = $"""
            {R}/test/A.Tests/Probe.cs(9,46): error CMTK0001: Invalid initialization of 'Option': Use Option.Some(value) or Option.None(). (https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0001) [{R}/test/A.Tests/A.Tests.csproj]
            {R}/test/A.Tests/Probe.cs(5,31): error CMTK1000: 'ZzzNotPartial' is a value object and must be declared partial. See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors [{R}/test/A.Tests/A.Tests.csproj]
            {R}/test/A.Tests/Probe.cs(6,31): error CMTK1002: 'ZzzNotReadonly' is a value object and must be declared readonly. See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors [{R}/test/A.Tests/A.Tests.csproj]
            {R}/test/A.Tests/Probe.cs(3,34): error CS0029: Cannot implicitly convert type 'string' to 'int' [{R}/test/A.Tests/A.Tests.csproj]
            {R}/test/A.Tests/Probe.cs(3,34): error CS0029: Cannot implicitly convert type 'string' to 'int' [{R}/test/B.Tests/B.Tests.csproj]
            {R}/test/A.Tests/Probe.cs(1,1): error LAMA0611: Error CS0542 in code generated by aspect 'ValueObjectAspect'. [{R}/test/A.Tests/A.Tests.csproj]

            """;
        var f = "  Determining projects to restore...\n" + diag + "\nBuild FAILED.\n\n" + diag +
                "    0 Warning(s)\n    12 Error(s)\n\nTime Elapsed 00:00:01.80\n";
        Exited("a failed build exits 1", 1, Verdict(1, f, "build"));
        First("unique errors are counted once, not per repeat", "BUILD FAILED: 5 unique errors");
        Has("paths under the root are made relative", "## test/A.Tests/Probe.cs:3");
        Has("the csproj suffix comes off the message", "CS0029  Cannot implicitly convert type 'string' to 'int'");
        Hasnt("the csproj suffix comes off the message (2)", "A.Tests.csproj]");
        Hasnt("a help link in parentheses comes off", "README.md#cmtk0001");
        Hasnt("a trailing See-link comes off", "See https://");
        Has("a project-repeated error names its projects", "(reported by 2 projects: A.Tests.csproj, B.Tests.csproj)");
        Has("an exact row carries its FIX", "FIX   Use the type's factory");
        Has("a prefix row covers the CMTK1 family", "FIX   The message names the fix");
        Has("a second error of the same row says as above", "FIX   (as above)");
        Has("LAMA0611 points at the cascade", "FIX   Usually a cascade");
        Hasnt("CS codes without a row are not UNMAPPED", "UNMAPPED");
        var codes = string.Join(' ', Lines().Where(l => l.StartsWith("CMTK", StringComparison.Ordinal) || l.StartsWith("CS", StringComparison.Ordinal) ||
                                                        l.StartsWith("LAMA", StringComparison.Ordinal)).Select(l => l.Split(' ')[0]));
        Check(codes == "CMTK1000 CMTK1002 CMTK0001 CS0029 LAMA0611", "CMTK first, LAMA last, by file and line within", codes);
        var blocks = Lines().Count(l => l.StartsWith("## ", StringComparison.Ordinal));
        Check(blocks == 5, "one block per unique error", $"{blocks} blocks");
        Holds();
    }

    // The same failing build in German: the "error CODE:" keyword is not translated, so the codes still read.
    [Fact]
    public void A_failed_build_in_German()
    {
        var f = $"""
            {R}/test/A.Tests/Probe.cs(3,34): error CS0029: Der Typ "string" kann nicht implizit in "int" konvertiert werden. [{R}/test/A.Tests/A.Tests.csproj]

            Fehler beim Buildvorgang.
                1 Fehler

            """;
        Exited("a German failing build still fails", 1, Verdict(1, f, "build"));
        Has("a German failing build still names its code", "CS0029  Der Typ");
        Holds();
    }

    // Codes without a row, and a diagnostic with no file.
    [Fact]
    public void Codes_without_a_row()
    {
        var f = $"""
            {R}/src/X/Program.cs(1,1): error FOO1234: Something new. [{R}/src/X/X.csproj]
            MSBUILD : error MSB1009: Project file does not exist.
            CSC : error LAMA0699: Metalama said something new. [{R}/src/X/X.csproj]

            """;
        Exited("unmapped codes still fail", 1, Verdict(1, f, "build"));
        Has("non-CS codes without a row are UNMAPPED", "UNMAPPED: FOO1234 MSB1009 -- no remedy row");
        Has("an unknown LAMA code takes the LAMA row", "FIX   Check the CMTK errors in the same verdict first");
        Has("a diagnostic without a position keeps its origin", "## MSBUILD");
        Has("a diagnostic without a position keeps its message", "MSB1009  Project file does not exist.");
        Holds();
    }

    // Exit 155 from a dotnet that cannot satisfy global.json (Homebrew's 10.0.401 against the 10.0.402 pin).
    [Fact]
    public void An_SDK_pin_failure()
    {
        var f = $"""
            The command could not be loaded, possibly because:
              * You intended to execute a .NET SDK command:
                  A compatible .NET SDK was not found.

            Requested SDK version: 10.0.402
            global.json file: {R}/global.json

            Installed SDKs:
            10.0.401 [/opt/homebrew/Cellar/dotnet/10.0.401_1/libexec/sdk]

            """;
        Exited("an SDK pin failure exits 1", 1, Verdict(155, f, "build"));
        Has("an SDK pin failure is named", "cannot satisfy global.json's SDK pin (exit 155)");
        Has("an SDK pin failure shows the requested version", "Requested SDK version: 10.0.402");
        Has("an SDK pin failure carries its FIX", "FIX   An environment problem, not a diff problem");
        Exited("a pre-MSBuild failure exits 1", 1, Verdict(1, "error: something before MSBuild\n", "build"));
        Has("a pre-MSBuild failure says no diagnostic came", "BUILD FAILED before MSBuild reported any diagnostic (exit 1)");
        Holds();
    }

    // ---- test: pass -----------------------------------------------------------------------------------

    [Fact]
    public void A_passing_run()
    {
        var f = $"""
            Running tests from {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
            skipped CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Skipped
              probe skip
              from {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) passed (805ms)

            Test run summary: Passed!
              total: 42
              failed: 0
              succeeded: 41
              skipped: 1
              duration: 920ms

            """;
        Exited("a passing run exits 0", 0, Verdict(0, f, "test"));
        First("a passing run states counts, scope, time", "TESTS OK: 41 passed, 1 skipped, solution, 920ms");
        Has("three or fewer skips are named", "skipped: CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Skipped");
        Exited("a project and a filter name the scope", 0, Verdict(0, f, "test", "--project", "test/A.Tests", "--filter-class", "*Probe*"));
        First("a project and a filter name the scope (2)", "TESTS OK: 41 passed, 1 skipped, A.Tests, --filter-class *Probe*, 920ms");
        Holds();
    }

    // The same passing run in German: exit 0, and no summary the parser knows. Never a pass.
    [Fact]
    public void An_unreadable_passing_run()
    {
        var f = $"""
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) erfolgreich (805ms)

            Testlaufzusammenfassung: Erfolgreich!
              gesamt: 41
              fehlgeschlagen: 0
              erfolgreich: 41
              übersprungen: 0
              Dauer: 920ms

            """;
        Exited("an unreadable passing run fails closed", 1, Verdict(0, f, "test"));
        Has("an unreadable passing run is named", "TESTS DID NOT REPORT: exit 0");
        Exited("an empty log with exit 0 fails closed", 1, Verdict(0, "", "test"));
        Has("an empty log with exit 0 is named", "TESTS DID NOT REPORT");
        Holds();
    }

    // The same passing run as GitHub Actions prints it: the test platform colours redirected output there, and
    // an escape sequence opens the summary's lines. Measured 2026-10-15 on the first CI replay of the probes.
    [Fact]
    public void A_passing_run_in_colour()
    {
        var esc = (char)27;
        var f = $"""
            Running tests from test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|x64)
            {esc}[m{R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|x64) {esc}[32mpassed{esc}[m {esc}[90m(17s 005ms){esc}[m

            {esc}[32mTest run summary: Passed!
            {esc}[m  total: 177
              failed: 0
            {esc}[32m  succeeded: 177
            {esc}[m  skipped: 0
              duration: 17s 005ms

            """;
        Exited("a passing run in colour exits 0", 0, Verdict(0, f, "test", "--project", "test/A.Tests"));
        First("a passing run in colour reads its summary", "TESTS OK: 177 passed, 0 skipped, A.Tests, 17s 005ms");
        Holds();
    }

    // A solution-wide filter that matched one test, which passed: every other assembly exits 8, and so does the run.
    [Fact]
    public void A_solution_filter_that_matched_and_passed()
    {
        var f = $"""
            {R}/test/B.Tests/bin/Debug/net10.0/B.Tests.dll (net10.0|arm64) Zero tests ran (440ms)
            Exit code: 8
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) passed (504ms)
            {R}/test/C.Tests/bin/Debug/net10.0/C.Tests.dll (net10.0|arm64) Zero tests ran (404ms)
            Exit code: 8

            Test run summary: Failed!
              error: 8

              total: 1
              failed: 0
              succeeded: 1
              skipped: 0
              duration: 1s 050ms
            Test run completed with non-success exit code: 8 (see: https://aka.ms/testingplatform/exitcodes)

            """;
        Exited("a solution filter that matched and passed exits 0", 0, Verdict(8, f, "test", "--filter-method", "*Probe.Passes"));
        Has("a solution filter that matched names the empty assemblies", "2 assemblies matched no test (exit 8 each)");
        Holds();
    }

    // ---- test: failures -------------------------------------------------------------------------------

    private string FailBlock(string name, int line) => $"""
        failed CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.{name} (23ms)
          from {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
          Shouldly.ShouldAssertException : public void {name}Option.Some(2).Match(x => x, 0)
              should be
          1
              but was
          2
            at CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.{name}() in {R}/test/A.Tests/ZzzVerdictProbeTests.cs:{line}
            at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)

        """;

    private static string Summary(int total, int failed, int succeeded, int skipped) =>
        $"\nTest run summary: Failed!\n  total: {total}\n  failed: {failed}\n  succeeded: {succeeded}\n  skipped: {skipped}\n  duration: 1s 261ms\n" +
        "Test run completed with non-success exit code: 2 (see: https://aka.ms/testingplatform/exitcodes)\n";

    [Fact]
    public void A_failing_run()
    {
        var f = $"Running tests from {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)\n" + $"""
            failed CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_throw (0ms)
              from {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
              Xunit.MicrosoftTestingPlatform.XunitException: System.InvalidOperationException : probe threw
                at CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_throw() in {R}/test/A.Tests/ZzzVerdictProbeTests.cs:9
                at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

            """ + FailBlock("Fails_on_a_value", 6) + $"""
            failed CodoMetis.TypeKit.Tests.ParsingTests.Rounds_trip(text: "1.5", culture: "de-DE") (1s 002ms)
              from {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
              Assert.Equal() Failure: Values differ
              Expected: 1.5
              Actual:   15
                at CodoMetis.TypeKit.Tests.ParsingTests.Rounds_trip(String text, String culture) in {R}/test/A.Tests/ParsingTests.cs:40
              Standard output: noise from the test
            failed Probes.L01JsonBytesProbe.Row [#6 shape.kind=record-struct shape.wrapped=int] (5ms)
              from {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
              Assert.Fail() Failure: [L01 #6] bytes differ
                at Probes.Rec.Run(String claim, Int32 idx) in {R}/test/A.Tests/Probes/Row.cs:12
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) failed with 4 error(s) (1s 147ms)
            Exit code: 2

            """ + Summary(850, 4, 845, 1);
        Exited("a failing run exits 1", 1, Verdict(2, f, "test"));
        First("the failed count leads", "TESTS FAILED: 4 failed of 850 (845 passed, 1 skipped), solution, 1s 261ms");
        Has("a block is headed Class.Method and file:line", "## ZzzVerdictProbeTests.Fails_on_a_value  test/A.Tests/ZzzVerdictProbeTests.cs:6");
        Has("a multi-line assertion is one line", "   ShouldAssertException: public void Fails_on_a_valueOption.Some(2).Match(x => x, 0) should be 1 but was 2");
        Has("an exception loses xUnit's wrapper and namespaces", "   InvalidOperationException: probe threw");
        Has("a theory keeps its arguments, dots and all", "## ParsingTests.Rounds_trip(text: \"1.5\", culture: \"de-DE\")  test/A.Tests/ParsingTests.cs:40");
        Has("a data row keeps its label, dots and all", "## L01JsonBytesProbe.Row [#6 shape.kind=record-struct shape.wrapped=int]  test/A.Tests/Probes/Row.cs:12");
        Has("an Assert.Equal keeps expected and actual", "Assert.Equal() Failure: Values differ Expected: 1.5 Actual:   15");
        Hasnt("from lines are dropped", "  from ");
        Hasnt("frames outside the tree are dropped", "MethodBaseInvoker");
        Hasnt("captured output is dropped", "noise from the test");
        Hasnt("every block was parsed", "failure block(s) parsed");
        Holds();
    }

    // A summary that counts more failures than there are blocks the parser can read: German blocks under
    // an English summary, the shape of a half-pinned run. The verdict says the blocks are missing.
    [Fact]
    public void Unreadable_failure_blocks()
    {
        var f = "fehlerhaft CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_value (16ms)\n" +
                $"  von {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)\n" +
                $"    um CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_value() in {R}/test/A.Tests/ZzzVerdictProbeTests.cs:6\n" +
                FailBlock("Fails_twice", 7) + Summary(850, 2, 848, 0);
        Exited("unreadable failure blocks still fail", 1, Verdict(2, f, "test"));
        Has("unreadable failure blocks are counted", "note: 1 failure block(s) parsed for 2 failed test(s)");
        Holds();
    }

    // The same failing run in German, whole: no summary the parser knows, never a pass and never silent.
    [Fact]
    public void An_unreadable_failing_run()
    {
        var f = """
            fehlerhaft CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_value (16ms)
            Testlaufzusammenfassung: Fehler!
              gesamt: 3704
              fehlgeschlagen: 2
              erfolgreich: 3701
              übersprungen: 1

            """;
        Exited("an unreadable failing run fails", 1, Verdict(2, f, "test"));
        Has("an unreadable failing run is named", "TESTS DID NOT REPORT: exit 2");
        Holds();
    }

    // ---- test: the named exits ------------------------------------------------------------------------

    [Fact]
    public void Exit_5_is_a_rejected_command_line()
    {
        var f = $"""
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0) Zero tests ran
            Exit code: 5

            Test run summary: Failed!
              error: 1

              total: 0
              failed: 0
              succeeded: 0
              skipped: 0
              duration: 83ms
            Test run completed with non-success exit code: 5 (see: https://aka.ms/testingplatform/exitcodes)

            Handshake failures:
              {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0)
            Exit code: 5

            """;
        Exited("exit 5 is a rejected command line", 1, Verdict(5, f, "test", "--project", "test/A.Tests", "--", "-nodeReuse:false"));
        Has("exit 5 is named", "TESTS REJECTED: the test hosts refused the command line (exit 5)");
        Has("exit 5 shows what ran, extras included", "ran: dotnet test --no-build --project test/A.Tests -nodeReuse:false");
        Has("exit 5 names the MSBuild-switch trap", "An MSBuild switch (-nodeReuse:false");
        Holds();
    }

    [Fact]
    public void Exit_8_with_nothing_run()
    {
        var f = $"""
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) Zero tests ran (500ms)
            Exit code: 8

            Test run summary: Zero tests ran
              error: 1

              total: 0
              failed: 0
              succeeded: 0
              skipped: 0
              duration: 614ms

            """;
        Exited("exit 8 with nothing run fails", 1, Verdict(8, f, "test", "--project", "test/A.Tests", "--filter-method", "Nope"));
        Has("exit 8 is named", "TESTS NONE: the filter matched no test (exit 8)");
        Has("exit 8 asks for the fully-qualified name", "match the fully-qualified name");
        Holds();
    }

    [Fact]
    public void A_mix_of_filter_kinds()
    {
        var f = $"""
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) Zero tests ran (297ms)
            Exit code: 134
              Error output: Unhandled exception. System.ArgumentException: Cannot add simple filter or query filter; VSTest filter already exists (Parameter 'query')
                 at Xunit.Runner.Common.XunitFilters.GuardEmptyVSTestFilter(String message) in /_/src/xunit.v3.runner.common/Filtering/XunitFilters.cs:line 266

            Test run summary: Zero tests ran
              error: 1

              total: 0
              failed: 0
              succeeded: 0
              skipped: 0
              duration: 404ms

            """;
        Exited("a mix of filter kinds fails", 1, Verdict(134, f, "test", "--filter-class", "*X", "--filter", "FullyQualifiedName~Y"));
        Has("a mix of filter kinds is named", "TESTS ABORTED: xunit.v3 refused a mix of filter kinds (exit 134)");
        Has("a mix of filter kinds carries its FIX", "FIX   Use one kind per run");
        Holds();
    }

    [Fact]
    public void A_crashed_host()
    {
        var f = $"""
            {R}/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) passed (504ms)
            {R}/test/B.Tests/bin/Debug/net10.0/B.Tests.dll (net10.0|arm64) failed (1s)
            Exit code: 7
              Error output: Stack overflow.
              total: 120
              failed: 0
              succeeded: 120
              skipped: 0

            """;
        Exited("a crashed host fails though nothing failed", 1, Verdict(7, f, "test"));
        Has("a crashed host is named with its exit", "TESTS ABORTED: exit 7, a test host exited without finishing, a crash");
        Has("a crashed host shows its error output", "Stack overflow.");
        Holds();
    }

    // ---- running: a stub dotnet -----------------------------------------------------------------------

    // It records each call, prints German unless DOTNET_CLI_UI_LANGUAGE=en (as this machine does), and
    // fails or passes as STUB_BUILD says. It is a file-based app outside the repository, run once to build
    // it and say where its executable is, which the tool then starts in place of dotnet.
    private const string StubSource = """
        var calls = Environment.GetEnvironmentVariable("STUB_CALLS")!;
        var root = Environment.GetEnvironmentVariable("STUB_ROOT");
        var en = Environment.GetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE") == "en";
        if (args is ["--where"]) { Console.WriteLine(Environment.ProcessPath); return 0; }
        File.AppendAllText(calls, $"{Environment.GetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE")}|{string.Join(' ', args)}\n");
        switch (args.FirstOrDefault())
        {
            case "--version":
                Console.WriteLine("10.0.402");
                break;
            case "build":
                if (Environment.GetEnvironmentVariable("STUB_BUILD") == "fail")
                {
                    Console.WriteLine($"{root}/test/A.Tests/X.cs(1,1): error CS0029: Cannot implicitly convert type 'string' to 'int' [{root}/test/A.Tests/A.Tests.csproj]");
                    return 1;
                }
                Console.WriteLine($"  A.Tests -> {root}/test/A.Tests/bin/A.Tests.dll");
                Console.WriteLine(en ? "Build succeeded.\nTime Elapsed 00:00:01.00" : "Der Buildvorgang wurde erfolgreich ausgeführt.");
                break;
            case "test":
                Console.Write(en ? "  total: 3\n  failed: 0\n  succeeded: 3\n  skipped: 0\n  duration: 1s\n" : "  gesamt: 3\n  fehlgeschlagen: 0\n  erfolgreich: 3\n");
                break;
        }
        return 0;
        """;

    private static readonly Lazy<string> Stub = new(() =>
    {
        var directory = Directory.CreateTempSubdirectory("BuildVerdict-stub-").FullName;
        var source = Path.Combine(directory, "dotnet-stub.cs");
        File.WriteAllText(source, StubSource);
        var (_, output, _) = ToolSandbox.RunProcess("dotnet", ["run", "--file", source, "--", "--where"], directory);
        return output.Trim();
    });

    private string Calls => Path.Combine(sandbox.Tool.Work, "calls");
    private string[] RecordedCalls() => File.Exists(Calls) ? File.ReadAllLines(Calls) : [];
    private string Call(int n) => RecordedCalls().ElementAtOrDefault(n - 1) ?? "";
    private string LogFile => Path.Combine(ToolSandbox.RunProcess("git", ["-C", R, "rev-parse", "--absolute-git-dir"], sandbox.Tool.Work).Output.Trim(), "build-verdict-last.log");

    private int RunStub(string? build, params string[] arguments)
    {
        File.Exists(Stub.Value).ShouldBeTrue($"the stub dotnet did not build: {Stub.Value}");
        File.WriteAllText(Calls, "");
        return Run(new Dictionary<string, string?>
        {
            ["BUILD_VERDICT_DOTNET"] = Stub.Value, ["STUB_CALLS"] = Calls, ["STUB_ROOT"] = R, ["STUB_BUILD"] = build,
        }, arguments);
    }

    [Fact]
    public void A_pinned_build()
    {
        Exited("a pinned build passes", 0, RunStub(null, "build"));
        First("a pinned build reads its English summary", "BUILD OK: 1 projects, 00:00:01.00");
        var languages = RecordedCalls().Select(c => c.Split('|')[0]).Distinct().ToList();
        Check(languages is ["en"], "every dotnet call is pinned to en", string.Join("; ", RecordedCalls()));
        Check(Call(2) == $"en|build {Path.Combine(R, "CodoMetis.TypeKit.slnx")}", "a bare build builds the solution", Call(2));
        Check(File.Exists(LogFile) && File.ReadAllText(LogFile).Contains("Build succeeded", StringComparison.Ordinal), "the log is under the git dir", LogFile);
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(LogFile)!).Any(f => f.EndsWith(".tmp", StringComparison.Ordinal)), "no temporary log is left beside it", "a .tmp remains");
        Holds();
    }

    [Fact]
    public void A_pinned_test_run()
    {
        var project = Path.GetFullPath(Path.Combine(R, "test", "A.Tests"));
        Exited("a pinned test run passes", 0, RunStub(null, "test", "--project", Path.Combine("repo", "test", "A.Tests"), "--filter-class", "*A*", "--", "--output", "Detailed"));
        First("a pinned test run reads its English summary", "TESTS OK: 3 passed, 0 skipped, A.Tests, --filter-class *A*, 1s");
        Check(Call(2) == $"en|build {project}", "the build gets the project, made absolute, and no filter", Call(2));
        Check(Call(3) == $"en|test --no-build --project {project} --filter-class *A* --output Detailed", "the test step is --no-build with the filters and the extras", Call(3));
        var log = File.Exists(LogFile) ? File.ReadAllText(LogFile) : "";
        Check(log.Split('\n').Any(l => l.StartsWith("==== build-verdict: dotnet test", StringComparison.Ordinal)) && log.Contains("Build succeeded", StringComparison.Ordinal),
              "the log holds the build and the test step", log);

        Exited("a bare test run passes", 0, RunStub(null, "test"));
        Check(Call(3) == $"en|test --no-build --solution {Path.Combine(R, "CodoMetis.TypeKit.slnx")}", "a bare test run runs the solution", Call(3));
        Holds();
    }

    /// <summary>
    /// The probe replay builds the probes once and runs many at a time; a build per run would race in the
    /// project's obj directory. So --no-build runs the test step alone, and the verdict reads as it does after
    /// a build.
    /// </summary>
    [Fact]
    public void A_pinned_test_run_without_a_build()
    {
        var project = Path.GetFullPath(Path.Combine(R, "test", "A.Tests"));
        Exited("a test run without a build passes", 0, RunStub(null, "test", "--no-build", "--project", Path.Combine("repo", "test", "A.Tests"), "--filter-class", "*A*"));
        First("a test run without a build reads its summary", "TESTS OK: 3 passed, 0 skipped, A.Tests, --filter-class *A*, 1s");
        Check(RecordedCalls().Length == 2, "no build runs: the SDK check and the test step only", string.Join("; ", RecordedCalls()));
        Check(Call(2) == $"en|test --no-build --project {project} --filter-class *A*", "the test step is --no-build with the filters", Call(2));
        var log = File.Exists(LogFile) ? File.ReadAllText(LogFile) : "";
        Check(log.Split('\n').Any(l => l.StartsWith("==== build-verdict: dotnet test", StringComparison.Ordinal)) && !log.Contains("Build succeeded", StringComparison.Ordinal),
              "the log holds the test step and no build", log);

        Exited("a failing build is still reported when --no-build is not given", 1, RunStub("fail", "test", "--project", Path.Combine("repo", "test", "A.Tests")));
        Holds();
    }

    [Fact]
    public void A_test_run_whose_build_fails()
    {
        Exited("a test run whose build fails exits 1", 1, RunStub("fail", "test", "--project", Path.Combine("repo", "test", "A.Tests")));
        First("a test run whose build fails reports the build", "BUILD FAILED: 1 unique error");
        Check(RecordedCalls().Length == 2, "no test step runs after a failed build", string.Join("; ", RecordedCalls()));
        Holds();
    }

    // ---- usage ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("no arguments is a usage error")]
    [InlineData("an unknown mode is a usage error", "frob")]
    [InlineData("an unknown test option is a usage error", "test", "--frob")]
    [InlineData("a filter without a value is a usage error", "test", "--filter-class")]
    [InlineData("two build targets are a usage error", "build", "a", "b")]
    public void A_usage_error(string name, params string[] arguments)
    {
        Exited(name, 2, Run(new Dictionary<string, string?>(), arguments));
        Holds();
    }
}
