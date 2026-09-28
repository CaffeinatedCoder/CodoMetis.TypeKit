using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0008: the wrapped values of two different value objects compared, as in
/// <c>order.CustomerId.Value == product.Id.Value</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>OrderId == CustomerId</c> does not compile, which is what the types are for. Unwrapping both
/// sides compiles, and puts back the bug they exist to prevent: two identifiers of different things
/// compared because both happen to be a <c>Guid</c>. Reported for <c>==</c>, <c>!=</c>, the ordering
/// operators and <c>Equals</c>/<c>CompareTo</c>, reading through <c>.Value</c>, <c>?.Value</c>,
/// <c>GetValue()</c> or <c>ValueOrNull()</c>, in expression trees too.
/// </para>
/// <para>
/// Recognised by syntax: <c>.Value</c> and the companions are generated, so on a value object of the
/// same project they do not bind where Metalama runs analyzers. An explicit cast is a deliberate
/// conversion and is not reported, nor is a value object's value compared with a raw one.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MixedValueComparisonAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MixedValueComparison,
        title: "Values of different value objects compared",
        messageFormat: "This compares the value of a '{0}' with the value of a '{1}', two different value objects, which their types exist to keep apart. Compare two of one type, or convert one explicitly if they really share an identity.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Two value objects of different types cannot be compared, which is the point of them. Comparing what they wrap compiles, and brings back the bug they prevent, such as an order id compared with a customer id."
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

            start.RegisterSyntaxNodeAction(
                node => AnalyzeBinary(node, symbols),
                SyntaxKind.EqualsExpression,
                SyntaxKind.NotEqualsExpression,
                SyntaxKind.LessThanExpression,
                SyntaxKind.LessThanOrEqualExpression,
                SyntaxKind.GreaterThanExpression,
                SyntaxKind.GreaterThanOrEqualExpression);

            start.RegisterSyntaxNodeAction(node => AnalyzeInvocation(node, symbols), SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeBinary(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var binary = (BinaryExpressionSyntax)context.Node;

        Report(context, binary, binary.Left, binary.Right, symbols);
    }

    /// <summary><c>a.Value.Equals(b.Value)</c> and <c>a.Value.CompareTo(b.Value)</c>.</summary>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Equals" or "CompareTo" } access) return;
        if (invocation.ArgumentList.Arguments.Count != 1) return;

        Report(context, invocation, access.Expression, invocation.ArgumentList.Arguments[0].Expression, symbols);
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode comparison, ExpressionSyntax left, ExpressionSyntax right, TypeKitSymbols symbols)
    {
        if (WrappedRead(left, context, symbols) is not { } leftType || WrappedRead(right, context, symbols) is not { } rightType) return;
        if (SymbolEqualityComparer.Default.Equals(leftType.OriginalDefinition, rightType.OriginalDefinition)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            comparison.GetLocation(),
            leftType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            rightType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    /// The value object whose wrapped value <paramref name="expression"/> reads: <c>x.Value</c> or
    /// <c>x?.Value</c> on a value object, or <c>x.GetValue()</c>/<c>x.ValueOrNull()</c> on one or on
    /// its <c>Nullable</c>.
    /// </summary>
    private static ITypeSymbol? WrappedRead(ExpressionSyntax expression, SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;

        switch (expression)
        {
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Value" } access:
                return ValueObject(access.Expression, context, symbols);

            case ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Value" } } conditional:
                return ValueObject(conditional.Expression, context, symbols);

            case InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetValue" or "ValueOrNull" } companion,
                ArgumentList.Arguments.Count: 0
            }:
                return ValueObject(companion.Expression, context, symbols);

            default:
                return null;
        }
    }

    /// <summary>
    /// The receiver's value object, through a <c>Nullable</c> as the companions take one. A
    /// <c>Nullable</c>'s own <c>Value</c> is the value object, which compared with a wrapped value
    /// does not compile, so it needs no case of its own.
    /// </summary>
    private static ITypeSymbol? ValueObject(ExpressionSyntax receiver, SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var type = context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) type = nullable.TypeArguments[0];

        return type is not null && symbols.FindMarker(type, out _) is not null ? type : null;
    }
}
