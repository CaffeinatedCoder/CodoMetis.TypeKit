using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// The covering array never puts an excluded pair or one no obligation wants in a row, not even to fill an axis
/// whose every value would bring one (#54 and #59 saw `rows` place them): a candidate needing one is dropped, and a
/// pair no row can hold without one is left uncovered.
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

    [Fact]
    public void An_axis_whose_every_value_brings_an_excluded_pair_puts_none_in_a_row()
    {
        // x=1 goes with no y, so no row can hold x=1: its pairs with z are left uncovered, never placed beside a y.
        Pair[] excluded = [new("x=1", "y=1"), new("x=1", "y=2")];
        var claim = Over(["x", "y", "z"], excluded);
        for (var seed = 1; seed <= 20; seed++)
        {
            var rows = Covering.Rows(claim, claim.Pairs(), 2000, seed);
            rows.ShouldNotContain(r => excluded.Any(p => Holds(r.Row, p)), $"seed {seed}");
            rows.SelectMany(r => r.Pairs).ShouldNotContain(p => p.A == "x=1" || p.B == "x=1", $"seed {seed}");
            rows.SelectMany(r => r.Pairs).Count().ShouldBe(8, $"seed {seed}: x=2 with y and z, y with z");
        }
    }

    [Fact]
    public void An_axis_whose_every_value_brings_a_pair_no_obligation_wants_puts_none_in_a_row()
    {
        var claim = Over(["x", "y", "z"], null, Declared("record:0001", ("x", ["1"]), ("y", ["1", "2"])));
        var dead = claim.DeclaredInapplicable().Keys.ToList();
        dead.Count.ShouldBe(2);
        for (var seed = 1; seed <= 20; seed++)
        {
            var rows = Covering.Rows(claim, claim.PairsWanted(), 2000, seed);
            rows.ShouldNotContain(r => dead.Any(p => Holds(r.Row, p)), $"seed {seed}");
            rows.SelectMany(r => r.Pairs).Count().ShouldBe(8, $"seed {seed}");
        }
    }

    [Fact]
    public void A_seeded_row_goes_back_to_an_earlier_axis_rather_than_hold_an_excluded_pair()
    {
        // c=1 goes with no d: the first fill (c=1) is taken back for c=2.
        var claim = Over(["a", "b", "c", "d"], [new("c=1", "d=1"), new("c=1", "d=2")]);
        Covering.Seeded(claim, new Pair("a=1", "b=1"), claim.DeclaredInapplicable())
                .ShouldBe(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "1", ["c"] = "2", ["d"] = "1" });
    }

    [Fact]
    public void A_pair_no_row_can_hold_without_an_excluded_pair_seeds_no_row()
    {
        var claim = Over(["x", "y", "z"], [new("x=1", "y=1"), new("x=1", "y=2")]);
        Covering.Seeded(claim, new Pair("x=1", "z=1"), claim.DeclaredInapplicable()).ShouldBeNull();
    }
}
