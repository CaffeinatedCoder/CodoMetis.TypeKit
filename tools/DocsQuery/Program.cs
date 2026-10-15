using System.Globalization;

namespace DocsQuery;

internal static class Program
{
    private const string Usage =
        """
        docs-query: pointers into the repository's Markdown (file, heading path, line), never prose.

          entry <id>             what an id names: a record (61, 0061, decision 61), a diagnostic
                                 (CMTK0004), a path (docs/json.md#preserve), an anchor (#preserve),
                                 a spike, package or guide name, a heading's text
          outline <doc>          a document's headings, with lines and anchors
          cites <id>             the edges out of an entry: link, superseded-by, mention, code
          cited-by <id>          the edges into it; --depth N follows links and supersessions only
          list [filter...]       documents, filtered: kind=adr status=open date>=2026-10-01 title~json
          find <words>           ranked (BM25); --top N, --kind K. A miss proves nothing
          claims [page]          every C# block, stated output and shown block of the README and
                                 docs pages; --kind csharp|stated|block
          check                  broken links, missing anchors, ids defined nowhere

          --json                 one JSON object per line, the summary first
          --root <dir>           another checkout; default: the one this dll was built in

        Exit codes: 0 found, 1 nothing found (or problems, for check), 2 usage, 3 the dll is stale.
        Kinds: readme, package, guide, note, roadmap, adr, spike, index (resolves lookups, never ranked).
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);

        var positional = new List<string>();
        string? root = null, kind = null;
        int top = 10, depth = 1;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--kind" when i + 1 < args.Length: kind = args[++i]; break;
                case "--top" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var n) && n > 0: top = n; i++; break;
                case "--depth" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var n) && n > 0: depth = n; i++; break;
                case var option when option.StartsWith("--", StringComparison.Ordinal): return Fail($"unknown or incomplete option {option}.");
                default: positional.Add(args[i]); break;
            }
        }

        if (positional.Count == 0 || positional[0] is "help" or "-h") { Console.Write(Usage); return positional.Count == 0 ? 2 : 0; }

        var home = Corpus.FindRoot(AppContext.BaseDirectory);
        if (home is not null && Freshness.Ensure(home, args) is { } handedOver) return handedOver;

        root = root is null ? home ?? Corpus.FindRoot(Directory.GetCurrentDirectory()) : Path.GetFullPath(root);
        if (root is null || !File.Exists(Path.Combine(root, Corpus.SolutionMarker))) return Fail($"no {Corpus.SolutionMarker} found; pass --root.");

        var (index, _) = Store.Load(root, Freshness.ToolVersion);
        var queries    = new Queries(index, root);
        var query      = string.Join(' ', positional.Skip(1));

        Answer? answer = positional[0] switch
        {
            "entry" when query.Length > 0    => queries.Entry(query),
            "outline" when query.Length > 0  => queries.Outline(query),
            "cites" when query.Length > 0    => queries.Edges(query, depth, reverse: false),
            "cited-by" when query.Length > 0 => queries.Edges(query, depth, reverse: true),
            "list"                           => queries.List([.. positional.Skip(1)]),
            "find" when query.Length > 0     => queries.Find(query, top, kind),
            "claims"                         => queries.Claims(query.Length > 0 ? query : null, kind),
            "check"                          => queries.Check(),
            _                                => null,
        };

        if (answer is null) return Fail($"'{string.Join(' ', positional)}' is no command; run `help`.");

        Console.Write(json ? answer.Json() : answer.Text());
        return answer.ExitCode;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"docs-query: {message}");
        return 2;
    }
}
