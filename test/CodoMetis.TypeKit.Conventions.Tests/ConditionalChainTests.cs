using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// No conditional expression in the solution's sources nests more than <see cref="MaxDepth"/>
/// conditionals through its branches. A longer chain is an <c>if</c> and <c>return</c> per arm.
/// </summary>
/// <remarks>
/// <para>
/// Rider's type inference walks a nested conditional again at every level of it. The 26-arm chain
/// <c>BuiltInConverters.For</c> was written as froze Rider's backend whenever the file was analysed,
/// and its thread dump crashed the IDE; the compiler and the tests never noticed.
/// </para>
/// <para>
/// It reads syntax, so a chain in a comment or a string does not count, and the positive control
/// keeps it from passing because the walker stopped finding anything.
/// </para>
/// </remarks>
public sealed class ConditionalChainTests
{
    private const int MaxDepth = 8;

    private static readonly string[] SolutionFolders = ["src", "test", "tools", "benchmarks"];

    [Fact]
    public void The_solutions_sources_are_discovered() =>
        Sources().Count.ShouldBeGreaterThanOrEqualTo(400, string.Join(", ", Sources().Take(20)));

    [Fact]
    public void A_chain_deeper_than_the_limit_is_found()
    {
        const string source =
            """
            class C
            {
                int Deep(int x) => x == 1 ? 1 : x == 2 ? 2 : (x == 3 ? 3 : x == 4 ? 4 : x == 5 ? 5 : x == 6 ? 6 : x == 7 ? 7 : x == 8 ? 8 : x == 9 ? 9 : 0);
                int AtTheLimit(int x) => x == 1 ? 1 : x == 2 ? 2 : x == 3 ? 3 : x == 4 ? 4 : x == 5 ? 5 : x == 6 ? 6 : x == 7 ? 7 : x == 8 ? 8 : 0;
                int Separate(int x) => (x == 1 ? 1 : x == 2 ? 2 : x == 3 ? 3 : x == 4 ? 4 : 0) + (x == 5 ? 5 : x == 6 ? 6 : x == 7 ? 7 : x == 8 ? 8 : x == 9 ? 9 : 0);
            }
            """;

        DeepChains(source, Path.Combine(Repository.Root, "C.cs")).ShouldBe(["C.cs:3: 9 nested conditionals"]);
    }

    [Fact]
    public void No_conditional_chain_is_deeper_than_the_limit()
    {
        var chains = Sources().SelectMany(path => DeepChains(File.ReadAllText(path), path)).ToArray();

        chains.ShouldBeEmpty(
            $"Rider's type inference walks a nested conditional again at every level, and a long chain freezes its backend. " +
            $"Write more than {MaxDepth} arms as an if and a return each:{Environment.NewLine}{string.Join(Environment.NewLine, chains)}");
    }

    private static IReadOnlyList<string> Sources() =>
    [
        .. SolutionFolders.Select(folder => Path.Combine(Repository.Root, folder))
                          .Where(Directory.Exists)
                          .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                                                         .Where(path => !Path.GetRelativePath(folder, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
                          .Order(StringComparer.Ordinal)
    ];

    private static IEnumerable<string> DeepChains(string source, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tree              = CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: cancellationToken);
        var lines             = tree.GetText(cancellationToken).Lines;

        return tree.GetRoot(cancellationToken)
                   .DescendantNodes()
                   .OfType<ConditionalExpressionSyntax>()
                   .Where(conditional => !IsBranchOfConditional(conditional))
                   .Select(conditional => (Node: conditional, Depth: Depth(conditional)))
                   .Where(chain => chain.Depth > MaxDepth)
                   .Select(chain => $"{Path.GetRelativePath(Repository.Root, path)}:{lines.GetLineFromPosition(chain.Node.SpanStart).LineNumber + 1}: {chain.Depth} nested conditionals");
    }

    /// <summary>The conditionals nested through branches, parentheses looked through.</summary>
    private static int Depth(ExpressionSyntax expression) =>
        Unparenthesized(expression) is ConditionalExpressionSyntax conditional
            ? 1 + Math.Max(Depth(conditional.WhenTrue), Depth(conditional.WhenFalse))
            : 0;

    private static bool IsBranchOfConditional(SyntaxNode node)
    {
        var child = node;
        while (child.Parent is ParenthesizedExpressionSyntax parenthesized) child = parenthesized;

        return child.Parent is ConditionalExpressionSyntax parent && (parent.WhenTrue == child || parent.WhenFalse == child);
    }

    private static ExpressionSyntax Unparenthesized(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
        return expression;
    }
}
