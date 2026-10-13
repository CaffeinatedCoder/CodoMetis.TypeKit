using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0011: a concrete type derives from or implements a type marked
/// <c>[RequireParameterlessConstructor]</c>, directly or through another, and has no parameterless
/// constructor of the accessibility it asks for.
/// </summary>
/// <remarks>
/// The check is the feature: a type the host creates by reflection fails at run time, in the host,
/// when it lacks the constructor. The rule puts that error on the type, in whichever project
/// declares it. A class that declares no constructor has a public one and is silent for
/// <c>Public</c>; a wrong accessibility is reported at the constructor, so the fix changes it there.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequiredParameterlessConstructorAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The property carrying the required accessibility, as <see cref="Accessibility"/> spells it, for the code fix.</summary>
    public const string RequiredProperty = "Required";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.RequiredParameterlessConstructor,
        title: "Parameterless constructor is required",
        messageFormat: "'{0}' {1}, but {2} requires one that is {3}",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A type marked [RequireParameterlessConstructor] requires every concrete type that derives from or implements it to have a parameterless constructor of the given accessibility, because the host creates such types by reflection.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.RequiredParameterlessConstructor)
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
            if (TypeKitSymbols.Resolve(start.Compilation) is not { HasRequireParameterlessConstructor: true } symbols) return;

            start.RegisterSymbolAction(symbol => AnalyzeType(symbol, symbols), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, TypeKitSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Only a type the host could create: an interface, an abstract or static type is never
        // constructed, and carries the requirement for its descendants.
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract || type.IsStatic || type.IsAnonymousType) return;

        var parameterless = type.InstanceConstructors.FirstOrDefault(constructor => constructor.Parameters.IsEmpty);
        var reported      = new HashSet<Accessibility>();

        foreach (var (source, required) in symbols.RequiredParameterlessConstructors(type))
        {
            if (parameterless?.DeclaredAccessibility == required || !reported.Add(required)) continue;

            var state = parameterless is null                   ? "declares no parameterless constructor"
                      : parameterless.IsImplicitlyDeclared      ? "has the implicit public parameterless constructor"
                      :                                           $"declares a {Spell(parameterless.DeclaredAccessibility)} parameterless constructor";

            // At the constructor where one is declared, so the fix changes it there; at the type
            // otherwise, where the fix adds one.
            ISymbol subject = parameterless is { IsImplicitlyDeclared: false } ? parameterless : type;
            var location = subject.Locations.FirstOrDefault(candidate => GeneratedCode.IsReportable(candidate, context.IsGeneratedCode, context.Compilation, context.CancellationToken))
                        ?? subject.Locations[0];

            context.Report(Diagnostic.Create(
                Rule,
                location,
                ImmutableDictionary<string, string?>.Empty.Add(RequiredProperty, required.ToString()),
                type.Name,
                state,
                source.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Spell(required)));
        }
    }

    /// <summary>The accessibility as C# writes it.</summary>
    public static string Spell(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public               => "public",
        Accessibility.Internal             => "internal",
        Accessibility.Protected            => "protected",
        Accessibility.ProtectedOrInternal  => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _                                  => "private",
    };
}
