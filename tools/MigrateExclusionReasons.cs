// One-off (design/adr/0102): gives every `exclude` entry of tools/probe/corpus/claims.json one reason.
// The gate round of 2026-10-16 wrote reasons in three invented fields (`excludeReasons` as a list of
// {match, reason}, `excludeReasons` as an object keyed by its match, `excludeWhy` as a list of {pairs, reason}),
// each naming its pairs in prose. This reads that prose as a pattern, puts each excluded pair under the first
// reason whose pattern takes it, and writes `exclude` as a list of {reason, pairs}, in the form `prober publish`
// writes. A pair no pattern takes gets one of the reasons below, written for this migration. It refuses to
// write when a pair is left without a reason or a pattern takes no pair.
//
// Run from a copy outside the repository, as tools/probe/enumerate.cs is: tools/Directory.Build.targets stamps a
// tool project's sources, which a file-based app has none of.
//
//     cp tools/MigrateExclusionReasons.cs "$TMPDIR/m/" && (cd "$TMPDIR/m" && dotnet run MigrateExclusionReasons.cs -- "$OLDPWD/tools/probe/corpus/claims.json")

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var path = args.Length == 1 ? args[0] : throw new ArgumentException("usage: MigrateExclusionReasons <claims.json>");
var claims = JsonNode.Parse(File.ReadAllText(path))!.AsArray();

// Reasons written for this migration, for pairs the round left without one (C20) or that no invented reason names.
var written = new Dictionary<string, List<(string Match, string Reason)>>
{
    ["C20-testing-shouldhold"] =
    [
        ("or.operation=Result.Catch x or.shape=Option",
         "Result.Catch turns an exception into a Result, never an Option (src/CodoMetis.TypeKit/ResultExtensions.cs), so no row can catch into an Option."),
        ("or.shape=Result-E x or.operation=query-syntax|FirstOrNone",
         "Result<TError> has no Select or SelectMany, so no query expression produces one, and FirstOrNone returns an Option<T>, never a Result<TError> (src/CodoMetis.TypeKit/ResultExtensions.cs and OptionExtensions.cs)."),
    ],
};

var failures = new List<string>();
foreach (var claim in claims.Select(c => c!.AsObject()))
{
    var id = (string)claim["id"]!;
    if (claim["exclude"] is not JsonArray pairs) continue;

    var groups = new List<(List<Func<string, string, bool>> Matchers, string Reason, string Text)>();
    switch (claim["excludeReasons"])
    {
        case JsonArray list:
            foreach (var e in list) groups.Add((Parse((string)e!["match"]!), (string)e["reason"]!, (string)e["match"]!));
            break;
        case JsonObject map:
            foreach (var (match, reason) in map) groups.Add((Parse(match), (string)reason!, match));
            break;
    }
    if (claim["excludeWhy"] is JsonArray why)
        foreach (var e in why)
        {
            var descriptions = e!["pairs"]!.AsArray().Select(p => (string)p!).ToList();
            groups.Add(([.. descriptions.SelectMany(Parse)], (string)e["reason"]!, string.Join("; ", descriptions)));
        }
    foreach (var (match, reason) in written.GetValueOrDefault(id) ?? []) groups.Add((Parse(match), reason, match));

    var taken = groups.Select(_ => new List<JsonArray>()).ToList();
    foreach (var pair in pairs.Select(p => p!.AsArray()))
    {
        var (a, b) = ((string)pair[0]!, (string)pair[1]!);
        var at = groups.FindIndex(g => g.Matchers.Any(m => m(a, b) || m(b, a)));
        if (at < 0) failures.Add($"{id}: no reason takes {a} {b}");
        else taken[at].Add(new JsonArray(a, b));
    }
    for (var i = 0; i < groups.Count; i++)
        if (taken[i].Count == 0) failures.Add($"{id}: the reason for \"{groups[i].Text}\" takes no pair");

    var exclude = new JsonArray([.. groups.Select((g, i) => (JsonNode)new JsonObject
    {
        ["reason"] = g.Reason, ["pairs"] = new JsonArray([.. taken[i].Select(p => (JsonNode)p)]),
    }).Where(o => o["pairs"]!.AsArray().Count > 0)]);
    // The claim's keys keep their order; `exclude` takes the reasons in, and the invented fields go.
    var rebuilt = claim.Where(kv => kv.Key is not ("excludeReasons" or "excludeWhy"))
                       .Select(kv => KeyValuePair.Create(kv.Key, kv.Key == "exclude" ? exclude : kv.Value?.DeepClone())).ToList();
    claim.Clear();
    foreach (var kv in rebuilt) claim.Add(kv);
    Console.WriteLine($"{id}: {pairs.Count} pairs under {exclude.Count} reason(s): " + string.Join(", ", taken.Where(t => t.Count > 0).Select(t => t.Count)));
}

if (failures.Count > 0)
{
    foreach (var f in failures) Console.Error.WriteLine(f);
    return 1;
}
File.WriteAllText(path, claims.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
Console.WriteLine($"wrote {path}");
return 0;

// One prose description of the round's, read as matchers of an ordered pair (a, b); the caller tries both orders.
//   "axis=v1|v2 x axis2"                      a setting of one side against any of the other's
//   "axis=v with every value of every other axis", or "axis=v" alone   any pair that holds it
//   "axis=v + axis2=w"                        exactly that pair
//   "X x Y (differing)"                       one setting under two names holding different values (none = default)
//   "X x Y (and Z)"                           also X against Z
static List<Func<string, string, bool>> Parse(string description)
{
    var d = description.Replace(" with every value of every other axis", "", StringComparison.Ordinal);
    var also = Regex.Match(d, @" \(and (?<z>[^)]*)\)$");
    var differing = d.EndsWith(" (differing)", StringComparison.Ordinal);
    // Only these two notes are dropped: a value such as "unsealed-record-class (CMTK1006)" holds its own parentheses.
    d = Regex.Replace(d, @" \((differing|and [^)]*)\)$", "");
    var sides = d.Split([" x ", " + "], StringSplitOptions.TrimEntries);
    var result = new List<Func<string, string, bool>>();
    if (sides.Length == 1) result.Add((a, b) => Side(sides[0], a));
    else if (differing) result.Add((a, b) => Side(sides[0], a) && Side(sides[1], b) && Value(a) is var x && Value(b) is var y && Normal(x) != Normal(y));
    else result.Add((a, b) => Side(sides[0], a) && Side(sides[1], b));
    if (also.Success) { var z = also.Groups["z"].Value; result.Add((a, b) => Side(sides[0], a) && Side(z, b)); }
    return result;

    static string Value(string setting) => setting[(setting.IndexOf('=') + 1)..];
    static string Normal(string value) => value == "none" ? "default" : value;
}

// "axis" takes any value of it; "axis=v1|v2|base-*" those values, "*" as fnmatch's wildcard.
static bool Side(string side, string setting)
{
    var at = setting.IndexOf('=');
    var (axis, value) = (setting[..at], setting[(at + 1)..]);
    var eq = side.IndexOf('=');
    if (eq < 0) return side == axis;
    if (side[..eq] != axis) return false;
    return side[(eq + 1)..].Split('|').Any(p => Regex.IsMatch(value, "^" + Regex.Escape(p).Replace("\\*", ".*", StringComparison.Ordinal) + "$"));
}
