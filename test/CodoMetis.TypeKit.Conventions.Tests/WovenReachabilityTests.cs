using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The woven code has no statement after a <c>return</c> or a <c>throw</c> of the same block: what the
/// generators write is all reachable.
/// </summary>
/// <remarks>
/// <para>
/// A template that returns from a compile-time branch ends the run-time body there, but compile-time
/// calls after it still run. <c>TryParseTemplate</c> called <c>meta.InsertStatement</c> after such a
/// branch, so every generated <c>TryParse(string?, IFormatProvider?, out …)</c> ended in a declaration and
/// a <c>try</c>/<c>catch</c> nothing reached (issue #100). The compiler keeps quiet about unreachable code
/// in generated code, and no test of behaviour can see it.
/// </para>
/// <para>
/// This reads the woven files the probes emit (<c>MetalamaEmitCompilerTransformedFiles</c>) for the build
/// of its own configuration. The probes hold a value object of every parse strategy.
/// </para>
/// </remarks>
public sealed class WovenReachabilityTests
{
    private const string Probes = "test/CodoMetis.TypeKit.Generators.Probes";

    [Fact]
    public void A_statement_after_a_return_is_found()
    {
        const string source =
            """
            class C
            {
                bool M(string? s)
                {
                    if (s is null) return false;
                    return true;
                    int dead;
                }

                int N()
                {
                    throw new System.Exception();
                    return 1;
                }

                int O()
                {
                    return Local();
                    int Local() => 1;
                }
            }
            """;

        Unreachable(source, Path.Combine(Repository.Root, "C.cs")).ShouldBe(["C.cs:7: C.M: int dead;", "C.cs:13: C.N: return 1;"]);
    }

    [Fact]
    public void The_woven_probes_are_read()
    {
        var methods = WovenFiles().SelectMany(path => Methods(File.ReadAllText(path), path)).ToArray();

        // Every value object of the probes that parses gets a TryParse over a string.
        methods.Count(method => method.Identifier.ValueText == "TryParse" && method.ParameterList.Parameters.FirstOrDefault()?.Type?.ToString() is "global::System.String?")
               .ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void The_woven_code_has_no_unreachable_statement()
    {
        var unreachable = WovenFiles().SelectMany(path => Unreachable(File.ReadAllText(path), path)).ToArray();

        unreachable.ShouldBeEmpty(
            "A template wrote a statement after a return or a throw. A compile-time call after a return in a compile-time branch " +
            $"still runs: put it in an else branch.{Environment.NewLine}{string.Join(Environment.NewLine, unreachable.Take(20))}");
    }

    /// <summary>The woven sources of the probes, written by the build of this configuration.</summary>
    private static IReadOnlyList<string> WovenFiles()
    {
        // bin/{configuration}/{framework}/ of this test project.
        var output        = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework     = output.Name;
        var configuration = output.Parent!.Name;
        var folder        = Path.Combine(Repository.Root, Probes, "obj", configuration, framework, "metalama");

        return Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : throw new DirectoryNotFoundException($"No woven probes at {folder}. Build {Probes} in {configuration} first; dotnet test --solution does.");
    }

    private static IEnumerable<MethodDeclarationSyntax> Methods(string source, string path) =>
        CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: TestContext.Current.CancellationToken)
                        .GetRoot(TestContext.Current.CancellationToken)
                        .DescendantNodes()
                        .OfType<MethodDeclarationSyntax>();

    /// <summary>Each statement that follows a <c>return</c> or a <c>throw</c> in the same block, a local function excepted.</summary>
    private static IEnumerable<string> Unreachable(string source, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tree              = CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: cancellationToken);
        var lines             = tree.GetText(cancellationToken).Lines;

        foreach (var block in tree.GetRoot(cancellationToken).DescendantNodes().OfType<BlockSyntax>())
        {
            var exit = block.Statements.IndexOf(statement => statement is ReturnStatementSyntax or ThrowStatementSyntax);
            if (exit < 0) continue;

            var after = block.Statements.Skip(exit + 1).FirstOrDefault(statement => statement is not LocalFunctionStatementSyntax);
            if (after is null) continue;

            var member = after.Ancestors().OfType<MemberDeclarationSyntax>().First();
            var type   = after.Ancestors().OfType<BaseTypeDeclarationSyntax>().First().Identifier.ValueText;
            var name   = member switch
            {
                MethodDeclarationSyntax method     => method.Identifier.ValueText,
                PropertyDeclarationSyntax property => property.Identifier.ValueText,
                _                                  => member.Kind().ToString(),
            };

            yield return $"{Path.GetRelativePath(Repository.Root, path)}:{lines.GetLineFromPosition(after.SpanStart).LineNumber + 1}: {type}.{name}: {after.ToString().Split('\n')[0].Trim()}";
        }
    }
}
