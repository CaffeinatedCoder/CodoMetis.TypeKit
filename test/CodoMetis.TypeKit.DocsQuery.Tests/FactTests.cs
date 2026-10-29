using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>
/// The verified facts of design/knowledge/ (design/adr/0093): their ids, their frontmatter, and the rules
/// <c>check</c> holds them to, the pins against what Directory.Packages.props and global.json install first.
/// </summary>
public sealed class FactTests : IDisposable
{
    private readonly TempRepo _repo = Facts(new TempRepo());

    public void Dispose() => _repo.Dispose();

    private static IReadOnlyList<string> Column(Answer answer, string name) =>
        [.. answer.Rows.Select(row => row.Fields.Single(field => field.Name == name).Value)];

    private static string Fact(int number, string pins, string test, string body = "") =>
        $"""
        ---
        verified: 2026-10-15
        pins:
        {pins}
        source: the framework's own output, measured
        turned-on: decision 1
        test: {test}
        ---

        # KB-{number:D3}. A fact

        {body}
        """;

    /// <summary>Installed: one package at 10.0.12, a pre-release, and the SDK 10.0.402; one fact that holds every rule.</summary>
    private static TempRepo Facts(TempRepo repo) => repo
        .With("Directory.Packages.props",
            """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.12" />
                <PackageVersion Include="Some.Beta" Version="1.0.0-beta08" />
              </ItemGroup>
            </Project>
            """)
        .With("global.json", """{ "//": ["pinned"], "sdk": { "version": "10.0.402", "rollForward": "latestPatch" } }""")
        .With("design/adr/0001-first.md", "# 0001. First\n\nDate: 2026-10-01\nStatus: accepted\n\nRests on KB-001.\n")
        .With("design/knowledge/README.md", "# Verified facts\n\n| Id | Fact |\n|---|---|\n| [KB-001](KB-001-a-fact.md) | A fact |\n")
        .With("design/knowledge/KB-001-a-fact.md", Fact(1, "  - Microsoft.AspNetCore.OpenApi 10.0.12\n  - sdk 10.0.4xx (the runtime)\n  - external Vogen 8.0.7", "test/CodoMetis.TypeKit.Facts/KB001.cs"))
        .With("test/CodoMetis.TypeKit.Facts/KB001.cs", "public sealed class KB001 { }\n");

    [Fact]
    public void A_clean_store_checks_clean()
    {
        var answer = _repo.Queries().Check();

        answer.Rows.ShouldBeEmpty(answer.Text());
        answer.Summary.ShouldEndWith("and 1 fact");
    }

    /// <summary>A note after the test's path, such as where it runs, leaves the path the test: the fact is tested and the test named.</summary>
    [Fact]
    public void A_test_named_with_a_note_after_its_path_is_the_fact_s_test()
    {
        _repo.With("design/knowledge/KB-002-noted.md", Fact(2, "  - sdk 10.0.4xx", "test/CodoMetis.TypeKit.Facts/KB002.cs (runs only with A_SERVER set, until a CI service)"))
             .With("test/CodoMetis.TypeKit.Facts/KB002.cs", "public sealed class KB002 { }\n");

        var answer = _repo.Queries().Check();

        answer.Rows.ShouldBeEmpty(answer.Text());
        answer.Summary.ShouldEndWith("and 2 facts");
    }

    [Fact]
    public void A_fact_answers_to_its_id_in_any_spelling_and_is_cited()
    {
        var queries = _repo.Queries();

        foreach (var spelling in new[] { "KB-001", "KB001", "kb-1", "kb1", "design/knowledge/KB-001-a-fact.md" })
            Column(queries.Entry(spelling), "at").First().ShouldBe("design/knowledge/KB-001-a-fact.md:1", spelling);

        Column(queries.Edges("KB-001", depth: 1, reverse: true), "at").ShouldBe(["design/adr/0001-first.md:6", "design/knowledge/README.md:5"]);
        Column(queries.Edges("KB-001", depth: 1, reverse: false), "target").ShouldBe(["0001"], "the fact's turned-on: names decision 1");
    }

    [Fact]
    public void The_frontmatter_gives_one_field_per_pin_and_the_listing_its_date()
    {
        var (index, _) = _repo.Load();
        var fact       = index.Docs.Single(doc => doc.Kind == Kind.Fact);

        fact.Id.ShouldBe("KB-001");
        fact.Meta.Where(field => field.Key == "pins").Select(field => (field.Value, field.Line))
            .ShouldBe([("Microsoft.AspNetCore.OpenApi 10.0.12", 4), ("sdk 10.0.4xx (the runtime)", 5), ("external Vogen 8.0.7", 6)]);
        fact.Get("turned-on").ShouldBe("decision 1");

        Column(_repo.Queries().List(["kind=fact", "verified>=2026-10-01"]), "date").ShouldBe(["2026-10-15"]);
        index.Docs.Single(doc => doc.Path == "design/knowledge/README.md").Kind.ShouldBe(Kind.Index);
    }

    /// <summary>
    /// Every rule, each broken once: a package pin behind and one ahead, the SDK band behind and ahead, a package
    /// nothing installs, a malformed pin, a missing field, a value that is not plain YAML, a test that is missing,
    /// one that declares another class, a fact whose test is no path, and a test no fact names.
    /// </summary>
    [Fact]
    public void Check_reports_each_broken_rule_where_it_is_written()
    {
        _repo.With("design/knowledge/KB-002-behind.md", Fact(2, "  - Microsoft.AspNetCore.OpenApi 10.0.11\n  - sdk 10.0.3xx", "none, prose"))
             .With("design/knowledge/KB-003-ahead.md", Fact(3, "  - Microsoft.AspNetCore.OpenApi 10.0.13\n  - sdk 10.0.5xx\n  - Some.Beta 1.0.0", "none, prose"))
             .With("design/knowledge/KB-004-unknown.md", Fact(4, "  - Not.Installed 1.0.0\n  - sdk 10", "none, prose"))
             .With("design/knowledge/KB-005-untested.md", Fact(5, "  - sdk 10.0.4xx", "test/CodoMetis.TypeKit.Facts/KB005.cs"))
             .With("design/knowledge/KB-006-other-class.md", Fact(6, "  - sdk 10.0.4xx", "test/CodoMetis.TypeKit.Facts/KB006.cs"))
             .With("test/CodoMetis.TypeKit.Facts/KB006.cs", "public sealed class KB0060 { }\n")
             .With("design/knowledge/KB-007-no-path.md", Fact(7, "  - sdk 10.0.4xx", "somewhere"))
             .With("design/knowledge/KB-008-incomplete.md", "---\nverified: 15.10.2026\npins:\n  - sdk 10.0.4xx\nsource: `quoted`\ntest: none, prose\n---\n\n# KB-008. Incomplete\n")
             .With("test/CodoMetis.TypeKit.Facts/KB009.cs", "public sealed class KB009 { }\n");

        var answer = _repo.Queries().Check();
        var found  = answer.Rows.Select(row => $"{row.Fields[0].Value} {row.Fields[1].Value} {row.Fields[2].Value}").ToList();

        answer.ExitCode.ShouldBe(1);
        found.ShouldBe(
        [
            "design/knowledge/KB-002-behind.md:4 fact-behind Microsoft.AspNetCore.OpenApi 10.0.11",
            "design/knowledge/KB-002-behind.md:5 fact-behind sdk 10.0.3xx",
            "design/knowledge/KB-003-ahead.md:4 fact-ahead Microsoft.AspNetCore.OpenApi 10.0.13",
            "design/knowledge/KB-003-ahead.md:5 fact-ahead sdk 10.0.5xx",
            "design/knowledge/KB-003-ahead.md:6 fact-ahead Some.Beta 1.0.0",
            "design/knowledge/KB-004-unknown.md:4 fact-not-installed Not.Installed 1.0.0",
            "design/knowledge/KB-004-unknown.md:5 fact-bad-pin sdk 10",
            "design/knowledge/KB-005-untested.md:7 fact-untested test/CodoMetis.TypeKit.Facts/KB005.cs",
            "design/knowledge/KB-006-other-class.md:7 fact-untested test/CodoMetis.TypeKit.Facts/KB006.cs",
            "design/knowledge/KB-007-no-path.md:7 fact-untested somewhere",
            "design/knowledge/KB-008-incomplete.md:1 fact-incomplete KB-008",
            "design/knowledge/KB-008-incomplete.md:5 fact-frontmatter source",
            "design/knowledge/KB-008-incomplete.md:2 fact-incomplete verified",
            "test/CodoMetis.TypeKit.Facts/KB009.cs:1 fact-orphan-test test/CodoMetis.TypeKit.Facts/KB009.cs",
        ], answer.Text());

        Column(answer, "detail").First().ShouldBe("pinned Microsoft.AspNetCore.OpenApi 10.0.11, installed 10.0.12: re-verify on 10.0.12");
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.OpenApi 10.0.12", nameof(Pins.State.Current))]
    [InlineData("Microsoft.AspNetCore.OpenApi 10.0.9", nameof(Pins.State.Behind))]
    [InlineData("microsoft.aspnetcore.openapi 10.0.12", nameof(Pins.State.Current))]
    [InlineData("Some.Beta 1.0.0-beta08", nameof(Pins.State.Current))]
    [InlineData("Some.Beta 1.0.0-beta07", nameof(Pins.State.Behind))]
    [InlineData("Some.Beta 1.0.0", nameof(Pins.State.Ahead))]
    [InlineData("Some.Beta 0.9.0", nameof(Pins.State.Behind))]
    [InlineData("sdk 10.0.4xx", nameof(Pins.State.Current))]
    [InlineData("sdk 10.0.402", nameof(Pins.State.Malformed))]
    [InlineData("sdk 9.0.4xx", nameof(Pins.State.Behind))]
    [InlineData("sdk 11.0.1xx", nameof(Pins.State.Ahead))]
    [InlineData("external Vogen 8.0.7", nameof(Pins.State.External))]
    [InlineData("external Vogen", nameof(Pins.State.Malformed))]
    [InlineData("Absent.Package 1.0.0", nameof(Pins.State.NotInstalled))]
    [InlineData("loose", nameof(Pins.State.Malformed))]
    public void A_pin_is_compared_with_what_is_installed(string pin, string expected) =>
        Pins.Compare(pin, Installed.Read(_repo.Root)).State.ToString().ShouldBe(expected);

    [Fact]
    public void What_is_installed_is_read_from_the_two_files()
    {
        var installed = Installed.Read(_repo.Root);

        installed.Sdk.ShouldBe("10.0.402");
        installed.Packages.ShouldContainKeyAndValue("Microsoft.AspNetCore.OpenApi", "10.0.12");
        installed.Packages.Count.ShouldBe(2);
    }

    /// <summary>
    /// A fact with an external pin is due once its verified: date is more than the given days old, and not a day
    /// before; one pinning only what is installed never is, however old, since <c>check</c> compares its pins.
    /// </summary>
    [Fact]
    public void Due_lists_an_external_fact_verified_more_than_the_given_days_ago()
    {
        _repo.With("design/knowledge/KB-002-installed-only.md", Fact(2, "  - sdk 10.0.4xx", "none, prose").Replace("2026-10-15", "2025-01-01", StringComparison.Ordinal));

        var today = new DateOnly(2026, 10, 29);
        var due   = _repo.Queries().Due(13, today, feed: null);

        due.ExitCode.ShouldBe(0);
        due.Rows.Select(row => row.ToString()).ShouldBe(["at=design/knowledge/KB-001-a-fact.md:2  kind=fact-old  id=KB-001  target=2026-10-15  detail=verified 14 days ago, more than 13: re-read it at its latest release"]);
        due.Summary.ShouldStartWith("1 fact due of 1 fact pinned external");

        var notYet = _repo.Queries().Due(14, today, feed: null);
        notYet.Rows.ShouldBeEmpty(notYet.Text());
        notYet.ExitCode.ShouldBe(1);
    }

    /// <summary>
    /// Given a feed, a pin is due when NuGet lists a later release on its track: stable for a stable pin, any for a
    /// pre-release, labels ordered as SemVer orders them (beta.10 after beta.2). A pin of a release still the latest, of
    /// a name NuGet has no package of, or with no version (even where NuGet has a package of the name), is not.
    /// </summary>
    [Fact]
    public void Due_with_a_feed_lists_a_pin_nuget_has_a_later_release_of()
    {
        _repo.With("design/knowledge/KB-002-competitors.md", Fact(2,
            "  - external Vogen 8.0.7\n  - external Vogen 9.0.0-beta.2\n  - external OneOf 3.0.271\n  - external PostgreSQL 17.11\n  - external NuGet registration data, 2026-10-29",
            "none, prose"));

        var asked = new List<string>();
        IReadOnlyList<string>? Feed(string id)
        {
            asked.Add(id);
            return id switch
            {
                "Vogen" => ["8.0.7", "9.0.0-beta.2", "9.0.0-beta.10"],
                "OneOf" => ["3.0.255", "3.0.271"],
                "NuGet" => ["0.0.1"],
                _       => null,
            };
        }

        var due = _repo.Queries().Due(1000, new DateOnly(2026, 10, 29), Feed);

        due.Rows.Select(row => $"{row.Fields[0].Value} {row.Fields[1].Value} {row.Fields[3].Value}: {row.Fields[4].Value}").ShouldBe(
        [
            "design/knowledge/KB-002-competitors.md:5 fact-released external Vogen 9.0.0-beta.2: NuGet's latest Vogen is 9.0.0-beta.10: re-read it there",
        ], due.Text());
        asked.ShouldBe(["Vogen", "Vogen", "Vogen", "OneOf", "PostgreSQL"], "KB-001's Vogen pin, then KB-002's; the pin with no version is never looked up");
        due.Summary.ShouldEndWith("or a later release on NuGet, against which 4 pins compared");

        var released = _repo.Queries().Due(1000, new DateOnly(2026, 10, 29), id => id == "Vogen" ? ["8.0.7", "8.0.8", "9.0.0-beta.2"] : null);
        released.Rows.Select(row => $"{row.Fields[0].Value} {row.Fields[4].Value}").ShouldBe(
        [
            "design/knowledge/KB-001-a-fact.md:6 NuGet's latest stable Vogen is 8.0.8: re-read it there",
            "design/knowledge/KB-002-competitors.md:4 NuGet's latest stable Vogen is 8.0.8: re-read it there",
        ], released.Text());
    }

    [Theory]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-beta-77", "1.0.0-beta-78", -1)]
    [InlineData("1.0.0-beta", "1.0.0-beta.1", -1)]
    [InlineData("1.0.0-alpha.9", "1.0.0-beta", -1)]
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    [InlineData("1.0.0-Beta08", "1.0.0-beta08", 0)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("10.6.0", "10.5.1", 1)]
    public void Versions_are_ordered_as_nuget_orders_them(string left, string right, int expected)
    {
        Pins.CompareVersions(left, right).ShouldBe(expected);
        Pins.CompareVersions(right, left).ShouldBe(-expected);
    }
}
