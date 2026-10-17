using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every note a probe makes names a key of the catalogue, and every skip names its finding as written
/// (decision 103): in the probe corpus, the first argument of a <c>Note</c> call is a member of
/// <c>NoteKeys</c>, and the first argument of a two-argument <c>Skip</c> (the harness's, not LINQ's) is a
/// string literal.
/// </summary>
/// <remarks>
/// The compiler already refuses a note key outside the catalogue, since <c>NoteKeys</c> has a private
/// constructor; this holds the shape for the sources the replay overlays, before any build, and for
/// <c>Skip</c>, whose finding id stays a string. It reads syntax, so a call in a comment or a string does
/// not count, and the positive control and the floor keep it from passing because the walker stopped
/// finding anything.
/// </remarks>
public sealed class ProbeNoteKeyTests
{
    private static string Corpus => Path.Combine(Repository.Root, "tools", "probe", "corpus");

    [Fact]
    public void The_corpus_notes_are_found() =>
        Calls(Sources()).Count(c => c.Method == "Note").ShouldBeGreaterThanOrEqualTo(100,
            "the corpus made 136 notes when this rule was written, and 127 once the http family's outcome notes became checks' citations (WS2 step 4)");

    [Fact]
    public void Every_note_and_skip_in_the_corpus_names_its_key_as_written() =>
        Sources().SelectMany(source => Problems(source.Path, source.Text)).ShouldBeEmpty();

    [Fact]
    public void A_key_or_a_finding_built_from_text_is_found()
    {
        const string source =
            """
            class P
            {
                void Check(Row r, string x)
                {
                    r.Note("msg:" + x, "detail");
                    r.Note(NoteKeys.Compared, "fine");
                    r.Skip("F" + x, "why");
                    r.Skip("F7", "fine");
                    var rest = new[] { 1, 2 }.Skip(x.Length);
                    // r.Note("in a comment", x);
                }
            }
            """;
        Problems("P.cs", source).ShouldBe(
        [
            "P.cs:5: Note(\"msg:\" + x, ...) does not name a key of NoteKeys",
            "P.cs:7: Skip(\"F\" + x, ...) does not name its finding as a string literal",
        ]);
    }

    private static List<(string Path, string Text)> Sources() =>
    [
        .. Directory.EnumerateFiles(Corpus, "*.cs", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(path => (Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'), File.ReadAllText(path)))
    ];

    private static IEnumerable<(string Method, InvocationExpressionSyntax Call)> Calls(IEnumerable<(string Path, string Text)> sources) =>
        sources.SelectMany(source => Calls(source.Text));

    private static IEnumerable<(string Method, InvocationExpressionSyntax Call)> Calls(string text) =>
        CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Select(call => (Method: call.Expression switch
                        {
                            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                            IdentifierNameSyntax name => name.Identifier.ValueText,
                            _ => "",
                        }, Call: call))
                        .Where(c => c.Method is "Note" or "Skip");

    private static IEnumerable<string> Problems(string path, string text)
    {
        foreach (var (method, call) in Calls(text))
        {
            var arguments = call.ArgumentList.Arguments;
            var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (method == "Note" && arguments.Count > 0 && !IsCatalogueKey(arguments[0].Expression))
                yield return $"{path}:{line}: Note({arguments[0]}, ...) does not name a key of NoteKeys";
            if (method == "Skip" && arguments.Count == 2 && !arguments[0].Expression.IsKind(SyntaxKind.StringLiteralExpression))
                yield return $"{path}:{line}: Skip({arguments[0]}, ...) does not name its finding as a string literal";
        }
    }

    private static bool IsCatalogueKey(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "NoteKeys" } };
}
