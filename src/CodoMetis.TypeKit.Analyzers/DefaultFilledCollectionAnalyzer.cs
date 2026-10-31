using System.Collections.Generic;
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
/// whenever the new size is larger, and the rule cannot tell whether it is. Also
/// <c>Array.CreateInstance(typeof(OrderId), n)</c>, <c>Array.CreateInstanceFromArrayType(typeof(OrderId[]), n)</c>
/// and <c>ArrayPool&lt;OrderId&gt;.Rent(n)</c> (#228): the first two only where a <c>typeof</c> names the element,
/// since a <c>Type</c> variable could be anything, and a rented array is reported as a fresh one, all
/// default, though a reused one holds what it was returned with. An array with initial
/// elements, a constant length or size of zero in any one dimension and a collection expression start
/// with no default element and are not reported. A warning, since filling such an array in a loop
/// straight after is correct code the rule cannot tell apart.
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
            var pool  = start.Compilation.GetTypeByMetadataName("System.Buffers.ArrayPool`1");

            start.RegisterOperationAction(operation => AnalyzeArrayCreation(operation, symbols), OperationKind.ArrayCreation);
            start.RegisterOperationAction(operation => AnalyzeInvocation(operation, symbols, gc, array, pool), OperationKind.Invocation);

            // IOperation has no public node for stackalloc; the syntax is read instead.
            start.RegisterSyntaxNodeAction(node => AnalyzeStackAlloc(node, symbols), SyntaxKind.StackAllocArrayCreationExpression);
        });
    }

    private static void AnalyzeArrayCreation(OperationAnalysisContext context, TypeKitSymbols symbols)
    {
        var creation = (IArrayCreationOperation)context.Operation;

        if (creation.Initializer is not null || creation.Type is not IArrayTypeSymbol { ElementType: var element }) return;
        // Any dimension of zero leaves the array with no element: new T[0, n] holds none.
        if (creation.DimensionSizes.Any(size => IsZero(size.ConstantValue))) return;

        if (Diagnose(context.ContainingSymbol, creation.Syntax, "An array created with a length", element, symbols) is { } diagnostic) context.Report(diagnostic);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, TypeKitSymbols symbols, INamedTypeSymbol? gc, INamedTypeSymbol? array, INamedTypeSymbol? arrayPool)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method     = invocation.TargetMethod;

        string?      form    = null;
        ITypeSymbol? element = null;

        if (method.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(method.ContainingType, gc) && AllocatingMethods.Contains(method.Name))
        {
            // (int length, bool pinned = false)
            form    = $"GC.{method.Name}";
            element = method.TypeArguments[0];
        }
        else if (method.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(method.ContainingType, array) && method.Name == "Resize")
        {
            // (ref T[]? array, int newSize)
            form    = "Array.Resize";
            element = method.TypeArguments[0];
        }
        else if (SymbolEqualityComparer.Default.Equals(method.ContainingType, array) && method.Name is "CreateInstance" or "CreateInstanceFromArrayType")
        {
            // (Type elementType or arrayType, the lengths: one to three ints, int[] or long[], then int[] lowerBounds or nothing).
            // Only a typeof names the element; a Type held in a variable could be anything.
            var type = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
            while (type is IConversionOperation conversion) type = conversion.Operand;

            form    = $"Array.{method.Name}";
            element = (type as ITypeOfOperation)?.TypeOperand;
            if (method.Name == "CreateInstanceFromArrayType") element = (element as IArrayTypeSymbol)?.ElementType;
        }
        else if (method.Name == "Rent" && PoolOf(method, arrayPool) is { } pool)
        {
            // (int minimumLength): a fresh array from the pool holds nothing but default slots.
            form    = "ArrayPool.Rent";
            element = pool.TypeArguments[0];
        }

        if (form is null || element is null) return;

        // A length of zero in any dimension creates no slot, as new T[0] and new T[0, n] do not.
        if (Lengths(invocation).Any(length => IsZero(length.ConstantValue))) return;

        if (Diagnose(context.ContainingSymbol, invocation.Syntax, form, element, symbols) is { } diagnostic) context.Report(diagnostic);
    }

    /// <summary>The <c>ArrayPool&lt;T&gt;</c> whose <c>Rent</c> <paramref name="method"/> is or overrides.</summary>
    private static INamedTypeSymbol? PoolOf(IMethodSymbol method, INamedTypeSymbol? arrayPool)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(current.ContainingType.OriginalDefinition, arrayPool)) return current.ContainingType;
        }

        return null;
    }

    /// <summary>
    /// The length arguments of an allocating call: every <c>int</c> or <c>long</c> after the element, and the
    /// elements of a <c>params</c> or written-out array of lengths, but not the lower bounds.
    /// </summary>
    private static IEnumerable<IOperation> Lengths(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not { } parameter || parameter.Name == "lowerBounds" || parameter.RefKind != RefKind.None) continue;

            switch (parameter.Type)
            {
                case { SpecialType: SpecialType.System_Int32 or SpecialType.System_Int64 }:
                    yield return argument.Value;
                    break;

                case IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 or SpecialType.System_Int64 }
                    when argument.Value is IArrayCreationOperation { Initializer: { } lengths }:
                    foreach (var length in lengths.ElementValues) yield return length;
                    break;
            }
        }
    }

    private static void AnalyzeStackAlloc(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var stackAlloc = (StackAllocArrayCreationExpressionSyntax)context.Node;

        if (stackAlloc.Initializer is not null || stackAlloc.Type is not ArrayTypeSyntax arrayType) return;

        var sizes = arrayType.RankSpecifiers.SelectMany(rank => rank.Sizes);
        if (sizes.Any(size => IsZero(context.SemanticModel.GetConstantValue(size, context.CancellationToken)))) return;

        if (context.SemanticModel.GetTypeInfo(arrayType.ElementType, context.CancellationToken).Type is not { } element) return;

        if (Diagnose(context.ContainingSymbol, stackAlloc, "A stackalloc with a length", element, symbols) is { } diagnostic) context.Report(diagnostic);
    }

    /// <summary>
    /// A constant zero of any integral type: a dimension may be a <c>long</c>, <c>uint</c> or <c>ulong</c>,
    /// and a size read from syntax keeps its type before the conversion to <c>int</c>, <c>(byte)0</c> or <c>'\0'</c>.
    /// </summary>
    private static bool IsZero(Optional<object?> constant) =>
        constant.HasValue && constant.Value switch
        {
            int value    => value == 0,
            long value   => value == 0,
            uint value   => value == 0,
            ulong value  => value == 0,
            short value  => value == 0,
            ushort value => value == 0,
            byte value   => value == 0,
            sbyte value  => value == 0,
            char value   => value == 0,
            _            => false,
        };

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
