#!/usr/bin/env bash
#
# Executable check of build-verdict.sh's parsers and verdicts, against fixture logs and a stub dotnet.
#
#   tools/build-verdict.test.sh [<script under test>]
#
# The fixtures are the English output the script pins with DOTNET_CLI_UI_LANGUAGE=en, cut from real
# runs on this repository (2026-10-15), and beside each parser one fixture it must reject: the German
# output of the same run, a summary with no total, a failure block the parser cannot read. Pointed at
# a copy whose parser has drifted, the cases here must FAIL; that is how each one was proved.
#
# The script under test runs from a throwaway root with its own CodoMetis.TypeKit.slnx marker and git
# repository, so paths, the log location and the stub's record of its calls are all checkable.

set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script=${1:-$here/build-verdict.sh}
[ -f "$script" ] || { printf 'no such script: %s\n' "$script" >&2; exit 2; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT INT TERM
repo=$work/repo
mkdir -p "$repo/tools" "$repo/test/A.Tests" "$repo/test/B.Tests"
: > "$repo/CodoMetis.TypeKit.slnx"
cp "$script" "$repo/tools/build-verdict.sh"
cp "$here/build-verdict-remedies.tsv" "$repo/tools/build-verdict-remedies.tsv"
git init -q "$repo"
verdict_script=$repo/tools/build-verdict.sh
out=$work/out
failures=0

ok()     { printf 'ok    %s\n' "$1"; }
fail()   { printf 'FAIL  %s%s\n' "$1" "${2:+ ($2)}"; failures=$((failures + 1)); }
has()    { if grep -qF -- "$2" "$out"; then ok "$1"; else fail "$1" "missing: $2"; fi; }
hasnt()  { if grep -qF -- "$2" "$out"; then fail "$1" "present: $2"; else ok "$1"; fi; }
first()  { if [ "$(head -1 "$out")" = "$2" ]; then ok "$1"; else fail "$1" "first line: $(head -1 "$out")"; fi; }
exited() { if [ "$2" = "$3" ]; then ok "$1"; else fail "$1" "wanted exit $2, got $3"; fi; }

# verdict <exit> <log> <args...>: the script in parse-only mode; output in $out, prints its exit code.
verdict() {
    local e=$1 l=$2 code=0; shift 2
    BUILD_VERDICT_LOG="$l" BUILD_VERDICT_EXIT="$e" bash "$verdict_script" "$@" > "$out" 2>&1 || code=$?
    echo $code
}

R=$repo
f=$work/fixture

# ---- build: pass ---------------------------------------------------------------------------------
cat > "$f" <<EOT
  Determining projects to restore...
  All projects are up-to-date for restore.
  CodoMetis.TypeKit.Analyzers -> $R/src/CodoMetis.TypeKit.Analyzers/bin/Debug/netstandard2.0/CodoMetis.TypeKit.Analyzers.dll
  CodoMetis.TypeKit -> $R/src/CodoMetis.TypeKit/bin/Debug/net10.0/CodoMetis.TypeKit.dll
  CodoMetis.TypeKit.Tests -> $R/test/CodoMetis.TypeKit.Tests/bin/Debug/net10.0/CodoMetis.TypeKit.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:12.14
EOT
exited "a clean build exits 0"                       0 "$(verdict 0 "$f" build)"
first  "a clean build counts projects and time"      "BUILD OK: 3 projects, 00:00:12.14"
hasnt  "a clean build prints no warnings line"       "warnings:"

# The same build in German, as this machine prints it unpinned: exit 0, and no line the parser knows.
cat > "$f" <<EOT
  CodoMetis.TypeKit -> $R/src/CodoMetis.TypeKit/bin/Debug/net10.0/CodoMetis.TypeKit.dll

Der Buildvorgang wurde erfolgreich ausgeführt.
    0 Warnung(en)
    0 Fehler

Verstrichene Zeit 00:00:12.14
EOT
exited "an unreadable clean build fails closed"      1 "$(verdict 0 "$f" build)"
has    "an unreadable clean build is named"          "BUILD DID NOT REPORT"

# A clean build with a warning that stays a warning (Directory.Build.props).
cat > "$f" <<EOT
$R/test/A.Tests/A.Tests.csproj : warning NU1903: Package 'X' 1.0.0 has a known high severity vulnerability [$R/test/A.Tests/A.Tests.csproj]
Build succeeded.
Time Elapsed 00:00:01.00
EOT
exited "a build with warnings passes"                0 "$(verdict 0 "$f" build)"
has    "warnings are summarised per code"            "warnings: NU1903 x1"
has    "a warning's remedy comes from a prefix row"  "NU1903: NU1901-NU1904 stay warnings on purpose"

# ---- build: failures -----------------------------------------------------------------------------
# The real failing build: a value object not partial, a default Option, a type error; MSBuild prints
# each once inline and once in its summary. B.Tests reports the CS0029 too, as a referencing project
# that compiles the same file would; CMTK1002 shares CMTK1000's row, to exercise "(as above)". The
# lines put LAMA and CS before CMTK in the file, so only the ranking gives CMTK-first.
diag() {
cat <<EOT
$R/test/A.Tests/Probe.cs(9,46): error CMTK0001: Invalid initialization of 'Option': Use Option.Some(value) or Option.None(). (https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0001) [$R/test/A.Tests/A.Tests.csproj]
$R/test/A.Tests/Probe.cs(5,31): error CMTK1000: 'ZzzNotPartial' is a value object and must be declared partial. See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors [$R/test/A.Tests/A.Tests.csproj]
$R/test/A.Tests/Probe.cs(6,31): error CMTK1002: 'ZzzNotReadonly' is a value object and must be declared readonly. See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors [$R/test/A.Tests/A.Tests.csproj]
$R/test/A.Tests/Probe.cs(3,34): error CS0029: Cannot implicitly convert type 'string' to 'int' [$R/test/A.Tests/A.Tests.csproj]
$R/test/A.Tests/Probe.cs(3,34): error CS0029: Cannot implicitly convert type 'string' to 'int' [$R/test/B.Tests/B.Tests.csproj]
$R/test/A.Tests/Probe.cs(1,1): error LAMA0611: Error CS0542 in code generated by aspect 'ValueObjectAspect'. [$R/test/A.Tests/A.Tests.csproj]
EOT
}
{ echo "  Determining projects to restore..."; diag; echo; echo "Build FAILED."; echo; diag; echo "    0 Warning(s)"; echo "    12 Error(s)"; echo; echo "Time Elapsed 00:00:01.80"; } > "$f"
exited "a failed build exits 1"                          1 "$(verdict 1 "$f" build)"
first  "unique errors are counted once, not per repeat"  "BUILD FAILED: 5 unique errors"
has    "paths under the root are made relative"          "## test/A.Tests/Probe.cs:3"
has    "the csproj suffix comes off the message"         "CS0029  Cannot implicitly convert type 'string' to 'int'"
hasnt  "the csproj suffix comes off the message (2)"     "A.Tests.csproj]"
hasnt  "a help link in parentheses comes off"            "README.md#cmtk0001"
hasnt  "a trailing See-link comes off"                   "See https://"
has    "a project-repeated error names its projects"     "(reported by 2 projects: A.Tests.csproj, B.Tests.csproj)"
has    "an exact row carries its FIX"                    "FIX   Use the type's factory"
has    "a prefix row covers the CMTK1 family"            "FIX   The message names the fix"
has    "a second error of the same row says as above"    "FIX   (as above)"
has    "LAMA0611 points at the cascade"                  "FIX   Usually a cascade"
hasnt  "CS codes without a row are not UNMAPPED"         "UNMAPPED"
if [ "$(grep '^CMTK\|^CS\|^LAMA' "$out" | cut -d' ' -f1 | paste -sd ' ' -)" = "CMTK1000 CMTK1002 CMTK0001 CS0029 LAMA0611" ]; then
    ok "CMTK first, LAMA last, by file and line within"
else
    fail "CMTK first, LAMA last, by file and line within" "$(grep '^CMTK\|^CS\|^LAMA' "$out" | cut -d' ' -f1 | paste -sd ' ' -)"
fi
if [ "$(grep -c '^## ' "$out")" = 5 ]; then ok "one block per unique error"; else fail "one block per unique error" "$(grep -c '^## ' "$out") blocks"; fi

# The same failing build in German: the "error CODE:" keyword is not translated, so the codes still read.
cat > "$f" <<EOT
$R/test/A.Tests/Probe.cs(3,34): error CS0029: Der Typ "string" kann nicht implizit in "int" konvertiert werden. [$R/test/A.Tests/A.Tests.csproj]

Fehler beim Buildvorgang.
    1 Fehler
EOT
exited "a German failing build still fails"          1 "$(verdict 1 "$f" build)"
has    "a German failing build still names its code" "CS0029  Der Typ"

# Codes without a row, and a diagnostic with no file.
cat > "$f" <<EOT
$R/src/X/Program.cs(1,1): error FOO1234: Something new. [$R/src/X/X.csproj]
MSBUILD : error MSB1009: Project file does not exist.
CSC : error LAMA0699: Metalama said something new. [$R/src/X/X.csproj]
EOT
exited "unmapped codes still fail"                   1 "$(verdict 1 "$f" build)"
has    "non-CS codes without a row are UNMAPPED"     "UNMAPPED: FOO1234 MSB1009 -- no remedy row"
has    "an unknown LAMA code takes the LAMA row"     "FIX   Check the CMTK errors in the same verdict first"
has    "a diagnostic without a position keeps its origin" "## MSBUILD"
has    "a diagnostic without a position keeps its message" "MSB1009  Project file does not exist."

# Exit 155 from a dotnet that cannot satisfy global.json (Homebrew's 10.0.401 against the 10.0.402 pin).
cat > "$f" <<EOT
The command could not be loaded, possibly because:
  * You intended to execute a .NET SDK command:
      A compatible .NET SDK was not found.

Requested SDK version: 10.0.402
global.json file: $R/global.json

Installed SDKs:
10.0.401 [/opt/homebrew/Cellar/dotnet/10.0.401_1/libexec/sdk]
EOT
exited "an SDK pin failure exits 1"                  1 "$(verdict 155 "$f" build)"
has    "an SDK pin failure is named"                 "cannot satisfy global.json's SDK pin (exit 155)"
has    "an SDK pin failure shows the requested version" "Requested SDK version: 10.0.402"
has    "an SDK pin failure carries its FIX"          "FIX   An environment problem, not a diff problem"
printf 'error: something before MSBuild\n' > "$f"
exited "a pre-MSBuild failure exits 1"               1 "$(verdict 1 "$f" build)"
has    "a pre-MSBuild failure says no diagnostic came" "BUILD FAILED before MSBuild reported any diagnostic (exit 1)"

# ---- test: pass ----------------------------------------------------------------------------------
cat > "$f" <<EOT
Running tests from $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
skipped CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Skipped
  probe skip
  from $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) passed (805ms)

Test run summary: Passed!
  total: 42
  failed: 0
  succeeded: 41
  skipped: 1
  duration: 920ms
EOT
exited "a passing run exits 0"                       0 "$(verdict 0 "$f" test)"
first  "a passing run states counts, scope, time"    "TESTS OK: 41 passed, 1 skipped, solution, 920ms"
has    "three or fewer skips are named"              "skipped: CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Skipped"
exited "a project and a filter name the scope"       0 "$(verdict 0 "$f" test --project test/A.Tests --filter-class '*Probe*')"
first  "a project and a filter name the scope (2)"   "TESTS OK: 41 passed, 1 skipped, A.Tests, --filter-class *Probe*, 920ms"

# The same passing run in German: exit 0, and no summary the parser knows. Never a pass.
cat > "$f" <<EOT
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) erfolgreich (805ms)

Testlaufzusammenfassung: Erfolgreich!
  gesamt: 41
  fehlgeschlagen: 0
  erfolgreich: 41
  übersprungen: 0
  Dauer: 920ms
EOT
exited "an unreadable passing run fails closed"      1 "$(verdict 0 "$f" test)"
has    "an unreadable passing run is named"          "TESTS DID NOT REPORT: exit 0"
: > "$f"
exited "an empty log with exit 0 fails closed"       1 "$(verdict 0 "$f" test)"
has    "an empty log with exit 0 is named"           "TESTS DID NOT REPORT"

# A solution-wide filter that matched one test, which passed: every other assembly exits 8, and so does the run.
cat > "$f" <<EOT
$R/test/B.Tests/bin/Debug/net10.0/B.Tests.dll (net10.0|arm64) Zero tests ran (440ms)
Exit code: 8
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) passed (504ms)
$R/test/C.Tests/bin/Debug/net10.0/C.Tests.dll (net10.0|arm64) Zero tests ran (404ms)
Exit code: 8

Test run summary: Failed!
  error: 8

  total: 1
  failed: 0
  succeeded: 1
  skipped: 0
  duration: 1s 050ms
Test run completed with non-success exit code: 8 (see: https://aka.ms/testingplatform/exitcodes)
EOT
exited "a solution filter that matched and passed exits 0" 0 "$(verdict 8 "$f" test --filter-method '*Probe.Passes')"
has    "a solution filter that matched names the empty assemblies" "2 assemblies matched no test (exit 8 each)"

# ---- test: failures ------------------------------------------------------------------------------
fail_block() { # <name> <line>: the real shape of a Shouldly failure
cat <<EOT
failed CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.$1 (23ms)
  from $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
  Shouldly.ShouldAssertException : public void $1Option.Some(2).Match(x => x, 0)
      should be
  1
      but was
  2
    at CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.$1() in $R/test/A.Tests/ZzzVerdictProbeTests.cs:$2
    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
EOT
}
summary() { # <total> <failed> <succeeded> <skipped>
printf '\nTest run summary: Failed!\n  total: %s\n  failed: %s\n  succeeded: %s\n  skipped: %s\n  duration: 1s 261ms\nTest run completed with non-success exit code: 2 (see: https://aka.ms/testingplatform/exitcodes)\n' "$@"
}
{
    echo "Running tests from $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)"
    cat <<EOT
failed CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_throw (0ms)
  from $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
  Xunit.MicrosoftTestingPlatform.XunitException: System.InvalidOperationException : probe threw
    at CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_throw() in $R/test/A.Tests/ZzzVerdictProbeTests.cs:9
    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
EOT
    fail_block Fails_on_a_value 6
    cat <<EOT
failed CodoMetis.TypeKit.Tests.ParsingTests.Rounds_trip(text: "1.5", culture: "de-DE") (1s 002ms)
  from $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)
  Assert.Equal() Failure: Values differ
  Expected: 1.5
  Actual:   15
    at CodoMetis.TypeKit.Tests.ParsingTests.Rounds_trip(String text, String culture) in $R/test/A.Tests/ParsingTests.cs:40
  Standard output: noise from the test
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) failed with 3 error(s) (1s 147ms)
Exit code: 2
EOT
    summary 850 3 846 1
} > "$f"
exited "a failing run exits 1"                       1 "$(verdict 2 "$f" test)"
first  "the failed count leads"                      "TESTS FAILED: 3 failed of 850 (846 passed, 1 skipped), solution, 1s 261ms"
has    "a block is headed Class.Method and file:line" "## ZzzVerdictProbeTests.Fails_on_a_value  test/A.Tests/ZzzVerdictProbeTests.cs:6"
has    "a multi-line assertion is one line"          "   ShouldAssertException: public void Fails_on_a_valueOption.Some(2).Match(x => x, 0) should be 1 but was 2"
has    "an exception loses xUnit's wrapper and namespaces" "   InvalidOperationException: probe threw"
has    "a theory keeps its arguments, dots and all"  "## ParsingTests.Rounds_trip(text: \"1.5\", culture: \"de-DE\")  test/A.Tests/ParsingTests.cs:40"
has    "an Assert.Equal keeps expected and actual"   "Assert.Equal() Failure: Values differ Expected: 1.5 Actual:   15"
hasnt  "from lines are dropped"                      "  from "
hasnt  "frames outside the tree are dropped"         "MethodBaseInvoker"
hasnt  "captured output is dropped"                  "noise from the test"
hasnt  "every block was parsed"                      "failure block(s) parsed"

# A summary that counts more failures than there are blocks the parser can read: German blocks under
# an English summary, the shape of a half-pinned run. The verdict says the blocks are missing.
{
    echo "fehlerhaft CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_value (16ms)"
    echo "  von $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64)"
    echo "    um CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_value() in $R/test/A.Tests/ZzzVerdictProbeTests.cs:6"
    fail_block Fails_twice 7
    summary 850 2 848 0
} > "$f"
exited "unreadable failure blocks still fail"        1 "$(verdict 2 "$f" test)"
has    "unreadable failure blocks are counted"       "note: 1 failure block(s) parsed for 2 failed test(s)"

# The same failing run in German, whole: no summary the parser knows, never a pass and never silent.
cat > "$f" <<EOT
fehlerhaft CodoMetis.TypeKit.Tests.ZzzVerdictProbeTests.Fails_on_a_value (16ms)
Testlaufzusammenfassung: Fehler!
  gesamt: 3704
  fehlgeschlagen: 2
  erfolgreich: 3701
  übersprungen: 1
EOT
exited "an unreadable failing run fails"             1 "$(verdict 2 "$f" test)"
has    "an unreadable failing run is named"          "TESTS DID NOT REPORT: exit 2"

# ---- test: the named exits -----------------------------------------------------------------------
cat > "$f" <<EOT
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0) Zero tests ran
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
  $R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0)
Exit code: 5
EOT
exited "exit 5 is a rejected command line"          1 "$(verdict 5 "$f" test --project test/A.Tests -- -nodeReuse:false)"
has    "exit 5 is named"                             "TESTS REJECTED: the test hosts refused the command line (exit 5)"
has    "exit 5 shows what ran, extras included"      "ran: dotnet test --no-build --project test/A.Tests -nodeReuse:false"
has    "exit 5 names the MSBuild-switch trap"        "An MSBuild switch (-nodeReuse:false"

cat > "$f" <<EOT
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) Zero tests ran (500ms)
Exit code: 8

Test run summary: Zero tests ran
  error: 1

  total: 0
  failed: 0
  succeeded: 0
  skipped: 0
  duration: 614ms
EOT
exited "exit 8 with nothing run fails"               1 "$(verdict 8 "$f" test --project test/A.Tests --filter-method Nope)"
has    "exit 8 is named"                             "TESTS NONE: the filter matched no test (exit 8)"
has    "exit 8 asks for the fully-qualified name"    "match the fully-qualified name"

cat > "$f" <<EOT
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) Zero tests ran (297ms)
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
EOT
exited "a mix of filter kinds fails"                 1 "$(verdict 134 "$f" test --filter-class '*X' --filter 'FullyQualifiedName~Y')"
has    "a mix of filter kinds is named"              "TESTS ABORTED: xunit.v3 refused a mix of filter kinds (exit 134)"
has    "a mix of filter kinds carries its FIX"       "FIX   Use one kind per run"

cat > "$f" <<EOT
$R/test/A.Tests/bin/Debug/net10.0/A.Tests.dll (net10.0|arm64) passed (504ms)
$R/test/B.Tests/bin/Debug/net10.0/B.Tests.dll (net10.0|arm64) failed (1s)
Exit code: 7
  Error output: Stack overflow.
  total: 120
  failed: 0
  succeeded: 120
  skipped: 0
EOT
exited "a crashed host fails though nothing failed"  1 "$(verdict 7 "$f" test)"
has    "a crashed host is named with its exit"       "TESTS ABORTED: exit 7, a test host exited without finishing, a crash"
has    "a crashed host shows its error output"       "Stack overflow."

# ---- running: a stub dotnet ----------------------------------------------------------------------
# It records each call, prints German unless DOTNET_CLI_UI_LANGUAGE=en (as this machine does), and
# fails or passes as STUB_BUILD and STUB_TEST say.
stub=$work/dotnet
calls=$work/calls
cat > "$stub" <<'EOT'
#!/usr/bin/env bash
printf '%s|%s\n' "${DOTNET_CLI_UI_LANGUAGE:-}" "$*" >> "$STUB_CALLS"
en=no; [ "${DOTNET_CLI_UI_LANGUAGE:-}" = en ] && en=yes
case $1 in
    --version) echo 10.0.402 ;;
    build)
        if [ "${STUB_BUILD:-ok}" = fail ]; then
            echo "$STUB_ROOT/test/A.Tests/X.cs(1,1): error CS0029: Cannot implicitly convert type 'string' to 'int' [$STUB_ROOT/test/A.Tests/A.Tests.csproj]"
            exit 1
        fi
        echo "  A.Tests -> $STUB_ROOT/test/A.Tests/bin/A.Tests.dll"
        if [ $en = yes ]; then echo 'Build succeeded.'; echo 'Time Elapsed 00:00:01.00'; else echo 'Der Buildvorgang wurde erfolgreich ausgeführt.'; fi ;;
    test)
        if [ $en = yes ]; then printf '  total: 3\n  failed: 0\n  succeeded: 3\n  skipped: 0\n  duration: 1s\n'
        else printf '  gesamt: 3\n  fehlgeschlagen: 0\n  erfolgreich: 3\n'; fi ;;
esac
EOT
chmod +x "$stub"
run_stub() { # <args...>: output in $out, prints the exit code
    local code=0
    : > "$calls"
    (cd "$work" && BUILD_VERDICT_DOTNET="$stub" STUB_CALLS="$calls" STUB_ROOT="$repo" bash "$verdict_script" "$@") > "$out" 2>&1 || code=$?
    echo $code
}
call() { sed -n "$1p" "$calls"; }
log_file=$(git -C "$repo" rev-parse --absolute-git-dir)/build-verdict-last.log

exited "a pinned build passes"                       0 "$(run_stub build)"
first  "a pinned build reads its English summary"    "BUILD OK: 1 projects, 00:00:01.00"
if [ "$(cut -d'|' -f1 "$calls" | sort -u)" = en ]; then ok "every dotnet call is pinned to en"; else fail "every dotnet call is pinned to en" "$(cat "$calls")"; fi
if [ "$(call 2)" = "en|build $repo/CodoMetis.TypeKit.slnx" ]; then ok "a bare build builds the solution"; else fail "a bare build builds the solution" "$(call 2)"; fi
if [ -s "$log_file" ] && grep -q 'Build succeeded' "$log_file"; then ok "the log is under the git dir"; else fail "the log is under the git dir" "$log_file"; fi

exited "a pinned test run passes"                    0 "$(run_stub test --project repo/test/A.Tests --filter-class '*A*' -- --output Detailed)"
first  "a pinned test run reads its English summary" "TESTS OK: 3 passed, 0 skipped, A.Tests, --filter-class *A*, 1s"
if [ "$(call 2)" = "en|build $repo/test/A.Tests" ]; then ok "the build gets the project, made absolute, and no filter"; else fail "the build gets the project, made absolute, and no filter" "$(call 2)"; fi
if [ "$(call 3)" = "en|test --no-build --project $repo/test/A.Tests --filter-class *A* --output Detailed" ]; then
    ok "the test step is --no-build with the filters and the extras"
else
    fail "the test step is --no-build with the filters and the extras" "$(call 3)"
fi
if grep -q '^==== build-verdict: dotnet test' "$log_file" && grep -q 'Build succeeded' "$log_file"; then ok "the log holds the build and the test step"; else fail "the log holds the build and the test step"; fi

exited "a bare test run passes"                      0 "$(run_stub test)"
if [ "$(call 3)" = "en|test --no-build --solution $repo/CodoMetis.TypeKit.slnx" ]; then ok "a bare test run runs the solution"; else fail "a bare test run runs the solution" "$(call 3)"; fi

export STUB_BUILD=fail
exited "a test run whose build fails exits 1"        1 "$(run_stub test --project repo/test/A.Tests)"
first  "a test run whose build fails reports the build" "BUILD FAILED: 1 unique error"
if [ "$(wc -l < "$calls" | tr -d ' ')" = 2 ]; then ok "no test step runs after a failed build"; else fail "no test step runs after a failed build" "$(cat "$calls")"; fi
unset STUB_BUILD

# ---- usage ---------------------------------------------------------------------------------------
usage_case() { # <name> <args...>
    local name=$1 code=0; shift
    bash "$verdict_script" "$@" > "$out" 2>&1 || code=$?
    exited "$name" 2 "$code"
}
usage_case "no arguments is a usage error"
usage_case "an unknown mode is a usage error"         frob
usage_case "an unknown test option is a usage error"  test --frob
usage_case "a filter without a value is a usage error" test --filter-class
usage_case "two build targets are a usage error"      build a b

echo
if [ "$failures" -eq 0 ]; then echo "build-verdict.test.sh: all cases hold"; else echo "build-verdict.test.sh: $failures case(s) FAILED"; exit 1; fi
