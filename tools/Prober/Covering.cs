namespace Prober;

/// <summary>
/// A greedy pairwise covering array: each row is the best of a few randomized greedy candidates, and the
/// rows together cover every pair asked for that at least one of the claim's obligations wants. A pair no
/// obligation wants (declared inapplicable) is never asked for, and neither it nor an excluded pair is ever put
/// in a row, not even to fill an axis: every check of such a row would be inapplicable, and an excluded pair is
/// one the claim says no row can run. A candidate that would need one is dropped; a pair that no row without one
/// can hold is left uncovered, never placed. Deterministic for a seed.
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
                SortedDictionary<string, string>? row = new(StringComparer.Ordinal);
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
                    // Every value of this axis puts a blocked pair in the row: the candidate is dropped.
                    if (pick is null) { row = null; break; }
                    row[name] = pick;
                }
                if (row is null) continue;
                var g = Gained(row, todo).Count;
                if (g > bestGain) { best = row; bestGain = g; }
            }
            if (bestGain <= 0)
            {
                // Seed a row from one uncovered pair so progress never stalls; a pair no row can hold without a
                // blocked pair is given up, not placed.
                var first = todo.OrderBy(p => p.A, StringComparer.Ordinal).ThenBy(p => p.B, StringComparer.Ordinal).First();
                best = Seeded(claim, first, dead);
                if (best is null) { todo.Remove(first); continue; }
            }
            var newly = Gained(best!, todo);
            todo.ExceptWith(newly);
            rows.Add((best!, newly));
        }
        return rows;
    }

    /// <summary>
    /// A row seeded from one pair: the pair's two settings, and each other axis at its first value that puts neither
    /// an excluded pair nor a pair no obligation wants (<paramref name="dead"/>) in the row, going back to try the next
    /// value of an earlier axis where a later one has none; null where no such row holds the pair.
    /// </summary>
    internal static SortedDictionary<string, string>? Seeded(Claim claim, Pair pair, IReadOnlyDictionary<Pair, SortedSet<string>> dead)
    {
        bool Blocked(Pair p) => claim.Excluded.Contains(p) || dead.ContainsKey(p);
        var row = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in new[] { pair.A, pair.B }) { var at = item.IndexOf('=', StringComparison.Ordinal); row[item[..at]] = item[(at + 1)..]; }
        if (Blocked(pair)) return null;
        var rest = claim.Axes.Keys.Where(n => !row.ContainsKey(n)).ToList();
        // A depth-first search in axis order, each axis's values in their order, so the row is the first one found;
        // the step bound keeps a claim with many blocked pairs from searching for long.
        var steps = 0;
        bool Fill(int i)
        {
            if (i == rest.Count) return true;
            var name = rest[i];
            foreach (var v in claim.Axes[name])
            {
                if (++steps > 100_000) return false;
                if (row.Any(kv => Blocked(new Pair($"{name}={v}", $"{kv.Key}={kv.Value}")))) continue;
                row[name] = v;
                if (Fill(i + 1)) return true;
                row.Remove(name);
            }
            return false;
        }
        return Fill(0) ? row : null;
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
