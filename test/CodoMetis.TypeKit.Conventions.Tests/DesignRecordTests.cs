namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The decision records under <c>design/adr/</c>: one file per decision, numbered without a gap, each
/// with its title, its date and its status, and every one listed in the index. A record nobody can
/// find, or one without the date and status a reader scans for, is a decision that will be taken
/// again.
/// </summary>
public sealed class DesignRecordTests
{
    private static readonly string Records = Path.Combine(Repository.Root, "design", "adr");

    private static IReadOnlyList<string> Names =>
        [.. Directory.EnumerateFiles(Records, "*.md").Select(Path.GetFileName).Where(name => name != "README.md").Order(StringComparer.Ordinal)!];

    public static TheoryData<string> RecordNames => [.. Names];

    [Fact]
    public void The_records_are_found() => Names.Count.ShouldBeGreaterThanOrEqualTo(50, string.Join(", ", Names));

    [Fact]
    public void The_records_are_numbered_without_a_gap() =>
        Names.Select(name => int.Parse(name[..4])).ShouldBe(Enumerable.Range(1, Names.Count), "Each record is NNNN-slug.md, and the numbers run from 1 without a gap.");

    [Theory]
    [MemberData(nameof(RecordNames))]
    public void A_record_has_its_title_date_and_status(string name)
    {
        var text = File.ReadAllText(Path.Combine(Records, name));
        var lines = text.Split('\n');

        lines[0].ShouldStartWith($"# {name[..4]}. ", customMessage: $"{name} opens with its number and title.");
        text.ShouldMatch(@"(?m)^Date: \d{4}-\d{2}-\d{2}\s*$", $"{name} has a 'Date: YYYY-MM-DD' line.");
        text.ShouldMatch(@"(?m)^Status: (accepted|open|superseded by \d{4})\s*$", $"{name} has a 'Status:' line of accepted, open or superseded by NNNN.");
    }

    [Fact]
    public void The_index_lists_every_record()
    {
        var index = File.ReadAllText(Path.Combine(Records, "README.md"));

        Names.Where(name => !index.Contains($"]({name})", StringComparison.Ordinal)).ShouldBeEmpty("Every record is linked from design/adr/README.md.");
    }
}
