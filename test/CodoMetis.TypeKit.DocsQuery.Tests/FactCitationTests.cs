using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>
/// A fact is executable or it explains only (design/adr/0107): <c>check</c> fails a fact the probe corpus cites to
/// prune or excuse a row (a refused entry of the axes file, a not-promised entry or an obligation's declaration in
/// claims.json, a <c>Cite.Fact</c> or <c>Baseline.Fact</c> of the harness) when it has no test, and a <c>fixture:</c>
/// whose file is missing or not beside the test.
/// </summary>
public sealed class FactCitationTests : IDisposable
{
    private readonly TempRepo _repo = Store(new TempRepo());

    public void Dispose() => _repo.Dispose();

    private static string Fact(int number, string test, string fixture = "") =>
        $"""
        ---
        verified: 2026-10-15
        pins:
          - sdk 10.0.4xx
        source: the framework's own output, measured
        turned-on: decision 1
        test: {test}
        {fixture}
        ---

        # KB-{number:D3}. A fact
        """;

    /// <summary>KB-001 tested, with a fixture; KB-002 prose only.</summary>
    private static TempRepo Store(TempRepo repo) => repo
        .With("global.json", """{ "sdk": { "version": "10.0.402", "rollForward": "latestPatch" } }""")
        .With("design/adr/0001-first.md", "# 0001. First\n\nDate: 2026-10-01\nStatus: accepted\n")
        .With("design/knowledge/KB-001-tested.md", Fact(1, "test/CodoMetis.TypeKit.Facts/KB001.cs", "fixture: test/CodoMetis.TypeKit.Facts/KB001.fixture.json"))
        .With("test/CodoMetis.TypeKit.Facts/KB001.cs", "public sealed class KB001 { }\n")
        .With("test/CodoMetis.TypeKit.Facts/KB001.fixture.json", """{ "fact": "KB-001", "answers": [] }""")
        .With("design/knowledge/KB-002-prose.md", Fact(2, "none, a sentence of the framework's documentation"));

    /// <summary>The axes file, its refused entries citing <paramref name="fact"/> in their reason.</summary>
    private TempRepo Axes(string fact) => _repo.With(".claude/probe-axes.md",
        $$"""
        # Axes

        ```json probe-axes
        {
          "axes": {},
          "refused": {
            "json.ReadCommentHandling": {
              "Allow": "System.Text.Json refuses it on the options object"
            },
            "mvc.MaxValidationDepth": {
              "0": "MVC refuses it, fact:{{fact}}"
            }
          }
        }
        ```
        """);

    /// <summary>claims.json with a not-promised entry whose source is <paramref name="source"/> and an obligation declaration citing <paramref name="cite"/>.</summary>
    private TempRepo Claims(string source, string cite) => _repo.With("tools/probe/corpus/claims.json",
        $$"""
        [
          {
            "id": "C01",
            "source": "README.md#a",
            "obligations": [
              {
                "id": "binds",
                "sentence": "It binds.",
                "inapplicable": [
                  { "axes": { "x": ["1"] }, "cite": "not-promised:np" },
                  { "axes": { "x": ["2"] }, "cite": "{{cite}}" }
                ]
              }
            ],
            "not-promised": [
              { "id": "np", "source": "{{source}}", "sentence": "So.", "axes": { "x": ["1"] } }
            ]
          }
        ]
        """);

    private List<string> Problems() =>
        [.. _repo.Queries().Check().Rows.Select(row => $"{row.Fields[0].Value} {row.Fields[1].Value} {row.Fields[2].Value}")];

    [Fact]
    public void Citations_of_a_tested_fact_check_clean_and_are_counted_by_kind()
    {
        Axes("KB-001");
        Claims("design/knowledge/KB-001-tested.md#kb-001-a-fact", "fact:KB-001");
        _repo.With("tools/probe/corpus/harness/Http.cs", "class Http { void M() { c.Inapplicable(Cite.Fact(\"KB-001\"), \"why\"); } }\n")
             .With("tools/probe/corpus/probes/P.cs", "class P { Baseline b = Baseline.Fact( \"KB-001\" ); }\n");

        var answer = _repo.Queries().Check();

        answer.Rows.ShouldBeEmpty(answer.Text());
        answer.Summary.ShouldContain("5 fact citations (1 refused, 1 not-promised, 1 obligation, 2 harness)");
        var scan = FactCitations.Find(_repo.Root);
        scan.Read.ShouldBe(new Dictionary<string, int> { ["refused"] = 2, ["not-promised"] = 1, ["obligation"] = 2, ["harness"] = 2 });
        scan.Citations.Select(c => $"{c.Kind} {c.At} {c.Fact} {c.Baseline}").ShouldBe(
        [
            "refused .claude/probe-axes.md:11 KB-001 False",
            "obligation tools/probe/corpus/claims.json:11 KB-001 False",
            "not-promised tools/probe/corpus/claims.json:16 KB-001 False",
            "harness tools/probe/corpus/harness/Http.cs:1 KB-001 False",
            "harness tools/probe/corpus/probes/P.cs:1 KB-001 True",
        ]);
    }

    [Fact]
    public void A_refused_entry_citing_a_fact_without_a_test_fails_where_it_is_written()
    {
        Axes("KB-002");

        Problems().ShouldBe([".claude/probe-axes.md:11 fact-not-executable-cited KB-002"]);
        _repo.Queries().Check().Rows.Single().Fields.Single(f => f.Name == "detail").Value
             .ShouldBe("the refused entry mvc.MaxValidationDepth=0 cites KB-002, and its test: is 'none, a sentence of the framework's documentation': " +
                       "a fact without its test explains only, never prunes or excuses a row; write its test in test/CodoMetis.TypeKit.Facts/, or cite a docs sentence");
    }

    [Fact]
    public void A_not_promised_entry_whose_source_is_a_fact_without_a_test_fails()
    {
        Claims("design/knowledge/KB-002-prose.md#kb-002-a-fact", "fact:KB-001");

        Problems().ShouldBe(["tools/probe/corpus/claims.json:16 fact-not-executable-cited KB-002"]);
    }

    [Fact]
    public void An_obligation_s_declaration_citing_a_fact_without_a_test_fails()
    {
        Claims("README.md#a", "fact:KB-002");

        Problems().ShouldBe(["tools/probe/corpus/claims.json:11 fact-not-executable-cited KB-002"]);
    }

    [Fact]
    public void A_harness_citation_of_a_fact_without_a_test_fails_and_one_in_a_comment_does_not_count()
    {
        _repo.With("tools/probe/corpus/harness/Http.cs",
            """
            class Http
            {
                // Cite.Fact("KB-002") is only mentioned here
                /* and Cite.Fact("KB-002") here */
                string url = "http://example"; object o = Cite.Fact("KB-002");
                void M() => c.Inapplicable(Cite.Fact("KB-002"), "why");
            }
            """);

        Problems().ShouldBe(["tools/probe/corpus/harness/Http.cs:5 fact-not-executable-cited KB-002", "tools/probe/corpus/harness/Http.cs:6 fact-not-executable-cited KB-002"]);
    }

    [Fact]
    public void A_cited_fact_whose_test_file_is_missing_fails_twice()
    {
        _repo.With("design/knowledge/KB-003-test-gone.md", Fact(3, "test/CodoMetis.TypeKit.Facts/KB003.cs"))
             .With("tools/probe/corpus/probes/P.cs", "class P { object o = Cite.Fact(\"KB-003\"); }\n");

        Problems().ShouldBe(
        [
            "design/knowledge/KB-003-test-gone.md:7 fact-untested test/CodoMetis.TypeKit.Facts/KB003.cs",
            "tools/probe/corpus/probes/P.cs:1 fact-not-executable-cited KB-003",
        ]);
    }

    [Fact]
    public void A_cited_fact_that_does_not_exist_fails()
    {
        _repo.With("tools/probe/corpus/probes/P.cs", "class P { object o = Cite.Fact(\"KB-099\"); }\n");

        Problems().ShouldBe(["tools/probe/corpus/probes/P.cs:1 fact-not-executable-cited KB-099"]);
    }

    /// <summary>A baseline is the fact's measured answer, so it takes the fixture as well as the test.</summary>
    [Fact]
    public void A_baseline_of_a_tested_fact_without_a_fixture_fails()
    {
        _repo.With("design/knowledge/KB-004-no-fixture.md", Fact(4, "test/CodoMetis.TypeKit.Facts/KB004.cs"))
             .With("test/CodoMetis.TypeKit.Facts/KB004.cs", "public sealed class KB004 { }\n")
             .With("tools/probe/corpus/harness/Http.cs", "class Http { Baseline a = Baseline.Fact(\"KB-001\"), b = Baseline.Fact(\"KB-004\"); object c = Cite.Fact(\"KB-004\"); }\n");

        Problems().ShouldBe(["tools/probe/corpus/harness/Http.cs:1 fact-not-executable-cited KB-004"]);
    }

    [Fact]
    public void A_fixture_must_exist_beside_the_test_of_a_tested_fact()
    {
        _repo.With("design/knowledge/KB-005-fixture-gone.md", Fact(5, "test/CodoMetis.TypeKit.Facts/KB005.cs", "fixture: test/CodoMetis.TypeKit.Facts/KB005.fixture.json"))
             .With("test/CodoMetis.TypeKit.Facts/KB005.cs", "public sealed class KB005 { }\n")
             .With("design/knowledge/KB-006-fixture-elsewhere.md", Fact(6, "test/CodoMetis.TypeKit.Facts/KB006.cs", "fixture: test/fixtures/KB006.json"))
             .With("test/CodoMetis.TypeKit.Facts/KB006.cs", "public sealed class KB006 { }\n")
             .With("test/fixtures/KB006.json", "{}")
             .With("design/knowledge/KB-007-fixture-untested.md", Fact(7, "none, prose", "fixture: test/CodoMetis.TypeKit.Facts/KB007.fixture.json"))
             .With("test/CodoMetis.TypeKit.Facts/KB007.fixture.json", "{}")
             .With("test/CodoMetis.TypeKit.Facts/KB008.fixture.json", "{}");

        Problems().ShouldBe(
        [
            "design/knowledge/KB-005-fixture-gone.md:8 fact-fixture-missing test/CodoMetis.TypeKit.Facts/KB005.fixture.json",
            "design/knowledge/KB-006-fixture-elsewhere.md:8 fact-fixture test/fixtures/KB006.json",
            "design/knowledge/KB-007-fixture-untested.md:8 fact-fixture test/CodoMetis.TypeKit.Facts/KB007.fixture.json",
            "test/CodoMetis.TypeKit.Facts/KB008.fixture.json:1 fact-orphan-fixture test/CodoMetis.TypeKit.Facts/KB008.fixture.json",
        ]);
    }

    [Fact]
    public void Comments_are_blanked_and_strings_kept()
    {
        var source = "a // b\nc /* d\ne */ f \"// g\" @\"h \"\" // i\" \"\"\"raw // j\"\"\" '\"' k";

        FactCitations.WithoutComments(source).ShouldBe("a     \nc     \n     f \"// g\" @\"h \"\" // i\" \"\"\"raw // j\"\"\" '\"' k");
    }
}
