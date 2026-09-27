using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0001: no <c>default</c>, <c>default(T)</c>, <c>new T()</c> or <c>new()</c> of a struct value
/// object or of a struct marked <c>[RequireCustomInitialization]</c>.
/// </summary>
/// <remarks>
/// A default value object wraps <c>default(T)</c> without passing any factory, so a validated value
/// object would hold a value its rules never accepted. A default <c>Result</c> is neither a success
/// nor an error. Code inside the type itself is exempt, since its factories have to construct it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForbiddenDefaultInitializationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ForbiddenDefaultInitialization,
        title: "Forbidden default initialization",
        messageFormat: "{0}",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "This type must be created through one of its factories. Its default value, which a parameterless constructor also produces, is not a valid instance."
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
                node => AnalyzeNode(node, symbols),
                SyntaxKind.ObjectCreationExpression,         // new OrderId()
                SyntaxKind.ImplicitObjectCreationExpression, // OrderId id = new();
                SyntaxKind.DefaultExpression,                // default(OrderId)
                SyntaxKind.DefaultLiteralExpression          // OrderId id = default;
            );
        });
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var targetType = context.Node switch
        {
            ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 }  => null,
            ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } => null,
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or DefaultExpressionSyntax
                => context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type,
            LiteralExpressionSyntax
                => context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).ConvertedType,
            _ => null
        };

        // A class has a legitimate null; Nullable<T> of a value object is a null, not an instance. A
        // type parameter with new() and no class constraint is a struct wherever it is a value
        // object, since a generated class has no public parameterless constructor.
        if (targetType is not ({ IsValueType: true } or ITypeParameterSymbol { HasConstructorConstraint: true, IsReferenceType: false })) return;

        var containingType = context.SemanticModel.GetEnclosingSymbol(context.Node.SpanStart, context.CancellationToken)?.ContainingType;
        if (SymbolEqualityComparer.Default.Equals(containingType?.OriginalDefinition, targetType.OriginalDefinition)) return;

        if (Restriction(targetType, symbols) is not { } message) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), message));
    }

    private static string? Restriction(ITypeSymbol type, TypeKitSymbols symbols)
    {
        if (symbols.FindRequireCustomInitialization(type) is { } attribute)
        {
            return attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string { Length: > 0 } customMessage
                       ? $"Invalid initialization of '{type.Name}': {customMessage}"
                       : $"The type '{type.Name}' forbids default initialization";
        }

        if (symbols.FindMarker(type, out var validated) is null) return null;

        return validated
                   ? $"The value object '{type.Name}' must be created with '{type.Name}.Create', 'TryFrom' or 'FromKnownGood', not as a default instance"
                   : $"The value object '{type.Name}' must be created with '{type.Name}.From', not as a default instance";
    }
}
