using System.Security.Cryptography;
using System.Text;

namespace DocsQuery;

/// <summary>What a document is, read from its path alone.</summary>
internal static class Kind
{
    public const string Readme   = "readme";   // README.md at the root
    public const string Package  = "package";  // src/{package}/README.md
    public const string Guide    = "guide";    // docs/*.md
    public const string Note     = "note";     // design/*.md, a design note
    public const string Roadmap  = "roadmap";  // design/roadmap.md
    public const string Record   = "adr";      // design/adr/NNNN-*.md
    public const string Spike    = "spike";    // spikes/*/README.md
    public const string Index    = "index";    // design/plan.md, design/adr/README.md, src/*/AnalyzerReleases.*.md

    /// <summary>
    /// A curated or generated index resolves lookups but is left out of ranking, so a record and the
    /// index row describing it never compete for the same query.
    /// </summary>
    public static bool IsRanked(string kind) => kind != Index;

    /// <summary>The pages whose code is compiled and whose stated outputs are tested: the claims' pages.</summary>
    public static bool HasClaims(string kind) => kind is Readme or Package or Guide;

    public static string Of(string path)
    {
        var parts = path.Split('/');

        return parts switch
        {
            ["README.md"]                                                  => Readme,
            ["src", _, "README.md"]                                        => Package,
            ["src", _, var name] when name.StartsWith("AnalyzerReleases.", StringComparison.Ordinal) => Index,
            ["docs", _]                                                    => Guide,
            ["design", "plan.md"]                                          => Index,
            ["design", "roadmap.md"]                                       => Roadmap,
            ["design", "adr", "README.md"]                                 => Index,
            ["design", "adr", var name] when Ids.RecordFile(name) is not null => Record,
            ["design", ..]                                                 => Note,
            ["spikes", _, "README.md"]                                     => Spike,
            _ => throw new InvalidOperationException($"{path} is in no kind of the corpus."),
        };
    }
}

/// <summary>The files the index is built from, and the fingerprint that says whether a cache still describes them.</summary>
internal static class Corpus
{
    public const string SolutionMarker = "CodoMetis.TypeKit.slnx";

    /// <summary>Where the compiled samples live; their regions are read so a claim can name the region that compiles it.</summary>
    public const string SamplesProject = "test/CodoMetis.TypeKit.Samples";

    /// <summary>The repository root, found by walking up to the solution file, never by a positional path.</summary>
    public static string? FindRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionMarker))) return directory.FullName;
        }

        return null;
    }

    /// <summary>The Markdown documents, relative to the root with <c>/</c>, in ordinal order.</summary>
    public static IReadOnlyList<string> Documents(string root)
    {
        var paths = new List<string>();

        void Add(string relativeDirectory, string pattern, SearchOption option)
        {
            var directory = Path.Combine(root, relativeDirectory);
            if (!Directory.Exists(directory)) return;

            paths.AddRange(Directory.EnumerateFiles(directory, pattern, option).Select(path => Relative(root, path)).Where(path => !InBuildOutput(path)));
        }

        if (File.Exists(Path.Combine(root, "README.md"))) paths.Add("README.md");
        Add("src", "README.md", SearchOption.AllDirectories);
        Add("src", "AnalyzerReleases.*.md", SearchOption.AllDirectories);
        Add("docs", "*.md", SearchOption.TopDirectoryOnly);
        Add("design", "*.md", SearchOption.AllDirectories);
        Add("spikes", "README.md", SearchOption.AllDirectories);

        return [.. paths.Where(path => path.Split('/') is ["README.md"] or ["src", _, _] or ["docs", _] or ["design", ..] or ["spikes", _, "README.md"])
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)];
    }

    /// <summary>The samples project's C# files, whose <c>// sample:</c> regions compile the pages' blocks.</summary>
    public static IReadOnlyList<string> Samples(string root)
    {
        var directory = Path.Combine(root, SamplesProject);
        if (!Directory.Exists(directory)) return [];

        return [.. Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                            .Select(path => Relative(root, path))
                            .Where(path => !InBuildOutput(path))
                            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The fingerprint of an index: the schema version, the tool's own version (the hash of its
    /// sources) and every input's relative path and content hash, in ordinal order. Never a timestamp:
    /// a restore that keeps an old mtime must not keep an old index.
    /// </summary>
    public static string Fingerprint(int schema, string toolVersion, IEnumerable<(string Path, byte[] Content)> inputs)
    {
        var manifest = new StringBuilder();
        manifest.Append("schema ").Append(schema).Append('\n');
        manifest.Append("tool ").Append(toolVersion).Append('\n');

        foreach (var (path, content) in inputs.OrderBy(input => input.Path, StringComparer.Ordinal))
            manifest.Append(path).Append('\0').Append(Hash(content)).Append('\n');

        return Hash(Encoding.UTF8.GetBytes(manifest.ToString()));
    }

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool InBuildOutput(string path) => path.Split('/').Any(part => part is "bin" or "obj" or "node_modules");
}
