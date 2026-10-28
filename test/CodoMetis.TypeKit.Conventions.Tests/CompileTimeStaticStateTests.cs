using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// A static field of the generators, or a static auto-property with storage, holds durable data only:
/// strings, <see cref="Type"/>s, Metalama's <c>DiagnosticDefinition</c>s and arrays, lists and tuples of
/// those. Never a code-model object such as an <c>AttributeConstruction</c>, an <c>INamedType</c> or an
/// <c>IExpression</c>.
/// </summary>
/// <remarks>
/// <para>
/// A code-model object belongs to one compilation, and a static field outlives it. At design time the IDE
/// keeps Metalama's process across edits, so every compilation that ever filled such a field stays
/// reachable. Gaël Fraiteur of Metalama found seven <c>AttributeConstruction</c> fields in
/// <c>CodeAnnotations</c> (discussion 2305, issue #90); they are properties that build the construction on
/// use now.
/// </para>
/// <para>
/// The rule is an allowlist on the declared type, read from syntax, so a field of a type nobody has
/// judged fails until someone adds the type here. A <c>[Template]</c> member is no storage: Metalama
/// introduces it into the target, and the generators never assign it.
/// </para>
/// </remarks>
public sealed class CompileTimeStaticStateTests
{
    /// <summary>Types whose instances hold no compilation: what a static field may be.</summary>
    private static readonly string[] DurableTypes = ["string", "bool", "int", "Type", "DiagnosticDefinition"];

    /// <summary>Collections that are durable when their elements are.</summary>
    private static readonly string[] DurableCollections = ["IReadOnlyList", "IReadOnlyCollection", "ImmutableArray", "FrozenSet", "FrozenDictionary"];

    [Fact]
    public void Static_storage_of_a_code_model_type_is_found()
    {
        const string source =
            """
            static class C
            {
                public static readonly AttributeConstruction A = AttributeConstruction.Create(typeof(X));
                private static INamedType? b;
                public static IReadOnlyList<IExpression> C { get; } = [];
                private static readonly (string Name, IType Type)[] D = [];
                public static readonly DiagnosticDefinition<(INamedType, string)> E = new("X", Severity.Error, "{0}");
                private static readonly (string Name, Type Type)[] F = [];
                public static IReadOnlyList<string> G { get; } = [];
                public static AttributeConstruction H => AttributeConstruction.Create(typeof(X));
                [Template] public static IReadOnlyList<object> _i;
                private const string J = "j";
                private readonly IType k;
            }
            """;

        StaticStorage(source, Path.Combine(Repository.Root, "C.cs"))
            .Where(storage => !storage.Durable)
            .Select(storage => storage.Location)
            .ShouldBe(
            [
                "C.cs:3: A: AttributeConstruction",
                "C.cs:4: b: INamedType?",
                "C.cs:5: C: IReadOnlyList<IExpression>",
                "C.cs:6: D: (string Name, IType Type)[]",
            ]);
    }

    [Fact]
    public void The_generators_hold_static_storage()
    {
        // The positive control: the diagnostics and the member tables are found, so an empty result below
        // cannot mean the walker stopped finding anything.
        var storage = GeneratorsSources().SelectMany(path => StaticStorage(File.ReadAllText(path), path)).ToArray();

        storage.Length.ShouldBeGreaterThanOrEqualTo(20, string.Join(Environment.NewLine, storage.Select(s => s.Location)));
    }

    [Fact]
    public void The_generators_keep_no_code_model_object_in_static_storage()
    {
        var offending = GeneratorsSources().SelectMany(path => StaticStorage(File.ReadAllText(path), path))
                                           .Where(storage => !storage.Durable)
                                           .Select(storage => storage.Location)
                                           .ToArray();

        offending.ShouldBeEmpty(
            "A static field outlives the compilation its code-model objects belong to, and the IDE keeps every one alive. " +
            "Build the object where it is used (a property with an expression body), or add a type that holds no compilation " +
            $"to {nameof(DurableTypes)}:{Environment.NewLine}{string.Join(Environment.NewLine, offending)}");
    }

    private static IReadOnlyList<string> GeneratorsSources()
    {
        var project = Path.Combine(Repository.Root, "src", "CodoMetis.TypeKit.Generators");

        return
        [
            .. Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                        .Where(path => !Path.GetRelativePath(project, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                        .Order(StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<(string Location, bool Durable)> StaticStorage(string source, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tree              = CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: cancellationToken);
        var root              = tree.GetRoot(cancellationToken);
        var lines             = tree.GetText(cancellationToken).Lines;

        var fields = root.DescendantNodes()
                         .OfType<FieldDeclarationSyntax>()
                         .Where(field => field.Modifiers.Any(SyntaxKind.StaticKeyword) && !IsTemplate(field.AttributeLists))
                         .SelectMany(field => field.Declaration.Variables.Select(variable => (Node: (SyntaxNode)variable, variable.Identifier, field.Declaration.Type)));

        // An auto-property has a backing field; one with an expression body or accessor bodies has none.
        var properties = root.DescendantNodes()
                             .OfType<PropertyDeclarationSyntax>()
                             .Where(property => property.Modifiers.Any(SyntaxKind.StaticKeyword)
                                             && !IsTemplate(property.AttributeLists)
                                             && property.AccessorList is { } accessors
                                             && accessors.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null))
                             .Select(property => (Node: (SyntaxNode)property, property.Identifier, property.Type));

        return fields.Concat(properties)
                     .OrderBy(storage => storage.Node.SpanStart)
                     .Select(storage => (
                         $"{Path.GetRelativePath(Repository.Root, path)}:{lines.GetLineFromPosition(storage.Node.SpanStart).LineNumber + 1}: {storage.Identifier.ValueText}: {storage.Type}",
                         IsDurable(storage.Type)));
    }

    private static bool IsTemplate(SyntaxList<AttributeListSyntax> attributeLists) =>
        attributeLists.SelectMany(list => list.Attributes).Any(attribute => attribute.Name.ToString() is "Template" or "TemplateAttribute");

    private static bool IsDurable(TypeSyntax type) =>
        type switch
        {
            PredefinedTypeSyntax predefined => DurableTypes.Contains(predefined.Keyword.ValueText),
            NullableTypeSyntax nullable     => IsDurable(nullable.ElementType),
            ArrayTypeSyntax array           => IsDurable(array.ElementType),
            TupleTypeSyntax tuple           => tuple.Elements.All(element => IsDurable(element.Type)),
            QualifiedNameSyntax qualified   => IsDurable(qualified.Right),

            // A diagnostic's type argument only says what its message is formatted from.
            GenericNameSyntax { Identifier.ValueText: "DiagnosticDefinition" } => true,
            GenericNameSyntax generic => DurableCollections.Contains(generic.Identifier.ValueText) && generic.TypeArgumentList.Arguments.All(IsDurable),
            IdentifierNameSyntax name => DurableTypes.Contains(name.Identifier.ValueText),
            _                         => false,
        };
}
