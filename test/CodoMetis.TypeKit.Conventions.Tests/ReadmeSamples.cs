using System.Text;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// A fenced code block in a README, its code dedented and trimmed. <see cref="OptOut"/> is the reason
/// after <c>&lt;!-- not compiled: … --&gt;</c> above the fence, empty when the marker gives none, and null
/// without a marker.
/// </summary>
internal sealed record CodeBlock(string Readme, int Line, string Language, string Code, string? OptOut)
{
    public string Location => $"{Readme}:{Line}";

    public bool IsCSharp => Language.ToLowerInvariant() is "csharp" or "cs" or "c#";
}

/// <summary>A sample in the samples project: its parts, in file order, each dedented, joined and trimmed.</summary>
internal sealed record SampleRegion(string Name, string File, int Line, string Code)
{
    public string Location => $"{File}:{Line}";

    /// <summary>The README the sample is shown in: the part of its name before the slash.</summary>
    public string ReadmePrefix => Name.Split('/')[0];
}

/// <summary>
/// The READMEs' C# blocks and the samples project's regions, as <see cref="ReadmeSampleTests"/> compares them.
/// </summary>
/// <remarks>
/// A region is the code between <c>// sample: {README}/{name}</c> and <c>// end sample</c>. A sample
/// whose README block mixes what C# keeps apart (a <c>using</c> and statements, a type and a
/// statement) is several regions of one name in one file, joined in file order, each dedented on
/// its own; blank lines inside a region are kept, so a part that ends in one reproduces the blank
/// line the README has between the two.
/// </remarks>
internal static partial class ReadmeSamples
{
    public const string SamplesProject = "test/CodoMetis.TypeKit.Samples";

    public const string RootReadme = "README.md";

    /// <summary>The root README and every shipping package's, discovered from <c>src/</c>.</summary>
    public static IReadOnlyList<string> Readmes() =>
        [RootReadme, .. Repository.ShippingProjects().Select(project => $"src/{project}/README.md")];

    /// <summary>The prefix of the samples a README shows: <c>README</c>, or the package id.</summary>
    public static string Prefix(string readme) => readme == RootReadme ? "README" : readme.Split('/')[1];

    public static IReadOnlyList<CodeBlock> CodeBlocks() =>
        [.. Readmes().SelectMany(readme => CodeBlocks(readme, File.ReadAllText(Path.Combine(Repository.Root, readme))))];

    public static IReadOnlyList<SampleRegion> Regions()
    {
        var project = Path.Combine(Repository.Root, SamplesProject);

        var regions = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                               .Where(path => !Path.GetRelativePath(project, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                               .Order(StringComparer.Ordinal)
                               .SelectMany(path => Regions(Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'), File.ReadAllText(path)))
                               .ToArray();

        var split = regions.GroupBy(region => region.Name).FirstOrDefault(group => group.Count() > 1);
        if (split is not null)
            throw new InvalidOperationException(
                $"Sample '{split.Key}' has parts in {string.Join(" and ", split.Select(region => region.Location))}. The parts of a sample are joined in file order, so they share one file.");

        return regions;
    }

    public static IReadOnlyList<CodeBlock> CodeBlocks(string readme, string markdown)
    {
        var lines  = markdown.ReplaceLineEndings("\n").Split('\n');
        var blocks = new List<CodeBlock>();

        for (var i = 0; i < lines.Length; i++)
        {
            var opening = FenceOpening().Match(lines[i]);
            if (!opening.Success) continue;

            var fence = opening.Groups["fence"].Value;
            var start = i;
            var body  = new List<string>();

            for (i++; i < lines.Length && !IsClosingFence(lines[i], fence); i++) body.Add(lines[i]);

            if (i == lines.Length) throw new InvalidOperationException($"{readme}:{start + 1}: the code fence is never closed.");

            blocks.Add(new CodeBlock(readme, start + 1, opening.Groups["info"].Value, Trim(Dedent(body)), OptOut(lines, start)));
        }

        return blocks;
    }

    public static IReadOnlyList<SampleRegion> Regions(string file, string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var parts = new List<(string Name, int Line, List<string> Lines)>();
        (string Name, int Line, List<string> Lines)? open = null;

        for (var i = 0; i < lines.Length; i++)
        {
            if (RegionStart().Match(lines[i]) is { Success: true } start)
            {
                if (open is { } outer)
                    throw new InvalidOperationException($"{file}:{i + 1}: sample '{start.Groups["name"].Value}' starts inside sample '{outer.Name}' ({file}:{outer.Line}).");

                open = (start.Groups["name"].Value, i + 1, []);
            }
            else if (RegionEnd().IsMatch(lines[i]))
            {
                if (open is not { } part) throw new InvalidOperationException($"{file}:{i + 1}: '// end sample' ends no sample.");

                if (part.Lines.All(string.IsNullOrWhiteSpace)) throw new InvalidOperationException($"{file}:{part.Line}: sample '{part.Name}' is empty.");

                parts.Add(part);
                open = null;
            }
            else if (LooksLikeAMarker().IsMatch(lines[i]))
            {
                throw new InvalidOperationException(
                    $"{file}:{i + 1}: '{lines[i].Trim()}' reads like a sample marker but is not one. Write '// sample: {{README}}/{{name}}' and '// end sample'.");
            }
            else
            {
                open?.Lines.Add(lines[i]);
            }
        }

        if (open is { } unclosed) throw new InvalidOperationException($"{file}:{unclosed.Line}: sample '{unclosed.Name}' never ends.");

        return
        [
            .. parts.GroupBy(part => part.Name)
                    .Select(sample => new SampleRegion(sample.Key, file, sample.First().Line, Trim(sample.SelectMany(part => Dedent(part.Lines)))))
        ];
    }

    /// <summary>
    /// A line diff of <paramref name="expected"/> against <paramref name="actual"/>: <c>-</c> only in
    /// the first, <c>+</c> only in the second.
    /// </summary>
    public static string Diff(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        var lengths = CommonLengths(a, b);
        var diff = new StringBuilder();

        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length && j < b.Length && a[i] == b[j]) { diff.Append("  ").AppendLine(a[i]); i++; j++; }
            else if (i < a.Length && (j == b.Length || lengths[i + 1, j] >= lengths[i, j + 1])) { diff.Append("- ").AppendLine(a[i]); i++; }
            else { diff.Append("+ ").AppendLine(b[j]); j++; }
        }

        return diff.ToString();
    }

    /// <summary>
    /// How many lines, whitespace aside, the longest common subsequence of the two has: a similarity for
    /// picking the closest candidate, which a realigned comment must not throw off.
    /// </summary>
    public static int CommonLines(string first, string second)
    {
        var a = first.Split('\n').Select(CollapseWhitespace).ToArray();
        var b = second.Split('\n').Select(CollapseWhitespace).ToArray();
        return CommonLengths(a, b)[0, 0];
    }

    /// <summary>The code with every run of whitespace one space: equal for two samples that differ only in spacing.</summary>
    public static string CollapseWhitespace(string code) => Whitespace().Replace(code, " ").Trim();

    /// <summary><c>[i, j]</c> is the length of the longest common subsequence of <c>a[i..]</c> and <c>b[j..]</c>.</summary>
    private static int[,] CommonLengths(string[] a, string[] b)
    {
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        for (var j = b.Length - 1; j >= 0; j--)
            lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        return lengths;
    }

    private static bool IsClosingFence(string line, string fence)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]);
    }

    /// <summary>The reason given by an opt-out marker on the last non-blank line above the fence.</summary>
    private static string? OptOut(string[] lines, int fence)
    {
        var above = fence - 1;
        while (above >= 0 && string.IsNullOrWhiteSpace(lines[above])) above--;

        return above >= 0 && OptOutMarker().Match(lines[above]) is { Success: true } marker ? marker.Groups["reason"].Value.Trim() : null;
    }

    /// <summary>
    /// Trailing whitespace dropped, and the indentation every non-blank line shares. A preprocessor
    /// directive loses all of its own and counts for none: C# style puts <c>#pragma</c> in column 0
    /// whatever the code around it, and a README shows it beside the code.
    /// </summary>
    private static IEnumerable<string> Dedent(IEnumerable<string> lines)
    {
        var trimmed = lines.Select(line => line.TrimEnd()).ToArray();
        var indent  = trimmed.Where(line => line.Length > 0 && !IsDirective(line))
                             .Select(line => line.Length - line.TrimStart().Length)
                             .DefaultIfEmpty(0)
                             .Min();

        return trimmed.Select(line => line.Length == 0 ? line : IsDirective(line) ? line.TrimStart() : line[indent..]);
    }

    private static bool IsDirective(string line) => line.TrimStart().StartsWith('#');

    /// <summary>Leading and trailing blank lines dropped, joined with <c>\n</c>.</summary>
    private static string Trim(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        while (list.Count > 0 && list[0].Length == 0) list.RemoveAt(0);
        while (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);

        return string.Join('\n', list);
    }

    [GeneratedRegex(@"^\s*(?<fence>`{3,}|~{3,})\s*(?<info>[^\s`]*)")]
    private static partial Regex FenceOpening();

    [GeneratedRegex(@"^\s*<!--\s*not compiled\b:?(?<reason>.*?)-->\s*$")]
    private static partial Regex OptOutMarker();

    [GeneratedRegex(@"^\s*// sample: (?<name>[^\s/]+/[^\s/]+)\s*$")]
    private static partial Regex RegionStart();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s*// end sample\s*$")]
    private static partial Regex RegionEnd();

    [GeneratedRegex(@"^\s*//\s*(end[\s-]*)?sample\s*(:|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeAMarker();
}
