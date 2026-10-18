// PreToolUse hook for Bash: refuses the raw dotnet toolchain, which tools/BuildVerdict owns.
//
// An agent that runs `dotnet build` or `dotnet test` itself reads the whole transcript, in German on
// this machine, and walks into the traps the tool names: an MSBuild switch forwarded to the test
// hosts (exit 5), a filter that matched nothing (exit 8), a second weave because the test step built
// again. Measured on 2026-10-15: a passing solution build is 4,483 bytes raw against a 35-byte
// verdict, and a failing solution test run 8,404 bytes against 609.
//
// Refused, as a command anywhere in the line:
//   - `dotnet build`, `dotnet test`, `dotnet msbuild`, by any path to dotnet, except with --help,
//     except `dotnet build` of a project under tools/: a tool is built on its own, never through the
//     verdict tool, which would overwrite itself while it runs, and except `dotnet msbuild` that only
//     reads a project: -getProperty or -getItem with no target, -getTargetResult or restore. MSBuild then
//     stops after evaluation and writes nothing (measured 2026-10-17), and the verdict has no such form;
//   - the verdict tool piped into head, tail, less or more: the verdict is already the short form, and
//     the pipe hides the tool's exit code.
//
// The command is split the way the shell would: on ; && || | & and newlines, into $( ), backticks and
// the string of `bash -c`, past leading assignments, `env`, `time`, `command`, `exec`, `nice` and the
// words that open a compound (`(`, `{`, if, then, do, ...). Quoted text, comments and heredoc bodies are
// data, so `git commit -m "dotnet build is refused"` passes.
//
// Reads the hook payload on stdin. Exit 0 allows the call; exit 2 refuses it, and Claude Code hands
// stderr to the agent. A payload it cannot read is allowed: this is a guide rail, not a sandbox, and a
// person in a terminal is never affected. .claude/settings.json builds it at session start and runs it
// as its built dll. A guard built from other sources than the checkout holds refuses every command
// but the build of a tool, and names the command that rebuilds it: a stale guard is never silent.
using System.Text;
using System.Text.Json;

Console.Error.NewLine = "\n";
string command;
try
{
    using var payload = JsonDocument.Parse(Console.OpenStandardInput());
    if (payload.RootElement.ValueKind != JsonValueKind.Object) return 0;
    if (!payload.RootElement.TryGetProperty("tool_input", out var input)) return 0;
    if (input.ValueKind != JsonValueKind.Object) return 0;
    if (!input.TryGetProperty("command", out var c) || c.ValueKind != JsonValueKind.String) return 0;
    command = c.GetString() ?? "";
}
catch (JsonException)
{
    return 0;
}
var stale = SourceFingerprint.Difference(SourceFingerprint.FindRoot());
if (stale is not null && !Guard.BuildsATool(command))
{
    Console.Error.WriteLine(
        $"Refused: the Bash guard ({SourceFingerprint.Project}) was built from other sources than the checkout holds ({stale}). " +
        $"Rebuild it first: {SourceFingerprint.RebuildCommand}");
    return 2;
}
foreach (var (rule, detail) in Guard.Findings(command))
{
    Console.Error.WriteLine(Guard.Message(rule, detail));
    return 2;
}
return 0;

// Splits one command string into segments: lists of words, each with the operator after it.
sealed class Scanner(string text)
{
    int i;
    public readonly List<(List<string> Words, string Op)> Segments = [];
    public readonly List<string> Nested = [];               // the text of each $( ) and backtick, scanned on its own
    readonly List<(string Delim, bool Strip)> heredocs = []; // waiting for the next newline
    List<string> words = [];
    StringBuilder? word;                                    // the word being built, null between words

    void EndWord()
    {
        if (word is null) return;
        words.Add(word.ToString());
        word = null;
    }

    void EndSegment(string op)
    {
        EndWord();
        Segments.Add((words, op));
        words = [];
    }

    void Add(string s) => (word ??= new StringBuilder()).Append(s);

    // The character k past the current one, or "" past the end.
    string Peek(int k = 0) => i + k < text.Length ? text[i + k].ToString() : "";

    // Whether that character is one of chars; past the end it counts as one, as in the scanner this was
    // ported from, except where that would loop: see SkipBlanks.
    bool PeekIn(int k, string chars) => i + k >= text.Length || chars.Contains(text[i + k]);

    // Past the end of the text there is nothing to skip. (The Python original looped there forever on a
    // command that ends in a redirection, `ls 2>` or `cat <<`, until the hook's timeout.)
    void SkipBlanks()
    {
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
    }

    static string Slice(string s, int from, int to)
    {
        from = Math.Clamp(from, 0, s.Length);
        to = Math.Clamp(to, from, s.Length);
        return s[from..to];
    }

    public Scanner Scan()
    {
        var t = text;
        while (i < t.Length)
        {
            var c = t[i];
            if (c == '\\') { Add(Peek(1)); i += 2; continue; }
            if (c == '\'')
            {
                var end = t.IndexOf('\'', i + 1);
                if (end < 0) end = t.Length;
                Add(Slice(t, i + 1, end));
                i = end + 1;
                continue;
            }
            if (c == '"') { DoubleQuoted(); continue; }
            if (c == '$' && Peek(1) == "(") { Add(Substitution()); continue; }
            if (c == '`') { Add(Backtick()); continue; }
            if (c == '#' && word is null)
            {
                while (i < t.Length && t[i] != '\n') i++;
                continue;
            }
            if (c == '\n') { EndSegment("\n"); i++; SkipHeredocs(); continue; }
            if (c is ' ' or '\t') { EndWord(); i++; continue; }
            if (c is '<' or '>') { Redirection(); continue; }
            var two = Slice(t, i, i + 2);
            if (two is "&&" or "||" or "|&") { EndSegment(two); i += 2; continue; }
            if (";|&()".Contains(c))
            {
                if (c == '&' && Peek(1) == ">") { Redirection(); continue; }
                EndSegment(c.ToString());
                i++;
                continue;
            }
            Add(c.ToString());
            i++;
        }
        EndSegment("");
        return this;
    }

    void DoubleQuoted()
    {
        var t = text;
        i++;
        while (i < t.Length && t[i] != '"')
        {
            var c = t[i];
            if (c == '\\' && PeekIn(1, "\"\\$`")) { Add(Peek(1)); i += 2; }
            else if (c == '$' && Peek(1) == "(") Add(Substitution());
            else if (c == '`') Add(Backtick());
            else { Add(c.ToString()); i++; }
        }
        Add("");
        i++;
    }

    // Consumes $( ... ) with nesting and quotes; keeps its text for its own scan.
    string Substitution()
    {
        var t = text;
        var start = i + 2;
        int depth = 1, j = start;
        while (j < t.Length && depth > 0)
        {
            var c = t[j];
            if (c == '\\') { j += 2; continue; }
            if (c == '\'')
            {
                var k = t.IndexOf('\'', j + 1);
                j = k < 0 ? t.Length : k + 1;
                continue;
            }
            if (c == '"')
            {
                j++;
                while (j < t.Length && t[j] != '"') j += t[j] == '\\' ? 2 : 1;
                j++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            j++;
        }
        Nested.Add(Slice(t, start, depth == 0 ? j - 1 : j));
        i = j;
        return "$(...)";
    }

    string Backtick()
    {
        var t = text;
        var end = t.IndexOf('`', i + 1);
        if (end < 0) end = t.Length;
        Nested.Add(Slice(t, i + 1, end));
        i = end + 1;
        return "`...`";
    }

    // A redirection and its target are not command words; a heredoc's body is not a command.
    void Redirection()
    {
        var t = text;
        if (word is not null && word.Length > 0 && word.ToString().All(char.IsDigit)) word = null;
        else EndWord();
        if (string.CompareOrdinal(t, i, "<<<", 0, 3) == 0) { i += 3; return; }
        if (string.CompareOrdinal(t, i, "<<", 0, 2) == 0)
        {
            i += 2;
            var strip = Peek() == "-";
            if (strip) i++;
            SkipBlanks();
            var delim = new StringBuilder();
            while (i < t.Length && !" \t\n;&|<>()".Contains(t[i]))
            {
                if (!"'\"\\".Contains(t[i])) delim.Append(t[i]);
                i++;
            }
            heredocs.Add((delim.ToString(), strip));
            return;
        }
        while (i < t.Length && "<>&|".Contains(t[i])) i++;
        SkipBlanks();
        // the target word: consumed whole, quotes included
        while (i < t.Length && !" \t\n;&|<>()".Contains(t[i]))
        {
            var c = t[i];
            if (c is '\'' or '"')
            {
                var end = t.IndexOf(c, i + 1);
                i = end < 0 ? t.Length : end + 1;
            }
            else
            {
                i++;
            }
        }
    }

    void SkipHeredocs()
    {
        var t = text;
        foreach (var (delim, strip) in heredocs)
        {
            while (i < t.Length)
            {
                var end = t.IndexOf('\n', i);
                if (end < 0) end = t.Length;
                var line = t[i..end];
                i = end + 1;
                if ((strip ? line.TrimStart('\t') : line) == delim) break;
            }
        }
        heredocs.Clear();
    }
}

static class Guard
{
    static readonly HashSet<string> PrefixWords =
        ["time", "command", "exec", "nohup", "builtin", "!", "{", "}", "if", "then", "elif", "else", "do", "while", "until", "fi", "done"];
    static readonly HashSet<string> Shells = ["bash", "sh", "zsh", "dash"];
    static readonly HashSet<string> RefusedVerbs = ["build", "test", "msbuild"];
    static readonly HashSet<string> Pagers = ["head", "tail", "less", "more"];
    const string VerdictTool = "tools/BuildVerdict";
    const string Verdict = "dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll";

    // The program a word names: its last path segment, without the .exe Windows adds.
    static string Program(string word)
    {
        var name = word[(word.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    static bool IsAssignment(string w)
    {
        var eq = w.IndexOf('=');
        if (eq <= 0) return false;
        return w[..eq].All(ch => ch == '_' || char.IsLetterOrDigit(ch));
    }

    // The words of a simple command past its prefix: assignments, env, time and the like.
    static List<string> CommandWords(List<string> words)
    {
        var k = 0;
        while (k < words.Count)
        {
            var w = words[k];
            if (PrefixWords.Contains(w))
            {
                k++;
            }
            else if (IsAssignment(w))
            {
                k++;
            }
            else if (w == "env")
            {
                k++;
                while (k < words.Count && (words[k].StartsWith('-') || words[k].Contains('='))) k++;
            }
            else if (w == "nice")
            {
                k++;
                if (k < words.Count && words[k] == "-n") k += 2;
                else if (k < words.Count && words[k].StartsWith('-')) k++;
            }
            else if (w == "timeout")
            {
                k++;
                while (k < words.Count && words[k].StartsWith('-'))
                    k += words[k] is "-s" or "-k" or "--signal" or "--kill-after" ? 2 : 1;
                k++; // the duration
            }
            else
            {
                break;
            }
        }
        return k < words.Count ? words[k..] : [];
    }

    // dotnet .../build-verdict.dll, by any path.
    static bool IsVerdict(List<string> words) =>
        words.Count > 1 && Program(words[0]) == "dotnet" && Program(words[1]) == "build-verdict.dll";

    // A path under tools/, the repository's tool projects: relative, or absolute into some checkout.
    static bool IsToolPath(string path)
    {
        var p = path.Replace('\\', '/');
        return p.StartsWith("tools/", StringComparison.Ordinal) || p.StartsWith("./tools/", StringComparison.Ordinal) ||
               p.Contains("/tools/", StringComparison.Ordinal);
    }

    // `dotnet build` whose target, its first argument that is no option, is a project under tools/.
    static bool IsToolBuild(List<string> words) =>
        words.Count > 2 && Program(words[0]) == "dotnet" && words[1] == "build" &&
        words.Skip(2).FirstOrDefault(w => !w.StartsWith('-')) is { } target && IsToolPath(target);

    // An MSBuild switch's name: -name, --name or /name, up to its : or =, lower-cased; null for a word that is none.
    static string? Switch(string word)
    {
        var body = word.StartsWith("--", StringComparison.Ordinal) ? word[2..] : word.StartsWith('-') || word.StartsWith('/') ? word[1..] : null;
        if (string.IsNullOrEmpty(body)) return null;
        var end = body.IndexOfAny([':', '=']);
        return (end < 0 ? body : body[..end]).ToLowerInvariant();
    }

    static readonly HashSet<string> Queries = ["getproperty", "getitem"];
    static readonly HashSet<string> Runs = ["t", "target", "gettargetresult", "r", "restore"];

    // `dotnet msbuild` that reads a project without building it: a property or item query and no target.
    static bool IsQuery(List<string> words) =>
        words.Count > 2 && words[1] == "msbuild" &&
        words.Skip(2).Select(Switch).Any(s => s is not null && Queries.Contains(s)) &&
        !words.Skip(2).Select(Switch).Any(s => s is not null && Runs.Contains(s));

    // Whether any command in the text builds a tool project: what a stale guard still lets through.
    public static bool BuildsATool(string text) =>
        new Scanner(text).Scan().Segments.Any(s => IsToolBuild(CommandWords(s.Words)));

    // The tool's form of a refused command: its target and filters kept, other flags after --.
    // An MSBuild switch (-p:X, -nodeReuse:false) is dropped from a test step, where it exits 5.
    static (string Run, List<string> Dropped) Suggestion(string verb, List<string> args)
    {
        List<string> kept = [], rest = [], dropped = [];
        var k = 0;
        while (k < args.Count)
        {
            var a = args[k];
            var takesValue = a is "--project" or "--solution" || (a.StartsWith("--filter", StringComparison.Ordinal) && !a.Contains('='));
            var value = takesValue && k + 1 < args.Count ? args[k + 1] : null;
            if (a == "--")
            {
            }
            else if (verb == "test" && a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.Contains(':'))
            {
                dropped.Add(a);
            }
            else if (verb == "test" && (a == "--project" || a.StartsWith("--filter", StringComparison.Ordinal)))
            {
                kept.Add(a);
                if (value is not null) kept.Add(value);
            }
            else if (verb == "test" && a is "--solution" or "--no-build")
            {
            }
            else if (verb != "test" && kept.Count == 0 && !a.StartsWith('-'))
            {
                kept = [a];
                k++;
                continue;
            }
            else
            {
                rest.Add(a);
                if (value is not null) rest.Add(value);
            }
            k += value is not null ? 2 : 1;
        }
        if (verb == "msbuild") verb = "build";
        List<string> parts = [Verdict, verb, .. kept];
        if (rest.Count > 0) parts = [.. parts, "--", .. rest];
        return (string.Join(' ', parts), dropped);
    }

    // Every refused command in the text, as (rule, detail).
    public static IEnumerable<(string Rule, object Detail)> Findings(string text, int depth = 0)
    {
        if (depth > 8) yield break;
        var scanner = new Scanner(text).Scan();
        foreach (var inner in scanner.Nested)
            foreach (var f in Findings(inner, depth + 1))
                yield return f;
        var segments = scanner.Segments.Select(s => (Words: CommandWords(s.Words), s.Op)).ToList();
        for (var n = 0; n < segments.Count; n++)
        {
            var (words, op) = segments[n];
            if (words.Count == 0) continue;
            var program = Program(words[0]);
            var flag = words.Count > 1 ? words[1] : "";
            if (Shells.Contains(program) && words.Count > 2 && flag.StartsWith('-') && !flag.StartsWith("--", StringComparison.Ordinal) && flag.Contains('c'))
            {
                foreach (var f in Findings(words[2], depth + 1)) yield return f;
                continue;
            }
            if (program == "dotnet" && words.Count > 1 && RefusedVerbs.Contains(words[1]) && !IsToolBuild(words) && !IsQuery(words))
            {
                if (!words.Skip(2).Any(a => a is "--help" or "-h" or "-?" or "/?"))
                    yield return ("toolchain", (words[1], words.Skip(2).ToList()));
            }
            if (IsVerdict(words) && op is ("|" or "|&") && n + 1 < segments.Count)
            {
                var next = segments[n + 1].Words;
                if (next.Count > 0 && Pagers.Contains(Program(next[0])))
                    yield return ("pipe", Program(next[0]));
            }
        }
    }

    public static string Message(string rule, object detail)
    {
        if (rule == "toolchain")
        {
            var (verb, args) = ((string, List<string>))detail;
            var (run, dropped) = Suggestion(verb, args);
            var note = dropped.Count > 0
                ? $"  Dropped from the test step: {string.Join(' ', dropped)} -- an MSBuild switch there exits 5; " +
                  $"build first with `{Verdict} build -- {string.Join(' ', dropped)}` if it matters.\n"
                : "";
            return
                $"Refused: raw `dotnet {verb}`. In this repository an agent builds and tests through " +
                $"{VerdictTool}, run as its built dll, which pins English output, builds once and runs `dotnet test " +
                $"--no-build`, names exit 5 (an option the test hosts refuse) and exit 8 (a filter that " +
                $"matched nothing), and prints a verdict instead of the transcript, the full log in " +
                $"$GIT_DIR/build-verdict-last.log.\n" +
                $"  Run: {run}\n{note}" +
                $"  Usage: {Verdict} build [<project|slnx>] [-- <build flags>] | test [--project P] " +
                $"[--filter-class C] [--filter-method M] [--filter-* V] [-- <test flags>]\n" +
                $"An MSBuild switch belongs on `build -- ...`, never on `test -- ...`. A person in a " +
                $"terminal is not affected by this hook.";
        }
        return
            $"Refused: the verdict tool ({VerdictTool}) piped into {detail}. The verdict is already the short " +
            $"form, and the pipe replaces the tool's exit code with {detail}'s. Run it bare; for " +
            $"more than the verdict, read the lines you need from $GIT_DIR/build-verdict-last.log.";
    }
}
