using System.Text.Json.Nodes;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// `register` and `publish` read the staged sources before anything is built (decision 103): a note names a
/// key of the catalogue as <c>NoteKeys.Name</c>, and the harness's two-argument skip names an open finding of
/// this ledger as a string literal. Each refusal names the file and the line.
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
}
