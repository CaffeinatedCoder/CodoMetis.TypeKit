using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0003: a <c>Result</c> or <c>Option</c> that a call returns, dropped by an expression
/// statement, awaited or not.
/// </summary>
/// <remarks>
/// <para>
/// A dropped result is an error nobody sees: <c>Email.Create(input);</c> validates and forgets the
/// verdict, <c>orders.Cancel(id);</c> may have failed. <c>_ = …</c> says the drop is intended.
/// CA1806 can require using a return value only per method name, not per return type.
/// </para>
/// <para>
/// <c>Tap</c> and <c>TapAsync</c> return their receiver unchanged, so <c>result.Tap(log);</c> on a
/// variable drops nothing the caller does not still hold. On anything else, such as
/// <c>Find(id).Tap(log);</c>, the result is gone and is reported.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoredOutcomeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.IgnoredOutcome,
        title: "Result or Option ignored",
        messageFormat: "The {0} that '{1}' returns is ignored, so its outcome is never seen. Handle it, or discard it with '_ =' if nothing depends on it.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Result carries an error and an Option an absence; dropping either as a statement means nobody decides what happens then. Assign it to '_' to make an intended drop explicit."
    );

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (TypeKitSymbols.Resolve(start.Compilation) is not { } symbols) return;

            var task      = start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            var valueTask = start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

            start.RegisterOperationAction(operation => Analyze(operation, symbols, task, valueTask), OperationKind.ExpressionStatement);
            start.RegisterSyntaxNodeAction(node => AnalyzeUnboundTryFrom(node, symbols), SyntaxKind.ExpressionStatement);
        });
    }

    private static void Analyze(OperationAnalysisContext context, TypeKitSymbols symbols, INamedTypeSymbol? task, INamedTypeSymbol? valueTask)
    {
        var expression = ((IExpressionStatementOperation)context.Operation).Operation;

        // Only a value a call produced: an assignment or an increment is a statement for its effect.
        var call = expression is IAwaitOperation { Operation: var awaited } ? awaited : Unwrap(expression);
        if (call is not IInvocationOperation invocation) return;

        // await LoadAsync().ConfigureAwait(false): the result is LoadAsync's, and so is the name.
        if (invocation is { TargetMethod.Name: "ConfigureAwait", Instance: IInvocationOperation configured }) invocation = configured;

        var outcome = symbols.OutcomeName(expression.Type) ?? PendingOutcome(expression.Type, symbols, task, valueTask);
        if (outcome is null || ReturnsItsStoredReceiver(invocation, symbols)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, expression.Syntax.GetLocation(), outcome, invocation.TargetMethod.Name));
    }

    /// <summary>
    /// <c>Email.TryFrom(input);</c> on a value object of the same project, which does not bind where
    /// Metalama runs analyzers (<see cref="TypeKitSymbols.GeneratedMemberCall"/>). A bound call is the
    /// operation path's.
    /// </summary>
    private static void AnalyzeUnboundTryFrom(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        if (((ExpressionStatementSyntax)context.Node).Expression is not InvocationExpressionSyntax invocation) return;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not null) return;
        if (symbols.GeneratedMemberCall(invocation, "TryFrom", context.SemanticModel, context.CancellationToken) is null) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), "Option", "TryFrom"));
    }

    private static IOperation Unwrap(IOperation operation) =>
        operation switch
        {
            IConditionalAccessOperation { WhenNotNull: var whenNotNull } => Unwrap(whenNotNull),
            IConversionOperation { IsImplicit: true, Operand: var operand } => Unwrap(operand),
            _ => operation
        };

    /// <summary>A <c>Task</c> or <c>ValueTask</c> of a result, not awaited: the result is dropped with it.</summary>
    private static string? PendingOutcome(ITypeSymbol? type, TypeKitSymbols symbols, INamedTypeSymbol? task, INamedTypeSymbol? valueTask) =>
        type is INamedTypeSymbol { TypeArguments.Length: 1 } named
     && (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, task) || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, valueTask))
            ? symbols.OutcomeName(named.TypeArguments[0])
            : null;

    private static bool ReturnsItsStoredReceiver(IInvocationOperation invocation, TypeKitSymbols symbols) =>
        invocation.TargetMethod is { IsStatic: false, Name: "Tap" or "TapAsync" } method
     && symbols.OutcomeName(method.ContainingType) is not null
     && invocation.Instance is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation;
}
