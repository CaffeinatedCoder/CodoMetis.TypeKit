using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>docs-query over this repository's own docs, which is what it is for.</summary>
public sealed class RepositoryTests
{
    /// <summary>Built fresh into a temporary cache, never the checkout's own, and the file deleted once read.</summary>
    private static readonly Lazy<DocIndex> Index = new(() =>
    {
        var cache = Path.Combine(Path.GetTempPath(), $"docs-query-repository-{Guid.NewGuid():N}.cache");
        try { return Store.Load(RepositoryRoot.Path, SourceFingerprint.ToolVersion, cache).Index; }
        finally { File.Delete(cache); }
    });

    private static Queries Queries => new(Index.Value, RepositoryRoot.Path);

    /// <summary>Floors, so a discovery that found nothing cannot pass the tests below on an empty corpus.</summary>
    [Theory]
    [InlineData(Kind.Record, 90)]
    [InlineData(Kind.Guide, 10)]
    [InlineData(Kind.Package, 7)]
    [InlineData(Kind.Spike, 10)]
    [InlineData(Kind.Fact, 9)]
    [InlineData(Kind.Note, 8)]
    [InlineData(Kind.Index, 4)]
    public void The_corpus_is_discovered(string kind, int floor) => Index.Value.Docs.Count(doc => doc.Kind == kind).ShouldBeGreaterThanOrEqualTo(floor);

    [Fact]
    public void A_diagnostic_s_entry_is_its_heading()
    {
        var readme = "src/CodoMetis.TypeKit.Analyzers/README.md";
        var line   = Array.IndexOf(File.ReadAllLines(Path.Combine(RepositoryRoot.Path, readme)), "## CMTK0010") + 1;

        line.ShouldBeGreaterThan(0);
        Queries.Entry("CMTK0010").Rows[0].Fields[0].Value.ShouldBe($"{readme}:{line}");
    }

    /// <summary>
    /// Every C# block of the README and docs pages finds the sample region the convention tests hold it
    /// to, or says it is not compiled: the prober reads its claims from these pointers.
    /// </summary>
    [Fact]
    public void Every_C_sharp_claim_names_its_region_or_its_opt_out()
    {
        var blocks = Queries.Claims(null, "csharp").Rows;

        blocks.Count.ShouldBeGreaterThanOrEqualTo(150);
        blocks.Select(row => row.Fields.Single(f => f.Name == "compiled").Value)
              .Where(compiled => compiled == "no region").ShouldBeEmpty();
    }

    /// <summary>The repository's links and ids check clean; a problem here is a broken link or an id nothing defines.</summary>
    [Fact]
    public void The_repository_checks_clean()
    {
        var answer = Queries.Check();
        answer.Rows.ShouldBeEmpty(answer.Text());
    }

    /// <summary>
    /// Floors on the places the citation rule reads and on the citations it finds there (design/adr/0107), so that a
    /// scan that found nothing cannot let the check above pass. On 2026-10-17: 3 refused entries, 41 not-promised
    /// entries, 11 obligation declarations and 87 harness and probe sources were read, and KB-001 and KB-027 were
    /// cited by not-promised entries, KB-037 twice by the harness.
    /// </summary>
    [Theory]
    [InlineData(FactCitations.Refused, 3, 0)]
    [InlineData(FactCitations.NotPromised, 30, 2)]
    [InlineData(FactCitations.Obligation, 10, 0)]
    [InlineData(FactCitations.Harness, 40, 2)]
    public void The_corpus_s_citations_of_facts_are_found(string kind, int read, int cited)
    {
        var scan = FactCitations.Find(RepositoryRoot.Path);

        scan.Read[kind].ShouldBeGreaterThanOrEqualTo(read);
        scan.Of(kind).ShouldBeGreaterThanOrEqualTo(cited);
    }

    [Fact]
    public void Every_record_is_listed_with_its_status_and_date() =>
        Queries.List(["kind=adr"]).Rows.ShouldAllBe(row => row.Fields.Single(f => f.Name == "status").Value.Length > 0 && row.Fields.Single(f => f.Name == "date").Value.Length == 10);
}
