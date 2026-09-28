using System.Collections.Immutable;
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
/// <c>args[0]</c>). That is input as far as this code can tell.
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
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
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

        if (symbols.GeneratedMemberCall(invocation, FromKnownGood, context.SemanticModel, context.CancellationToken) is not { } valueObject) return;

        // The value comes first; the generated method adds the caller's expression after it.
        if (invocation.ArgumentList.Arguments.Count == 0 || invocation.ArgumentList.Arguments[0] is not { NameColon: null } argument) return;

        var value = context.SemanticModel.GetOperation(argument.Expression, context.CancellationToken);
        if (value is null || !ArrivesFromCaller(value)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, argument.Expression.GetLocation(), valueObject.Name, argument.Expression.ToString()));
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
