// The analyzer probes' compiler (C17 CMTK0014, C19 CMTK0010): a CSharpCompilation over the real CodoMetis.TypeKit and the
// running framework, with the CMTK analyzer that reports the claim's id attached, as test/CodoMetis.TypeKit.Analyzers.Tests
// runs it. Sources carry markup {|#label:...|}; a marked span is where the probe expects (or forbids) a report.
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
namespace Probes.Build;

/// <summary>One marked span of a source file, by its label.</summary>
public sealed record Mark(string Label, string File, TextSpan Span, string Text);

/// <summary>A source file whose markup was taken out, with the spans it marked.</summary>
public sealed record Marked(string File, string Text, IReadOnlyList<Mark> Marks)
{
    /// <summary>Strips <c>{|#label:…|}</c> (not nested) and records each span in the stripped text.</summary>
    public static Marked Parse(string file, string source)
    {
        var sb = new System.Text.StringBuilder(); var marks = new List<Mark>(); var i = 0;
        while (i < source.Length)
        {
            var open = source.IndexOf("{|#", i, StringComparison.Ordinal);
            if (open < 0) { sb.Append(source, i, source.Length - i); break; }
            sb.Append(source, i, open - i);
            var colon = source.IndexOf(':', open);
            var label = source[(open + 3)..colon];
            var close = source.IndexOf("|}", colon, StringComparison.Ordinal);
            if (close < 0) throw new InvalidOperationException($"unclosed markup {label} in {file}");
            var inner = source[(colon + 1)..close];
            if (inner.Contains("{|", StringComparison.Ordinal)) throw new InvalidOperationException($"nested markup in {label} of {file}");
            marks.Add(new Mark(label, file, new TextSpan(sb.Length, inner.Length), inner));
            sb.Append(inner);
            i = close + 2;
        }
        return new Marked(file, sb.ToString(), marks);
    }
}

/// <summary>What one compilation reported: compiler errors, the analyzer's diagnostics, analyzer failures.</summary>
public sealed record Analysis(IReadOnlyList<Diagnostic> Errors, IReadOnlyList<Diagnostic> Reported, IReadOnlyList<Diagnostic> AnalyzerFailures, IReadOnlyList<Marked> Files)
{
    public IEnumerable<Mark> Marks => Files.SelectMany(f => f.Marks);
    public static string Where(Diagnostic d) => $"{d.Location.SourceTree?.FilePath}:{d.Location.SourceSpan} '{d.Location.SourceTree?.GetText().ToString(d.Location.SourceSpan)}'";
    public string ErrorText() => string.Join("; ", Errors.Take(5).Select(e => $"{e.Id} {Where(e)}: {e.GetMessage()}"));
}

public static class Rz
{
    static readonly Lazy<ImmutableArray<MetadataReference>> Refs = new(() =>
    {
        // The running shared framework (implementation assemblies, as the probe's own process binds), plus the real base package and NodaTime.
        var framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetDirectoryName(p) == framework && Path.GetFileName(p) is var n && (n.StartsWith("System.") || n.StartsWith("Microsoft.") || n is "mscorlib.dll" or "netstandard.dll"));
        return [.. tpa.Concat([typeof(CodoMetis.TypeKit.Option<>).Assembly.Location, typeof(NodaTime.Instant).Assembly.Location])
                      .Distinct().Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
    });

    /// <summary>The CMTK analyzers that report <paramref name="id"/>, found by the id they declare, never by type name.</summary>
    public static ImmutableArray<DiagnosticAnalyzer> AnalyzersFor(string id) =>
    [
        .. typeof(CodoMetis.TypeKit.Analyzers.TypeArgumentConstraintAnalyzer).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .Where(a => a.SupportedDiagnostics.Any(d => d.Id == id)),
    ];

    public static async Task<Analysis> Analyze(string id, IEnumerable<(string File, string Source)> sources)
    {
        var files = sources.Select(s => Marked.Parse(s.File, s.Source)).ToList();
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f.Text, parse, path: f.File)).ToList();
        var compilation = CSharpCompilation.Create("ProbeBuild" + Guid.NewGuid().ToString("N"), trees, Refs.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var analyzers = AnalyzersFor(id);
        if (analyzers.Length == 0) throw new InvalidOperationException($"no CMTK analyzer declares {id}");
        var failures = new List<Diagnostic>();
        var options = new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), (ex, analyzer, diagnostic) => { lock (failures) failures.Add(diagnostic); },
            concurrentAnalysis: false, logAnalyzerExecutionTime: false);
        var with = compilation.WithAnalyzers(analyzers, options);
        var analyzerDiagnostics = await with.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        failures.AddRange(analyzerDiagnostics.Where(d => d.Id == "AD0001"));
        return new Analysis(errors, [.. analyzerDiagnostics.Where(d => d.Id == id)], failures, files);
    }

    /// <summary>
    /// Compares the reports with the marks: every mark in <paramref name="expected"/> reported exactly once at its span, nothing
    /// reported anywhere else. Null when they agree, else what differs.
    /// </summary>
    public static string? Agree(Analysis a, IReadOnlySet<string> expected, Func<Diagnostic, Mark, string?>? message = null) =>
        Agree(a, expected, _ => true, outside: true, message);

    /// <summary>
    /// The same over the marks <paramref name="judged"/> selects, one check's share of a compilation: each of them in
    /// <paramref name="expected"/> reported exactly once at its span, the others silent, and with <paramref name="outside"/>, nothing
    /// reported outside every mark. A report at a mark the check does not judge is another check's.
    /// </summary>
    public static string? Agree(Analysis a, IReadOnlySet<string> expected, Func<Mark, bool> judged, bool outside, Func<Diagnostic, Mark, string?>? message = null)
    {
        var marks = a.Marks.ToList();
        foreach (var label in expected) if (!marks.Any(m => m.Label == label)) throw new InvalidOperationException($"no mark {label}");
        var problems = new List<string>();
        foreach (var m in marks.Where(judged))
        {
            var at = At(a, m);
            if (expected.Contains(m.Label))
            {
                if (at.Count != 1) problems.Add($"{m.Label} '{m.Text}': reported {at.Count} times, expected once");
                else if (message?.Invoke(at[0], m) is { } bad) problems.Add($"{m.Label} '{m.Text}': {bad}");
            }
            else if (at.Count != 0) problems.Add($"{m.Label} '{m.Text}': reported, expected silent ({at[0].GetMessage()})");
        }
        if (outside) problems.AddRange(Outside(a).Select(d => $"reported outside every mark at {Analysis.Where(d)}: {d.GetMessage()}"));
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    /// <summary>The reports exactly at a mark's span.</summary>
    public static List<Diagnostic> At(Analysis a, Mark m) =>
        [.. a.Reported.Where(d => d.Location.SourceTree?.FilePath == m.File && d.Location.SourceSpan == m.Span)];

    /// <summary>The reports whose span holds a mark's: at it, or at the syntax around it.</summary>
    public static List<Diagnostic> Around(Analysis a, Mark m) =>
        [.. a.Reported.Where(d => d.Location.SourceTree?.FilePath == m.File && d.Location.SourceSpan.Contains(m.Span))];

    /// <summary>The reports whose span holds no mark.</summary>
    public static List<Diagnostic> Outside(Analysis a) =>
        [.. a.Reported.Where(d => !a.Marks.Any(m => d.Location.SourceTree?.FilePath == m.File && d.Location.SourceSpan.Contains(m.Span)))];

    /// <summary>
    /// Whether a value is reported, wherever around it: each mark <paramref name="judged"/> names that is in <paramref name="expected"/>
    /// has exactly one report at or around it, the others none. Where the report sits is <see cref="WhereWritten"/>'s question, so a
    /// report moved to the syntax around its value still counts here. Null when they agree, else what differs.
    /// </summary>
    public static string? Reported(Analysis a, IEnumerable<string> judged, IReadOnlySet<string> expected, Func<Diagnostic, Mark, string?>? message = null)
    {
        var problems = new List<string>();
        foreach (var label in judged)
        {
            var m = a.Marks.SingleOrDefault(x => x.Label == label) ?? throw new InvalidOperationException($"no mark {label}");
            var around = Around(a, m);
            if (expected.Contains(label))
            {
                if (around.Count != 1) problems.Add($"{m.Label} '{m.Text}': reported {around.Count} times, expected once");
                else if (message?.Invoke(around[0], m) is { } bad) problems.Add($"{m.Label} '{m.Text}': {bad}");
            }
            else if (around.Count != 0) problems.Add($"{m.Label} '{m.Text}': reported, expected silent ({around[0].GetMessage()})");
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    /// <summary>
    /// Where the reports sit: every report at or around a mark in <paramref name="expected"/> is at the mark's own span, the value as
    /// written, and none is outside every mark. Null when so, else what differs.
    /// </summary>
    public static string? WhereWritten(Analysis a, IReadOnlySet<string> expected)
    {
        var problems = new List<string>();
        foreach (var m in a.Marks.Where(m => expected.Contains(m.Label)))
            foreach (var d in Around(a, m).Where(d => d.Location.SourceSpan != m.Span))
                problems.Add($"{m.Label} '{m.Text}': reported at {Analysis.Where(d)}, not at the value");
        problems.AddRange(Outside(a).Select(d => $"reported outside every mark at {Analysis.Where(d)}: {d.GetMessage()}"));
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }
}
