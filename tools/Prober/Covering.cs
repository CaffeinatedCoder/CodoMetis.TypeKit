namespace Prober;

/// <summary>
/// A greedy pairwise covering array: each row is the best of a few randomized greedy candidates, and the
/// rows together cover every pair asked for that at least one of the claim's obligations wants. A pair no
/// obligation wants (declared inapplicable) is never asked for and never put in a row, as an excluded pair is
/// not: every check of such a row would be inapplicable. Deterministic for a seed.
/// </summary>
internal static class Covering
{
    public static List<(SortedDictionary<string, string> Row, List<Pair> Pairs)> Rows(Claim claim, IReadOnlySet<Pair> wanted, int max, int seed)
    {
        var dead = claim.DeclaredInapplicable();
        bool Blocked(Pair pair) => claim.Excluded.Contains(pair) || dead.ContainsKey(pair);
        // The pairs asked for, each once however many obligations want it, so a row's gain counts it once.
        var todo = new HashSet<Pair>(wanted);
        todo.ExceptWith(dead.Keys);
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
                    string? pick = null; var pickGain = int.MinValue;
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
                    row[name] = pick!;
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

    /// <summary>
    /// A row seeded from one pair: the pair's two settings, and each other axis at its first value that puts no pair
    /// no obligation wants (<paramref name="dead"/>) in the row, else at its first value.
    /// </summary>
    internal static SortedDictionary<string, string> Seeded(Claim claim, Pair pair, IReadOnlyDictionary<Pair, SortedSet<string>> dead)
    {
        var row = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in new[] { pair.A, pair.B }) { var at = item.IndexOf('=', StringComparison.Ordinal); row[item[..at]] = item[(at + 1)..]; }
        foreach (var name in claim.Axes.Keys.Where(n => !row.ContainsKey(n)).ToList())
            row[name] = claim.Axes[name].FirstOrDefault(v => !row.Any(kv => dead.ContainsKey(new Pair($"{name}={v}", $"{kv.Key}={kv.Value}")))) ?? claim.Axes[name][0];
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
