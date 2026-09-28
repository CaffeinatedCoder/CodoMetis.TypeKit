using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
/// <para>
/// A default value object wraps <c>default(T)</c> without passing any factory, so a validated value
/// object would hold a value its rules never accepted. A default <c>Result</c> is neither a success
/// nor an error. Code inside the type itself is exempt, since its factories have to construct it.
/// </para>
/// <para>
/// A default compared with <c>==</c> or <c>!=</c>, or passed to a call whose name says it compares
/// (<see cref="IsComparisonName"/>), is a guard or an assertion, not an instance anyone keeps:
/// <c>if (id == default)</c>, <c>ArgumentOutOfRangeException.ThrowIfEqual(id, default)</c> and
/// <c>Assert.NotEqual(default, id)</c> are the only defence against the defaults this rule cannot
/// see, so they are not reported.
/// </para>
/// <para>
/// A <c>default</c> assigned to an <c>out</c> parameter of a method that returns <c>bool</c> is the
/// Try pattern, <c>bool TryFind(string key, out OrderId id) { id = default; return false; }</c>, as
/// <c>int.TryParse</c> writes it: the caller reads the parameter only when the method returned true.
/// It is not reported. The rule does not follow which value the method returns.
/// </para>
/// <para>
/// In a project that declares value objects, Metalama runs analyzers on the source before weaving,
/// where <c>cond ? OrderId.From(g) : default</c> does not bind and gives the literal no type. The
/// target type is then taken from outside: through parentheses, <c>!</c>, a conditional, a switch
/// arm or a collection element, to the type the whole expression converts to, which is the declared
/// type of what it initializes, is assigned to or returns. A local declared with <c>var</c> from a
/// generated member, as in <c>var id = OrderId.New(); id = default;</c>, has no type there to take,
/// and stays unreported in that project.
/// </para>
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
        description: "This type must be created through one of its factories. Its default value, which a parameterless constructor also produces, is not a valid instance.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.ForbiddenDefaultInitialization)
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
        // Cheapest first: syntax, then the node's type, then the cached verdict on that type, and only
        // for a restricted type the enclosing symbol, which is the expensive part.
        if (context.Node is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } || IsComparedOperand(context.Node)) return;

        var model      = context.SemanticModel;
        var targetType = context.Node is LiteralExpressionSyntax
                             ? TargetTypeOfLiteral(context.Node, model, context.CancellationToken)
                             : model.GetTypeInfo(context.Node, context.CancellationToken).Type;

        if (targetType is null || symbols.DefaultRestriction(targetType) is not { } message) return;
        if (IsAssignedToTryOut(context.Node, model, context.CancellationToken)) return;

        var containingType = model.GetEnclosingSymbol(context.Node.SpanStart, context.CancellationToken)?.ContainingType;
        if (SymbolEqualityComparer.Default.Equals(containingType?.OriginalDefinition, targetType.OriginalDefinition)) return;

        context.Report(Diagnostic.Create(Rule, context.Node.GetLocation(), message));
    }

    /// <summary>
    /// An operand of <c>==</c> or <c>!=</c>, or an argument of a call whose name says it compares
    /// (<see cref="IsComparisonName"/>): <c>id.Equals(default)</c>,
    /// <c>EqualityComparer&lt;OrderId&gt;.Default.Equals(id, default)</c>,
    /// <c>ArgumentOutOfRangeException.ThrowIfEqual(id, default)</c>, <c>Assert.NotEqual(default, id)</c>,
    /// <c>id.ShouldNotBe(default)</c>. The default itself is the argument, not a part of one.
    /// </summary>
    private static bool IsComparedOperand(SyntaxNode node)
    {
        var current = node;
        while (current.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax) current = current.Parent;

        return current.Parent switch
        {
            BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression),
            ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } => IsComparisonName(InvokedName(invocation)),
            _ => false
        };
    }

    /// <summary>
    /// Whether a method's name says it compares its arguments: one of its words is <c>Equal</c>,
    /// <c>Equals</c> or <c>Compare</c>, or its last word is <c>Be</c>. A word starts at an upper-case
    /// letter.
    /// </summary>
    /// <remarks>
    /// That covers <c>Equals</c>, <c>ReferenceEquals</c>, the <c>ThrowIfEqual</c> and
    /// <c>ThrowIfNotEqual</c> guards, <c>CompareTo</c> and a comparer's <c>Compare</c>, and the
    /// assertions of xUnit (<c>Equal</c>, <c>NotEqual</c>), MSTest (<c>AreEqual</c>, <c>AreNotEqual</c>),
    /// NUnit (<c>EqualTo</c>) and Shouldly (<c>ShouldBe</c>, <c>ShouldNotBe</c>). A name that only
    /// contains the letters, such as <c>WithEquality</c> or <c>Maybe</c>, is not one.
    /// </remarks>
    private static bool IsComparisonName(string? name)
    {
        if (name is null) return false;

        for (var start = 0; start < name.Length;)
        {
            var end = start + 1;
            while (end < name.Length && !char.IsUpper(name[end])) end++;

            if (IsWord(name, start, end, "Equal") || IsWord(name, start, end, "Equals") || IsWord(name, start, end, "Compare")
             || end == name.Length && IsWord(name, start, end, "Be"))
                return true;

            start = end;
        }

        return false;
    }

    private static bool IsWord(string name, int start, int end, string word) =>
        end - start == word.Length && string.CompareOrdinal(name, start, word, 0, word.Length) == 0;

    /// <summary>
    /// A <c>default</c> or <c>default(T)</c> that is the value assigned to an <c>out</c> parameter of a
    /// method, local function or lambda that returns <c>bool</c>: the Try pattern. It may be reached
    /// through parentheses, <c>!</c>, a conditional's branch or a switch arm, as in
    /// <c>id = found ? OrderId.From(g) : default;</c>. A parameterless <c>new()</c> is not the pattern's
    /// form and stays reported, and so does a <c>default</c> kept anywhere else first.
    /// </summary>
    private static bool IsAssignedToTryOut(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        if (node is not (LiteralExpressionSyntax or DefaultExpressionSyntax)) return false;

        var current = node;
        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax or PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression }:
                case ConditionalExpressionSyntax conditional when conditional.Condition != current:
                    current = current.Parent;
                    continue;

                case SwitchExpressionArmSyntax arm when arm.Expression == current:
                    current = arm.Parent!;
                    continue;
            }

            break;
        }

        return current.Parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment
            && assignment.Right == current
            && model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is IParameterSymbol
               {
                   RefKind: RefKind.Out,
                   ContainingSymbol: IMethodSymbol { ReturnType.SpecialType: SpecialType.System_Boolean }
               };
    }

    private static string? InvokedName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access   => access.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            SimpleNameSyntax name                 => name.Identifier.ValueText,
            _                                     => null
        };

    /// <summary>
    /// The type a <c>default</c> literal converts to, or, where that does not bind, the type the
    /// whole expression around it converts to: the declared type of what it initializes, is assigned
    /// to or returns.
    /// </summary>
    private static ITypeSymbol? TargetTypeOfLiteral(SyntaxNode literal, SemanticModel model, CancellationToken cancellationToken)
    {
        if (ConvertedType(literal, model, cancellationToken) is { } converted) return converted;

        for (var current = literal; ;)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax or PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression }:
                case ConditionalExpressionSyntax conditional when conditional.Condition != current:
                    current = current.Parent;
                    break;

                case SwitchExpressionArmSyntax arm when arm.Expression == current:
                    current = arm.Parent!;
                    break;

                // [OrderId.New(), default]: the element of whatever the collection becomes.
                case ExpressionElementSyntax { Parent: CollectionExpressionSyntax collection }:
                    return ElementType(ConvertedType(collection, model, cancellationToken), model.Compilation);

                default:
                    return null;
            }

            if (ConvertedType(current, model, cancellationToken) is { } outer) return outer;
        }
    }

    private static ITypeSymbol? ConvertedType(SyntaxNode value, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetTypeInfo(value, cancellationToken).ConvertedType is { } converted && IsResolved(converted) ? converted : null;

    /// <summary>
    /// The element type of a collection expression's target: an array's element, or what the type
    /// enumerates, through <c>IEnumerable&lt;T&gt;</c> or the <c>GetEnumerator()</c> pattern (<c>Span&lt;T&gt;</c>).
    /// </summary>
    private static ITypeSymbol? ElementType(ITypeSymbol? collection, Compilation compilation)
    {
        switch (collection)
        {
            case null:
                return null;

            case IArrayTypeSymbol array:
                return array.ElementType;
        }

        var enumerable = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        var implemented = collection.OriginalDefinition.Equals(enumerable, SymbolEqualityComparer.Default)
                              ? (INamedTypeSymbol)collection
                              : collection.AllInterfaces.FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, enumerable));

        if (implemented is not null) return implemented.TypeArguments[0];

        var enumerator = collection.GetMembers("GetEnumerator").OfType<IMethodSymbol>().FirstOrDefault(method => method.Parameters.IsEmpty)?.ReturnType;
        return enumerator?.GetMembers("Current").OfType<IPropertySymbol>().FirstOrDefault()?.Type;
    }

    private static bool IsResolved(ITypeSymbol? type) => type is not null and not IErrorTypeSymbol;
}
