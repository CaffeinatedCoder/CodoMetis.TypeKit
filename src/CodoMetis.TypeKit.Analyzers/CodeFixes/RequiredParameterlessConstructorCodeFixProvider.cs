using System.Collections.Generic;
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
using Microsoft.CodeAnalysis.Text;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// The fix for CMTK0011: writes the parameterless constructor the type lacks, or changes the
/// accessibility of the one it declares.
/// </summary>
/// <remarks>
/// <para>
/// A type with a primary constructor, in any of its parts, gets no fix: every other constructor of
/// such a type chains to the primary one (CS8862), and the arguments to chain with are the author's to
/// choose. Nor does a type whose base <c>base()</c> does not reach, which the added constructor calls
/// implicitly: a base with only constructors that take arguments (CS7036), one the type cannot access
/// (CS0122), or two that <c>base()</c> could each be (CS0121). Nor does a struct, which the rule never
/// reports: its parameterless constructor can only be public.
/// </para>
/// <para>
/// The fix writes its own trivia and asks no formatter, which would lay out the whole type again: the
/// type's and the constructor's doc comments, attributes and comments stay where they are, the members
/// keep their layout, and the lines it adds are indented as the members are and end as the file's do.
/// </para>
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
            else if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } type
                  && await CanAddConstructorAsync(context.Document, type, context.CancellationToken).ConfigureAwait(false))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Add {(required == Accessibility.Internal ? "an" : "a")} {spelling} parameterless constructor",
                        cancellationToken => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(type, WithConstructor(type, required, root.SyntaxTree.GetText(cancellationToken))))),
                        equivalenceKey: $"{DiagnosticId}:add"),
                    diagnostic);
            }
        }
    }

    /// <summary>
    /// Whether a parameterless constructor added to the type compiles, or whether that cannot be told: a
    /// fix withheld is better than one that does not compile. It does not where any part of the type
    /// declares a primary constructor, nor where its implicit <c>base()</c> binds to nothing.
    /// </summary>
    private static async Task<bool> CanAddConstructorAsync(Document document, TypeDeclarationSyntax type, CancellationToken cancellationToken)
    {
        if (type.ParameterList is not null) return false;

        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(type, cancellationToken) is not INamedTypeSymbol symbol) return false;

        return !symbol.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax { ParameterList: not null })
            && BaseCallBinds(symbol, model.Compilation);
    }

    /// <summary>
    /// Whether <c>base()</c> in a constructor of <paramref name="type"/> binds: to the base's one
    /// accessible parameterless constructor, or, without one, to its one accessible constructor whose
    /// parameters are all optional or a <c>params</c> array. Two of the second kind are ambiguous.
    /// </summary>
    private static bool BaseCallBinds(INamedTypeSymbol type, Compilation compilation)
    {
        if (type.BaseType is not { SpecialType: not SpecialType.System_Object } baseType) return true;

        var reachable = baseType.InstanceConstructors.Where(constructor => compilation.IsSymbolAccessibleWithin(constructor, type)).ToList();
        if (reachable.Any(constructor => constructor.Parameters.IsEmpty)) return true;

        return reachable.Count(constructor => constructor.Parameters.All(parameter => parameter.IsOptional || parameter.IsParams)) == 1;
    }

    /// <summary>
    /// Only the accessibility changes. It takes the place of the first accessibility keyword written,
    /// with what led it and what followed the last, comments included; where none is written, it goes
    /// before the other modifiers, or before the name, after the attributes, taking what led there.
    /// </summary>
    private static ConstructorDeclarationSyntax WithAccessibility(ConstructorDeclarationSyntax constructor, Accessibility required)
    {
        var modifiers = constructor.Modifiers;
        var written   = modifiers.Where(IsAccessibility).ToList();

        if (written.Count == 0)
        {
            if (modifiers.Count == 0)
            {
                return constructor.WithModifiers(SyntaxFactory.TokenList(Keywords(required, constructor.Identifier.LeadingTrivia, SyntaxFactory.TriviaList(SyntaxFactory.Space))))
                                  .WithIdentifier(constructor.Identifier.WithLeadingTrivia());
            }

            var leading = Keywords(required, modifiers[0].LeadingTrivia, SyntaxFactory.TriviaList(SyntaxFactory.Space));
            return constructor.WithModifiers(SyntaxFactory.TokenList(leading.Concat(modifiers.Select((modifier, index) => index == 0 ? modifier.WithLeadingTrivia() : modifier))));
        }

        // protected /* note */ internal: a comment after a keyword that goes is kept after the new ones.
        var last     = written[written.Count - 1];
        var trailing = SyntaxFactory.TriviaList(written.Take(written.Count - 1)
                                                       .SelectMany(keyword => keyword.TrailingTrivia.Any(IsComment) ? keyword.TrailingTrivia : SyntaxFactory.TriviaList())
                                                       .Concat(last.TrailingTrivia));
        var tokens = new List<SyntaxToken>();

        foreach (var modifier in modifiers)
        {
            if (modifier == written[0]) tokens.AddRange(Keywords(required, modifier.LeadingTrivia, trailing));
            else if (!IsAccessibility(modifier)) tokens.Add(modifier);
        }

        return constructor.WithModifiers(SyntaxFactory.TokenList(tokens));
    }

    /// <summary>
    /// The type with the constructor as its first member, laid out as the body it joins: on a body of
    /// several lines, on lines of its own, indented as the members are; on a body of one line, on that
    /// line; on a declaration ending in <c>;</c>, in a body on that line, after which the <c>;</c>'s
    /// trivia, a comment included, is kept.
    /// </summary>
    private static TypeDeclarationSyntax WithConstructor(TypeDeclarationSyntax type, Accessibility required, SourceText text)
    {
        var signature = $"{Spell(required)} {type.Identifier.Text}()";

        if (type.OpenBraceToken.IsKind(SyntaxKind.None) || type.OpenBraceToken.IsMissing)
        {
            var semicolon = type.SemicolonToken;
            var spaced    = semicolon.GetPreviousToken().TrailingTrivia.LastOrDefault().IsKind(SyntaxKind.WhitespaceTrivia);
            var open      = SyntaxFactory.Token(spaced ? semicolon.LeadingTrivia : semicolon.LeadingTrivia.Add(SyntaxFactory.Space), SyntaxKind.OpenBraceToken, SyntaxFactory.TriviaList(SyntaxFactory.Space));
            var close     = SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.CloseBraceToken, semicolon.TrailingTrivia);

            return type.WithSemicolonToken(default)
                       .WithOpenBraceToken(open)
                       .WithMembers(SyntaxFactory.SingletonList(Constructor($"{signature} {{ }}", SyntaxFactory.TriviaList(), SyntaxFactory.TriviaList(SyntaxFactory.Space))))
                       .WithCloseBraceToken(close);
        }

        var end = type.OpenBraceToken.TrailingTrivia.LastOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        if (!end.IsKind(SyntaxKind.EndOfLineTrivia))
        {
            var lead = type.OpenBraceToken.TrailingTrivia.Count == 0 ? SyntaxFactory.TriviaList(SyntaxFactory.Space) : SyntaxFactory.TriviaList();
            return type.WithMembers(type.Members.Insert(0, Constructor($"{signature} {{ }}", lead, SyntaxFactory.TriviaList(SyntaxFactory.Space))));
        }

        // The line ending of the brace the constructor follows, so a CRLF file stays CRLF.
        var eol      = end.ToString();
        var indent   = TypeLayout.MemberIndentation(type, text);
        var leading  = indent.Length == 0 ? SyntaxFactory.TriviaList() : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent));
        var trailing = type.Members.Count > 0
            ? SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(eol), SyntaxFactory.EndOfLine(eol))
            : SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(eol));

        return type.WithMembers(type.Members.Insert(0, Constructor($"{signature}{eol}{indent}{{{eol}{indent}}}", leading, trailing)));
    }

    /// <summary>
    /// A constructor parsed from its text, so every token carries the trivia written there, and none
    /// is elastic trivia a code action's cleanup would lay out again; the trivia around it is given.
    /// </summary>
    private static MemberDeclarationSyntax Constructor(string source, SyntaxTriviaList leading, SyntaxTriviaList trailing) =>
        (SyntaxFactory.ParseMemberDeclaration(source) ?? throw new System.InvalidOperationException($"'{source}' does not parse as a constructor."))
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(trailing);

    /// <summary>The accessibility's keywords, the first led by <paramref name="leading"/>, the last followed by <paramref name="trailing"/>.</summary>
    private static IEnumerable<SyntaxToken> Keywords(Accessibility accessibility, SyntaxTriviaList leading, SyntaxTriviaList trailing)
    {
        var kinds = Modifiers(accessibility);

        for (var i = 0; i < kinds.Length; i++)
        {
            yield return SyntaxFactory.Token(
                i == 0 ? leading : SyntaxFactory.TriviaList(),
                kinds[i],
                i == kinds.Length - 1 ? trailing : SyntaxFactory.TriviaList(SyntaxFactory.Space));
        }
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

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

    private static SyntaxKind[] Modifiers(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public               => [SyntaxKind.PublicKeyword],
        Accessibility.Internal             => [SyntaxKind.InternalKeyword],
        Accessibility.Protected            => [SyntaxKind.ProtectedKeyword],
        Accessibility.ProtectedOrInternal  => [SyntaxKind.ProtectedKeyword, SyntaxKind.InternalKeyword],
        Accessibility.ProtectedAndInternal => [SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword],
        _                                  => [SyntaxKind.PrivateKeyword],
    };
}
