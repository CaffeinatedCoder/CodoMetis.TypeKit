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
/// operators, <c>a.Value.Equals(b.Value)</c> and <c>CompareTo</c> (with a comparison argument too),
/// and the two-value forms <c>string.Equals</c>, <c>string.Compare</c>, <c>string.CompareOrdinal</c>,
/// <c>object.Equals</c> and <c>comparer.Equals</c>, into which analyzers such as MA0006 rewrite
/// <c>==</c>. The value is read through <c>.Value</c>, <c>?.Value</c>, <c>GetValue()</c> or
/// <c>ValueOrNull()</c>, in expression trees too.
/// </para>
/// <para>
/// Two value objects of different types compared without unwrapping, <c>order.Id.Equals(customerId)</c>
/// or <c>Equals(order.Id, customerId)</c>, compile through <c>Equals(object)</c> and are always false:
/// the same bug, reported by the same rule.
/// </para>
/// <para>
/// Recognised by syntax: <c>.Value</c> and the companions are generated, so on a value object of the
/// same project they do not bind where Metalama runs analyzers, and neither does a call that takes
/// them. The receiver's type still binds when it is declared: a parameter, a field, a property or a
/// typed local. A local declared with <c>var</c> from a generated factory (<c>var a = OrderId.From(g)</c>)
/// or a factory call itself has no type there, and is not seen in that project. An explicit cast is
/// a deliberate conversion and is not reported, nor is a value object or its value compared with a
/// raw value.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MixedValueComparisonAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MixedValueComparison,
        title: "Values of different value objects compared",
        messageFormat: "This compares {0} with {1}, two different value objects, which their types exist to keep apart. Compare two of one type, or convert one explicitly if they really share an identity.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Two value objects of different types cannot be compared with ==, which is the point of them. Comparing what they wrap compiles, and brings back the bug they prevent, such as an order id compared with a customer id. Equals(object) between them compiles too, and is always false."
    );

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

    /// <summary>
    /// <c>a.Value.Equals(b.Value)</c> and <c>a.Value.CompareTo(b.Value)</c>, a comparison argument
    /// after the value included; <c>string.Equals(a.Value, b.Value)</c>, <c>string.Compare</c>,
    /// <c>string.CompareOrdinal</c>, <c>object.Equals</c> and <c>comparer.Equals</c>, which take both
    /// values first; and <c>Equals</c> between two value objects themselves.
    /// </summary>
    /// <remarks>
    /// By name, since in the declaring project a call given a generated <c>.Value</c> does not bind.
    /// Both operands must still be read from value objects of two different types, which is what the
    /// rule is about whatever the method is.
    /// </remarks>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var (receiver, name) = invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => (access.Expression, access.Name.Identifier.ValueText),
            IdentifierNameSyntax identifier     => (null, identifier.Identifier.ValueText),
            _                                   => ((ExpressionSyntax?)null, (string?)null)
        };

        if (name is not ("Equals" or "CompareTo" or "Compare" or "CompareOrdinal")) return;

        var arguments = invocation.ArgumentList.Arguments;

        // a.Value.Equals(b.Value) and a.Value.Equals(b.Value, StringComparison.Ordinal).
        if (receiver is not null && name is "Equals" or "CompareTo" && arguments.Count is 1 or 2
         && Report(context, invocation, receiver, arguments[0].Expression, symbols)) return;

        // string.Equals(a.Value, b.Value, …), object.Equals, EqualityComparer<Guid>.Default.Equals, string.Compare.
        if (name is not "CompareTo" && arguments.Count >= 2) Report(context, invocation, arguments[0].Expression, arguments[1].Expression, symbols);
    }

    private static bool Report(SyntaxNodeAnalysisContext context, SyntaxNode comparison, ExpressionSyntax left, ExpressionSyntax right, TypeKitSymbols symbols)
    {
        ITypeSymbol? leftType, rightType;
        string form;

        if (WrappedRead(left, context, symbols) is { } leftRead && WrappedRead(right, context, symbols) is { } rightRead)
        {
            (leftType, rightType, form) = (leftRead, rightRead, "the value of a '{0}'");
        }
        else if (comparison is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Equals" } or IdentifierNameSyntax { Identifier.ValueText: "Equals" } }
              && ValueObjectOperand(left, context, symbols) is { } leftObject)
        {
            // Equals(object) only: the value objects themselves, not unwrapped. Asked last and only for
            // Equals, since it needs the operands' types, which every x.CompareTo(y) would pay for.
            (leftType, rightType, form) = (leftObject, ValueObjectOperand(right, context, symbols), "a '{0}'");
        }
        else return false;

        if (leftType is null || rightType is null || SymbolEqualityComparer.Default.Equals(leftType.OriginalDefinition, rightType.OriginalDefinition)) return false;

        context.Report(Diagnostic.Create(
            Rule,
            comparison.GetLocation(),
            string.Format(form, leftType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
            string.Format(form, rightType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))));

        return true;
    }

    /// <summary>
    /// The value object <paramref name="expression"/> is itself, not unwrapped, as in
    /// <c>order.Id.Equals(customerId)</c>. An explicit cast is a deliberate conversion.
    /// </summary>
    private static ITypeSymbol? ValueObjectOperand(ExpressionSyntax expression, SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;

        if (expression is CastExpressionSyntax) return null;

        // A concrete value object: an interface or a type parameter may hold one of either type.
        return ValueObject(expression, context, symbols) is { TypeKind: TypeKind.Struct or TypeKind.Class } type ? type : null;
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
