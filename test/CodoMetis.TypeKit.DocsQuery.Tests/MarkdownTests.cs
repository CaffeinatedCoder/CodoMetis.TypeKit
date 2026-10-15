using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

public sealed class MarkdownTests
{
    /// <summary>
    /// The anchors are GitHub's, as the convention test that checks the links derives them: entities,
    /// inline code, links and repeats included, and a line in a fence is no heading.
    /// </summary>
    [Fact]
    public void Anchors_are_derived_as_GitHub_derives_them()
    {
        const string markdown =
            """
            # Title

            ```bash
            # Not a heading
            ```

            ## Option&lt;T&gt;

            ## Result&lt;T, TError&gt; and Result&lt;TError&gt;

            ## The surface

            ## The surface

            ### `Option<T>.Match` and [its overloads](option.md) ###

            ### CMTK0010
            """;

        new Page("docs/page.md", markdown).Headings.Select(heading => heading.Anchor).ShouldBe(
            ["title", "optiont", "resultt-terror-and-resultterror", "the-surface", "the-surface-1", "optiontmatch-and-its-overloads", "cmtk0010"]);
    }

    [Fact]
    public void Headings_carry_their_lines() =>
        new Page("p.md", "# A\n\ntext\n\n## B\n").Headings.Select(heading => (heading.Line, heading.Level, heading.Text)).ShouldBe([(1, 1, "A"), (5, 2, "B")]);

    [Fact]
    public void A_record_s_and_a_spike_s_fields_are_read()
    {
        new Page("design/adr/0001-a.md", "# 0001. A\n\nDate: 2026-10-01\nStatus: superseded by 0002\n\nBody.").Metadata()
            .Select(field => (field.Key, field.Value, field.Line)).ShouldBe([("date", "2026-10-01", 3), ("status", "superseded by 0002", 4)]);

        new Page("spikes/S/README.md", "# Spike\n\n**Status: run 2026-10-03, answered.** More.").Metadata()
            .Select(field => (field.Key, field.Value)).ShouldBe([("status", "run 2026-10-03, answered")]);
    }

    [Theory]
    [InlineData("README.md", Kind.Readme)]
    [InlineData("src/CodoMetis.TypeKit/README.md", Kind.Package)]
    [InlineData("src/CodoMetis.TypeKit.Analyzers/AnalyzerReleases.Shipped.md", Kind.Index)]
    [InlineData("docs/json.md", Kind.Guide)]
    [InlineData("design/plan.md", Kind.Index)]
    [InlineData("design/roadmap.md", Kind.Roadmap)]
    [InlineData("design/adr/README.md", Kind.Index)]
    [InlineData("design/adr/0061-scope.md", Kind.Record)]
    [InlineData("design/openapi.md", Kind.Note)]
    [InlineData("spikes/ClosedSets/README.md", Kind.Spike)]
    public void A_document_s_kind_is_read_from_its_path(string path, string kind) => Kind.Of(path).ShouldBe(kind);

    [Theory]
    [InlineData("docs/json.md", "../design/adr/0001-a.md", "design/adr/0001-a.md", null)]
    [InlineData("docs/json.md", "#preserve", "docs/json.md", "preserve")]
    [InlineData("README.md", "docs/json.md#a%20b", "docs/json.md", "a b")]
    [InlineData("src/P/README.md", "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/json.md#x", "docs/json.md", "x")]
    public void A_link_resolves_against_its_page(string page, string target, string path, string? anchor) =>
        IndexBuilder.Resolve(page, target).ShouldBe((path, anchor));
}
