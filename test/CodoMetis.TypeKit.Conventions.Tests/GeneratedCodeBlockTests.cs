using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// A README block that shows generated code shows what the build writes: every member in it, and every
/// interface in its base list, is in the woven code of the sample it names.
/// </summary>
/// <remarks>
/// <para>
/// Such a block repeats a partial declaration, so it cannot compile and is marked
/// <c>&lt;!-- not compiled: generated code for {file}, trimmed (GeneratedCodeBlockTests) --&gt;</c>, where
/// <c>{file}</c> is a source of the samples project, relative to it. The samples project sets
/// <c>MetalamaEmitCompilerTransformedFiles</c>, so the build writes that file's woven form under
/// <c>obj/{configuration}/net10.0/metalama/</c>, and this reads the one of its own configuration.
/// </para>
/// <para>
/// The woven code is fully qualified, carries an attribute on every member and a cast on every
/// returned expression, so both sides are compared in a reading form: no attributes, no comments, no
/// namespace before a type, <c>string</c> for <c>System.String</c>, no casts and no <c>this.</c>. A block
/// may leave members out; the members it shows have to be the generated ones, bodies included.
/// </para>
/// </remarks>
public sealed partial class GeneratedCodeBlockTests
{
    private const string SamplesObj = "test/CodoMetis.TypeKit.Samples/obj";

    public static TheoryData<string, string, string> Blocks =>
    [
        .. ReadmeSamples.CodeBlocks()
                        .Where(block => block.IsCSharp && block.OptOut is { } reason && reason.StartsWith(MarkerStart, StringComparison.Ordinal))
                        .Select(block => (block.Location, SampleFile(block), block.Code))
    ];

    private const string MarkerStart = "generated code for ";

    [Fact]
    public void The_blocks_are_found() =>
        Blocks.Count.ShouldBeGreaterThanOrEqualTo(3);

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_block_of_generated_code_is_what_the_build_writes(string location, string sample, string code) =>
        Differences(code, File.ReadAllText(WovenFile(sample)), KnownNamespaces.Value)
            .Select(difference => $"{difference.Key}: {difference.Detail}")
            .ShouldBeEmpty($"{location} shows code the build does not write for {sample}. Correct the block from {WovenFile(sample)}.");

    [Fact]
    public void A_member_that_differs_is_found()
    {
        const string woven =
            """
            namespace Shop;
            [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]
            public readonly partial record struct OrderId : IValue<Guid>, global::System.IComparable<global::Shop.OrderId>
            {
                [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]private readonly global::System.Guid _value;
                [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]public global::System.Guid Value
                { get{    return (global::System.Guid)this._value; } }
                public static global::Shop.OrderId From(global::System.Guid value){    return new global::Shop.OrderId(value); }
                public override global::System.String ToString(){ return (global::System.String)this.Value.ToString(); }
            }
            """;

        const string shown =
            """
            public readonly partial record struct OrderId : IValue<Guid>, IComparable<OrderId>, IParsable<OrderId>
            {
                private readonly Guid _value;

                public Guid Value
                {
                    get { return _value; }
                }

                public static OrderId From(Guid value)
                {
                    return default;
                }

                public override string ToString()
                {
                    return Value.ToString();
                }

                public static OrderId Parse(string s, IFormatProvider? provider)
                {
                    return From(Guid.Parse(s));
                }
            }
            """;

        Differences(shown, woven, new HashSet<string> { "Shop", "System" })
            .Select(difference => difference.Key)
            .ShouldBe(["OrderId : IParsable<OrderId>", "OrderId.From", "OrderId.Parse"]);
    }

    /// <summary>
    /// What <paramref name="shown"/> declares that <paramref name="woven"/> does not: a type, an interface
    /// of a type's base list, or a member, each with what the woven code has under that name instead.
    /// </summary>
    private static IEnumerable<(string Key, string Detail)> Differences(string shown, string woven, IReadOnlySet<string> namespaces)
    {
        var wovenTypes = TypesByName(Parse(woven));

        foreach (var type in Parse(shown).DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!wovenTypes.TryGetValue(type.Identifier.ValueText, out var wovenType))
            {
                yield return (type.Identifier.ValueText, "no such type in the woven code");
                continue;
            }

            var wovenBases = wovenType.BaseList?.Types.Select(baseType => Reading(baseType.Type, namespaces)).ToHashSet() ?? [];
            foreach (var baseType in type.BaseList?.Types ?? [])
            {
                var reading = Reading(baseType.Type, namespaces);
                if (!wovenBases.Contains(reading)) yield return ($"{type.Identifier.ValueText} : {reading}", $"not in the woven base list ({string.Join(", ", wovenBases)})");
            }

            var wovenMembers = wovenType.Members.Where(member => member is not BaseTypeDeclarationSyntax).Select(member => Reading(member, namespaces)).ToArray();
            foreach (var member in type.Members.Where(member => member is not BaseTypeDeclarationSyntax))
            {
                var reading = Reading(member, namespaces);
                if (wovenMembers.Contains(reading)) continue;

                var name      = Name(member);
                var namesakes = wovenType.Members.Where(candidate => Name(candidate) == name).Select(candidate => Reading(candidate, namespaces));

                yield return ($"{type.Identifier.ValueText}.{name}",
                              $"shown as{Environment.NewLine}{reading}{Environment.NewLine}generated as{Environment.NewLine}{string.Join(Environment.NewLine, namesakes.DefaultIfEmpty("(nothing of that name)"))}");
            }
        }
    }

    private static Dictionary<string, TypeDeclarationSyntax> TypesByName(SyntaxNode root) =>
        root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .GroupBy(type => type.Identifier.ValueText)
            .ToDictionary(group => group.Key, group => group.First());

    private static SyntaxNode Parse(string code) =>
        CSharpSyntaxTree.ParseText(code, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);

    private static string Name(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax method           => method.Identifier.ValueText,
            PropertyDeclarationSyntax property       => property.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            FieldDeclarationSyntax field             => string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            OperatorDeclarationSyntax op             => "operator " + op.OperatorToken.ValueText,
            _                                        => member.Kind().ToString(),
        };

    /// <summary>The code as a reader would write it, on one normalised layout.</summary>
    private static string Reading(SyntaxNode node, IReadOnlySet<string> namespaces) =>
        new ReadingForm(namespaces).Visit(node)!.NormalizeWhitespace().ToFullString();

    /// <summary>The woven form of <paramref name="sample"/>, written by the build of this configuration.</summary>
    private static string WovenFile(string sample)
    {
        // bin/{configuration}/{framework}/ of this test project.
        var output        = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework     = output.Name;
        var configuration = output.Parent!.Name;
        var path          = Path.Combine(Repository.Root, SamplesObj, configuration, framework, "metalama", sample);

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"No woven {sample} at {path}. Build {ReadmeSamples.SamplesProject} in {configuration} first; dotnet test --solution does.");
    }

    private static string SampleFile(CodeBlock block)
    {
        var match = Marker().Match(block.OptOut!);
        return match.Success
            ? match.Groups["file"].Value
            : throw new InvalidOperationException($"{block.Location}: the marker names no sample: <!-- not compiled: {MarkerStart}{{file}}, trimmed (GeneratedCodeBlockTests) -->");
    }

    [GeneratedRegex(@"^generated code for (?<file>\S+), trimmed \(GeneratedCodeBlockTests\)$")]
    private static partial Regex Marker();

    /// <summary>The namespaces of every type the samples can name, so a qualified name loses only those.</summary>
    private static readonly Lazy<IReadOnlySet<string>> KnownNamespaces = new(() =>
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        // TypeKit's and the samples' own, read from their sources: this project loads neither assembly.
        foreach (var folder in new[] { "src", ReadmeSamples.SamplesProject })
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Repository.Root, folder), "*.cs", SearchOption.AllDirectories)
                                      .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        foreach (var declaration in Parse(File.ReadAllText(path)).DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            AddWithParents(names, declaration.Name.ToString());

        // The framework's, read from the metadata of every assembly of the runtime and of this test's output.
        var folders = new[] { Path.GetDirectoryName(typeof(object).Assembly.Location)!, AppContext.BaseDirectory };
        foreach (var path in folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.dll")))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new PEReader(stream);
                if (!reader.HasMetadata) continue;

                var metadata = reader.GetMetadataReader();
                foreach (var handle in metadata.TypeDefinitions)
                    if (metadata.GetString(metadata.GetTypeDefinition(handle).Namespace) is { Length: > 0 } ns) AddWithParents(names, ns);
            }
            catch (BadImageFormatException)
            {
                // A native library beside the managed ones.
            }
        }

        return names;
    });

    private static void AddWithParents(HashSet<string> names, string ns)
    {
        for (var name = ns; name.Length > 0; name = name.Contains('.') ? name[..name.LastIndexOf('.')] : "") names.Add(name);
    }

    /// <summary>Takes out what the woven code adds for the compiler and not for a reader.</summary>
    private sealed class ReadingForm(IReadOnlySet<string> namespaces) : CSharpSyntaxRewriter
    {
        private static readonly Dictionary<string, SyntaxKind> Keywords = new(StringComparer.Ordinal)
        {
            ["Boolean"] = SyntaxKind.BoolKeyword, ["String"] = SyntaxKind.StringKeyword, ["Int32"] = SyntaxKind.IntKeyword,
            ["Int64"]   = SyntaxKind.LongKeyword, ["Object"] = SyntaxKind.ObjectKeyword, ["Char"]  = SyntaxKind.CharKeyword,
            ["Byte"]    = SyntaxKind.ByteKeyword, ["Double"] = SyntaxKind.DoubleKeyword, ["Decimal"] = SyntaxKind.DecimalKeyword,
        };

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node) => null;

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
            trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ? default : trivia;

        /// <summary>
        /// A cast goes, and so do the parentheses around its operand where the cast stood alone: a returned
        /// value, an initializer, an argument or another cast.
        /// </summary>
        public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node) =>
            node.Expression is ParenthesizedExpressionSyntax parenthesized
         && node.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax or EqualsValueClauseSyntax or ArgumentSyntax or CastExpressionSyntax
                ? Visit(parenthesized.Expression)
                : Visit(node.Expression);

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is ThisExpressionSyntax) return Visit(node.Name);

            var qualifier = Qualifier(node.Expression);
            if (qualifier is not null && namespaces.Contains(qualifier)) return Unqualified(node.Name);

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var qualifier = Qualifier(node.Left);
            return qualifier is not null && namespaces.Contains(qualifier) ? Unqualified(node.Right) : base.VisitQualifiedName(node);
        }

        public override SyntaxNode? VisitAliasQualifiedName(AliasQualifiedNameSyntax node) => Unqualified(node.Name);

        /// <summary>A name whose namespace was taken off: <c>System.Object</c> is read as <c>object</c>.</summary>
        private SyntaxNode? Unqualified(SimpleNameSyntax name) =>
            name is IdentifierNameSyntax identifier && Keywords.TryGetValue(identifier.Identifier.ValueText, out var keyword)
                ? Keyword(identifier, keyword)
                : Visit(name);

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            Keywords.TryGetValue(node.Identifier.ValueText, out var keyword) && !(node.Parent is MemberAccessExpressionSyntax access && access.Name == node)
                ? Keyword(node, keyword)
                : base.VisitIdentifierName(node);

        private static SyntaxNode Keyword(IdentifierNameSyntax node, SyntaxKind keyword) =>
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(keyword)).WithTriviaFrom(node);

        /// <summary>The dotted text of a name or member-access chain, without <c>global::</c>; null for anything else.</summary>
        private static string? Qualifier(ExpressionSyntax expression) =>
            expression switch
            {
                AliasQualifiedNameSyntax alias      => alias.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier     => identifier.Identifier.ValueText,
                QualifiedNameSyntax qualified       => Qualifier(qualified.Left) is { } left ? $"{left}.{qualified.Right.Identifier.ValueText}" : null,
                MemberAccessExpressionSyntax access => Qualifier(access.Expression) is { } left ? $"{left}.{access.Name.Identifier.ValueText}" : null,
                _                                   => null,
            };
    }
}
