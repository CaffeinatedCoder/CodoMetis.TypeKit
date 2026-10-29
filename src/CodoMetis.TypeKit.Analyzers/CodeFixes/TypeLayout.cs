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

    /// <summary>
    /// The line ending of the line holding <paramref name="position"/>, or of the nearest line before it
    /// that has one, so lines added there end as their neighbours do in a file whose endings are mixed;
    /// LF in a file of one line.
    /// </summary>
    public static string LineEndingAt(SourceText text, int position)
    {
        for (var number = text.Lines.GetLineFromPosition(position).LineNumber; number >= 0; number--)
        {
            var line = text.Lines[number];
            if (line.EndIncludingLineBreak > line.End) return text.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak));
        }

        return "\n";
    }
}
