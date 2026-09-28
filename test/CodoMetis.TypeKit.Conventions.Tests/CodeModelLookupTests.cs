using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The generators never look up a type or member in Metalama's code model by name: no <c>OfName</c>,
/// no <c>OfExactSignature</c> or <c>OfCompatibleSignature</c>, and no string indexer on the field,
/// property or event collections, which all read the same index. They enumerate and compare names.
/// </summary>
/// <remarks>
/// <para>
/// Metalama builds a collection's by-name index lazily and without a lock, and the fabric's factory,
/// like the instances of one aspect layer, runs in parallel on one code model. An <c>OfName</c> that
/// overlapped another caller completing the collection returned nothing for a declared type, and
/// CMTK1007 was missed in 13 of 300 builds (spikes/ConcurrentNamespaceTypes). Enumerating goes
/// through the collection's lock.
/// </para>
/// <para>
/// The race has no deterministic test, so this pins its cause. It reads syntax, so a comment that
/// names <c>OfName</c> does not count, and the positive control keeps it from passing because the
/// walker stopped finding anything.
/// </para>
/// </remarks>
public sealed class CodeModelLookupTests
{
    private static readonly string[] ByNameMethods = ["OfName", "OfExactSignature", "OfCompatibleSignature"];

    /// <summary>The collections whose string indexer is <c>Single(OfName(name))</c>.</summary>
    private static readonly string[] ByNameIndexedCollections = ["Fields", "Properties", "Events", "AllFields", "AllProperties", "AllEvents"];

    [Fact]
    public void The_generators_sources_are_discovered() =>
        GeneratorsSources().Count.ShouldBeGreaterThanOrEqualTo(20, string.Join(", ", GeneratorsSources()));

    [Fact]
    public void A_lookup_by_name_is_found()
    {
        const string source =
            """
            class C
            {
                void M(INamespace ns, INamedType type)
                {
                    _ = ns.Types.OfName("A");
                    _ = type?.Methods.OfExactSignature("B", []);
                    _ = type.Methods?.OfCompatibleSignature("C", []);
                    _ = type.Fields["D"];
                    _ = ns.Types.FirstOrDefault(t => t.Name == "E");
                    _ = method.Parameters["result"];
                }
            }
            """;

        ByNameLookups(source, Path.Combine(Repository.Root, "C.cs"))
            .ShouldBe(
            [
                "C.cs:5: _ = ns.Types.OfName(\"A\");",
                "C.cs:6: _ = type?.Methods.OfExactSignature(\"B\", []);",
                "C.cs:7: _ = type.Methods?.OfCompatibleSignature(\"C\", []);",
                "C.cs:8: _ = type.Fields[\"D\"];",
            ]);
    }

    [Fact]
    public void The_generators_never_look_up_the_code_model_by_name()
    {
        var lookups = GeneratorsSources().SelectMany(path => ByNameLookups(File.ReadAllText(path), path)).ToArray();

        lookups.ShouldBeEmpty(
            "Metalama's by-name index is not safe for parallel callers, and a lookup through it missed declared types. " +
            $"Enumerate the collection and compare names instead:{Environment.NewLine}{string.Join(Environment.NewLine, lookups)}");
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

    private static IEnumerable<string> ByNameLookups(string source, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tree              = CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: cancellationToken);
        var root              = tree.GetRoot(cancellationToken);
        var lines             = tree.GetText(cancellationToken).Lines;

        var calls = root.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Where(call => MemberName(call.Expression) is { } name && ByNameMethods.Contains(name))
                        .Cast<SyntaxNode>();

        var indexers = root.DescendantNodes()
                           .OfType<ElementAccessExpressionSyntax>()
                           .Where(access => MemberName(access.Expression) is { } name && ByNameIndexedCollections.Contains(name));

        return calls.Concat(indexers)
                    .OrderBy(node => node.SpanStart)
                    .Select(node => lines.GetLineFromPosition(node.SpanStart))
                    .Select(line => $"{Path.GetRelativePath(Repository.Root, path)}:{line.LineNumber + 1}: {line.ToString().Trim()}");
    }

    /// <summary>The name after the last dot of <c>x.Name</c> or <c>x?.Name</c>.</summary>
    private static string? MemberName(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax access   => access.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            _                                     => null,
        };
}
