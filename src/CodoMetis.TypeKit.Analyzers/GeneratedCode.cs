using System;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// How every CMTK rule treats generated code: it is analysed, and a diagnostic located in it is
/// reported only when it maps to Razor markup. Every analyzer configures itself with
/// <see cref="AnalysisFlags"/> and reports through the <c>Report</c> overloads, never through
/// <c>ReportDiagnostic</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// The C# the Razor compiler generates for a <c>.razor</c> or <c>.cshtml</c> file is generated code,
/// and a rule that skipped generated code, as Roslyn's default does, never saw
/// <c>@code { OrderId _id = default; }</c>. That code carries <c>#line</c> directives back to the
/// markup, which is what a location maps to and what the IDE shows.
/// </para>
/// <para>
/// Everything else generated stays unreported, as before: EF Core's compiled model calls
/// <c>ValueObjectConverter.Materialize</c> (CMTK0004), and source-generated JSON and regex code is
/// nobody's to fix. What counts as generated is what Roslyn counts: the analysis context's own flag,
/// and, for a location in another tree than the one analysed, <c>generated_code</c> in
/// <c>.editorconfig</c>, a generated file name or an <c>&lt;auto-generated&gt;</c> header. Code hidden
/// by <c>#line hidden</c>, which Roslyn also suppressed, stays suppressed.
/// </para>
/// </remarks>
internal static class GeneratedCode
{
    /// <summary>
    /// Analyse generated code, and let the <c>Report</c> overloads decide what of it is reported:
    /// nothing but what <see cref="IsReportable"/> lets through.
    /// </summary>
    public const GeneratedCodeAnalysisFlags AnalysisFlags = GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics;

    public static void Report(this SyntaxNodeAnalysisContext context, Diagnostic diagnostic)
    {
        if (IsReportable(diagnostic.Location, context.IsGeneratedCode, context.SemanticModel.Compilation, context.CancellationToken))
            context.ReportDiagnostic(diagnostic);
    }

    public static void Report(this OperationAnalysisContext context, Diagnostic diagnostic)
    {
        if (IsReportable(diagnostic.Location, context.IsGeneratedCode, context.Compilation, context.CancellationToken))
            context.ReportDiagnostic(diagnostic);
    }

    public static void Report(this SymbolAnalysisContext context, Diagnostic diagnostic)
    {
        if (IsReportable(diagnostic.Location, context.IsGeneratedCode, context.Compilation, context.CancellationToken))
            context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Whether a diagnostic at <paramref name="location"/> is reported: always when it maps to Razor
    /// markup, otherwise only outside generated code.
    /// </summary>
    /// <param name="location">Where the diagnostic would be reported.</param>
    /// <param name="isGeneratedCode">The analysis context's verdict on the code it analysed.</param>
    /// <param name="compilation">The compilation, for <c>generated_code</c> in <c>.editorconfig</c>.</param>
    /// <param name="cancellationToken">The analysis's cancellation token.</param>
    public static bool IsReportable(Location location, bool isGeneratedCode, Compilation compilation, CancellationToken cancellationToken)
    {
        if (location.SourceTree is not { } tree) return !isGeneratedCode;

        // A position under #line "Component.razor" maps to the markup. One under #line hidden maps
        // nowhere, which is the scaffolding the Razor compiler adds around the user's code.
        var mapped = location.GetMappedLineSpan();
        if (mapped.HasMappedPath && IsMarkup(mapped.Path)) return true;

        return !isGeneratedCode
            && !IsHidden(tree, location.SourceSpan.Start, cancellationToken)
            && !IsGeneratedTree(tree, compilation, cancellationToken);
    }

    /// <summary>
    /// Roslyn's own test for C#: in a file with <c>#line hidden</c>, a line under it and a line before
    /// the first <c>#line</c> directive are hidden.
    /// </summary>
    private static bool IsHidden(SyntaxTree tree, int position, CancellationToken cancellationToken) =>
        tree.HasHiddenRegions()
     && tree.GetLineVisibility(position, cancellationToken) is LineVisibility.Hidden or LineVisibility.BeforeFirstLineDirective;

    private static bool IsMarkup(string path) =>
        path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Roslyn's own test, which it does not expose: <c>generated_code</c> in <c>.editorconfig</c>
    /// decides where it is set; otherwise a generated file name or an auto-generated header does.
    /// </summary>
    private static bool IsGeneratedTree(SyntaxTree tree, Compilation compilation, CancellationToken cancellationToken)
    {
        switch (compilation.Options.SyntaxTreeOptionsProvider?.IsGenerated(tree, cancellationToken))
        {
            case GeneratedKind.MarkedGenerated: return true;
            case GeneratedKind.NotGenerated:    return false;
        }

        return HasGeneratedFileName(tree.FilePath) || BeginsWithAutoGeneratedComment(tree, cancellationToken);
    }

    private static bool HasGeneratedFileName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrEmpty(Path.GetExtension(fileName))) return false;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.EndsWith(".designer", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".generated", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".g", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".g.i", StringComparison.OrdinalIgnoreCase);
    }

    private static bool BeginsWithAutoGeneratedComment(SyntaxTree tree, CancellationToken cancellationToken)
    {
        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) continue;

            var text = trivia.ToString();
            if (text.IndexOf("<autogenerated", StringComparison.OrdinalIgnoreCase) >= 0
             || text.IndexOf("<auto-generated", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }
}
