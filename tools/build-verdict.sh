#!/usr/bin/env bash
#
# Runs the dotnet toolchain and prints a verdict instead of the transcript: the exit code read and
# named, the diagnostic codes deduplicated and each paired with its remedy from
# build-verdict-remedies.tsv, the failing tests by name with their assertion. The full log stays at
# $GIT_DIR/build-verdict-last.log, for the rare question the verdict does not answer.
#
# Usage:
#   tools/build-verdict.sh build [<project-dir|csproj|slnx>] [-- <dotnet build flags>]
#   tools/build-verdict.sh test [--project <dir|csproj>] [--filter-class <C>] [--filter-method <M>] [-- <dotnet test flags>]
#
# Every --filter-* option of the test hosts is passed on with its value, and may repeat (the
# filters of one kind OR together). Without --project the whole solution builds and runs.
#
# The verdict's first line is one of BUILD OK, BUILD FAILED, BUILD DID NOT REPORT, TESTS OK, TESTS
# FAILED, TESTS REJECTED, TESTS NONE, TESTS ABORTED, TESTS DID NOT REPORT. Exit 0 is a pass, 1 a
# failed verdict, 2 a usage error.
#
# Details each of which was measured on this repository:
#   - DOTNET_CLI_UI_LANGUAGE=en pins the lines the parsers key on. Without it this machine prints
#     "0 Fehler", "fehlerhaft <test>" and "fehlgeschlagen: 2", and an English-only parser finds
#     nothing on a failing run exactly as on a passing one.
#   - A test run is `dotnet build`, then `dotnet test --no-build`: a compile error reports as a build
#     verdict, and Metalama weaves the tree once.
#   - `dotnet test` runs on Microsoft Testing Platform (global.json), which forwards an option it does
#     not know to every test host: an MSBuild switch there exits 5 having run nothing. A filter that
#     matches nothing exits 8. Both are named verdicts, never a pass and never a bare failure.
#   - A filter on the whole solution exits 8 even when it matched tests and they passed, because
#     each assembly it matched nothing in reports 8. With a total above zero and no failure that is a
#     pass, said with the count of such assemblies; with a failure among them the run exits 1.
#   - An empty parse is never a pass: an exit 0 without the summary block the parsers read is a
#     DID NOT REPORT verdict, exit 1.
#   - MSBuild prints a diagnostic once inline and once in its summary, and a project's errors again
#     for each project that references it; the trailing [X.csproj] comes off before the dedup.
#   - An agent's shell may find a dotnet first on PATH that cannot satisfy global.json (exit 155).
#     Then the one in /usr/local/share/dotnet is used, if it can, and the verdict says so.
#
# Parse-only mode, for build-verdict.test.sh: BUILD_VERDICT_LOG=<file> BUILD_VERDICT_EXIT=<n> reads
# that fixture as the output of the one dotnet command the mode would run (for `test`, the test
# step), and runs nothing. BUILD_VERDICT_DOTNET=<path> runs that executable in place of dotnet.

set -uo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$script_dir
until [ -f "$root/CodoMetis.TypeKit.slnx" ]; do
    [ "$root" != / ] || { echo 'build-verdict: no CodoMetis.TypeKit.slnx above the script' >&2; exit 2; }
    root=$(dirname "$root")
done
solution=$root/CodoMetis.TypeKit.slnx
remedies=$script_dir/build-verdict-remedies.tsv
# Internal rows are separated by the unit separator, never a tab: tab is IFS whitespace, so `read`
# would collapse an empty field (a diagnostic with no file) into its neighbours.
us=$'\037'

parse_only=${BUILD_VERDICT_LOG:+yes}
if [ -n "$parse_only" ]; then
    log=$BUILD_VERDICT_LOG
else
    gitdir=$(git -C "$root" rev-parse --absolute-git-dir 2>/dev/null) || gitdir=${TMPDIR:-/tmp}
    log=$gitdir/build-verdict-last.log
fi
dotnet=${BUILD_VERDICT_DOTNET:-dotnet}
note=""

usage() {
    sed -n '/^# Usage:/,/^#$/p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
}

rel() { case $1 in "$root"/*) printf '%s' "${1#"$root"/}" ;; *) printf '%s' "$1" ;; esac; }
log_line() { printf '\nlog: %s\n' "$log"; }
# A tail of the log for the reader: repository paths relative, indented.
excerpt() { tail -"$1" | sed -e "s|$root/||g" -e 's/^/  /'; }
rel_args() { local a out=""; for a in "$@"; do out="$out $(rel "$a")"; done; printf '%s' "${out# }"; }

# The remedy for a code: its own row, else the longest prefix row (a code ending in *).
remedy() {
    awk -F'\t' -v c="$1" '
        /^#/ { next }
        $1 == c { print $3; found = 1; exit }
        $1 ~ /\*$/ { p = substr($1, 1, length($1) - 1); if (index(c, p) == 1 && length(p) > best) { best = length(p); r = $3 } }
        END { if (!found && best) print r }' "$remedies"
}

fix_line() { local r; r=$(remedy "$1"); [ -z "$r" ] || printf 'FIX   %s\n' "$r"; }

# ---- running ---------------------------------------------------------------------------------------

# A dotnet that cannot satisfy global.json fails every command with the same message. Try the one the
# SDK installer puts in /usr/local/share/dotnet before giving up.
resolve_sdk() {
    [ -z "$parse_only" ] || return 0
    (cd "$root" && DOTNET_CLI_UI_LANGUAGE=en "$dotnet" --version) > "$log" 2>&1 && return 0
    local fallback=/usr/local/share/dotnet
    if [ -z "${BUILD_VERDICT_DOTNET:-}" ] && [ -x "$fallback/dotnet" ] &&
        (cd "$root" && DOTNET_CLI_UI_LANGUAGE=en "$fallback/dotnet" --version) > /dev/null 2>&1; then
        note="note: $(command -v dotnet || echo dotnet) cannot satisfy global.json; used $fallback/dotnet"
        export PATH="$fallback:$PATH" DOTNET_ROOT=$fallback
        return 0
    fi
    report_build 1
    exit 1
}

# Runs one dotnet command from the repository root; its output goes to the log (appended when
# $append is set). Returns the command's exit code, or the fixture's.
run() {
    if [ -n "$parse_only" ]; then return "${BUILD_VERDICT_EXIT:-0}"; fi
    if [ -n "${append:-}" ]; then
        printf '\n==== build-verdict: dotnet %s\n' "$*" >> "$log"
        (cd "$root" && DOTNET_CLI_UI_LANGUAGE=en "$dotnet" "$@") >> "$log" 2>&1
    else
        (cd "$root" && DOTNET_CLI_UI_LANGUAGE=en "$dotnet" "$@") > "$log" 2>&1
    fi
}

# The test step's part of the log: after the marker `run` writes, or the whole fixture.
test_section() {
    if grep -q '^==== build-verdict: dotnet test' "$log"; then
        sed -n '/^==== build-verdict: dotnet test/,$p' "$log" | sed 1d
    else
        cat "$log"
    fi
}

# The build step's part of the log: before the test marker.
build_section() { sed '/^==== build-verdict: dotnet test/,$d' "$log"; }

# ---- build -----------------------------------------------------------------------------------------

# One row per distinct diagnostic: severity, code, file, line, message, the number of projects that
# reported it, and their names. Repository paths become relative and help links come off the message.
# CMTK first, since a refused declaration is the cause of the CS and LAMA errors on the type it left
# ungenerated; LAMA last for the same reason.
diagnostics() {
    build_section | grep -E '(^|[ :])(error|warning) [A-Za-z]+[0-9]+: ' |
        awk -v root="$root/" '
        {
            line = $0; sub(/\r$/, "", line); sub(/^[ \t]+/, "", line); proj = ""
            if (match(line, / \[[^][]*\]$/)) {
                p = substr(line, RSTART + 2, RLENGTH - 3)
                if (p ~ /\.(cs|fs|vb)proj(::.*)?$/ || p ~ /\.slnx?$/) {
                    line = substr(line, 1, RSTART - 1); sub(/::.*/, "", p); k = split(p, parts, "/"); proj = parts[k]
                }
            }
            if ((i = index(line, ": error ")) > 0)        { sev = "error";   left = substr(line, 1, i - 1); rest = substr(line, i + 8) }
            else if ((i = index(line, ": warning ")) > 0) { sev = "warning"; left = substr(line, 1, i - 1); rest = substr(line, i + 10) }
            else if (line ~ /^error /)                    { sev = "error";   left = ""; rest = substr(line, 7) }
            else if (line ~ /^warning /)                  { sev = "warning"; left = ""; rest = substr(line, 9) }
            else next
            if (rest !~ /^[A-Za-z]+[0-9]+: /) next
            code = rest; sub(/:.*/, "", code); msg = rest; sub(/^[A-Za-z]+[0-9]+: */, "", msg)
            sub(/ \(https?:\/\/[^ ()]*\)$/, "", msg); sub(/ See https?:\/\/[^ ]*$/, "", msg)
            file = left; ln = ""
            if (match(file, /\([0-9]+(,[0-9]+)*\)$/)) { pos = substr(file, RSTART + 1, RLENGTH - 2); sub(/,.*/, "", pos); ln = pos; file = substr(file, 1, RSTART - 1) }
            sub(/[ \t]+$/, "", file)
            if (index(file, root) == 1) file = substr(file, length(root) + 1)
            key = sev SUBSEP code SUBSEP file SUBSEP ln SUBSEP msg
            if (!(key in seen)) { seen[key] = 1; order[++n] = key }
            if (proj != "" && !((key, proj) in pseen)) { pseen[key, proj] = 1; projs[key] = projs[key] (np[key]++ ? ", " : "") proj }
        }
        END {
            for (j = 1; j <= n; j++) {
                key = order[j]; split(key, f, SUBSEP)
                rank = (f[2] ~ /^CMTK/) ? 0 : ((f[2] ~ /^LAMA/) ? 2 : 1)
                printf "%d\037%s\037%s\037%s\037%s\037%s\037%d\037%s\n", rank, f[1], f[2], f[3], f[4], f[5], np[key], projs[key]
            }
        }' | sort -t "$us" -k1,1n -k4,4 -k5,5n | cut -d "$us" -f2-
}

warn_summary() { # warning rows
    [ -n "$1" ] || return 0
    printf '%s\n' "$1" | awk -F"$us" '{ n[$2]++ } END { for (c in n) printf "%s x%d\n", c, n[c] }' | sort |
        paste -sd ',' - | sed 's/,/, /g; s/^/warnings: /'
    local c
    for c in $(printf '%s\n' "$1" | cut -d "$us" -f2 | sort -u); do
        local r; r=$(remedy "$c"); [ -z "$r" ] || printf '      %s: %s\n' "$c" "$r"
    done
}

report_build() { # exit code
    local diags errors warnings n_err
    diags=$(diagnostics)
    errors=$(printf '%s\n' "$diags" | awk -F"$us" '$1 == "error"')
    warnings=$(printf '%s\n' "$diags" | awk -F"$us" '$1 == "warning"')
    n_err=0; [ -z "$errors" ] || n_err=$(printf '%s\n' "$errors" | wc -l | tr -d ' ')
    [ -z "$note" ] || printf '%s\n' "$note"

    if [ "$1" -eq 0 ] && [ "$n_err" -eq 0 ]; then
        if ! build_section | grep -q '^Build succeeded'; then
            printf 'BUILD DID NOT REPORT: exit 0 and no "Build succeeded." line, so the output format or language moved\n\n'
            build_section | excerpt 15
            log_line; return 1
        fi
        local projects elapsed
        projects=$(build_section | grep -c ' -> ')
        elapsed=$(build_section | sed -n 's/^Time Elapsed //p' | tail -1)
        printf 'BUILD OK: %s projects, %s\n' "$projects" "${elapsed:-?}"
        warn_summary "$warnings"
        return 0
    fi

    if [ "$n_err" -gt 0 ]; then
        printf 'BUILD FAILED: %s unique error%s\n' "$n_err" "$([ "$n_err" -eq 1 ] || echo s)"
        # A remedy already printed, for this code or another that shares its row, is not printed again.
        local unmapped="" printed="" sev code file ln msg nproj projs fix
        while IFS="$us" read -r sev code file ln msg nproj projs; do
            printf '\n## %s%s\n' "${file:-(no file)}" "${ln:+:$ln}"
            printf '%s  %s\n' "$code" "$msg"
            fix=$(remedy "$code")
            if [ -z "$fix" ]; then
                case $code in CS[0-9]*) ;; *) unmapped="$unmapped $code" ;; esac
            elif printf '%s\n' "$printed" | grep -qxF -- "$fix"; then
                echo 'FIX   (as above)'
            else
                printf 'FIX   %s\n' "$fix"; printed="$printed"$'\n'"$fix"
            fi
            [ "${nproj:-0}" -le 1 ] || printf '      (reported by %s projects: %s)\n' "$nproj" "$projs"
        done <<< "$errors"
        if [ -n "$warnings" ]; then echo; warn_summary "$warnings"; fi
        if [ -n "$unmapped" ]; then
            unmapped=$(printf '%s\n' $unmapped | sort -u | paste -sd ' ' -)
            printf '\nUNMAPPED: %s -- no remedy row; read the message, and add a row to tools/build-verdict-remedies.tsv once the fix is known\n' "$unmapped"
        fi
        log_line
        return 1
    fi

    # A failing exit with no diagnostic: the failure is before MSBuild's, in the SDK or the restore.
    if build_section | grep -q 'global\.json file:'; then
        echo "BUILD FAILED: the dotnet on PATH cannot satisfy global.json's SDK pin (exit $1)"
        build_section | grep -E '^(Requested SDK version|global\.json file):' | sed 's/^/      /'
        fix_line exit:sdk
    else
        printf 'BUILD FAILED before MSBuild reported any diagnostic (exit %s); the last lines:\n\n' "$1"
        build_section | excerpt 20
    fi
    log_line
    return 1
}

# ---- test ------------------------------------------------------------------------------------------

skipped_names() {
    local names n
    names=$(test_section | sed -n 's/^skipped //p')
    [ -n "$names" ] || return 0
    n=$(printf '%s\n' "$names" | wc -l | tr -d ' ')
    if [ "$n" -le 3 ]; then printf '%s\n' "$names" | sed 's/^/skipped: /'
    else printf 'skipped: %s tests\n' "$n"; fi
}

# One block per failed test: Class.Method (and a theory's arguments), the first frame in this tree as
# file:line, and the assertion's lines up to the first stack frame joined into one. `from` lines,
# frames outside the tree and captured output are dropped. The count of blocks goes to stderr.
failure_blocks() {
    test_section | awk -v root="$root/" '
        function short(full,    head, tail, i, k, p) {
            i = index(full, "("); head = (i > 0) ? substr(full, 1, i - 1) : full; tail = (i > 0) ? substr(full, i) : ""
            k = split(head, p, ".")
            return ((k >= 2) ? p[k-1] "." p[k] : head) tail
        }
        function bare(s,    t, k, a) { # Namespace.FooException : msg -> FooException: msg
            if (match(s, /^[A-Za-z0-9_.`]+Exception ?: /)) {
                t = substr(s, 1, RLENGTH); sub(/ ?: $/, "", t); k = split(t, a, ".")
                s = a[k] ": " substr(s, RLENGTH + 1)
            }
            return s
        }
        function flush(    text, j) {
            if (name == "") return
            text = ""
            for (j = 1; j <= n && j <= 8; j++) text = text (j > 1 ? " " : "") body[j]
            text = bare(text); sub(/^XunitException: /, "", text); text = bare(text)
            if (length(text) > 400) text = substr(text, 1, 400) "..."
            printf "\n## %s%s\n", short(name), (loc != "" ? "  " loc : "")
            if (text != "") printf "   %s\n", text
            blocks++; name = ""; n = 0; loc = ""
        }
        /^failed / {
            flush(); name = substr($0, 8); sub(/ \(([0-9]+h )?([0-9]+m )?([0-9]+s )?[0-9]+m?s\)$/, "", name)
            on = 1; instack = 0; next
        }
        on && /^  from /   { next }
        on && /^    at /   {
            instack = 1
            if (loc == "" && match($0, / in \/.*:[0-9]+$/)) { f = substr($0, RSTART + 4); if (index(f, root) == 1) loc = substr(f, length(root) + 1) }
            next
        }
        on && /^[^ ]/      { flush(); on = 0; next }
        on && !instack && /^  / { s = $0; sub(/^[ \t]+/, "", s); if (s != "" && s !~ /^Standard output:/) body[++n] = s; next }
        END { flush(); printf "%d\n", blocks + 0 > "/dev/stderr" }
    '
}

mtp_exit_meaning() {
    case $1 in
        1)  echo 'a generic failure' ;;
        2)  echo 'a test failed, though the summary counted none' ;;
        3)  echo 'the test session was aborted' ;;
        4)  echo 'the platform setup is invalid' ;;
        7)  echo 'a test host exited without finishing, a crash' ;;
        9)  echo 'fewer tests ran than --minimum-expected-tests asks' ;;
        10) echo 'the test adapter failed the session' ;;
        13) echo 'the run stopped at --maximum-failed-tests' ;;
        134) echo 'a test host aborted (SIGABRT), usually an unhandled exception' ;;
        *)  echo 'see https://aka.ms/testingplatform/exitcodes' ;;
    esac
}

report_test() { # exit scope command-line
    local counts n_pass n_skip n_total n_fail seen duration
    counts=$(test_section | awk '
        /^[ \t]*total:[ \t]+[0-9]+[ \t]*$/     { t += $2; seen = 1 }
        /^[ \t]*failed:[ \t]+[0-9]+[ \t]*$/    { f += $2 }
        /^[ \t]*succeeded:[ \t]+[0-9]+[ \t]*$/ { p += $2 }
        /^[ \t]*skipped:[ \t]+[0-9]+[ \t]*$/   { s += $2 }
        END { printf "%d %d %d %d %d\n", p, s, t, f, seen + 0 }')
    read -r n_pass n_skip n_total n_fail seen <<< "$counts"
    duration=$(test_section | sed -n 's/^[ \t]*duration: //p' | tail -1)
    [ -z "$note" ] || printf '%s\n' "$note"

    if [ "$1" -eq 5 ]; then
        echo 'TESTS REJECTED: the test hosts refused the command line (exit 5) and ran nothing'
        printf '      ran: %s\n' "$3"
        fix_line exit:test:5
        log_line; return 1
    fi

    if test_section | grep -q 'Cannot add simple filter or query filter'; then
        printf 'TESTS ABORTED: xunit.v3 refused a mix of filter kinds (exit %s) and ran nothing\n' "$1"
        printf '      ran: %s\n' "$3"
        fix_line exit:test:filter-mix
        log_line; return 1
    fi

    if [ "$1" -eq 8 ] && [ "$n_total" -eq 0 ]; then
        echo 'TESTS NONE: the filter matched no test (exit 8)'
        printf '      ran: %s\n' "$3"
        fix_line exit:test:8
        log_line; return 1
    fi

    if [ "$seen" -eq 0 ] || [ "$n_total" -eq 0 ]; then
        printf 'TESTS DID NOT REPORT: exit %s and no test summary with a total was parsed; either nothing ran or the output format moved\n\n' "$1"
        test_section | excerpt 15
        log_line; return 1
    fi

    # The exit codes of the assemblies other than 0, and the 8 of each assembly a filter matched nothing in.
    local other_exits zero_ran
    other_exits=$(test_section | sed -n 's/^Exit code: \([0-9]*\)$/\1/p' | grep -vxE '0|8' | sort -u | paste -sd ' ' -)
    zero_ran=$(test_section | grep -cE '^Exit code: 8$')

    if [ "$n_fail" -eq 0 ] && { [ "$1" -eq 0 ] || { [ "$1" -eq 8 ] && [ -z "$other_exits" ]; }; }; then
        printf 'TESTS OK: %s passed, %s skipped, %s, %s\n' "$n_pass" "$n_skip" "$2" "${duration:-?}"
        [ "$zero_ran" -eq 0 ] || printf '      %s assemblies matched no test (exit 8 each), which is the run'"'"'s exit code too\n' "$zero_ran"
        skipped_names
        return 0
    fi

    if [ "$n_fail" -gt 0 ]; then
        local blocks_file n_blocks
        blocks_file=$(mktemp)
        printf 'TESTS FAILED: %s failed of %s (%s passed, %s skipped), %s, %s\n' "$n_fail" "$n_total" "$n_pass" "$n_skip" "$2" "${duration:-?}"
        failure_blocks 2> "$blocks_file"
        n_blocks=$(tr -d ' \n' < "$blocks_file"); rm -f "$blocks_file"
        if [ "${n_blocks:-0}" -ne "$n_fail" ]; then
            printf '\nnote: %s failure block(s) parsed for %s failed test(s); the log has the rest\n' "${n_blocks:-0}" "$n_fail"
        fi
        echo
        skipped_names
        log_line
        return 1
    fi

    local why
    why=$(test_section | sed -n 's/^[ \t]*Error output: //p' | head -1)
    printf 'TESTS ABORTED: exit %s, %s; %s passed of %s before it\n' "$1" "$(mtp_exit_meaning "$1")" "$n_pass" "$n_total"
    [ -z "$other_exits" ] || printf '      assembly exit codes: %s\n' "$other_exits"
    [ -z "$why" ] || printf '      %s\n' "$why"
    log_line
    return 1
}

# ---- dispatch --------------------------------------------------------------------------------------

# A path the caller gave, made absolute against the caller's directory: the commands run from the root.
absolute() { if [ -e "$1" ]; then (cd "$(dirname "$1")" && printf '%s/%s' "$(pwd)" "$(basename "$1")"); else printf '%s' "$1"; fi; }

mode=${1:-}; [ $# -eq 0 ] || shift
case $mode in
    build)
        target=""; extra=()
        while [ $# -gt 0 ]; do
            case $1 in
                --) shift; extra=("$@"); break ;;
                -*) usage ;;
                *)  [ -z "$target" ] || usage; target=$(absolute "$1") ;;
            esac
            shift
        done
        resolve_sdk
        run build "${target:-$solution}" ${extra[@]+"${extra[@]}"}
        report_build $? ;;

    test)
        project=""; filters=(); extra=()
        while [ $# -gt 0 ]; do
            case $1 in
                --project)  [ $# -ge 2 ] || usage; project=$(absolute "$2"); shift ;;
                --filter*)  [ $# -ge 2 ] || usage; filters+=("$1" "$2"); shift ;;
                --)         shift; extra=("$@"); break ;;
                *)          usage ;;
            esac
            shift
        done
        if [ -n "$project" ]; then scope=$(basename "${project%.csproj}"); else scope=solution; fi
        [ ${#filters[@]} -eq 0 ] || scope="$scope, ${filters[*]}"
        if [ -n "$project" ]; then selector=(--project "$project"); else selector=(--solution "$solution"); fi

        resolve_sdk
        # Parse-only mode carries one fixture and one exit code, and they belong to the test step.
        if [ -z "$parse_only" ]; then
            run build "${project:-$solution}"
            code=$?
            if [ "$code" -ne 0 ] || ! build_section | grep -q '^Build succeeded'; then report_build "$code"; exit 1; fi
        fi
        set -- test --no-build "${selector[@]}" ${filters[@]+"${filters[@]}"} ${extra[@]+"${extra[@]}"}
        append=yes run "$@"
        report_test $? "$scope" "dotnet $(rel_args "$@")" ;;

    *) usage ;;
esac
