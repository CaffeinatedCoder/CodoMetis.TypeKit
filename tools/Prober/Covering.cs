namespace Prober;

/// <summary>
/// A greedy pairwise covering array: each row is the best of a few randomized greedy candidates, and the
/// rows together cover every pair asked for that at least one of the claim's obligations wants. A pair no
/// obligation wants (declared inapplicable) is never asked for, and neither it nor an excluded pair is ever put
/// in a row, not even to fill an axis: every check of such a row would be inapplicable, and an excluded pair is
/// one the claim says no row can run. An axis none of whose values can join a row without one is left out of
/// that row: shape.containing beside shape.position=root, and shape.refused-kind wherever the claim excludes each of
/// its pairs. Deterministic for a seed.
/// </summary>
internal static class Covering
{
    public static List<(SortedDictionary<string, string> Row, List<Pair> Pairs)> Rows(Claim claim, IReadOnlySet<Pair> wanted, int max, int seed)
    {
        var dead = claim.DeclaredInapplicable();
        bool Blocked(Pair pair) => claim.Excluded.Contains(pair) || dead.ContainsKey(pair);
        // The pairs asked for, each once however many obligations want it, so a row's gain counts it once.
        var todo = new HashSet<Pair>(wanted);
        todo.RemoveWhere(Blocked);
        var names = claim.Axes.Keys.ToList();
        var random = new Random(seed);
        var rows = new List<(SortedDictionary<string, string>, List<Pair>)>();
        while (todo.Count > 0 && rows.Count < max)
        {
            SortedDictionary<string, string>? best = null; var bestGain = -1;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var order = names.OrderBy(_ => random.Next()).ToList();
                var row = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in order)
                {
                    var options = claim.Axes[name].OrderBy(_ => random.Next()).ToList();
                    string? pick = null; var pickGain = -1;
                    foreach (var v in options)
                    {
                        var gain = 0;
                        foreach (var (n, w) in row)
                        {
                            var pair = new Pair($"{name}={v}", $"{n}={w}");
                            if (Blocked(pair)) { gain = -1; break; }
                            if (todo.Contains(pair)) gain++;
                        }
                        if (gain > pickGain) { pick = v; pickGain = gain; }
                    }
                    // Every value of this axis puts a blocked pair in the row: the axis is left out of it.
                    if (pick is not null) row[name] = pick;
                }
                var g = Gained(row, todo).Count;
                if (g > bestGain) { best = row; bestGain = g; }
            }
            if (bestGain <= 0)
            {
                // Seed a row from one uncovered pair so progress never stalls.
                best = Seeded(claim, todo.OrderBy(p => p.A, StringComparer.Ordinal).ThenBy(p => p.B, StringComparer.Ordinal).First(), dead);
            }
            var newly = Gained(best!, todo);
            todo.ExceptWith(newly);
            rows.Add((best!, newly));
        }
        return rows;
    }

    static string Axis(string setting) => setting[..setting.IndexOf('=', StringComparison.Ordinal)];

    /// <summary>
    /// A row seeded from one pair: the pair's two settings, and each other axis at its first value that puts neither an excluded pair
    /// nor a pair no obligation wants (<paramref name="dead"/>) in the row, or left out where none does.
    /// </summary>
    internal static SortedDictionary<string, string> Seeded(Claim claim, Pair pair, IReadOnlyDictionary<Pair, SortedSet<string>> dead)
    {
        bool Blocked(Pair p) => claim.Excluded.Contains(p) || dead.ContainsKey(p);
        var row = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in new[] { pair.A, pair.B }) row[Axis(item)] = item[(item.IndexOf('=', StringComparison.Ordinal) + 1)..];
        foreach (var name in claim.Axes.Keys.Where(n => !row.ContainsKey(n)).ToList())
            if (claim.Axes[name].FirstOrDefault(v => !row.Any(kv => Blocked(new Pair($"{name}={v}", $"{kv.Key}={kv.Value}")))) is { } value)
                row[name] = value;
        return row;
    }

    private static List<Pair> Gained(SortedDictionary<string, string> row, HashSet<Pair> todo)
    {
        var result = new List<Pair>();
        var items = row.ToList();
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
            {
                var pair = new Pair($"{items[i].Key}={items[i].Value}", $"{items[j].Key}={items[j].Value}");
                if (todo.Contains(pair)) result.Add(pair);
            }
        return result;
    }
}
