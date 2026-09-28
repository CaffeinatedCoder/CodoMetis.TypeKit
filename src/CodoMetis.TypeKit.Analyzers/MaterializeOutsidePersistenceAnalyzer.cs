using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0004: <c>Materialize</c>, which rebuilds a value object without applying its rules, called
/// in application code.
/// </summary>
/// <remarks>
/// <para>
/// The validation-free path exists for values the application stored itself, and the EF Core
/// satellite is its one caller. That call is compiled into the satellite, where a consumer's
/// analyzer never looks, and into the compiled model and precompiled queries EF generates in the
/// application, which are generated code and not reported (<see cref="GeneratedCode"/>). Anything
/// else that calls it hands input past <c>Create</c>.
/// </para>
/// <para>
/// A call and a method-group reference both count, inside an expression tree too: a hand-written
/// <c>HasConversion(…, v =&gt; ValueObjectConverter&lt;…&gt;.Materialize(v))</c> is the same bypass
/// as a direct call.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MaterializeOutsidePersistenceAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MaterializeOutsidePersistence,
        title: "Value object rebuilt without validation",
        messageFormat: "Materialize rebuilds '{0}' without applying its rules. It is for values the application stored itself, and only the EF Core satellite calls it. Create the value through its factories instead.",
        category: "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IValueObjectMaterializer.Materialize and ValueObjectConverter.Materialize skip Create, so that a rule added later cannot make stored rows unreadable. Called on anything else, they let input past the value object's rules. EF Core's generated compiled model is generated code and is not reported.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.MaterializeOutsidePersistence)
    );

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analysed for Razor, and reported only where it maps to markup: EF's
        // compiled model, which calls the converter, stays unreported.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCode.AnalysisFlags);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (TypeKitSymbols.Resolve(start.Compilation) is not { } symbols) return;

            start.RegisterOperationAction(
                operation => Analyze(operation, ((IInvocationOperation)operation.Operation).TargetMethod, symbols),
                OperationKind.Invocation);

            start.RegisterOperationAction(
                operation => Analyze(operation, ((IMethodReferenceOperation)operation.Operation).Method, symbols),
                OperationKind.MethodReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, IMethodSymbol method, TypeKitSymbols symbols)
    {
        if (!symbols.IsMaterialize(method, out var valueObject)) return;

        // A value object declared by hand may call its own Materialize, as its factories construct it.
        if (valueObject is INamedTypeSymbol owner && IsInside(context.ContainingSymbol, owner)) return;

        context.Report(Diagnostic.Create(
            Rule,
            context.Operation.Syntax.GetLocation(),
            valueObject?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? method.ContainingType.Name));
    }

    private static bool IsInside(ISymbol? symbol, INamedTypeSymbol type)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, type.OriginalDefinition)) return true;
        }

        return false;
    }
}
