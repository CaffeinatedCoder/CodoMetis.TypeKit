using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0005: an array or span of a struct that forbids <c>default</c>, created with a length, so
/// every element starts as a <c>default</c> instance.
/// </summary>
/// <remarks>
/// CMTK0001 sees <c>default</c> written out, and none of these forms writes it: <c>new OrderId[n]</c>,
/// <c>stackalloc OrderId[n]</c>, <c>GC.AllocateUninitializedArray&lt;OrderId&gt;(n)</c>,
/// <c>GC.AllocateArray</c> and <c>Array.Resize</c> (measured 2026-09-28), which adds default slots
/// whenever the new size is larger, and the rule cannot tell whether it is. An array with initial
/// elements, a constant length or size of zero and a collection expression start with no default
/// element and are not reported. A warning, since filling such an array in a loop straight after is
/// correct code the rule cannot tell apart.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultFilledCollectionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.DefaultFilledCollection,
        title: "Collection filled with default instances",
        messageFormat: "{0} fills every slot it creates with a default '{1}', which passed no factory. Create the elements from values instead, for instance with a collection expression or Select(...).ToArray().",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A default value object, Option or Result is not a valid instance, and CMTK0001 only sees 'default' written out. An array or span created with a length holds nothing else until every element is assigned.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.DefaultFilledCollection)
    );

    private static readonly ImmutableHashSet<string> AllocatingMethods = ["AllocateUninitializedArray", "AllocateArray"];

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

            var gc    = start.Compilation.GetTypeByMetadataName("System.GC");
            var array = start.Compilation.GetTypeByMetadataName("System.Array");

            start.RegisterOperationAction(operation => AnalyzeArrayCreation(operation, symbols), OperationKind.ArrayCreation);
            start.RegisterOperationAction(operation => AnalyzeInvocation(operation, symbols, gc, array), OperationKind.Invocation);

            // IOperation has no public node for stackalloc; the syntax is read instead.
            start.RegisterSyntaxNodeAction(node => AnalyzeStackAlloc(node, symbols), SyntaxKind.StackAllocArrayCreationExpression);
        });
    }

    private static void AnalyzeArrayCreation(OperationAnalysisContext context, TypeKitSymbols symbols)
    {
        var creation = (IArrayCreationOperation)context.Operation;

        if (creation.Initializer is not null || creation.Type is not IArrayTypeSymbol { ElementType: var element }) return;
        if (creation.DimensionSizes.All(size => size.ConstantValue is { HasValue: true, Value: 0 })) return;

        if (Diagnose(context.ContainingSymbol, creation.Syntax, "An array created with a length", element, symbols) is { } diagnostic) context.Report(diagnostic);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, TypeKitSymbols symbols, INamedTypeSymbol? gc, INamedTypeSymbol? array)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method     = invocation.TargetMethod;

        if (method.TypeArguments.Length != 1) return;

        string? form = null;
        var     size = -1;

        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, gc) && AllocatingMethods.Contains(method.Name))
        {
            form = $"GC.{method.Name}";
            size = 0; // (int length, bool pinned = false)
        }
        else if (SymbolEqualityComparer.Default.Equals(method.ContainingType, array) && method.Name == "Resize")
        {
            form = "Array.Resize";
            size = 1; // (ref T[]? array, int newSize)
        }

        if (form is null) return;

        // A length of zero creates no slot, as new T[0] does not.
        if (invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == size)?.Value.ConstantValue is { HasValue: true, Value: 0 }) return;

        if (Diagnose(context.ContainingSymbol, invocation.Syntax, form, method.TypeArguments[0], symbols) is { } diagnostic) context.Report(diagnostic);
    }

    private static void AnalyzeStackAlloc(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var stackAlloc = (StackAllocArrayCreationExpressionSyntax)context.Node;

        if (stackAlloc.Initializer is not null || stackAlloc.Type is not ArrayTypeSyntax arrayType) return;

        var sizes = arrayType.RankSpecifiers.SelectMany(rank => rank.Sizes);
        if (sizes.All(size => context.SemanticModel.GetConstantValue(size, context.CancellationToken) is { HasValue: true, Value: 0 })) return;

        if (context.SemanticModel.GetTypeInfo(arrayType.ElementType, context.CancellationToken).Type is not { } element) return;

        if (Diagnose(context.ContainingSymbol, stackAlloc, "A stackalloc with a length", element, symbols) is { } diagnostic) context.Report(diagnostic);
    }

    private static Diagnostic? Diagnose(ISymbol? containingSymbol, SyntaxNode node, string form, ITypeSymbol element, TypeKitSymbols symbols)
    {
        if (!symbols.IsNoDefaultStruct(element)) return null;

        // Inside the type itself, as CMTK0001 exempts it: its own code knows how it fills the slots.
        for (var current = containingSymbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, element.OriginalDefinition)) return null;
        }

        return Diagnostic.Create(Rule, node.GetLocation(), form, element.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }
}
