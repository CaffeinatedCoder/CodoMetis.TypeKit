using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// The files under <c>test/oracles/</c>, loaded once: the claims the documents make, the oracle classes that hold
/// them, the seeds that prove the oracles can fail, the waivers for public members no claim covers, and the known
/// disagreements (design/adr/0112).
/// </summary>
/// <remarks>
/// Everything here is data a maintainer edits by hand, and the <c>Oracle*Tests</c> hold it consistent with the
/// repository: a claim's quote must still be in its document, a surface must still exist, an oracle must still be a
/// test class with a seed. The tests are what make the catalogue a worklist rather than a wish list.
/// </remarks>
internal static partial class OracleCatalogue
{
    // Declared first: static initializers run in textual order, and the loaders below use it.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static string Root { get; } = FindRoot();

    internal static string Directory { get; } = Path.Combine(Root, "test", "oracles");

    internal static IReadOnlyList<Claim> Claims { get; } = Load<List<Claim>>("claims.json");

    internal static IReadOnlyList<Oracle> Oracles { get; } = Load<List<Oracle>>("oracles.json");

    /// <summary>Surface patterns no claim names, each with the reason it needs no oracle.</summary>
    internal static IReadOnlyDictionary<string, string> SurfaceWaivers { get; } = Load<Dictionary<string, string>>("surface-waivers.json");

    internal static IReadOnlyList<Seed> Seeds { get; } =
        [.. new DirectoryInfo(Path.Combine(Directory, "seeds")).EnumerateFiles("*.patch").OrderBy(file => file.Name, StringComparer.Ordinal).Select(Seed.Read)];

    /// <summary>The documents that make promises to consumers; every one must carry at least one claim.</summary>
    internal static readonly string[] PromiseDocuments =
    [
        "README.md",
        "docs/json.md",
        "docs/text.md",
        "docs/merge-patches.md",
        "docs/option.md",
        "docs/result.md",
        "docs/ef-core.md",
        "docs/type-constraints.md",
        "docs/walkthrough.md",
        "src/CodoMetis.TypeKit.AspNetCore/README.md",
        "src/CodoMetis.TypeKit.EntityFrameworkCore/README.md",
        "src/CodoMetis.TypeKit.Generators/README.md",
        "src/CodoMetis.TypeKit.Analyzers/README.md",
        "src/CodoMetis.TypeKit.Testing/README.md",
        "src/CodoMetis.TypeKit.Serilog/README.md",
    ];

    /// <summary>Every test class under <c>test/</c>, a class whose file holds a <c>[Fact]</c> or a <c>[Theory]</c>, with its project.</summary>
    internal static IReadOnlyDictionary<string, string> TestClasses { get; } = DiscoverTestClasses();

    /// <summary>GitHub's heading slug: lower case, punctuation dropped, spaces to hyphens.</summary>
    internal static string Slug(string heading) =>
        Regex.Replace(Regex.Replace(heading.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}\s-]", ""), @"\s", "-");

    /// <summary>Headings of a Markdown file, as slugs, in order.</summary>
    internal static IReadOnlyList<string> HeadingSlugs(string relativePath)
    {
        var inCode = false;
        var slugs = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(Root, relativePath)))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
            if (inCode) continue;
            var match = Regex.Match(line, @"^#{1,6}\s+(.*?)\s*#*\s*$");
            if (match.Success) slugs.Add(Slug(match.Groups[1].Value));
        }

        return slugs;
    }

    /// <summary>A file's text with every run of whitespace collapsed to one space, for quote matching across line wraps.</summary>
    internal static string Flattened(string relativePath) => Regex.Replace(File.ReadAllText(Path.Combine(Root, relativePath)), @"\s+", " ");

    private static T Load<T>(string file)
    {
        var path = Path.Combine(Directory, file);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new InvalidOperationException($"'{path}' is empty.");
    }

    private static Dictionary<string, string> DiscoverTestClasses()
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        var testRoot = Path.Combine(Root, "test");
        var separator = Path.DirectorySeparatorChar;

        foreach (var file in System.IO.Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{separator}obj{separator}", StringComparison.Ordinal) || file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            if (!TestAttribute().IsMatch(text)) continue;

            var project = Path.GetRelativePath(testRoot, file).Split(separator)[0];
            foreach (Match match in ClassDeclaration().Matches(text))
                classes.TryAdd(match.Groups["name"].Value, project);
        }

        return classes;
    }

    private static string FindRoot()
    {
        const string marker = "CodoMetis.TypeKit.slnx";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, marker))) return directory.FullName;
        }

        throw new InvalidOperationException($"Could not find '{marker}' above '{AppContext.BaseDirectory}'.");
    }

    [GeneratedRegex(@"\[(Fact|Theory)\b")]
    private static partial Regex TestAttribute();

    [GeneratedRegex(@"(?m)^(?:public|internal)\s+(?:sealed\s+|static\s+|partial\s+|abstract\s+)*class\s+(?<name>\w+)")]
    private static partial Regex ClassDeclaration();

    /// <summary>A sentence a document states about the library's behaviour, and what holds it.</summary>
    internal sealed class Claim
    {
        public string Id { get; init; } = "";

        /// <summary><c>file#heading-slug</c>: where the sentence is.</summary>
        public string Source { get; init; } = "";

        /// <summary>The sentence, verbatim, so the catalogue test can find it in the file.</summary>
        public string Quote { get; init; } = "";

        /// <summary>The restatement as "under conditions, observable result".</summary>
        [JsonPropertyName("claim")]
        public string Statement { get; init; } = "";

        /// <summary>claim, differential, metamorphic, exception or silent-default.</summary>
        public string Oracle { get; init; } = "";

        /// <summary>The public members a test of the claim calls, as <see cref="ApiSurface"/> patterns.</summary>
        public IReadOnlyList<string> Surfaces { get; init; } = [];

        /// <summary>The test classes that assert the claim; empty is the worklist.</summary>
        public IReadOnlyList<string> HeldBy { get; init; } = [];

        /// <summary>The source file, before the <c>#</c>.</summary>
        internal string File => Source.Split('#')[0];

        /// <summary>The heading slug after the <c>#</c>, or null for a claim about the whole file.</summary>
        internal string? Heading => Source.Contains('#') ? Source[(Source.IndexOf('#') + 1)..] : null;
    }

    /// <summary>A test class that checks the library against an independent answer over many cases.</summary>
    internal sealed record Oracle(string Class, string Project, string Kind, string Against);

    /// <summary>A patch under <c>seeds/</c>: its header lines, the oracles it names and the files it changes.</summary>
    internal sealed record Seed(FileInfo File, string Defect, IReadOnlyList<string> Oracles, string Body)
    {
        internal static Seed Read(FileInfo file)
        {
            var text = System.IO.File.ReadAllText(file.FullName);
            var diffAt = text.IndexOf("diff --git", StringComparison.Ordinal);
            var header = diffAt < 0 ? text : text[..diffAt];
            return new Seed(
                file,
                HeaderValue(header, "Seed"),
                HeaderValue(header, "Oracles").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                diffAt < 0 ? "" : text[diffAt..]);
        }

        /// <summary>The <c>a/</c> paths the diff changes.</summary>
        internal IReadOnlyList<string> ChangedFiles =>
            [.. Regex.Matches(Body, @"^diff --git a/(?<path>\S+) b/", RegexOptions.Multiline).Select(match => match.Groups["path"].Value)];

        private static string HeaderValue(string header, string key)
        {
            var match = Regex.Match(header, $@"^{key}:\s*(?<value>.+?)\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups["value"].Value : "";
        }
    }
}
