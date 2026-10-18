using System.Text.Json.Nodes;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// `register` and `publish` read the staged sources before anything is built (decision 103): a note names a
/// key of the catalogue as <c>NoteKeys.Name</c>, the harness's two-argument skip names an open finding of
/// this ledger as a string literal, and a check's <c>Inapplicable</c> or <c>Refused</c> cites by a <c>Cite</c>
/// factory with a string literal something that exists. Each refusal names the file and the line.
/// </summary>
public sealed class SourceLintTests : IDisposable
{
    private readonly Fixture fx = new();

    public SourceLintTests()
    {
        fx.Write("tools/probe/corpus/harness/Row.cs", """
            public sealed class NoteKeys
            {
                // public static readonly NoteKeys InAComment = new("in-a-comment", Pass);
                public static readonly NoteKeys Compared = new("compared", Pass);
                public static readonly NoteKeys BothThrew = new("both-threw", ExpectedRefusal);
            }
            """);
        // C1's not-promised entry, C2's, and what a citation may name besides: a file of the docs, a knowledge entry, a record.
        fx.Write("tools/probe/corpus/claims.json", new JsonArray(
            new JsonObject { ["id"] = "C1", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
                             ["not-promised"] = new JsonArray(new JsonObject { ["id"] = "c1-limit", ["source"] = "README.md#a", ["sentence"] = "The claim." }) },
            new JsonObject { ["id"] = "C2", ["source"] = "README.md#a", ["surfaces"] = new JsonArray("s"),
                             ["not-promised"] = new JsonArray(new JsonObject { ["id"] = "c2-limit", ["source"] = "README.md#a", ["sentence"] = "The claim." }) }).ToJsonString());
        fx.Write("design/knowledge/KB-007-a-fact.md", "# KB-007\n");
        fx.Write("design/adr/0094-a-record.md", "# 94\n");
        fx.Commit("the catalogue");
    }

    public void Dispose() => fx.Dispose();

    private string Staged => Path.Combine(fx.BranchDir, "staging");

    private void Stage(string body) =>
        fx.Write(Path.GetRelativePath(fx.Root, Path.Combine(Staged, "probes", "C1Probe.cs")), $$"""
            public sealed class C1Probe
            {
                static string? Check(Row r, string x)
                {
                    var rest = new[] { 1, 2 }.Skip(x.Length);
                    // r.Note("a comment is no call", x);
                    var text = "r.Note(\"a string is no call\")";
            {{body}}
                    return null;
                }
            }
            """);

    /// <summary>Stages a source of the probe P (C1's), or a harness source no one probe owns, holding <paramref name="body"/> in a check.</summary>
    private void StageCheck(string body, string file = "probes/P.cs") =>
        fx.Write(Path.GetRelativePath(fx.Root, Path.Combine(Staged, file)), $$"""
            public sealed class P
            {
                static string? Check(Check c, string x)
                {
                    var refused = new Entry().Refused();
                    // c.Inapplicable(Cite.NotPromised("in-a-comment"));
                    var text = "c.Refused(Cite.Docs(\"in-a-string.md\"))";
            {{body}}
                    return null;
                }
            }
            """);

    private (int Exit, string Out, string Err) Register() => fx.Ledger("register", "--probe", "P", "--claim", "C1", "--filter", "*C1Probe");

    private void Finding(string id, string status) =>
        File.AppendAllText(Path.Combine(fx.BranchDir, "findings.jsonl"),
            new JsonObject { ["id"] = id, ["claim"] = "C1", ["status"] = status }.ToJsonString() + "\n");

    [Fact]
    public void A_probe_whose_notes_and_skips_are_written_out_registers()
    {
        Finding("F7", "open");
        Stage("""
                    r.Note(NoteKeys.Compared, $"{x}: {(x.Length > 1 ? "long" : "short")}");
                    r.Note(NoteKeys.BothThrew);
                    r.Skip("F7", "the open finding");
            """);
        var (exit, output, error) = Register();
        exit.ShouldBe(0, output + error);
    }

    [Fact]
    public void A_note_key_built_from_text_is_refused_by_file_and_line()
    {
        Stage("""        r.Note("msg:" + x, "detail");""");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("staging/probes/C1Probe.cs:8: Note(\"msg:\" + x, ...) does not name a key of NoteKeys");
    }

    [Fact]
    public void A_note_key_the_catalogue_does_not_declare_is_refused()
    {
        Stage("""        r.Note(NoteKeys.InAComment);""");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("C1Probe.cs:8: Note(NoteKeys.InAComment, ...) names no key of NoteKeys");
    }

    [Fact]
    public void A_finding_built_from_text_is_refused()
    {
        Stage("""        r.Skip("F" + x, "why");""");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("C1Probe.cs:8: Skip(\"F\" + x, ...) does not name its finding as a string literal");
    }

    [Fact]
    public void A_skip_for_a_finding_that_is_not_open_is_refused()
    {
        Finding("F7", "open");
        Finding("F7", "withdrawn");
        Stage("""
                    r.Skip("F7", "withdrawn since");
                    r.Skip("F8", "never filed");
            """);
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("C1Probe.cs:8: Skip(\"F7\", ...) names finding F7, which is withdrawn");
        error.ShouldContain("C1Probe.cs:9: Skip(\"F8\", ...) names no finding this ledger has");
    }

    [Fact]
    public void Publish_refuses_what_register_refuses_and_moves_nothing()
    {
        Stage("""        r.Note("msg:" + x, "detail");""");
        var (exit, _, error) = fx.Ledger("publish");
        exit.ShouldBe(1);
        error.ShouldContain("publish: ");
        error.ShouldContain("C1Probe.cs:8: Note(\"msg:\" + x, ...)");
        File.Exists(Path.Combine(fx.Root, "tools/probe/corpus/probes/C1Probe.cs")).ShouldBeFalse();
    }

    [Fact]
    public void A_check_citing_what_exists_registers()
    {
        StageCheck("""
                    c.Inapplicable(Cite.NotPromised("c1-limit"), x);
                    c.Refused(Cite.Docs("README.md#a"));
                    c.Refused(Cite.Docs("README.md"));
                    c.Inapplicable(Cite.Fact("KB-007"));
                    c.Refused(Cite.Record( "0094" ), "detail");
            """);
        var (exit, output, error) = Register();
        exit.ShouldBe(0, output + error);
    }

    [Fact]
    public void A_citation_that_is_not_a_factory_with_a_string_literal_is_refused()
    {
        StageCheck("""
                    var cite = Cite.NotPromised("c1-limit");
                    c.Inapplicable(cite);
                    c.Refused(Cite.NotPromised("c1-" + x));
                    c.Inapplicable(Cite.Text("c1-limit"));
            """);
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("staging/probes/P.cs:9: Inapplicable(cite, ...) does not cite by a Cite factory with a string literal");
        error.ShouldContain("P.cs:10: Refused(Cite.NotPromised(\"c1-\" + x), ...) does not cite by a Cite factory with a string literal");
        error.ShouldContain("P.cs:11: Inapplicable(Cite.Text(\"c1-limit\"), ...) does not cite by a Cite factory with a string literal");
    }

    [Fact]
    public void A_not_promised_id_the_probes_claim_does_not_list_is_refused()
    {
        StageCheck("""        c.Inapplicable(Cite.NotPromised("c2-limit"));""");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("P.cs:8: Inapplicable(Cite.NotPromised(\"c2-limit\"), ...) names no not-promised entry of its claim C1");
    }

    [Fact]
    public void A_harness_source_cites_any_claims_not_promised_entry_and_none_that_no_claim_lists()
    {
        StageCheck("""
                    c.Inapplicable(Cite.NotPromised("c2-limit"));
                    c.Inapplicable(Cite.NotPromised("no-claim-lists-it"));
            """, "harness/Shared.cs");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldNotContain("Shared.cs:8:");
        error.ShouldContain("staging/harness/Shared.cs:9: Inapplicable(Cite.NotPromised(\"no-claim-lists-it\"), ...) names no not-promised entry of any claim");
    }

    [Fact]
    public void A_docs_source_naming_no_file_is_refused()
    {
        StageCheck("""        c.Refused(Cite.Docs("docs/gone.md#a"));""");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("P.cs:8: Refused(Cite.Docs(\"docs/gone.md#a\"), ...) names no file docs/gone.md in the repository");
    }

    [Fact]
    public void A_fact_with_no_knowledge_entry_is_refused()
    {
        StageCheck("""        c.Refused(Cite.Fact("KB-070"));""");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("P.cs:8: Refused(Cite.Fact(\"KB-070\"), ...) names no entry under design/knowledge/");
    }

    [Fact]
    public void A_record_with_no_file_under_design_adr_is_refused()
    {
        StageCheck("""        c.Inapplicable(Cite.Record("0095"));""");
        var (exit, _, error) = Register();
        exit.ShouldBe(1);
        error.ShouldContain("P.cs:8: Inapplicable(Cite.Record(\"0095\"), ...) names no record under design/adr/");
    }

    [Fact]
    public void Publish_refuses_a_citation_that_names_nothing()
    {
        StageCheck("""        c.Inapplicable(Cite.Record("0095"));""");
        var (exit, _, error) = fx.Ledger("publish");
        exit.ShouldBe(1);
        error.ShouldContain("publish: ");
        error.ShouldContain("P.cs:8: Inapplicable(Cite.Record(\"0095\"), ...) names no record under design/adr/");
    }
}
