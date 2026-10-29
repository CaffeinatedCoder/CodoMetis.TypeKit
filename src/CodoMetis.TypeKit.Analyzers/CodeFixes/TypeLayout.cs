using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// How a type's members are laid out, read from the text, for a fix that adds one and asks no
/// formatter.
/// </summary>
internal static class TypeLayout
{
    /// <summary>
    /// The indentation of the first member's line; without a member, the closing brace's, or the
    /// type's, one level deeper, a tab where the file indents with tabs.
    /// </summary>
    public static string MemberIndentation(TypeDeclarationSyntax type, SourceText text)
    {
        if (type.Members.Count > 0 && Indentation(text, type.Members[0].SpanStart) is { } member) return member;

        var outer = Indentation(text, type.CloseBraceToken.SpanStart) ?? Indentation(text, type.SpanStart) ?? "";
        return outer + Unit(outer, text);
    }

    /// <summary>One level of indentation: a tab where the file indents with tabs, otherwise four spaces.</summary>
    public static string Unit(string indentation, SourceText text) =>
        indentation.IndexOf('\t') >= 0 || text.Lines.Any(line => line.Span.Length > 0 && text[line.Start] == '\t') ? "\t" : "    ";

    /// <summary>The whitespace that starts the line of <paramref name="position"/>, or <see langword="null"/> when anything else stands before it.</summary>
    public static string? Indentation(SourceText text, int position)
    {
        var line = text.Lines.GetLineFromPosition(position);
        var end  = line.Start;

        while (end < position && text[end] is ' ' or '\t') end++;

        return end == position ? text.ToString(TextSpan.FromBounds(line.Start, end)) : null;
    }

    /// <summary>The file's line ending: CRLF where it has one, otherwise LF.</summary>
    public static string LineEnding(SourceText text) =>
        text.Lines.Select(line => line.EndIncludingLineBreak - line.End).FirstOrDefault(length => length > 0) == 2 ? "\r\n" : "\n";
}
