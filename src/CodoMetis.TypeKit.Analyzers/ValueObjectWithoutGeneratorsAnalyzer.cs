using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0002: a type implements <c>IValue&lt;T&gt;</c> or <c>IValidatedValue&lt;,,&gt;</c>, but the
/// project does not reference CodoMetis.TypeKit.Generators, so nothing is generated for it.
/// </summary>
/// <remarks>
/// The generators apply through a transitive fabric, not through an attribute on the interfaces,
/// which keeps CodoMetis.TypeKit free of Metalama. The price is that a project which can see the
/// interfaces but not the generators compiles a value object with no field, no <c>Value</c> and no
/// factory, without any error. This rule is that error.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueObjectWithoutGeneratorsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ValueObjectWithoutGenerators,
        title: "Value object is not generated",
        messageFormat: "'{0}' implements {1}, but this project does not reference CodoMetis.TypeKit.Generators, so nothing is generated for it. Reference the CodoMetis.TypeKit.Generators package.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Value objects are generated at compile time by CodoMetis.TypeKit.Generators. Without it, a type that implements IValue<T> or IValidatedValue<,,> compiles, but has no field, no Value and no factory.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.ValueObjectWithoutGenerators)
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
            if (TypeKitSymbols.Resolve(start.Compilation) is not { ReferencesGenerators: false } symbols) return;

            start.RegisterSymbolAction(symbol => AnalyzeType(symbol, symbols), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, TypeKitSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Only types the generators would pick up: an interface or abstract type is never generated.
        if (type.TypeKind is not (TypeKind.Struct or TypeKind.Class) || type.IsAbstract || type.IsStatic) return;

        if (symbols.FindMarker(type, out _) is not { } marker) return;

        // A partial declared in several files is reported where it can be: in the user's file, or in
        // the markup a Razor component's generated part maps to.
        var location = type.Locations.FirstOrDefault(candidate => GeneratedCode.IsReportable(candidate, context.IsGeneratedCode, context.Compilation, context.CancellationToken))
                    ?? type.Locations[0];

        context.Report(Diagnostic.Create(
            Rule,
            location,
            type.Name,
            marker.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }
}
