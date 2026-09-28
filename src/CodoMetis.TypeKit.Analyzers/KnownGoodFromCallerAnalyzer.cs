using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0007: <c>FromKnownGood</c> given a value that arrived from the caller.
/// </summary>
/// <remarks>
/// <para>
/// <c>FromKnownGood</c> is for values the code knows to be valid, and throws when one is not, which
/// on input turns a validation failure into an exception (a 500 where a 400 was meant). A value is
/// reported when it comes straight from a parameter of the enclosing method, local function or
/// lambda: the parameter itself, or a member or element reached from it (<c>request.Email</c>,
/// <c>args[0]</c>), through <c>!</c> and parentheses, passed by position or by name, and called as
/// <c>X.FromKnownGood(…)</c> or, under <c>using static</c>, as <c>FromKnownGood(…)</c>. That is input
/// as far as this code can tell.
/// </para>
/// <para>
/// The call is recognised by syntax, since on a value object of the same project it does not bind
/// where Metalama runs analyzers (<see cref="TypeKitSymbols.GeneratedMemberCall"/>). A value the code
/// produced itself is not reported, since "a value the caller just produced" is
/// legitimate (docs/plan.md §10): a constant, a local, a field, a method's result, an interpolation.
/// A parameter copied into a local first is therefore not seen. Info, because a private helper that
/// only ever receives constants, and a test theory's parameters, are reported too.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KnownGoodFromCallerAnalyzer : DiagnosticAnalyzer
{
    private const string FromKnownGood = "FromKnownGood";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.KnownGoodFromCaller,
        title: "FromKnownGood given input",
        messageFormat: "'{0}.FromKnownGood' is given '{1}', which arrives from the caller, and throws if it breaks the rules. Validate input with Create or TryFrom; FromKnownGood is for values known to be valid.",
        category: "Usage",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "FromKnownGood throws on a value that breaks the rules, which is right for constants and wrong for input: a request that fails validation becomes an exception. Create returns the fault and TryFrom an Option."
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

            start.RegisterSyntaxNodeAction(node => Analyze(node, symbols), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var model      = context.SemanticModel;

        if (symbols.GeneratedMemberCall(invocation, FromKnownGood, model, context.CancellationToken) is not { } valueObject) return;
        if (ValueArgument(invocation, model, context.CancellationToken) is not { } argument) return;

        // IOperation has no node for ! or for parentheses, so GetOperation on either returns null.
        var value = model.GetOperation(WithoutSuppressionOrParentheses(argument.Expression), context.CancellationToken);
        if (value is null || !ArrivesFromCaller(value)) return;

        context.Report(Diagnostic.Create(Rule, argument.Expression.GetLocation(), valueObject.Name, argument.Expression.ToString()));
    }

    /// <summary>
    /// The argument for the value, the method's first parameter; the generated method adds the
    /// caller's expression after it. A bound call says which argument that is, named or not. An
    /// unbound one passes it first by position, or alone by any name.
    /// </summary>
    private static ArgumentSyntax? ValueArgument(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        // The argument operation's syntax is the ArgumentSyntax, or, through ! or parentheses, the
        // expression inside them (and the operation is then marked implicit).
        if (model.GetOperation(invocation, cancellationToken) is IInvocationOperation bound)
            return bound.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Syntax.FirstAncestorOrSelf<ArgumentSyntax>() is { } syntax
                && syntax.Parent == invocation.ArgumentList
                       ? syntax
                       : null;

        var arguments = invocation.ArgumentList.Arguments;

        return arguments.Count switch
        {
            0                                   => null,
            _ when arguments[0].NameColon is null => arguments[0],
            1                                   => arguments[0],
            _                                   => null
        };
    }

    private static ExpressionSyntax WithoutSuppressionOrParentheses(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;

                case PostfixUnaryExpressionSyntax suppressed when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppressed.Operand;
                    break;

                default:
                    return expression;
            }
        }
    }

    private static bool ArrivesFromCaller(IOperation operation) =>
        operation switch
        {
            IParameterReferenceOperation                              => true,
            IConversionOperation { Operand: var operand }             => ArrivesFromCaller(operand),
            IMemberReferenceOperation { Instance: { } instance }      => ArrivesFromCaller(instance),
            IArrayElementReferenceOperation { ArrayReference: var array } => ArrivesFromCaller(array),
            _                                                         => false
        };
}
