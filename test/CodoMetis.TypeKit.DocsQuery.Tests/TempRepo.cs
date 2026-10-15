using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>A throwaway checkout: the solution marker and the files a test writes, deleted on dispose.</summary>
internal sealed class TempRepo : IDisposable
{
    public TempRepo()
    {
        Root = Path.Combine(Path.GetTempPath(), "docs-query-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, Corpus.SolutionMarker), "<Solution />");
    }

    public string Root { get; }

    public string Cache => Path.Combine(Root, Store.CacheFile);

    public TempRepo With(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text.ReplaceLineEndings("\n"));
        return this;
    }

    public (DocIndex Index, Store.Outcome Outcome) Load(string toolVersion = "test") => Store.Load(Root, toolVersion);

    public Queries Queries() => new(Load().Index, Root);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A small corpus with one of every kind, every kind of edge, and a compiled sample.</summary>
    public static TempRepo Sample() => new TempRepo()
        .With("README.md",
            """
            # Kit

            Strong types. See [JSON](docs/json.md#preserve) and [decision 2](design/adr/0002-second.md).

            ## Documentation

            | Page | Covers |
            |---|---|
            | [JSON](docs/json.md) | JSON |
            """)
        .With("docs/json.md",
            """
            # JSON

            The wrapped type's JSON, byte for byte.

            ## Preserve

            `ReferenceHandler.Preserve` is refused, as [decision 1](../design/adr/0001-first.md) says, and CMTK0004 too.

            ```csharp
            var json = JsonSerializer.Serialize(order); // {"id":1}
            bool same = json == other;
            // true, the same bytes
            ```

            ```json
            {"id":1}
            ```
            """)
        .With("src/Kit.Analyzers/README.md",
            """
            # Kit.Analyzers

            | Id | Meaning |
            |---|---|
            | CMTK1011 | A hand-written member. |

            ## CMTK0004

            Materialize outside the satellite. CMTK0004 again, inside its own section.
            """)
        .With("src/Kit.Analyzers/AnalyzerReleases.Shipped.md",
            """
            ## Release 1.0.0

            CMTK0004 | Security | Error | Materialize
            """)
        .With("design/plan.md",
            """
            # Design

            | Page | Holds |
            |---|---|
            | [Decisions](adr/README.md) | preserve references refused |
            """)
        .With("design/adr/README.md",
            """
            # Decisions

            | # | Decision |
            |---|---|
            | [0001](0001-first.md) | Preserved references are refused |
            | [0002](0002-second.md) | Second |
            """)
        .With("design/adr/0001-first.md",
            """
            # 0001. Preserved references are refused

            Date: 2026-10-01
            Status: superseded by 0002

            A value object refuses `ReferenceHandler.Preserve`. Decision 1 names itself.
            """)
        .With("design/adr/0002-second.md",
            """
            # 0002. Second

            Date: 2026-10-02
            Status: accepted

            Replaces [decision 1](0001-first.md); see the [notes](../notes.md) and CMTK1011.
            """)
        .With("design/notes.md",
            """
            # Notes

            Links [the plan](plan.md). Mentions decision 2 in prose.

            ```csharp
            #pragma warning disable CMTK0004
            ```
            """)
        .With("spikes/Probe/README.md",
            """
            # Spike: does it work?

            **Status: run 2026-10-03, answered.**
            """)
        .With("test/CodoMetis.TypeKit.Samples/Docs/JsonSamples.cs",
            """
            class JsonSamples
            {
                void Run()
                {
                    // sample: json/serialize
                    var json = JsonSerializer.Serialize(order); // {"id":1}
                    bool same = json == other;
                    // true, the same bytes
                    // end sample
                }
            }
            """);
}
