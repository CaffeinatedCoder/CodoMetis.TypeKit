#!/usr/bin/env python3
"""PreToolUse hook for Bash: refuses the raw dotnet toolchain, which tools/build-verdict.sh owns.

An agent that runs `dotnet build` or `dotnet test` itself reads the whole transcript, in German on
this machine, and walks into the traps the script names: an MSBuild switch forwarded to the test
hosts (exit 5), a filter that matched nothing (exit 8), a second weave because the test step built
again. Measured on 2026-10-15: a passing solution build is 4,483 bytes raw against a 35-byte
verdict, and a failing solution test run 8,404 bytes against 609.

Refused, as a command anywhere in the line:
  - `dotnet build`, `dotnet test`, `dotnet msbuild`, by any path to dotnet, except with --help;
  - tools/build-verdict.sh piped into head, tail, less or more: the verdict is already the short form,
    and the pipe hides the script's exit code.

The command is split the way the shell would: on ; && || | & and newlines, into $( ), backticks and
the string of `bash -c`, past leading assignments, `env`, `time`, `command`, `exec`, `nice` and the
words that open a compound (`(`, `{`, if, then, do, ...). Quoted text, comments and heredoc bodies are
data, so `git commit -m "dotnet build is refused"` passes.

Reads the hook payload on stdin. Exit 0 allows the call; exit 2 refuses it, and Claude Code hands
stderr to the agent. A payload it cannot read is allowed: this is a guide rail, not a sandbox, and a
person in a terminal is never affected.
"""

import json
import os
import sys

CONTROL = {";", "&&", "||", "|", "|&", "&", "\n", "(", ")"}
PREFIX_WORDS = {"time", "command", "exec", "nohup", "builtin", "!", "{", "}",
                "if", "then", "elif", "else", "do", "while", "until", "fi", "done"}
SHELLS = {"bash", "sh", "zsh", "dash"}
REFUSED_VERBS = {"build", "test", "msbuild"}
PAGERS = {"head", "tail", "less", "more"}
VERDICT = "tools/build-verdict.sh"


class Scanner:
    """Splits one command string into segments: lists of words, each with the operator after it."""

    def __init__(self, text):
        self.text = text
        self.i = 0
        self.segments = []      # (words, operator that ends the segment)
        self.nested = []        # the text of each $( ) and backtick, scanned on its own
        self.heredocs = []      # (delimiter, strip tabs) waiting for the next newline
        self.words = []
        self.word = None        # the word being built, None between words

    def end_word(self):
        if self.word is not None:
            self.words.append(self.word)
            self.word = None

    def end_segment(self, op):
        self.end_word()
        self.segments.append((self.words, op))
        self.words = []

    def add(self, s):
        self.word = (self.word or "") + s

    def peek(self, k=0):
        j = self.i + k
        return self.text[j] if j < len(self.text) else ""

    def scan(self):
        t = self.text
        while self.i < len(t):
            c = t[self.i]
            if c == "\\":
                self.add(self.peek(1)); self.i += 2; continue
            if c == "'":
                end = t.find("'", self.i + 1)
                end = len(t) if end < 0 else end
                self.add(t[self.i + 1:end]); self.i = end + 1; continue
            if c == '"':
                self.double_quoted(); continue
            if c == "$" and self.peek(1) == "(":
                self.add(self.substitution()); continue
            if c == "`":
                self.add(self.backtick()); continue
            if c == "#" and self.word is None:
                while self.i < len(t) and t[self.i] != "\n":
                    self.i += 1
                continue
            if c == "\n":
                self.end_segment("\n"); self.i += 1; self.skip_heredocs(); continue
            if c in " \t":
                self.end_word(); self.i += 1; continue
            if c in "<>":
                self.redirection(); continue
            two = t[self.i:self.i + 2]
            if two in ("&&", "||", "|&"):
                self.end_segment(two); self.i += 2; continue
            if c in ";|&()":
                if c == "&" and self.peek(1) == ">":
                    self.redirection(); continue
                self.end_segment(c); self.i += 1; continue
            self.add(c); self.i += 1
        self.end_segment("")
        return self

    def double_quoted(self):
        t = self.text
        self.i += 1
        while self.i < len(t) and t[self.i] != '"':
            c = t[self.i]
            if c == "\\" and self.peek(1) in '"\\$`':
                self.add(self.peek(1)); self.i += 2
            elif c == "$" and self.peek(1) == "(":
                self.add(self.substitution())
            elif c == "`":
                self.add(self.backtick())
            else:
                self.add(c); self.i += 1
        self.add(""); self.i += 1

    def substitution(self):
        """Consumes $( ... ) with nesting and quotes; keeps its text for its own scan."""
        t = self.text
        start = self.i + 2
        depth, j = 1, start
        while j < len(t) and depth:
            c = t[j]
            if c == "\\":
                j += 2; continue
            if c == "'":
                k = t.find("'", j + 1); j = len(t) if k < 0 else k + 1; continue
            if c == '"':
                j += 1
                while j < len(t) and t[j] != '"':
                    j += 2 if t[j] == "\\" else 1
                j += 1; continue
            if c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
            j += 1
        self.nested.append(t[start:j - 1 if depth == 0 else j])
        self.i = j
        return "$(...)"

    def backtick(self):
        t = self.text
        end = t.find("`", self.i + 1)
        end = len(t) if end < 0 else end
        self.nested.append(t[self.i + 1:end])
        self.i = end + 1
        return "`...`"

    def redirection(self):
        """A redirection and its target are not command words; a heredoc's body is not a command."""
        t = self.text
        if self.word is not None and self.word.isdigit():
            self.word = None
        else:
            self.end_word()
        if t.startswith("<<<", self.i):
            self.i += 3; return
        if t.startswith("<<", self.i):
            self.i += 2
            strip = self.peek() == "-"
            if strip:
                self.i += 1
            while self.peek() in " \t":
                self.i += 1
            delim = []
            while self.i < len(t) and t[self.i] not in " \t\n;&|<>()":
                if t[self.i] not in "'\"\\":
                    delim.append(t[self.i])
                self.i += 1
            self.heredocs.append(("".join(delim), strip))
            return
        while self.i < len(t) and t[self.i] in "<>&|":
            self.i += 1
        while self.peek() in " \t":
            self.i += 1
        # the target word: consumed whole, quotes included
        target = Scanner(t[self.i:])
        while target.i < len(target.text) and target.text[target.i] not in " \t\n;&|<>()":
            c = target.text[target.i]
            if c in "'\"":
                end = target.text.find(c, target.i + 1)
                target.i = len(target.text) if end < 0 else end + 1
            else:
                target.i += 1
        self.i += target.i

    def skip_heredocs(self):
        t = self.text
        for delim, strip in self.heredocs:
            while self.i < len(t):
                end = t.find("\n", self.i)
                end = len(t) if end < 0 else end
                line = t[self.i:end]
                self.i = end + 1
                if (line.lstrip("\t") if strip else line) == delim:
                    break
        self.heredocs = []


def command_words(words):
    """The words of a simple command past its prefix: assignments, env, time and the like."""
    k = 0
    while k < len(words):
        w = words[k]
        if w in PREFIX_WORDS:
            k += 1
        elif "=" in w and not w.startswith("=") and w.split("=", 1)[0].replace("_", "a").isalnum():
            k += 1
        elif w == "env":
            k += 1
            while k < len(words) and (words[k].startswith("-") or "=" in words[k]):
                k += 1
        elif w == "nice":
            k += 1
            if k < len(words) and words[k] == "-n":
                k += 2
            elif k < len(words) and words[k].startswith("-"):
                k += 1
        elif w == "timeout":
            k += 1
            while k < len(words) and words[k].startswith("-"):
                k += 2 if words[k] in ("-s", "-k", "--signal", "--kill-after") else 1
            k += 1  # the duration
        else:
            break
    return words[k:]


def is_verdict(words):
    if not words:
        return False
    if os.path.basename(words[0]) == "build-verdict.sh":
        return True
    return os.path.basename(words[0]) in SHELLS and len(words) > 1 and words[1].endswith("build-verdict.sh")


def suggestion(verb, args):
    """The script's form of a refused command: its target and filters kept, other flags after --.
    An MSBuild switch (-p:X, -nodeReuse:false) is dropped from a test step, where it exits 5."""
    kept, rest, dropped, k = [], [], [], 0
    while k < len(args):
        a = args[k]
        takes_value = a in ("--project", "--solution") or (a.startswith("--filter") and "=" not in a)
        value = args[k + 1] if takes_value and k + 1 < len(args) else None
        if a == "--":
            pass
        elif verb == "test" and a.startswith("-") and not a.startswith("--") and ":" in a:
            dropped.append(a)
        elif verb == "test" and (a == "--project" or a.startswith("--filter")):
            kept += [a] + ([value] if value is not None else [])
        elif verb == "test" and a in ("--solution", "--no-build"):
            pass
        elif verb != "test" and not kept and not a.startswith("-"):
            kept = [a]
            k += 1; continue
        else:
            rest += [a] + ([value] if value is not None else [])
        k += 2 if value is not None else 1
    if verb == "msbuild":
        verb = "build"
    return " ".join([VERDICT, verb] + kept + (["--"] + rest if rest else [])), dropped


def findings(text, depth=0):
    """Yields (rule, detail) for every refused command in the text."""
    if depth > 8:
        return
    scanner = Scanner(text).scan()
    for inner in scanner.nested:
        yield from findings(inner, depth + 1)
    segments = [(command_words(words), op) for words, op in scanner.segments]
    for n, (words, op) in enumerate(segments):
        if not words:
            continue
        program = os.path.basename(words[0])
        flag = words[1] if len(words) > 1 else ""
        if program in SHELLS and len(words) > 2 and flag.startswith("-") and not flag.startswith("--") and "c" in flag:
            yield from findings(words[2], depth + 1)
            continue
        if program == "dotnet" and len(words) > 1 and words[1] in REFUSED_VERBS:
            if not any(a in ("--help", "-h", "-?", "/?") for a in words[2:]):
                yield ("toolchain", (words[1], words[2:]))
        if is_verdict(words) and op in ("|", "|&") and n + 1 < len(segments):
            nxt = segments[n + 1][0]
            if nxt and os.path.basename(nxt[0]) in PAGERS:
                yield ("pipe", os.path.basename(nxt[0]))


def message(rule, detail):
    if rule == "toolchain":
        verb, args = detail
        run, dropped = suggestion(verb, args)
        note = (f"  Dropped from the test step: {' '.join(dropped)} -- an MSBuild switch there exits 5; "
                f"build first with `{VERDICT} build -- {' '.join(dropped)}` if it matters.\n") if dropped else ""
        return (
            f"Refused: raw `dotnet {verb}`. In this repository an agent builds and tests through "
            f"tools/build-verdict.sh, which pins English output, builds once and runs `dotnet test "
            f"--no-build`, names exit 5 (an option the test hosts refuse) and exit 8 (a filter that "
            f"matched nothing), and prints a verdict instead of the transcript, the full log in "
            f"$GIT_DIR/build-verdict-last.log.\n"
            f"  Run: {run}\n{note}"
            f"  Usage: build [<project|slnx>] [-- <build flags>] | test [--project P] "
            f"[--filter-class C] [--filter-method M] [--filter-* V] [-- <test flags>]\n"
            f"An MSBuild switch belongs on `build -- ...`, never on `test -- ...`. A person in a "
            f"terminal is not affected by this hook.")
    return (
        f"Refused: tools/build-verdict.sh piped into {detail}. The verdict is already the short "
        f"form, and the pipe replaces the script's exit code with {detail}'s. Run it bare; for "
        f"more than the verdict, read the lines you need from $GIT_DIR/build-verdict-last.log.")


def main():
    try:
        payload = json.load(sys.stdin)
        command = payload.get("tool_input", {}).get("command") or ""
    except (ValueError, AttributeError):
        return 0
    for rule, detail in findings(command):
        print(message(rule, detail), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
