using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// The covering array never puts an excluded pair or one no obligation wants in a row, not even to fill an axis whose every
/// value would bring one (#54 and #59 saw `rows` place them): such an axis is left out of that row, and an axis with no wanted
/// pair at all is left out of every row.
/// </summary>
public sealed class CoveringTests
{
    private static Inapplicability Declared(string cite, params (string Axis, string[] Values)[] axes) =>
        new(axes.ToDictionary(a => a.Axis, a => a.Values.ToList(), StringComparer.Ordinal), cite);

    private static Claim Over(string[] axes, IEnumerable<Pair>? excluded = null, params Inapplicability[] declared) => new()
    {
        Id = "C1", Source = "README.md#a", Surfaces = ["s"], Watched = [], Excluded = [.. excluded ?? []],
        Obligations = declared.Length == 0 ? [] : [new("keys", "The claim.", new SortedDictionary<string, List<string>>(StringComparer.Ordinal)) { Inapplicable = declared }],
        Axes = new(axes.ToDictionary(a => a, _ => new List<string> { "1", "2" }), StringComparer.Ordinal),
    };

    private static bool Holds(IReadOnlyDictionary<string, string> row, Pair pair) =>
        new[] { pair.A, pair.B }.All(item => row.TryGetValue(item[..item.IndexOf('=', StringComparison.Ordinal)], out var v) && v == item[(item.IndexOf('=', StringComparison.Ordinal) + 1)..]);

    private static IEnumerable<string> Shown(IEnumerable<Pair> pairs) => pairs.Select(p => p.ToString()).Order(StringComparer.Ordinal);

    [Fact]
    public void An_axis_whose_every_value_brings_an_excluded_pair_is_left_out_of_the_row()
    {
        // x=1 goes with no y (as shape.containing goes with no shape.position=root): a row holding x=1 holds no y.
        Pair[] excluded = [new("x=1", "y=1"), new("x=1", "y=2")];
        var claim = Over(["x", "y", "z"], excluded);
        for (var seed = 1; seed <= 20; seed++)
        {
            var rows = Covering.Rows(claim, claim.Pairs(), 2000, seed);
            rows.ShouldNotContain(r => excluded.Any(p => Holds(r.Row, p)), $"seed {seed}");
            rows.Where(r => r.Row["x"] == "1").ShouldAllBe(r => !r.Row.ContainsKey("y"), $"seed {seed}");
            Shown(rows.SelectMany(r => r.Pairs)).ShouldBe(Shown(claim.Pairs()), $"seed {seed}: every pair once, x=1 with z among them");
        }
    }

    [Fact]
    public void An_axis_whose_every_value_brings_a_pair_no_obligation_wants_is_left_out_of_the_row()
    {
        var claim = Over(["x", "y", "z"], null, Declared("record:0001", ("x", ["1"]), ("y", ["1", "2"])));
        var dead = claim.DeclaredInapplicable().Keys.ToList();
        dead.Count.ShouldBe(2);
        for (var seed = 1; seed <= 20; seed++)
        {
            var rows = Covering.Rows(claim, claim.PairsWanted(), 2000, seed);
            rows.ShouldNotContain(r => dead.Any(p => Holds(r.Row, p)), $"seed {seed}");
            Shown(rows.SelectMany(r => r.Pairs)).ShouldBe(Shown(claim.PairsWanted()), $"seed {seed}");
        }
    }

    [Fact]
    public void An_axis_with_no_wanted_pair_is_in_no_row()
    {
        // every pair of z is excluded (as shape.refused-kind is where the refusal is not the claim): rows over x and y alone.
        var claim = Over(["x", "y", "z"], [.. from x in new[] { "x=1", "x=2", "y=1", "y=2" } from z in new[] { "z=1", "z=2" } select new Pair(x, z)]);
        for (var seed = 1; seed <= 5; seed++)
        {
            var rows = Covering.Rows(claim, claim.Pairs(), 2000, seed);
            rows.ShouldAllBe(r => !r.Row.ContainsKey("z"), $"seed {seed}");
            Shown(rows.SelectMany(r => r.Pairs)).ShouldBe(["x=1 y=1", "x=1 y=2", "x=2 y=1", "x=2 y=2"], $"seed {seed}");
        }
    }

    [Fact]
    public void A_seeded_row_leaves_out_an_axis_no_value_of_which_can_join_it()
    {
        // c=1 goes with no d: the row seeded from a=1 c=1 is a, b and c.
        var claim = Over(["a", "b", "c", "d"], [new("c=1", "d=1"), new("c=1", "d=2")]);
        Covering.Seeded(claim, new Pair("a=1", "c=1"), claim.DeclaredInapplicable())
                .ShouldBe(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "1", ["c"] = "1" });
        Covering.Seeded(claim, new Pair("a=1", "b=2"), claim.DeclaredInapplicable())
                .ShouldBe(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2", ["c"] = "1" },
                          "c=1 is c's first value, and d then has none");
    }
}
