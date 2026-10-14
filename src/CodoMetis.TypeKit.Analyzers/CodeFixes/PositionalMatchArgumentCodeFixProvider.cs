using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// The fix for CMTK0012: names every argument of a closed set's <c>Match</c> after the parameter it
/// is bound to now, so the call means what it meant and no longer depends on the members' order.
/// </summary>
/// <remarks>
/// An argument by position after a named one is in its own position, or the call would not compile,
/// so an argument's index is its parameter's.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PositionalMatchArgumentCodeFixProvider))]
[Shared]
public sealed class PositionalMatchArgumentCodeFixProvider : CodeFixProvider
{
    /// <summary>The rule this fixes; held to the analyzer's id by its tests.</summary>
    public const string DiagnosticId = "CMTK0012";

    /// <summary>The diagnostic property carrying the parameter names; held to the analyzer's by its tests.</summary>
    public const string NamesProperty = "Names";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticId];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(NamesProperty, out var joined) || joined is null) continue;
            if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocation) continue;

            var names = joined.Split(',');
            if (names.Length != invocation.ArgumentList.Arguments.Count) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Name every argument of Match",
                    cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation.ArgumentList, Named(invocation.ArgumentList, names)))),
                    equivalenceKey: DiagnosticId),
                diagnostic);
        }
    }

    private static ArgumentListSyntax Named(ArgumentListSyntax list, string[] names)
    {
        var arguments = list.Arguments.Select((argument, index) => argument.NameColon is not null ? argument : Named(argument, names[index]));
        return list.WithArguments(SyntaxFactory.SeparatedList(arguments, list.Arguments.GetSeparators()));
    }

    /// <summary>The argument with <c>name:</c> before it, in the argument's place: what led the argument now leads the name.</summary>
    private static ArgumentSyntax Named(ArgumentSyntax argument, string name)
    {
        var identifier = SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? SyntaxFactory.Identifier(name)
            : SyntaxFactory.Identifier(SyntaxTriviaList.Empty, SyntaxKind.IdentifierToken, "@" + name, name, SyntaxTriviaList.Empty);

        var nameColon = SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(identifier), SyntaxFactory.Token(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space))
                                     .WithLeadingTrivia(argument.GetLeadingTrivia());

        return argument.WithoutLeadingTrivia().WithNameColon(nameColon);
    }
}
