using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>The commands over a small corpus with one of every kind of document and edge.</summary>
public sealed class QueryTests : IDisposable
{
    private readonly TempRepo _repo = TempRepo.Sample();

    public void Dispose() => _repo.Dispose();

    private static IReadOnlyList<string> Column(Answer answer, string name) =>
        [.. answer.Rows.Select(row => row.Fields.Single(field => field.Name == name).Value)];

    [Fact]
    public void An_entry_points_at_the_definition_first_and_labels_index_rows()
    {
        var answer = _repo.Queries().Entry("CMTK0004");

        answer.Contract.ShouldBe(Answer.Exact);
        Column(answer, "at").ShouldBe(["src/Kit.Analyzers/README.md:7", "src/Kit.Analyzers/AnalyzerReleases.Shipped.md:3"]);
        Column(answer, "kind").ShouldBe(["definition", "index-row"]);
    }

    [Fact]
    public void A_table_row_defines_an_id() =>
        Column(_repo.Queries().Entry("CMTK1011"), "at").ShouldBe(["src/Kit.Analyzers/README.md:5"]);

    [Fact]
    public void A_record_answers_to_its_number_in_any_spelling()
    {
        foreach (var spelling in new[] { "1", "0001", "decision 1", "ADR-0001", "design/adr/0001-first.md" })
            Column(_repo.Queries().Entry(spelling), "at").First().ShouldBe("design/adr/0001-first.md:1", spelling);
    }

    [Fact]
    public void An_exact_miss_says_so_and_fails()
    {
        var answer = _repo.Queries().Entry("CMTK9999");
        answer.ExitCode.ShouldBe(1);
        answer.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void An_anchor_a_path_and_a_heading_resolve()
    {
        var queries = _repo.Queries();
        Column(queries.Entry("docs/json.md#preserve"), "at").ShouldBe(["docs/json.md:5"]);
        Column(queries.Entry("#preserve"), "at").ShouldBe(["docs/json.md:5"]);
        Column(queries.Entry("Preserve"), "at").ShouldBe(["docs/json.md:5"]);
        Column(queries.Entry("Probe"), "at").ShouldBe(["spikes/Probe/README.md:1"]);
    }

    /// <summary>Each edge carries its kind and source line; a mention inside the link that already names the id is not counted twice.</summary>
    [Fact]
    public void Edges_are_typed_with_their_source_line()
    {
        var answer = _repo.Queries().Edges("1", depth: 1, reverse: true);

        answer.Rows.Select(row => (row.Fields.Single(f => f.Name == "at").Value, row.Fields.Single(f => f.Name == "kind").Value)).ShouldBe(
        [
            ("design/adr/0002-second.md:6", "link"),
            ("design/adr/README.md:5", "link"),
            ("docs/json.md:7", "link"),
        ]);
    }

    [Fact]
    public void A_supersession_a_mention_and_code_are_edges_of_their_own_kinds()
    {
        var into2 = _repo.Queries().Edges("2", depth: 1, reverse: true);
        Column(into2, "kind").ShouldBe(["link", "superseded-by", "link", "mention"]);

        var intoCmtk = _repo.Queries().Edges("CMTK0004", depth: 1, reverse: true);
        Column(intoCmtk, "kind").ShouldBe(["code", "mention", "mention"]);
        Column(intoCmtk, "from").ShouldBe([Kind.Note, Kind.Guide, Kind.Index]);
    }

    /// <summary>An id named where it is defined is no edge: a record naming itself, or a diagnostic inside its own section.</summary>
    [Fact]
    public void An_id_inside_its_own_definition_is_no_edge()
    {
        var into1 = _repo.Queries().Edges("1", depth: 1, reverse: true);
        Column(into1, "at").ShouldNotContain(at => at.StartsWith("design/adr/0001-first.md", StringComparison.Ordinal));

        Column(_repo.Queries().Edges("CMTK0004", depth: 1, reverse: true), "at").ShouldNotContain(at => at.StartsWith("src/Kit.Analyzers/README.md", StringComparison.Ordinal));
    }

    /// <summary>
    /// A transitive walk follows links and supersessions only. From the README, depth 2 reaches the
    /// record's links (the notes), never its prose mention of CMTK1011.
    /// </summary>
    [Fact]
    public void A_transitive_walk_follows_explicit_edges_only()
    {
        var answer = _repo.Queries().Edges("README.md", depth: 3, reverse: false);
        var deeper = answer.Rows.Where(row => row.Fields.Single(f => f.Name == "depth").Value != "1").ToList();

        deeper.ShouldNotBeEmpty();
        deeper.Select(row => row.Fields.Single(f => f.Name == "kind").Value).ShouldAllBe(kind => kind == "link" || kind == "superseded-by");
        Column(answer, "target").ShouldContain("design/notes.md");
        Column(answer, "target").ShouldNotContain("CMTK1011", "the record's mention is not followed");
    }

    [Fact]
    public void A_list_filters_on_fields()
    {
        var queries = _repo.Queries();
        Column(queries.List(["kind=adr"]), "id").ShouldBe(["0001", "0002"]);
        Column(queries.List(["kind=adr", "status=superseded"]), "id").ShouldBe(["0001"]);
        Column(queries.List(["date>=2026-10-02"]), "id").ShouldBe(["0002"]);
        Column(queries.List(["kind=spike"]), "status").ShouldBe(["run 2026-10-03, answered"]);
        queries.List(["nonsense"]).ExitCode.ShouldBe(2);
    }

    /// <summary>A record and the index row describing it never compete: the index is left out of ranking.</summary>
    [Fact]
    public void Ranking_leaves_the_indexes_out()
    {
        var answer = _repo.Queries().Find("preserved references refused", 10, null);

        answer.Contract.ShouldBe(Answer.Ranked);
        Column(answer, "at").First().ShouldBe("design/adr/0001-first.md:1");
        Column(answer, "kind").ShouldNotContain(kind => kind.StartsWith(Kind.Index, StringComparison.Ordinal));
        answer.Summary.ShouldContain("A miss proves nothing");
    }

    /// <summary>A page that names the id more often than its definition does still ranks below the definition.</summary>
    [Fact]
    public void An_exact_id_in_a_query_puts_its_definition_first()
    {
        _repo.With("docs/noise.md", "# Noise\n\n## Using it\n\nCMTK0004, CMTK0004, CMTK0004 and CMTK0004 again.\n");
        var answer = _repo.Queries().Find("CMTK0004", 10, null);
        Column(answer, "at").First().ShouldBe("src/Kit.Analyzers/README.md:7");
        Column(answer, "kind").First().ShouldEndWith("defines-id");
    }

    [Fact]
    public void A_ranked_miss_says_it_proves_nothing()
    {
        var answer = _repo.Queries().Find("zeppelin", 10, null);
        answer.ExitCode.ShouldBe(1);
        answer.Summary.ShouldContain("A miss proves nothing");
    }

    [Fact]
    public void Claims_point_at_blocks_and_stated_outputs_with_their_regions()
    {
        var answer = _repo.Queries().Claims("docs/json.md", null);

        answer.Rows.Select(row => (row.Fields.Single(f => f.Name == "at").Value, row.Fields.Single(f => f.Name == "kind").Value, row.Fields.Single(f => f.Name == "id").Value)).ShouldBe(
        [
            ("docs/json.md:9", "csharp", "json#preserve/csharp-1"),
            ("docs/json.md:10", "stated", "json#preserve/stated-1"),
            ("docs/json.md:11", "stated", "json#preserve/stated-2"),
            ("docs/json.md:15", "block:json", "json#preserve/block:json-1"),
        ]);

        Column(answer, "compiled").Take(3).ShouldAllBe(compiled => compiled.StartsWith("region json/serialize @ test/CodoMetis.TypeKit.Samples/Docs/JsonSamples.cs:5", StringComparison.Ordinal));
        Column(answer, "text")[2].ShouldBe("bool same = json == other; // true, the same bytes");
        Column(answer, "section").First().ShouldBe("JSON > Preserve");
    }

    /// <summary>A claim's id is its page, anchor, kind and order in the section, so text added elsewhere moves its line, never its id.</summary>
    [Fact]
    public void A_claim_s_id_survives_an_edit_elsewhere()
    {
        var before = _repo.Queries().Claims("docs/json.md", "csharp").Rows.Single();
        _repo.With("docs/json.md", File.ReadAllText(Path.Combine(_repo.Root, "docs/json.md")).Replace("byte for byte.", "byte for byte.\n\nA new paragraph.\n", StringComparison.Ordinal));
        var after = _repo.Queries().Claims("docs/json.md", "csharp").Rows.Single();

        after.Fields.Single(f => f.Name == "id").ShouldBe(before.Fields.Single(f => f.Name == "id"));
        after.Fields.Single(f => f.Name == "at").Value.ShouldBe("docs/json.md:12");
    }

    [Fact]
    public void Claims_come_from_the_README_and_docs_pages_only() =>
        _repo.Queries().Claims(null, null).Rows.Select(row => row.Fields.Single(f => f.Name == "at").Value.Split(':')[0]).Distinct().ShouldBe(["docs/json.md"]);

    [Fact]
    public void A_clean_corpus_checks_clean() => _repo.Queries().Check().Rows.ShouldBeEmpty();

    [Fact]
    public void Check_reports_a_broken_link_a_missing_anchor_and_a_dangling_id()
    {
        _repo.With("docs/broken.md", "# Broken\n\n[gone](gone.md), [nowhere](json.md#nowhere), CMTK0999 and decision 99.\n");
        var answer = _repo.Queries().Check();

        answer.ExitCode.ShouldBe(1);
        Column(answer, "kind").ShouldBe(["broken-link", "missing-anchor", "dangling-id", "dangling-id"]);
        Column(answer, "at").ShouldAllBe(at => at == "docs/broken.md:3");
    }

    [Fact]
    public void Json_output_splits_the_pointer()
    {
        var json = _repo.Queries().Entry("CMTK0004").Json().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        json[0].ShouldContain("\"contract\":\"exact\"");
        json[1].ShouldStartWith("{\"path\":\"src/Kit.Analyzers/README.md\",\"line\":7,");
    }
}
