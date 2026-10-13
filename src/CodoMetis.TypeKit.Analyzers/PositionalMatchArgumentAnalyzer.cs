using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0012: a closed set's generated <c>Match</c> given an argument by position.
/// </summary>
/// <remarks>
/// <para>
/// <c>Match</c> takes one <c>Func&lt;TResult&gt;</c> per member, in declaration order, so every
/// parameter has the same type. An argument by position is bound to whichever member is declared
/// at that place: reorder two members, and every such call swaps their branches, compiling as before.
/// Named, an argument follows its member wherever it moves, and a member renamed or removed fails the
/// call. A call with every argument named is silent.
/// </para>
/// <para>
/// In the project that declares the set, Metalama runs analyzers before weaving, where the generated
/// <c>Match</c> does not exist and the call does not bind (<see cref="TypeKitSymbols.GeneratedMemberCall"/>).
/// The receiver binds, so an unbound call named <c>Match</c> on a closed set with one argument per
/// member is taken for the generated one, and the parameter names are the generators' rule applied to
/// the members: the field's name with its first letter lower-cased. A call that binds counts only when
/// it binds to the generated signature; an overload the set declares by hand is its own.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PositionalMatchArgumentAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic property carrying the parameter names in order, comma-separated, for the code fix.</summary>
    public const string NamesProperty = "Names";

    private const string Match = "Match";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.PositionalMatchArgument,
        title: "A closed set's Match given an argument by position",
        messageFormat: "'{0}.Match' is given an argument by position, which binds to whichever member is declared at that place: reorder the members and the branches swap without a word. Name every argument ({1}).",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every parameter of a closed set's Match has the same type, one delegate per member in declaration order, so the compiler cannot tell a swapped pair. A named argument follows its member, and fails the build when the member is renamed or removed.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.PositionalMatchArgument)
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
            if (start.Compilation.GetTypeByMetadataName("System.Func`1") is not { } func) return;

            start.RegisterSyntaxNodeAction(node => Analyze(node, symbols, func), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols, INamedTypeSymbol func)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var arguments  = invocation.ArgumentList.Arguments;

        if (arguments.All(argument => argument.NameColon is not null)) return;
        if (Receiver(invocation, context.SemanticModel, context.CancellationToken) is not { } set) return;
        if (set.TypeKind != TypeKind.Class || symbols.FindClosedSetMarker(set) is null) return;

        var members = Members(set);
        string[] names;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol bound)
        {
            if (!IsGeneratedMatch(bound.OriginalDefinition, set, members.Length, func)) return;
            names = [.. bound.Parameters.Select(parameter => parameter.Name)];
        }
        else
        {
            if (arguments.Count != members.Length) return;
            names = [.. members.Select(member => ParameterName(member.Name))];
        }

        var first = arguments.First(argument => argument.NameColon is null);
        context.Report(Diagnostic.Create(
            Rule,
            first.GetLocation(),
            ImmutableDictionary<string, string?>.Empty.Add(NamesProperty, string.Join(",", names)),
            set.Name,
            string.Join(", ", names.Select(name => Escaped(name) + ":"))));
    }

    /// <summary>
    /// The type <c>Match</c> is called on: the receiver of <c>x.Match(…)</c> or <c>x?.Match(…)</c>, or,
    /// for a simple name, the closed set the call is written in. A type as the receiver is not an
    /// instance, and <c>Match</c> is an instance method.
    /// </summary>
    private static INamedTypeSymbol? Receiver(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax access when access.Name.Identifier.ValueText == Match:
                if (model.GetSymbolInfo(access.Expression, cancellationToken).Symbol is ITypeSymbol) return null;
                return model.GetTypeInfo(access.Expression, cancellationToken).Type as INamedTypeSymbol;

            case MemberBindingExpressionSyntax binding when binding.Name.Identifier.ValueText == Match:
                return invocation.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>() is { } conditional
                    ? model.GetTypeInfo(conditional.Expression, cancellationToken).Type as INamedTypeSymbol
                    : null;

            case SimpleNameSyntax name when name.Identifier.ValueText == Match:
                return model.GetEnclosingSymbol(invocation.SpanStart, cancellationToken)?.ContainingType;

            default:
                return null;
        }
    }

    /// <summary>The members, as the generators find them: the public static readonly fields of the set's own type, in declaration order.</summary>
    private static IFieldSymbol[] Members(INamedTypeSymbol set) =>
        [.. set.GetMembers().OfType<IFieldSymbol>().Where(field => field is { IsStatic: true, IsReadOnly: true, DeclaredAccessibility: Accessibility.Public }
                                                                && SymbolEqualityComparer.Default.Equals(field.Type, set))];

    /// <summary>The generated signature: an instance <c>Match&lt;TResult&gt;</c> of the set itself with one <c>Func&lt;TResult&gt;</c> per member.</summary>
    private static bool IsGeneratedMatch(IMethodSymbol method, INamedTypeSymbol set, int memberCount, INamedTypeSymbol func) =>
        method is { Name: Match, IsStatic: false, TypeParameters.Length: 1 }
     && SymbolEqualityComparer.Default.Equals(method.ContainingType, set)
     && method.Parameters.Length == memberCount
     && method.Parameters.All(parameter => parameter.Type is INamedTypeSymbol type
                                        && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, func)
                                        && SymbolEqualityComparer.Default.Equals(type.TypeArguments[0], method.TypeParameters[0]));

    /// <summary>The generators' rule for a member's parameter: its name with the first letter lower-cased (ClosedSetTypes.ParameterName).</summary>
    internal static string ParameterName(string member) => char.ToLowerInvariant(member[0]) + member.Substring(1);

    /// <summary>A name as it is written at a call: a keyword needs <c>@</c>.</summary>
    internal static string Escaped(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
}
