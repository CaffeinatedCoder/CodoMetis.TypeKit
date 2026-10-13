using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// The fix for CMTK0011: writes the parameterless constructor the type lacks, or changes the
/// accessibility of the one it declares.
/// </summary>
/// <remarks>
/// A type with a primary constructor gets no fix: every other constructor of such a type chains to
/// the primary one, and the arguments to chain with are the author's to choose. Nor does a struct,
/// which the rule never reports: its parameterless constructor can only be public.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequiredParameterlessConstructorCodeFixProvider))]
[Shared]
public sealed class RequiredParameterlessConstructorCodeFixProvider : CodeFixProvider
{
    /// <summary>The rule this fixes; held to the analyzer's id by its tests.</summary>
    public const string DiagnosticId = "CMTK0011";

    /// <summary>The diagnostic property carrying the required accessibility, as <see cref="Accessibility"/> spells it; held to the analyzer's by its tests.</summary>
    public const string RequiredProperty = "Required";

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
            if (!diagnostic.Properties.TryGetValue(RequiredProperty, out var requiredName)
             || !System.Enum.TryParse<Accessibility>(requiredName, out var required))
                continue;

            var node     = root.FindNode(diagnostic.Location.SourceSpan);
            var spelling = Spell(required);

            // A struct's parameterless constructor is public and can be nothing else (CS8958): the rule
            // reports none, and a fix that wrote another accessibility there would not compile.
            if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } declaring && declaring.Kind() is SyntaxKind.StructDeclaration or SyntaxKind.RecordStructDeclaration)
                continue;

            if (node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is { } constructor)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Make the constructor {spelling}",
                        cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(constructor, WithAccessibility(constructor, required)))),
                        equivalenceKey: $"{DiagnosticId}:change"),
                    diagnostic);
            }
            else if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { ParameterList: null } type)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Add {(required == Accessibility.Internal ? "an" : "a")} {spelling} parameterless constructor",
                        cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(type, WithConstructor(type, required)))),
                        equivalenceKey: $"{DiagnosticId}:add"),
                    diagnostic);
            }
        }
    }

    /// <summary>Only the modifiers change; the rest of the declaration keeps its own layout.</summary>
    private static ConstructorDeclarationSyntax WithAccessibility(ConstructorDeclarationSyntax constructor, Accessibility required)
    {
        var leading  = constructor.Modifiers.Count > 0 ? constructor.Modifiers[0].LeadingTrivia : constructor.GetLeadingTrivia();
        var kept     = constructor.Modifiers.Where(modifier => !IsAccessibility(modifier)).Select(modifier => modifier.WithLeadingTrivia());
        var replaced = Modifiers(required).Select(token => token.WithTrailingTrivia(SyntaxFactory.Space)).Concat(kept).ToArray();

        replaced[0] = replaced[0].WithLeadingTrivia(leading);

        return constructor.Modifiers.Count > 0
            ? constructor.WithModifiers(SyntaxFactory.TokenList(replaced))
            : constructor.WithModifiers(SyntaxFactory.TokenList(replaced)).WithIdentifier(constructor.Identifier.WithLeadingTrivia());
    }

    private static TypeDeclarationSyntax WithConstructor(TypeDeclarationSyntax type, Accessibility required)
    {
        var constructor = SyntaxFactory.ConstructorDeclaration(type.Identifier.WithoutTrivia())
                                       .WithModifiers(SyntaxFactory.TokenList(Modifiers(required)))
                                       .WithBody(SyntaxFactory.Block())
                                       .WithAdditionalAnnotations(Formatter.Annotation);

        // A declaration ending in ';' (a record with no body) gets braces to hold the constructor.
        var withBody = type.SemicolonToken.IsKind(SyntaxKind.SemicolonToken)
            ? type.WithSemicolonToken(default).WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken)).WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            : type;

        return withBody.WithMembers(withBody.Members.Insert(0, constructor)).WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static string Spell(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public               => "public",
        Accessibility.Internal             => "internal",
        Accessibility.Protected            => "protected",
        Accessibility.ProtectedOrInternal  => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _                                  => "private",
    };

    private static bool IsAccessibility(SyntaxToken token) =>
        token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.InternalKeyword)
     || token.IsKind(SyntaxKind.ProtectedKeyword) || token.IsKind(SyntaxKind.PrivateKeyword);

    private static SyntaxToken[] Modifiers(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public               => [SyntaxFactory.Token(SyntaxKind.PublicKeyword)],
        Accessibility.Internal             => [SyntaxFactory.Token(SyntaxKind.InternalKeyword)],
        Accessibility.Protected            => [SyntaxFactory.Token(SyntaxKind.ProtectedKeyword)],
        Accessibility.ProtectedOrInternal  => [SyntaxFactory.Token(SyntaxKind.ProtectedKeyword), SyntaxFactory.Token(SyntaxKind.InternalKeyword)],
        Accessibility.ProtectedAndInternal => [SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ProtectedKeyword)],
        _                                  => [SyntaxFactory.Token(SyntaxKind.PrivateKeyword)],
    };
}
