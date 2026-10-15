// Runs the dotnet toolchain and prints a verdict instead of the transcript: the exit code read and
// named, the diagnostic codes deduplicated and each paired with its remedy from
// build-verdict-remedies.tsv, the failing tests by name with their assertion. The full log stays at
// $GIT_DIR/build-verdict-last.log, for the rare question the verdict does not answer.
//
// Usage:
//   dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll build [<project-dir|csproj|slnx>] [-- <dotnet build flags>]
//   dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test [--project <dir|csproj>] [--filter-class <C>] [--filter-method <M>] [-- <dotnet test flags>]
//
// Every --filter-* option of the test hosts is passed on with its value, and may repeat (the filters
// of one kind OR together). Without --project the whole solution builds and runs.
//
// The verdict's first line is one of BUILD OK, BUILD FAILED, BUILD DID NOT REPORT, TESTS OK, TESTS
// FAILED, TESTS REJECTED, TESTS NONE, TESTS ABORTED, TESTS DID NOT REPORT, or TOOL STALE. Exit 0 is a
// pass, 1 a failed verdict, 2 a usage error, 3 a tool built from other sources than the checkout
// holds, which names the command that rebuilds it (`dotnet build tools/BuildVerdict -c Release`).
//
// Details each of which was measured on this repository:
//   - DOTNET_CLI_UI_LANGUAGE=en pins the lines the parsers key on. Without it this machine prints
//     "0 Fehler", "fehlerhaft <test>" and "fehlgeschlagen: 2", and an English-only parser finds
//     nothing on a failing run exactly as on a passing one.
//   - A test run is `dotnet build`, then `dotnet test --no-build`: a compile error reports as a build
//     verdict, and Metalama weaves the tree once.
//   - `dotnet test` runs on Microsoft Testing Platform (global.json), which forwards an option it does
//     not know to every test host: an MSBuild switch there exits 5 having run nothing. A filter that
//     matches nothing exits 8. Both are named verdicts, never a pass and never a bare failure.
//   - A filter on the whole solution exits 8 even when it matched tests and they passed, because
//     each assembly it matched nothing in reports 8. With a total above zero and no failure that is a
//     pass, said with the count of such assemblies; with a failure among them the run exits 1.
//   - Under GitHub Actions the test platform colours its output although it is redirected, and an
//     escape sequence opens the summary's lines ("ESC[m  total: 177"), so a passing run read as DID
//     NOT REPORT (2026-10-15, the first CI replay of the probes). The log keeps the colours; the
//     parsers read the lines without them.
//   - An empty parse is never a pass: an exit 0 without the summary block the parsers read is a
//     DID NOT REPORT verdict, exit 1.
//   - MSBuild prints a diagnostic once inline and once in its summary, and a project's errors again
//     for each project that references it; the trailing [X.csproj] comes off before the dedup.
//   - An agent's shell may find a dotnet first on PATH that cannot satisfy global.json (exit 155).
//     Then the one the SDK installer puts in /usr/local/share/dotnet or %ProgramFiles%\dotnet is
//     used, if it can, and the verdict says so.
//   - The log is written beside and renamed into place, so a reader never sees half of it.
//
// Parse-only mode, for BuildVerdict.Tests: BUILD_VERDICT_LOG=<file> BUILD_VERDICT_EXIT=<n> reads
// that fixture as the output of the one dotnet command the mode would run (for `test`, the test
// step), and runs nothing. BUILD_VERDICT_DOTNET=<path> runs that executable in place of dotnet.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

Console.Out.NewLine = "\n";
Console.Error.NewLine = "\n";
return Verdict.Start(args);

static class Verdict
{
    const string Usage = """
        Usage:
          dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll build [<project-dir|csproj|slnx>] [-- <dotnet build flags>]
          dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test [--project <dir|csproj>] [--filter-class <C>] [--filter-method <M>] [-- <dotnet test flags>]
        """;
    const string TestMarker = "==== build-verdict: dotnet test";

    static string root = "";
    static string solution = "";
    static string remedies = "";
    static string log = "";
    static bool parseOnly;
    static string dotnet = "dotnet";
    static string note = "";

    sealed class UsageException : Exception;

    public static int Start(string[] args)
    {
        var found = SourceFingerprint.FindRoot();
        var stale = SourceFingerprint.Difference(found);
        if (stale is not null)
        {
            Console.WriteLine($"TOOL STALE: {SourceFingerprint.Project} was built from other sources than the checkout holds ({stale})");
            Console.WriteLine($"      rebuild it: {SourceFingerprint.RebuildCommand}");
            return SourceFingerprint.StaleExitCode;
        }
        root = found!;
        solution = Path.Combine(root, "CodoMetis.TypeKit.slnx");
        remedies = Path.Combine(root, SourceFingerprint.Project, "build-verdict-remedies.tsv");

        var fixture = Environment.GetEnvironmentVariable("BUILD_VERDICT_LOG");
        parseOnly = !string.IsNullOrEmpty(fixture);
        if (parseOnly)
        {
            log = fixture!;
        }
        else
        {
            var (code, output) = Capture("git", ["-C", root, "rev-parse", "--absolute-git-dir"]);
            var gitDir = code == 0 ? output.Trim() : Path.GetTempPath();
            log = Path.Combine(gitDir, "build-verdict-last.log");
        }
        var overrideDotnet = Environment.GetEnvironmentVariable("BUILD_VERDICT_DOTNET");
        if (!string.IsNullOrEmpty(overrideDotnet)) dotnet = overrideDotnet;

        try
        {
            return Dispatch(args);
        }
        catch (UsageException)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    static int Dispatch(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToList();
        switch (mode)
        {
            case "build":
            {
                string? target = null;
                var extra = new List<string>();
                for (var i = 0; i < rest.Count; i++)
                {
                    var a = rest[i];
                    if (a == "--") { extra = rest.Skip(i + 1).ToList(); break; }
                    if (a.StartsWith('-')) throw new UsageException();
                    if (target is not null) throw new UsageException();
                    target = Absolute(a);
                }
                ResolveSdk();
                var code = Run([ "build", target ?? solution, .. extra ], append: false);
                return ReportBuild(code);
            }
            case "test":
            {
                string? project = null;
                var filters = new List<string>();
                var extra = new List<string>();
                for (var i = 0; i < rest.Count; i++)
                {
                    var a = rest[i];
                    if (a == "--project")
                    {
                        if (i + 1 >= rest.Count) throw new UsageException();
                        project = Absolute(rest[++i]);
                    }
                    else if (a.StartsWith("--filter", StringComparison.Ordinal))
                    {
                        if (i + 1 >= rest.Count) throw new UsageException();
                        filters.Add(a);
                        filters.Add(rest[++i]);
                    }
                    else if (a == "--")
                    {
                        extra = rest.Skip(i + 1).ToList();
                        break;
                    }
                    else
                    {
                        throw new UsageException();
                    }
                }
                var scope = project is null ? "solution" : BaseName(StripSuffix(project, ".csproj"));
                if (filters.Count > 0) scope = $"{scope}, {string.Join(' ', filters)}";
                string[] selector = project is null ? ["--solution", solution] : ["--project", project];

                ResolveSdk();
                // Parse-only mode carries one fixture and one exit code, and they belong to the test step.
                if (!parseOnly)
                {
                    var code = Run(["build", project ?? solution], append: false);
                    if (code != 0 || !BuildSection().Any(l => l.StartsWith("Build succeeded", StringComparison.Ordinal)))
                    {
                        ReportBuild(code);
                        return 1;
                    }
                }
                string[] testArgs = ["test", "--no-build", .. selector, .. filters, .. extra];
                var testCode = Run(testArgs, append: true);
                return ReportTest(testCode, scope, "dotnet " + string.Join(' ', testArgs.Select(Rel)));
            }
            default:
                throw new UsageException();
        }
    }

    // ---- paths and the log -----------------------------------------------------------------------

    static string StripRoot(string s) =>
        s.Replace(root + "/", "", StringComparison.Ordinal).Replace(root + "\\", "", StringComparison.Ordinal);

    static string Rel(string path) =>
        path.StartsWith(root + "/", StringComparison.Ordinal) || path.StartsWith(root + "\\", StringComparison.Ordinal)
            ? path[(root.Length + 1)..]
            : path;

    // A path the caller gave, made absolute against the caller's directory: the commands run from the root.
    static string Absolute(string path) =>
        File.Exists(path) || Directory.Exists(path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : path;

    static string BaseName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var i = trimmed.LastIndexOfAny(['/', '\\']);
        return i < 0 ? trimmed : trimmed[(i + 1)..];
    }

    static string StripSuffix(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;

    static List<string> LogLines()
    {
        if (!File.Exists(log)) return [];
        var text = SharedFile.ReadAllText(log);
        var lines = Ansi.Replace(text, "").Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    // A terminal control sequence (CSI): ESC [, parameters, intermediates, a final byte.
    static readonly Regex Ansi = new(@"\e\[[0-?]*[ -/]*[@-~]");

    // The test step's part of the log: after the marker Run writes, or the whole fixture.
    static List<string> TestSection()
    {
        var lines = LogLines();
        var i = lines.FindIndex(l => l.StartsWith(TestMarker, StringComparison.Ordinal));
        return i < 0 ? lines : lines.Skip(i + 1).ToList();
    }

    // The build step's part of the log: before the test marker.
    static List<string> BuildSection()
    {
        var lines = LogLines();
        var i = lines.FindIndex(l => l.StartsWith(TestMarker, StringComparison.Ordinal));
        return i < 0 ? lines : lines.Take(i).ToList();
    }

    static void LogLine() => Console.Write($"\nlog: {log}\n");

    // A tail of the log for the reader: repository paths relative, indented.
    static void Excerpt(List<string> lines, int n)
    {
        foreach (var l in lines.Skip(Math.Max(0, lines.Count - n))) Console.WriteLine("  " + StripRoot(l));
    }

    // The remedy for a code: its own row, else the longest prefix row (a code ending in *).
    static string Remedy(string code)
    {
        if (!File.Exists(remedies)) return "";
        string best = "";
        var bestLength = 0;
        foreach (var line in File.ReadLines(remedies))
        {
            if (line.StartsWith('#')) continue;
            var f = line.Split('\t');
            var third = f.Length > 2 ? f[2] : "";
            if (f[0] == code) return third;
            if (f[0].EndsWith('*'))
            {
                var p = f[0][..^1];
                if (code.StartsWith(p, StringComparison.Ordinal) && p.Length > bestLength) { bestLength = p.Length; best = third; }
            }
        }
        return best;
    }

    static void FixLine(string code)
    {
        var r = Remedy(code);
        if (r.Length > 0) Console.WriteLine($"FIX   {r}");
    }

    // ---- running ---------------------------------------------------------------------------------

    static (int Code, string Output) Capture(string file, IEnumerable<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            using var p = Process.Start(psi)!;
            var stderr = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stdout + stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (127, e.Message + "\n");
        }
    }

    // A dotnet that cannot satisfy global.json fails every command with the same message. Try the one the
    // SDK installer puts in its default location before giving up.
    static void ResolveSdk()
    {
        if (parseOnly) return;
        var (code, output) = Capture(dotnet, ["--version"]);
        WriteAtomically(log, output);
        if (code == 0) return;
        var fallbackDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
            : "/usr/local/share/dotnet";
        var fallback = Path.Combine(fallbackDir, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_VERDICT_DOTNET")) && File.Exists(fallback) &&
            Capture(fallback, ["--version"]).Code == 0)
        {
            note = $"note: {OnPath("dotnet") ?? "dotnet"} cannot satisfy global.json; used {fallback}";
            Environment.SetEnvironmentVariable("PATH", fallbackDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            Environment.SetEnvironmentVariable("DOTNET_ROOT", fallbackDir);
            dotnet = fallback;
            return;
        }
        ReportBuild(1);
        Environment.Exit(1);
    }

    static string? OnPath(string name)
    {
        var exts = OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    // Runs one dotnet command from the repository root; its output goes to the log (appended when
    // append is set). Returns the command's exit code, or the fixture's.
    static int Run(string[] args, bool append)
    {
        if (parseOnly)
            return int.TryParse(Environment.GetEnvironmentVariable("BUILD_VERDICT_EXIT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : 0;
        var temporary = TemporaryBeside(log);
        if (append && File.Exists(log)) File.Copy(log, temporary, overwrite: true);
        var code = RunInto(temporary, args, append);
        SharedFile.Replace(temporary, log);
        return code;
    }

    static string TemporaryBeside(string path) => $"{path}.{Environment.ProcessId}.tmp";

    static void WriteAtomically(string path, string text)
    {
        var temporary = TemporaryBeside(path);
        File.WriteAllText(temporary, text);
        SharedFile.Replace(temporary, path);
    }

    static int RunInto(string file, string[] args, bool append)
    {
        using var writer = new StreamWriter(file, append, new UTF8Encoding(false)) { NewLine = "\n" };
        if (append) writer.Write($"\n==== build-verdict: dotnet {string.Join(' ', args)}\n");
        var psi = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        var gate = new object();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, d) => { if (d.Data is not null) lock (gate) writer.WriteLine(d.Data); };
        p.ErrorDataReceived += (_, d) => { if (d.Data is not null) lock (gate) writer.WriteLine(d.Data); };
        try
        {
            p.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            writer.WriteLine(ex.Message);
            return 127;
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }

    // ---- build -----------------------------------------------------------------------------------

    sealed record Diag(string Sev, string Code, string File, string Line, string Msg, int NProj, string Projs);

    static readonly Regex DiagLine = new(@"(^|[ :])(error|warning) [A-Za-z]+[0-9]+: ");
    static readonly Regex ProjectSuffix = new(@" \[[^\]\[]*\]$");
    static readonly Regex ProjectFile = new(@"\.(cs|fs|vb)proj(::.*)?$|\.slnx?$");
    static readonly Regex CodeHead = new(@"^[A-Za-z]+[0-9]+: ");
    static readonly Regex CodeAndColon = new(@"^[A-Za-z]+[0-9]+: *");
    static readonly Regex HelpLink = new(@" \(https?://[^ ()]*\)$");
    static readonly Regex SeeLink = new(@" See https?://[^ ]*$");
    static readonly Regex Position = new(@"\([0-9]+(,[0-9]+)*\)$");

    // One row per distinct diagnostic: severity, code, file, line, message, the number of projects that
    // reported it, and their names. Repository paths become relative and help links come off the message.
    // CMTK first, since a refused declaration is the cause of the CS and LAMA errors on the type it left
    // ungenerated; LAMA last for the same reason.
    static List<Diag> Diagnostics()
    {
        var order = new List<(string Sev, string Code, string File, string Line, string Msg)>();
        var projects = new Dictionary<(string, string, string, string, string), List<string>>();
        foreach (var raw in BuildSection())
        {
            if (!DiagLine.IsMatch(raw)) continue;
            var line = raw.TrimEnd('\r').TrimStart(' ', '\t');
            var proj = "";
            var m = ProjectSuffix.Match(line);
            if (m.Success)
            {
                var p = m.Value[2..^1];
                if (ProjectFile.IsMatch(p))
                {
                    line = line[..m.Index];
                    var colons = p.IndexOf("::", StringComparison.Ordinal);
                    if (colons >= 0) p = p[..colons];
                    proj = BaseName(p);
                }
            }
            string sev, left, rest;
            int i;
            if ((i = line.IndexOf(": error ", StringComparison.Ordinal)) >= 0) { sev = "error"; left = line[..i]; rest = line[(i + 8)..]; }
            else if ((i = line.IndexOf(": warning ", StringComparison.Ordinal)) >= 0) { sev = "warning"; left = line[..i]; rest = line[(i + 10)..]; }
            else if (line.StartsWith("error ", StringComparison.Ordinal)) { sev = "error"; left = ""; rest = line[6..]; }
            else if (line.StartsWith("warning ", StringComparison.Ordinal)) { sev = "warning"; left = ""; rest = line[8..]; }
            else continue;
            if (!CodeHead.IsMatch(rest)) continue;
            var code = rest[..rest.IndexOf(':')];
            var msg = CodeAndColon.Replace(rest, "", 1);
            msg = HelpLink.Replace(msg, "", 1);
            msg = SeeLink.Replace(msg, "", 1);
            var file = left;
            var ln = "";
            var pos = Position.Match(file);
            if (pos.Success)
            {
                var inner = pos.Value[1..^1];
                var comma = inner.IndexOf(',');
                ln = comma < 0 ? inner : inner[..comma];
                file = file[..pos.Index];
            }
            file = file.TrimEnd(' ', '\t');
            file = Rel(file);
            var key = (sev, code, file, ln, msg);
            if (!projects.TryGetValue(key, out var list))
            {
                projects[key] = list = [];
                order.Add(key);
            }
            if (proj != "" && !list.Contains(proj)) list.Add(proj);
        }

        static int Rank(string code) => code.StartsWith("CMTK", StringComparison.Ordinal) ? 0 : code.StartsWith("LAMA", StringComparison.Ordinal) ? 2 : 1;
        static long Num(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return order
            .Select(k => new Diag(k.Sev, k.Code, k.File, k.Line, k.Msg, projects[k].Count, string.Join(", ", projects[k])))
            .OrderBy(d => Rank(d.Code))
            .ThenBy(d => d.File, StringComparer.Ordinal)
            .ThenBy(d => Num(d.Line))
            .ThenBy(d => $"{d.Sev}\u001f{d.Code}\u001f{d.File}\u001f{d.Line}\u001f{d.Msg}", StringComparer.Ordinal)
            .ToList();
    }

    static void WarnSummary(List<Diag> warnings)
    {
        if (warnings.Count == 0) return;
        var counts = warnings.GroupBy(w => w.Code).Select(g => $"{g.Key} x{g.Count()}").OrderBy(s => s, StringComparer.Ordinal);
        Console.WriteLine("warnings: " + string.Join(", ", counts));
        foreach (var c in warnings.Select(w => w.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal))
        {
            var r = Remedy(c);
            if (r.Length > 0) Console.WriteLine($"      {c}: {r}");
        }
    }

    static int ReportBuild(int exit)
    {
        var diags = Diagnostics();
        var errors = diags.Where(d => d.Sev == "error").ToList();
        var warnings = diags.Where(d => d.Sev == "warning").ToList();
        if (note.Length > 0) Console.WriteLine(note);
        var build = BuildSection();

        if (exit == 0 && errors.Count == 0)
        {
            if (!build.Any(l => l.StartsWith("Build succeeded", StringComparison.Ordinal)))
            {
                Console.Write("BUILD DID NOT REPORT: exit 0 and no \"Build succeeded.\" line, so the output format or language moved\n\n");
                Excerpt(build, 15);
                LogLine();
                return 1;
            }
            var projects = build.Count(l => l.Contains(" -> ", StringComparison.Ordinal));
            var elapsed = build.Where(l => l.StartsWith("Time Elapsed ", StringComparison.Ordinal)).Select(l => l[13..]).LastOrDefault();
            Console.WriteLine($"BUILD OK: {projects} projects, {(string.IsNullOrEmpty(elapsed) ? "?" : elapsed)}");
            WarnSummary(warnings);
            return 0;
        }

        if (errors.Count > 0)
        {
            Console.WriteLine($"BUILD FAILED: {errors.Count} unique error{(errors.Count == 1 ? "" : "s")}");
            // A remedy already printed, for this code or another that shares its row, is not printed again.
            var unmapped = new SortedSet<string>(StringComparer.Ordinal);
            var printed = new HashSet<string>();
            foreach (var e in errors)
            {
                Console.Write($"\n## {(e.File.Length > 0 ? e.File : "(no file)")}{(e.Line.Length > 0 ? ":" + e.Line : "")}\n");
                Console.WriteLine($"{e.Code}  {e.Msg}");
                var fix = Remedy(e.Code);
                if (fix.Length == 0)
                {
                    if (!(e.Code.Length > 2 && e.Code.StartsWith("CS", StringComparison.Ordinal) && char.IsAsciiDigit(e.Code[2]))) unmapped.Add(e.Code);
                }
                else if (!printed.Add(fix))
                {
                    Console.WriteLine("FIX   (as above)");
                }
                else
                {
                    Console.WriteLine($"FIX   {fix}");
                }
                if (e.NProj > 1) Console.WriteLine($"      (reported by {e.NProj} projects: {e.Projs})");
            }
            if (warnings.Count > 0) { Console.WriteLine(); WarnSummary(warnings); }
            if (unmapped.Count > 0)
                Console.Write($"\nUNMAPPED: {string.Join(' ', unmapped)} -- no remedy row; read the message, and add a row to tools/BuildVerdict/build-verdict-remedies.tsv once the fix is known\n");
            LogLine();
            return 1;
        }

        // A failing exit with no diagnostic: the failure is before MSBuild's, in the SDK or the restore.
        if (build.Any(l => l.Contains("global.json file:", StringComparison.Ordinal)))
        {
            Console.WriteLine($"BUILD FAILED: the dotnet on PATH cannot satisfy global.json's SDK pin (exit {exit})");
            foreach (var l in build.Where(l => l.StartsWith("Requested SDK version:", StringComparison.Ordinal) || l.StartsWith("global.json file:", StringComparison.Ordinal)))
                Console.WriteLine("      " + l);
            FixLine("exit:sdk");
        }
        else
        {
            Console.Write($"BUILD FAILED before MSBuild reported any diagnostic (exit {exit}); the last lines:\n\n");
            Excerpt(build, 20);
        }
        LogLine();
        return 1;
    }

    // ---- test ------------------------------------------------------------------------------------

    static void SkippedNames(List<string> section)
    {
        var names = section.Where(l => l.StartsWith("skipped ", StringComparison.Ordinal)).Select(l => l[8..]).ToList();
        if (names.Count == 0) return;
        if (names.Count <= 3) foreach (var n in names) Console.WriteLine($"skipped: {n}");
        else Console.WriteLine($"skipped: {names.Count} tests");
    }

    static readonly Regex Duration = new(@" \(([0-9]+h )?([0-9]+m )?([0-9]+s )?[0-9]+m?s\)$");
    static readonly Regex FrameFile = new(@" in ((?:/|[A-Za-z]:[\\/]).*:[0-9]+)$");
    static readonly Regex ExceptionHead = new(@"^[A-Za-z0-9_.`]+Exception ?: ");

    // Namespace.FooException : msg -> FooException: msg
    static string Bare(string s)
    {
        var m = ExceptionHead.Match(s);
        if (!m.Success) return s;
        var t = m.Value.TrimEnd(' ').TrimEnd(':').TrimEnd(' ');
        return t[(t.LastIndexOf('.') + 1)..] + ": " + s[m.Length..];
    }

    static string Short(string full)
    {
        var i = full.IndexOf('(');
        var head = i >= 0 ? full[..i] : full;
        var tail = i >= 0 ? full[i..] : "";
        var p = head.Split('.');
        return (p.Length >= 2 ? p[^2] + "." + p[^1] : head) + tail;
    }

    // One block per failed test: Class.Method (and a theory's arguments), the first frame in this tree as
    // file:line, and the assertion's lines up to the first stack frame joined into one. `from` lines,
    // frames outside the tree and captured output are dropped. Returns the count of blocks.
    static int FailureBlocks(List<string> section)
    {
        string name = "", loc = "";
        var body = new List<string>();
        bool on = false, inStack = false;
        var blocks = 0;

        void Flush()
        {
            if (name == "") return;
            var text = string.Join(' ', body.Take(8));
            text = Bare(text);
            if (text.StartsWith("XunitException: ", StringComparison.Ordinal)) text = text["XunitException: ".Length..];
            text = Bare(text);
            if (text.Length > 400) text = text[..400] + "...";
            Console.Write($"\n## {Short(name)}{(loc != "" ? "  " + loc : "")}\n");
            if (text != "") Console.WriteLine($"   {text}");
            blocks++;
            name = "";
            loc = "";
            body.Clear();
        }

        foreach (var line in section)
        {
            if (line.StartsWith("failed ", StringComparison.Ordinal))
            {
                Flush();
                name = Duration.Replace(line[7..], "", 1);
                on = true;
                inStack = false;
                continue;
            }
            if (!on) continue;
            if (line.StartsWith("  from ", StringComparison.Ordinal)) continue;
            if (line.StartsWith("    at ", StringComparison.Ordinal))
            {
                inStack = true;
                var m = FrameFile.Match(line);
                if (loc == "" && m.Success)
                {
                    var f = m.Groups[1].Value;
                    if (Rel(f) != f) loc = Rel(f);
                }
                continue;
            }
            if (line.Length > 0 && line[0] != ' ') { Flush(); on = false; continue; }
            if (!inStack && line.StartsWith("  ", StringComparison.Ordinal))
            {
                var s = line.TrimStart(' ', '\t');
                if (s != "" && !s.StartsWith("Standard output:", StringComparison.Ordinal)) body.Add(s);
            }
        }
        Flush();
        return blocks;
    }

    static string MtpExitMeaning(int code) => code switch
    {
        1 => "a generic failure",
        2 => "a test failed, though the summary counted none",
        3 => "the test session was aborted",
        4 => "the platform setup is invalid",
        7 => "a test host exited without finishing, a crash",
        9 => "fewer tests ran than --minimum-expected-tests asks",
        10 => "the test adapter failed the session",
        13 => "the run stopped at --maximum-failed-tests",
        134 => "a test host aborted (SIGABRT), usually an unhandled exception",
        _ => "see https://aka.ms/testingplatform/exitcodes",
    };

    static readonly Regex Count = new(@"^[ \t]*(total|failed|succeeded|skipped):[ \t]+([0-9]+)[ \t]*$");
    static readonly Regex DurationLine = new(@"^[ \t]*duration: ");
    static readonly Regex ErrorOutput = new(@"^[ \t]*Error output: ");
    static readonly Regex ExitCode = new(@"^Exit code: ([0-9]*)$");

    static int ReportTest(int exit, string scope, string commandLine)
    {
        var section = TestSection();
        long pass = 0, skip = 0, total = 0, fail = 0;
        var seen = false;
        foreach (var l in section)
        {
            var m = Count.Match(l);
            if (!m.Success) continue;
            var n = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            switch (m.Groups[1].Value)
            {
                case "total": total += n; seen = true; break;
                case "failed": fail += n; break;
                case "succeeded": pass += n; break;
                case "skipped": skip += n; break;
            }
        }
        var duration = section.Where(l => DurationLine.IsMatch(l)).Select(l => DurationLine.Replace(l, "", 1)).LastOrDefault();
        if (string.IsNullOrEmpty(duration)) duration = "?";
        if (note.Length > 0) Console.WriteLine(note);

        if (exit == 5)
        {
            Console.WriteLine("TESTS REJECTED: the test hosts refused the command line (exit 5) and ran nothing");
            Console.WriteLine($"      ran: {commandLine}");
            FixLine("exit:test:5");
            LogLine();
            return 1;
        }

        if (section.Any(l => l.Contains("Cannot add simple filter or query filter", StringComparison.Ordinal)))
        {
            Console.WriteLine($"TESTS ABORTED: xunit.v3 refused a mix of filter kinds (exit {exit}) and ran nothing");
            Console.WriteLine($"      ran: {commandLine}");
            FixLine("exit:test:filter-mix");
            LogLine();
            return 1;
        }

        if (exit == 8 && total == 0)
        {
            Console.WriteLine("TESTS NONE: the filter matched no test (exit 8)");
            Console.WriteLine($"      ran: {commandLine}");
            FixLine("exit:test:8");
            LogLine();
            return 1;
        }

        if (!seen || total == 0)
        {
            Console.Write($"TESTS DID NOT REPORT: exit {exit} and no test summary with a total was parsed; either nothing ran or the output format moved\n\n");
            Excerpt(section, 15);
            LogLine();
            return 1;
        }

        // The exit codes of the assemblies other than 0, and the 8 of each assembly a filter matched nothing in.
        var exits = section.Select(l => ExitCode.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        var otherExits = string.Join(' ', exits.Where(e => e != "0" && e != "8").Distinct().OrderBy(e => e, StringComparer.Ordinal));
        var zeroRan = section.Count(l => l == "Exit code: 8");

        if (fail == 0 && (exit == 0 || (exit == 8 && otherExits.Length == 0)))
        {
            Console.WriteLine($"TESTS OK: {pass} passed, {skip} skipped, {scope}, {duration}");
            if (zeroRan != 0) Console.WriteLine($"      {zeroRan} assemblies matched no test (exit 8 each), which is the run's exit code too");
            SkippedNames(section);
            return 0;
        }

        if (fail > 0)
        {
            Console.WriteLine($"TESTS FAILED: {fail} failed of {total} ({pass} passed, {skip} skipped), {scope}, {duration}");
            var blocks = FailureBlocks(section);
            if (blocks != fail)
                Console.Write($"\nnote: {blocks} failure block(s) parsed for {fail} failed test(s); the log has the rest\n");
            Console.WriteLine();
            SkippedNames(section);
            LogLine();
            return 1;
        }

        var why = section.Where(l => ErrorOutput.IsMatch(l)).Select(l => ErrorOutput.Replace(l, "", 1)).FirstOrDefault();
        Console.WriteLine($"TESTS ABORTED: exit {exit}, {MtpExitMeaning(exit)}; {pass} passed of {total} before it");
        if (otherExits.Length > 0) Console.WriteLine($"      assembly exit codes: {otherExits}");
        if (!string.IsNullOrEmpty(why)) Console.WriteLine($"      {why}");
        LogLine();
        return 1;
    }
}
