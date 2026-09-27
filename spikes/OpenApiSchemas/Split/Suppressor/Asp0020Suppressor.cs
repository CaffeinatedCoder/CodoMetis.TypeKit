using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>Measurement only: suppresses every ASP0020, to learn whether an error-severity analyzer diagnostic can be suppressed at all.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Asp0020Suppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Rule = new("SPK0020", "ASP0020", "The value object implements IParsable once woven.");
    private static readonly SuppressionDescriptor Control = new("SPK0168", "CS0168", "Control: a plain compiler warning.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(Rule, Control);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
            context.ReportSuppression(Suppression.Create(diagnostic.Id == "ASP0020" ? Rule : Control, diagnostic));
    }
}
