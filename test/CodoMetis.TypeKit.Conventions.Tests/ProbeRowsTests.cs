using System.IO.Compression;
using System.Text.Json.Nodes;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The probe corpus keeps its rows per probe (<c>tools/probe/corpus/rows/&lt;probe&gt;.json.gz</c>): every registered
/// probe has a rows file named for it, each file names its probe's claim, a claim of <c>claims.json</c>, and no two rows of
/// one file have the same axis map.
/// </summary>
/// <remarks>
/// A shared rows file tied two probes of a claim together, so rows appended for one changed the other's identity, and
/// appending instead of regenerating left duplicates (C02 42 of 88, C20 90 of 351), each a row run again for nothing. A
/// rows file named for no registered probe is a leftover no replay runs. The positive control holds each rule to finding
/// its defect, and the floor keeps the walker from passing by finding no file.
/// </remarks>
public sealed class ProbeRowsTests
{
    private static string Corpus => Path.Combine(Repository.Root, "tools", "probe", "corpus");

    [Fact]
    public void The_rows_files_are_found() =>
        RowsFiles().Count.ShouldBeGreaterThanOrEqualTo(43, "the corpus had a rows file for each of its 43 probes when this rule was written");

    [Fact]
    public void Every_probe_has_rows_of_its_own_each_axis_map_once() =>
        Problems(RowsFiles(), Claims(), Registry()).ShouldBeEmpty();

    [Fact]
    public void A_duplicate_row_a_wrong_claim_a_leftover_file_and_a_probe_without_rows_are_found()
    {
        static Dictionary<string, string> Row(string x, string y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };
        var files = new Dictionary<string, (string? Claim, List<Dictionary<string, string>> Rows)>(StringComparer.Ordinal)
        {
            ["P"] = ("C1", [Row("1", "1"), Row("1", "2"), new(StringComparer.Ordinal) { ["y"] = "1", ["x"] = "1" }]),
            ["Q"] = ("C9", [Row("1", "1")]),
            ["R"] = ("C1", [Row("1", "1")]),
            ["Old"] = ("C1", [Row("2", "2")]),
            ["N"] = (null, [Row("2", "2")]),
        };
        var registry = new Dictionary<string, string>(StringComparer.Ordinal) { ["P"] = "C1", ["Q"] = "C9", ["R"] = "C2", ["N"] = "C1", ["S"] = "C1" };
        Problems(files, new HashSet<string>(StringComparer.Ordinal) { "C1", "C2" }, registry).ShouldBe(
        [
            "rows/N.json.gz names no claim",
            "rows/Old.json.gz names no registered probe",
            "rows/P.json.gz: row 2 has the axis map of row 0",
            "rows/Q.json.gz names claim C9, which claims.json does not have",
            "rows/R.json.gz names claim C1, but its probe R is a probe of C2",
            "S is a registered probe without a rows file (rows/S.json.gz)",
        ]);
    }

    /// <summary>Every rule's problems, in ordinal order: files by name, then the probes without a file.</summary>
    private static List<string> Problems(IReadOnlyDictionary<string, (string? Claim, List<Dictionary<string, string>> Rows)> files,
                                         IReadOnlySet<string> claims, IReadOnlyDictionary<string, string> registry)
    {
        var problems = new List<string>();
        foreach (var (probe, (claim, rows)) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var name = $"rows/{probe}.json.gz";
            if (claim is null) problems.Add($"{name} names no claim");
            else if (!claims.Contains(claim)) problems.Add($"{name} names claim {claim}, which claims.json does not have");
            if (!registry.TryGetValue(probe, out var registered)) problems.Add($"{name} names no registered probe");
            else if (claim is not null && claims.Contains(claim) && registered != claim) problems.Add($"{name} names claim {claim}, but its probe {probe} is a probe of {registered}");
            var first = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < rows.Count; i++)
            {
                var key = string.Join("\n", rows[i].OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
                if (first.TryGetValue(key, out var earlier)) problems.Add($"{name}: row {i} has the axis map of row {earlier}");
                else first[key] = i;
            }
        }
        foreach (var probe in registry.Keys.Where(p => !files.ContainsKey(p)).Order(StringComparer.Ordinal))
            problems.Add($"{probe} is a registered probe without a rows file (rows/{probe}.json.gz)");
        return problems;
    }

    /// <summary>Every rows file of the corpus by its probe's name, with the claim it names and its rows' axis maps.</summary>
    private static Dictionary<string, (string? Claim, List<Dictionary<string, string>> Rows)> RowsFiles()
    {
        var files = new Dictionary<string, (string?, List<Dictionary<string, string>>)>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Corpus, "rows")).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            name.ShouldEndWith(".json.gz", Case.Sensitive, $"rows/{name}: the corpus keeps its rows gzipped, one file per probe");
            using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            var document = JsonNode.Parse(gz)!;
            var claim = document["claim"] is JsonValue c && c.TryGetValue<string>(out var s) ? s : null;
            var rows = document["rows"]!.AsArray()
                                        .Select(r => r!["row"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>(), StringComparer.Ordinal))
                                        .ToList();
            files[name[..^".json.gz".Length]] = (claim, rows);
        }
        return files;
    }

    private static HashSet<string> Claims() =>
        [.. JsonNode.Parse(File.ReadAllText(Path.Combine(Corpus, "claims.json")))!.AsArray().Select(c => c!["id"]!.GetValue<string>())];

    private static Dictionary<string, string> Registry() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Corpus, "registry.json")))!["probes"]!.AsObject()
                .ToDictionary(kv => kv.Key, kv => kv.Value!["claim"]!.GetValue<string>(), StringComparer.Ordinal);
}
