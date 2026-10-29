using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
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
/// The fix for a validated value object that lacks <c>Create</c>, the one member of
/// <c>IValidatedValue&lt;TSelf, T, TFault&gt;</c> written by hand: the compiler's CS0535 at the
/// interface. It writes <c>Create</c> as a public static method that accepts every value, for the
/// author to add the rules to.
/// </summary>
/// <remarks>
/// <para>
/// Offered where the interface's <c>TSelf</c> is the type itself and no member of the type named
/// <c>Create</c> takes a <c>T</c>, which would clash with the one written. The types are spelled as
/// they are named at the type, so a fault or a wrapped type of another namespace is qualified, and
/// the body builds the instance with the private constructor the generators add.
/// </para>
/// <para>
/// Like the fix for CMTK0011 it writes its own trivia and asks no formatter: <c>Create</c> becomes the
/// first member, indented as the members are; a declaration ending in <c>;</c>, or an empty body on one
/// line, gets a body on lines of its own. A body on one line that holds members gets no fix.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCreateCodeFixProvider))]
[Shared]
public sealed class MissingCreateCodeFixProvider : CodeFixProvider
{
    /// <summary>The compiler's "does not implement interface member", which a missing <c>Create</c> is.</summary>
    public const string DiagnosticId = "CS0535";

    /// <summary>The metadata name of the validated value-object contract; held to the real type by its tests.</summary>
    public const string ValidatedValueMetadataName = "CodoMetis.TypeKit.ValueObjects.IValidatedValue`3";

    /// <summary>The member the fix writes; held to the contract's by its tests.</summary>
    public const string CreateName = "Create";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticId];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null) return;

        var validated = model.Compilation.GetTypesByMetadataName(ValidatedValueMetadataName);
        if (validated.IsEmpty) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            // One CS0535 per missing member, each at the base type that brings it: only the one for
            // Create, which the message names whatever the language.
            if (diagnostic.GetMessage(CultureInfo.InvariantCulture).IndexOf($".{CreateName}(", System.StringComparison.Ordinal) < 0) continue;

            var node = root.FindNode(diagnostic.Location.SourceSpan);
            if (node.FirstAncestorOrSelf<BaseTypeSyntax>() is not { } written) continue;
            if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type || !CanLayOut(type)) continue;
            if (model.GetDeclaredSymbol(type, context.CancellationToken) is not { } self) continue;
            if (!Brings(model.GetTypeInfo(written.Type, context.CancellationToken).Type, validated)) continue;
            if (MissingCreate(self, validated) is not { } create) continue;

            var position = type.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken) && !type.OpenBraceToken.IsMissing
                ? type.OpenBraceToken.Span.End
                : type.Identifier.SpanStart;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add Create",
                    cancellationToken => AddCreateAsync(context.Document, root, type, Signature(create, self, model, position), cancellationToken),
                    equivalenceKey: nameof(MissingCreateCodeFixProvider)),
                diagnostic);
        }
    }

    /// <summary>Whether the base type written is the contract, or an interface that derives from it.</summary>
    private static bool Brings(ITypeSymbol? written, ImmutableArray<INamedTypeSymbol> validated) =>
        written is INamedTypeSymbol named
     && (IsValidated(named, validated) || named.AllInterfaces.Any(candidate => IsValidated(candidate, validated)));

    private static bool IsValidated(INamedTypeSymbol type, ImmutableArray<INamedTypeSymbol> validated) =>
        validated.Contains(type.OriginalDefinition, SymbolEqualityComparer.Default);

    /// <summary>
    /// The contract's <c>Create</c> that the type leaves unimplemented, closed over the type's arguments,
    /// or <see langword="null"/>: where <c>TSelf</c> is another type, the instance to return is not this
    /// type's to build, and a member of the type named <c>Create</c> taking a <c>T</c> would clash.
    /// </summary>
    private static IMethodSymbol? MissingCreate(INamedTypeSymbol self, ImmutableArray<INamedTypeSymbol> validated)
    {
        foreach (var contract in self.AllInterfaces.Where(candidate => IsValidated(candidate, validated)))
        {
            if (!SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], self)) continue;

            var create = contract.GetMembers(CreateName).OfType<IMethodSymbol>().FirstOrDefault(method => method.IsStatic && method.Parameters.Length == 1);
            if (create is null || self.FindImplementationForInterfaceMember(create) is not null) continue;

            var wrapped = create.Parameters[0].Type;
            var clash   = self.GetMembers(CreateName).OfType<IMethodSymbol>()
                              .Any(method => method.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, wrapped));

            return clash ? null : create;
        }

        return null;
    }

    /// <summary>What the method is made of, its types spelled as the type's own code would name them.</summary>
    private static Parts Signature(IMethodSymbol create, INamedTypeSymbol self, SemanticModel model, int position)
    {
        var parameter = create.Parameters[0];
        var fault     = ((INamedTypeSymbol)create.ReturnType).TypeArguments[1];

        // A fault that is an enum names its first member in the comment, the shape a rule takes.
        var example = fault is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration
                   && enumeration.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.HasConstantValue) is { } member
            ? $"{fault.ToMinimalDisplayString(model, position)}.{Escaped(member.Name)}"
            : "...";

        return new Parts(
            create.ReturnType.ToMinimalDisplayString(model, position),
            parameter.Type.ToMinimalDisplayString(model, position),
            Escaped(parameter.Name),
            self.ToMinimalDisplayString(model, position),
            example);
    }

    private static string Escaped(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    /// <summary>
    /// Whether the fix can lay the method out: in a body of several lines, an empty body on one line, or a
    /// declaration ending in <c>;</c>, whose line starts with the type, not a body on one line that holds
    /// members.
    /// </summary>
    private static bool CanLayOut(TypeDeclarationSyntax type)
    {
        if (!HasBody(type)) return true;
        return type.Members.Count == 0 || type.OpenBraceToken.TrailingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
    }

    private static bool HasBody(TypeDeclarationSyntax type) =>
        type.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken) && !type.OpenBraceToken.IsMissing;

    private static async Task<Document> AddCreateAsync(Document document, SyntaxNode root, TypeDeclarationSyntax type, Parts parts, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        return document.WithSyntaxRoot(root.ReplaceNode(type, WithCreate(type, parts, text)));
    }

    /// <summary>
    /// The type with <c>Create</c> as its first member. In a body of several lines it goes on lines of
    /// its own, indented as the members are and ending as the brace's line does, a blank line before the
    /// next member. A declaration ending in <c>;</c>, or an empty body on one line, gets a body on lines of
    /// its own at the type's indentation, the <c>;</c>'s trivia, a comment included, kept after it.
    /// </summary>
    private static TypeDeclarationSyntax WithCreate(TypeDeclarationSyntax type, Parts parts, SourceText text)
    {
        var end = HasBody(type) ? type.OpenBraceToken.TrailingTrivia.LastOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)) : default;

        if (end.IsKind(SyntaxKind.EndOfLineTrivia))
        {
            var eol    = end.ToString();
            var indent = TypeLayout.MemberIndentation(type, text);
            var outer  = TypeLayout.Indentation(text, type.CloseBraceToken.SpanStart) ?? "";
            var unit   = indent.Length > outer.Length && indent.StartsWith(outer, System.StringComparison.Ordinal) ? indent.Substring(outer.Length) : TypeLayout.Unit(indent, text);
            var after  = type.Members.Count > 0 ? SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(eol), SyntaxFactory.EndOfLine(eol)) : SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(eol));

            return type.WithMembers(type.Members.Insert(0, Method(parts, indent, unit, eol, after)));
        }

        // A body opened here: the brace on a line of its own at the type's indentation.
        var typeIndent = TypeLayout.Indentation(text, type.SpanStart) ?? "";
        var unitHere   = TypeLayout.Unit(typeIndent, text);
        var lineEnd    = TypeLayout.LineEnding(text);
        var closing    = HasBody(type) ? type.CloseBraceToken.TrailingTrivia : type.SemicolonToken.TrailingTrivia;
        var last       = (HasBody(type) ? type.OpenBraceToken : type.SemicolonToken).GetPreviousToken();

        var opened = type.ReplaceToken(last, last.WithTrailingTrivia(last.TrailingTrivia.Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia))));
        var lead   = SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(lineEnd)).AddRange(Whitespace(typeIndent));
        var open   = SyntaxFactory.Token(lead, SyntaxKind.OpenBraceToken, SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(lineEnd)));
        var close  = SyntaxFactory.Token(Whitespace(typeIndent), SyntaxKind.CloseBraceToken, closing);

        return opened.WithSemicolonToken(default)
                     .WithOpenBraceToken(open)
                     .WithMembers(SyntaxFactory.SingletonList(Method(parts, typeIndent + unitHere, unitHere, lineEnd, SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(lineEnd)))))
                     .WithCloseBraceToken(close);
    }

    /// <summary>
    /// <c>Create</c> parsed from its text, so every token carries the trivia written there and none is
    /// elastic trivia a code action's cleanup would lay out again.
    /// </summary>
    private static MemberDeclarationSyntax Method(Parts parts, string indent, string unit, string eol, SyntaxTriviaList trailing)
    {
        var source =
            $"public static {parts.Returns} {CreateName}({parts.Wrapped} {parts.Parameter}){eol}"
          + $"{indent}{{{eol}"
          + $"{indent}{unit}// Add your rules: if (...) return Result.Error({parts.Example});{eol}"
          + $"{indent}{unit}return new {parts.Self}({parts.Parameter});{eol}"
          + $"{indent}}}";

        return (SyntaxFactory.ParseMemberDeclaration(source) ?? throw new System.InvalidOperationException($"'{source}' does not parse as a method."))
               .WithLeadingTrivia(Whitespace(indent))
               .WithTrailingTrivia(trailing);
    }

    private static SyntaxTriviaList Whitespace(string indent) =>
        indent.Length == 0 ? SyntaxFactory.TriviaList() : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent));

    private sealed class Parts
    {
        public Parts(string returns, string wrapped, string parameter, string self, string example)
        {
            Returns   = returns;
            Wrapped   = wrapped;
            Parameter = parameter;
            Self      = self;
            Example   = example;
        }

        public string Returns { get; }
        public string Wrapped { get; }
        public string Parameter { get; }
        public string Self { get; }
        public string Example { get; }
    }
}
