using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// No probe decides how a row ends by the words of a message, except to fail it: in the probe corpus (harness and probes), a
/// <c>.Message.Contains(</c>, <c>StartsWith(</c>, <c>EndsWith(</c> or <c>IndexOf(</c> stands only in the condition of a failure text,
/// an <c>if</c> whose branch returns one or raises a harness error, or a <c>?:</c> between <c>null</c> and one. A refusal the framework
/// makes on its own is shown by side (1) of the three-sided differential (the harness's <c>Sides</c>), compared by its type and stage;
/// an intended difference by a citation. A message read to excuse a row, the substring excuse decision 99 retired, is refused here.
/// </summary>
/// <remarks>
/// It reads syntax, as <see cref="ProbeNoteKeyTests"/> does: a lambda inside the condition counts as the condition (<c>errors.Any(e =>
/// e.Message.StartsWith(...))</c>), an assignment, a <c>catch ... when</c> or a branch that passes does not. The EF family stays
/// two-sided in WS2's last step and is not changed by it: its one excuse by a message is listed in <see cref="Kept"/> with its reason,
/// and a listed site that no longer reads a message fails, so the list cannot outlive it.
/// </remarks>
public sealed class ProbeMessageTextTests
{
    private static string Corpus => Path.Combine(Repository.Root, "tools", "probe", "corpus");

    private static readonly string[] Reads = ["Contains", "StartsWith", "EndsWith", "IndexOf"];

    /// <summary>The sites kept, by file, each with why: the EF family is two-sided by decision of WS2's last step.</summary>
    private static readonly Dictionary<string, string> Kept = new()
    {
        ["tools/probe/corpus/probes/E06OwnConverterNoValueProbe.cs"] =
            "EF stays two-sided in WS2's last step: EF's 'could not be translated' refusal is matched by its words until the EF family gets its side (1)",
    };

    [Fact]
    public void The_corpus_reads_of_a_message_are_found() =>
        Sources().Sum(source => Sites(source.Text).Count()).ShouldBeGreaterThanOrEqualTo(30,
            "the corpus read a message's words at 33 places when this rule was written (WS2's three-sided differential), all but the one EF site a failure's condition");

    [Fact]
    public void No_message_text_decides_a_row_but_a_failure() =>
        Sources().Where(source => !Kept.ContainsKey(source.Path)).SelectMany(source => Problems(source.Path, source.Text)).ShouldBeEmpty();

    [Fact]
    public void Each_kept_site_still_reads_a_message_outside_a_failure() =>
        Kept.Keys.Where(path => !Problems(path, File.ReadAllText(Path.Combine(Repository.Root, path))).Any()).ShouldBeEmpty();

    [Fact]
    public void A_message_read_to_excuse_a_row_is_found()
    {
        const string source =
            """
            class P
            {
                string? Check(Check c, Exception x, Exception? y, string[] names)
                {
                    if (x.Message.Contains("maximum allowed depth")) { c.Note(NoteKeys.StjMaxdepth); return null; }
                    if (!x.Message.Contains("Refused")) return $"does not name the fault: {x.Message}";
                    if (x.InnerException is { } i && i.Message.Contains("secret")) return "inner quotes the input";
                    if (!x.Message.StartsWith("V") || x.Message.EndsWith(".")) return "not named: " + x.Message;
                    if (names.Any(n => !x.Message.Contains(n))) { r.Outcome("harness-error", "not named"); return null; }
                    var named = x.Message.Contains("V");
                    try { throw x; } catch (Exception e) when (e.Message.Contains("could not be translated")) { return null; }
                    var missing = names.Where(n => !x.Message.Contains(n)).ToList();
                    return x.Message.Contains("V") ? null : $"not named: {x.Message}";
                    // x.Message.Contains("in a comment") is no call
                }
            }
            """;
        Problems("P.cs", source).ShouldBe(
        [
            "P.cs:5: x.Message.Contains(\"maximum allowed depth\") decides a row by a message's words outside a failure",
            "P.cs:10: x.Message.Contains(\"V\") decides a row by a message's words outside a failure",
            "P.cs:11: e.Message.Contains(\"could not be translated\") decides a row by a message's words outside a failure",
            "P.cs:12: x.Message.Contains(n) decides a row by a message's words outside a failure",
        ]);
    }

    private static List<(string Path, string Text)> Sources() =>
    [
        .. Directory.EnumerateFiles(Corpus, "*.cs", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(path => (Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'), File.ReadAllText(path)))
    ];

    /// <summary>Every <c>X.Message.Contains(...)</c> and its kin in a source.</summary>
    private static IEnumerable<InvocationExpressionSyntax> Sites(string text) =>
        CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Where(call => call.Expression is MemberAccessExpressionSyntax
                        {
                            Name.Identifier.ValueText: var read,
                            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Message" },
                        } && Reads.Contains(read));

    private static IEnumerable<string> Problems(string path, string text)
    {
        foreach (var call in Sites(text))
            if (!InAFailuresCondition(call))
                yield return $"{path}:{call.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {call} decides a row by a message's words outside a failure";
    }

    /// <summary>
    /// The read stands in the condition of an <c>if</c> whose branch fails the row (returns a failure text, or raises a harness error), or
    /// of a <c>?:</c> between <c>null</c> and a failure text. A lambda on the way up is part of that condition.
    /// </summary>
    private static bool InAFailuresCondition(SyntaxNode read)
    {
        for (var node = read; node.Parent is { } parent; node = parent)
        {
            switch (parent)
            {
                case IfStatementSyntax branch when branch.Condition == node:
                    return Fails(branch.Statement);
                case ConditionalExpressionSyntax choice when choice.Condition == node:
                    return (IsNull(choice.WhenTrue) && IsFailure(choice.WhenFalse)) || (IsNull(choice.WhenFalse) && IsFailure(choice.WhenTrue));
                case StatementSyntax:
                case CatchFilterClauseSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>A branch that fails the row: it returns a failure text, or raises a harness error, first.</summary>
    private static bool Fails(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax { Expression: { } returned } => IsFailure(returned),
        BlockSyntax { Statements: [var first, ..] } => Fails(first),
        ExpressionStatementSyntax { Expression: InvocationExpressionSyntax raise } => RaisesAHarnessError(raise),
        _ => false,
    };

    private static bool RaisesAHarnessError(InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Outcome" }
        && call.ArgumentList.Arguments is [{ Expression: LiteralExpressionSyntax { Token.ValueText: "harness-error" } }, ..];

    /// <summary>A failure text: a string, an interpolated one, a concatenation with one, or a <c>?:</c> one of whose branches is one.</summary>
    private static bool IsFailure(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        InterpolatedStringExpressionSyntax => true,
        BinaryExpressionSyntax add when add.IsKind(SyntaxKind.AddExpression) => IsFailure(add.Left) || IsFailure(add.Right),
        ConditionalExpressionSyntax choice => IsFailure(choice.WhenTrue) || IsFailure(choice.WhenFalse),
        ParenthesizedExpressionSyntax inner => IsFailure(inner.Expression),
        _ => false,
    };

    private static bool IsNull(ExpressionSyntax expression) => expression.IsKind(SyntaxKind.NullLiteralExpression);
}
