using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0003: a <c>Result</c> or <c>Option</c> that a call returns, dropped by an expression
/// statement, awaited or not, or by a conversion of its <c>Task</c> to a plain <c>Task</c>.
/// </summary>
/// <remarks>
/// <para>
/// A dropped result is an error nobody sees: <c>Email.Create(input);</c> validates and forgets the
/// verdict, <c>orders.Cancel(id);</c> may have failed. <c>_ = …</c> says the drop is intended.
/// CA1806 can require using a return value only per method name, not per return type.
/// </para>
/// <para>
/// A <c>Task&lt;Result&lt;…&gt;&gt;</c> converts implicitly to <c>Task</c>, which drops the result as
/// silently: <c>Task Cancel(Guid id) =&gt; orders.CancelAsync(id);</c>, <c>return svc.DoAsync();</c>
/// in a method returning <c>Task</c>, <c>Func&lt;Task&gt; f = () =&gt; svc.DoAsync();</c>. The implicit
/// conversion of a call's task is reported; an explicit <c>(Task)</c> cast says the drop is intended.
/// A <c>ValueTask&lt;T&gt;</c> has no conversion to <c>ValueTask</c>.
/// </para>
/// <para>
/// <c>Tap</c>, <c>TapAsync</c> and <c>TapError</c> return their receiver unchanged, and so do the
/// <c>Task</c> continuations <c>TapAsync</c> and <c>TapErrorAsync</c>, so <c>result.Tap(log);</c> or
/// <c>await pending.TapAsync(log);</c> on a variable drops nothing the caller does not still hold. On
/// anything else, such as <c>Find(id).Tap(log);</c>, the result is gone and is reported.
/// </para>
/// <para>
/// In a project that declares value objects, Metalama runs analyzers on the source before weaving,
/// where the generated <c>TryFrom</c> and <c>Revalidate</c> do not bind. An ignored
/// <c>X.TryFrom(…)</c> or <c>x.Revalidate()</c> is recognised there by name
/// (<see cref="TypeKitSymbols.GeneratedMemberCall"/>) as a statement, as the body of a method, local
/// function or accessor that returns nothing, or as the body of a lambda that returns nothing. A call
/// chained onto one, such as <c>X.TryFrom(s).Tap(log)</c>, does not bind either and is not seen there.
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
        description: "A Result carries an error and an Option an absence; dropping either as a statement, or converting its Task to a plain Task, means nobody decides what happens then. Assign it to '_' to make an intended drop explicit.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.IgnoredOutcome)
    );

    private const string TryFrom = "TryFrom";

    private const string Revalidate = "Revalidate";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCode.AnalysisFlags);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (TypeKitSymbols.Resolve(start.Compilation) is not { } symbols) return;

            var tasks = new Tasks(
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"),
                start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"));

            start.RegisterOperationAction(operation => Analyze(operation, symbols, tasks), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(operation => AnalyzeConversion(operation, symbols, tasks), OperationKind.Conversion);
            start.RegisterSyntaxNodeAction(
                node => AnalyzeUnbound(node, symbols),
                SyntaxKind.ExpressionStatement,
                SyntaxKind.ArrowExpressionClause,
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression);
        });
    }

    private static void Analyze(OperationAnalysisContext context, TypeKitSymbols symbols, Tasks tasks)
    {
        var expression = ((IExpressionStatementOperation)context.Operation).Operation;

        // Only a value a call produced: an assignment or an increment is a statement for its effect.
        var call = expression is IAwaitOperation { Operation: var awaited } ? awaited : Unwrap(expression);
        if (call is not IInvocationOperation invocation) return;

        // await LoadAsync().ConfigureAwait(false): the result is LoadAsync's, and so is the name.
        if (invocation is { TargetMethod.Name: "ConfigureAwait", Instance: IInvocationOperation configured }) invocation = configured;

        var outcome = symbols.OutcomeName(expression.Type) ?? tasks.PendingOutcome(expression.Type, symbols);
        if (outcome is null || ReturnsItsStoredReceiver(invocation, symbols)) return;

        context.Report(Diagnostic.Create(Rule, expression.Syntax.GetLocation(), outcome, invocation.TargetMethod.Name));
    }

    /// <summary>
    /// A call's <c>Task</c> of a result, converted implicitly to a plain <c>Task</c>, which has no
    /// result to hand on.
    /// </summary>
    private static void AnalyzeConversion(OperationAnalysisContext context, TypeKitSymbols symbols, Tasks tasks)
    {
        var conversion = (IConversionOperation)context.Operation;

        if (!conversion.IsImplicit || !SymbolEqualityComparer.Default.Equals(conversion.Type, tasks.Plain)) return;
        if (conversion.Operand is not IInvocationOperation invocation || tasks.PendingOutcome(invocation.Type, symbols) is not { } outcome) return;

        context.Report(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), outcome, invocation.TargetMethod.Name));
    }

    /// <summary>
    /// <c>Email.TryFrom(input);</c> and <c>code.Revalidate();</c> on a value object of the same
    /// project, which do not bind where Metalama runs analyzers
    /// (<see cref="TypeKitSymbols.GeneratedMemberCall"/>): as a statement, or as the whole body of a
    /// method or lambda that returns nothing. A bound call is the operation path's.
    /// </summary>
    private static void AnalyzeUnbound(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var model = context.SemanticModel;
        var token = context.CancellationToken;

        var dropped = context.Node switch
        {
            ExpressionStatementSyntax statement => statement.Expression,
            ArrowExpressionClauseSyntax arrow when model.GetDeclaredSymbol(arrow.Parent!, token) is IMethodSymbol { ReturnsVoid: true } => arrow.Expression,
            LambdaExpressionSyntax { ExpressionBody: { } body } lambda when model.GetSymbolInfo(lambda, token).Symbol is IMethodSymbol { ReturnsVoid: true } => body,
            _ => null
        };

        if (dropped is not InvocationExpressionSyntax invocation || model.GetSymbolInfo(invocation, token).Symbol is not null) return;

        if (symbols.GeneratedMemberCall(invocation, TryFrom, model, token) is not null)
            context.Report(Diagnostic.Create(Rule, invocation.GetLocation(), "Option", TryFrom));
        else if (symbols.GeneratedInstanceMemberCall(invocation, Revalidate, model, token) is not null)
            context.Report(Diagnostic.Create(Rule, invocation.GetLocation(), "Result", Revalidate));
    }

    private static IOperation Unwrap(IOperation operation) =>
        operation switch
        {
            IConditionalAccessOperation { WhenNotNull: var whenNotNull } => Unwrap(whenNotNull),
            IConversionOperation { IsImplicit: true, Operand: var operand } => Unwrap(operand),
            _ => operation
        };

    /// <summary>
    /// A call that returns its receiver unchanged, on a receiver the caller still holds: the
    /// outcome's own <c>Tap</c>, <c>TapAsync</c> and <c>TapError</c>, and the <c>Task</c>
    /// continuations <c>TapAsync</c> and <c>TapErrorAsync</c>, which return the task's result as it was.
    /// </summary>
    private static bool ReturnsItsStoredReceiver(IInvocationOperation invocation, TypeKitSymbols symbols)
    {
        var method = invocation.TargetMethod;

        if (method is { IsStatic: false, Name: "Tap" or "TapAsync" or "TapError" } && symbols.OutcomeName(method.ContainingType) is not null)
            return IsStored(invocation.Instance);

        // pending.TapAsync(log) binds to the extension block's member, with the task as its instance;
        // Result.TapAsync(pending, log) to its implementation, with the task as the first argument.
        if (method.Name is "TapAsync" or "TapErrorAsync" && symbols.IsDeclaredByResultClass(method))
            return IsStored(invocation.Instance ?? invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value);

        return false;
    }

    private static bool IsStored(IOperation? receiver)
    {
        while (receiver is IConversionOperation { IsImplicit: true } conversion) receiver = conversion.Operand;

        return receiver is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation;
    }

    /// <summary>The task types, resolved once per compilation.</summary>
    private sealed class Tasks(INamedTypeSymbol? plain, INamedTypeSymbol? generic, INamedTypeSymbol? genericValueTask)
    {
        public INamedTypeSymbol? Plain { get; } = plain;

        /// <summary>A <c>Task</c> or <c>ValueTask</c> of a result, not awaited: the result is dropped with it.</summary>
        public string? PendingOutcome(ITypeSymbol? type, TypeKitSymbols symbols) =>
            type is INamedTypeSymbol { TypeArguments.Length: 1 } named
         && (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, generic) || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, genericValueTask))
                ? symbols.OutcomeName(named.TypeArguments[0])
                : null;
    }
}
