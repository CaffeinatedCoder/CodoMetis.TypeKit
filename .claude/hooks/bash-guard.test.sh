#!/usr/bin/env bash
#
# Executable check of bash-guard.py: the commands it must refuse and the ones it must let through.
#
#   .claude/hooks/bash-guard.test.sh [<hook under test>]
#
# Pointed at a copy with one rule broken (a heredoc read as commands, quotes ignored, a prefix not
# stripped), the cases here must FAIL; that is how each one was proved.

set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
hook=${1:-$here/bash-guard.py}
[ -f "$hook" ] || { printf 'no such hook: %s\n' "$hook" >&2; exit 2; }

err=$(mktemp)
trap 'rm -f "$err"' EXIT INT TERM
failures=0

ok()   { printf 'ok    %s\n' "$1"; }
fail() { printf 'FAIL  %s%s\n' "$1" "${2:+ ($2)}"; failures=$((failures + 1)); }

# guard <command>: runs the hook on a Bash payload; stderr in $err, prints the exit code.
guard() {
    local code=0
    python3 -c 'import json, sys; print(json.dumps({"tool_name": "Bash", "tool_input": {"command": sys.argv[1]}}))' "$1" |
        python3 "$hook" 2> "$err" || code=$?
    echo $code
}
refused() { local c; c=$(guard "$2"); if [ "$c" = 2 ]; then ok "$1"; else fail "$1" "exit $c, wanted 2"; fi; }
allowed() { local c; c=$(guard "$2"); if [ "$c" = 0 ]; then ok "$1"; else fail "$1" "exit $c, wanted 0: $(head -1 "$err")"; fi; }
says()    { if grep -qF -- "$2" "$err"; then ok "$1"; else fail "$1" "missing: $2"; fi; }

# ---- the raw toolchain, in every shape a command takes -------------------------------------------
refused "dotnet build"                              'dotnet build'
refused "dotnet test"                               'dotnet test --solution CodoMetis.TypeKit.slnx'
refused "dotnet msbuild"                            'dotnet msbuild -t:Pack'
refused "dotnet by its path"                        '/usr/local/share/dotnet/dotnet build'
refused "after assignments"                         'PATH=/usr/local/share/dotnet:$PATH DOTNET_ROOT=/usr/local/share/dotnet dotnet test'
refused "after env and its options"                 'env -i DOTNET_CLI_UI_LANGUAGE=en dotnet test'
refused "after time"                                'time dotnet build'
refused "after timeout and its duration"            'timeout -s KILL 600 dotnet test'
refused "after nice -n"                             'nice -n 5 dotnet build'
refused "after cd and &&"                           'cd test && dotnet build'
refused "first in a pipeline, redirected"           'dotnet build CodoMetis.TypeKit.slnx 2>&1 | tail -20'
refused "after ;"                                   'echo start; dotnet test'
refused "after ||"                                  'false || dotnet build'
refused "in the background"                         'dotnet build &'
refused "on the second line"                        $'echo start\ndotnet test'
refused "in a subshell"                             '(cd test; dotnet build)'
refused "in a group"                                '{ dotnet build; }'
refused "in an if"                                  'if true; then dotnet test; fi'
refused "in a loop"                                 'for p in a b; do dotnet test --project "$p"; done'
refused "in a command substitution"                 'out=$(dotnet build)'
refused "in a substitution inside double quotes"    'echo "result: $(dotnet test)"'
refused "in backticks"                              'echo `dotnet build`'
refused "in bash -c"                                'bash -c "dotnet build"'
refused "in bash -lc, after &&"                     "bash -lc 'cd x && dotnet test'"
refused "after a heredoc has ended"                 $'cat > notes.txt <<EOF\nnothing here\nEOF\ndotnet test'
refused "after a tab-stripped heredoc has ended"   $'cat <<-EOF\n\tnothing here\n\tEOF\ndotnet test'
refused "after a background job"                   'sleep 1 & dotnet build'
refused "after a leading redirection"               '2>/dev/null dotnet build'
refused "after a comment has ended"                 $'ls # a note; with a semicolon\ndotnet build'

refused "the verdict piped into tail"               'tools/build-verdict.sh test | tail -5'
says    "the pipe refusal names the pager"          'piped into tail'
refused "the verdict piped into head, redirected"   './tools/build-verdict.sh build 2>&1 | head'
refused "the verdict through bash, piped into less" 'bash tools/build-verdict.sh test |& less'

# ---- what the refusal says -----------------------------------------------------------------------
refused "a refusal for the message"                 'cd x && dotnet test --solution CodoMetis.TypeKit.slnx --filter-class "*OptionTests" --output Detailed -- -nodeReuse:false 2>&1 | tail'
says    "it names the command it refused"           'Refused: raw `dotnet test`'
says    "it gives the script form, filters kept"    'Run: tools/build-verdict.sh test --filter-class *OptionTests -- --output Detailed'
says    "it drops an MSBuild switch from a test"    'Dropped from the test step: -nodeReuse:false'
says    "it says where MSBuild switches go"         'An MSBuild switch belongs on `build -- ...`'
refused "a build refusal for the message"           'dotnet build src/A/A.csproj -c Release'
says    "a build keeps its target, flags after --"  'Run: tools/build-verdict.sh build src/A/A.csproj -- -c Release'

# ---- what passes ---------------------------------------------------------------------------------
allowed "dotnet --version"                          'dotnet --version'
allowed "dotnet restore"                            'dotnet restore CodoMetis.TypeKit.slnx'
allowed "dotnet pack"                               'dotnet pack src/CodoMetis.TypeKit -o dist'
allowed "dotnet tool"                               'dotnet tool restore'
allowed "dotnet ef"                                 'dotnet ef migrations list'
allowed "dotnet build-server is another verb"       'dotnet build-server shutdown'
allowed "help for test"                             'dotnet test --project test/CodoMetis.TypeKit.Tests --no-build --help'
allowed "help for build"                            'dotnet build -h'
allowed "the verdict script"                        'tools/build-verdict.sh test --project test/CodoMetis.TypeKit.Tests --filter-class "*OptionTests"'
allowed "the verdict script redirected to a file"   'tools/build-verdict.sh build > /tmp/v.txt 2>&1'
allowed "the verdict script into grep"              'tools/build-verdict.sh test | grep FAIL'
allowed "the smoke test"                            './test/consumer-smoke-test.sh'
allowed "dotnet as an argument"                     'echo dotnet build'
allowed "inside double quotes"                      'git commit -m "dotnet build is refused"'
allowed "inside double quotes with operators"       'echo "x && dotnet build && y"'
allowed "inside single quotes with operators"       "echo 'x; dotnet test | tail'"
allowed "in a search pattern"                       'grep -n "dotnet test" AGENTS.md'
allowed "in a comment"                              'ls # dotnet build'
allowed "in a comment that holds an operator"      'ls # then; dotnet build'
allowed "in a heredoc body"                         $'cat > probe.sh <<\'EOF\'\ndotnet build\ncd x && dotnet test | tail\nEOF\necho written'
allowed "in a tab-stripped heredoc body"            $'cat <<-EOF\n\tdotnet test\n\tEOF\necho done'
allowed "a here-string"                             'grep -c x <<< "dotnet build"'
allowed "a tail that reads a log"                   'tail -20 "$(git rev-parse --absolute-git-dir)/build-verdict-last.log"'

# ---- payloads it cannot read are allowed ---------------------------------------------------------
code=0; printf 'not json' | python3 "$hook" 2> /dev/null || code=$?
if [ "$code" = 0 ]; then ok "a payload that is not JSON is allowed"; else fail "a payload that is not JSON is allowed" "exit $code"; fi
code=0; printf '{"tool_input": {}}' | python3 "$hook" 2> /dev/null || code=$?
if [ "$code" = 0 ]; then ok "a payload without a command is allowed"; else fail "a payload without a command is allowed" "exit $code"; fi

echo
if [ "$failures" -eq 0 ]; then echo "bash-guard.test.sh: all cases hold"; else echo "bash-guard.test.sh: $failures case(s) FAILED"; exit 1; fi
