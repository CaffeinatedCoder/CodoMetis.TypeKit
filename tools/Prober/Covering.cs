namespace Prober;

/// <summary>
/// A greedy pairwise covering array: each row is the best of a few randomized greedy candidates, and the
/// rows together cover every pair asked for. Deterministic for a seed.
/// </summary>
internal static class Covering
{
    public static List<(SortedDictionary<string, string> Row, List<Pair> Pairs)> Rows(Claim claim, IReadOnlySet<Pair> wanted, int max, int seed)
    {
        var todo = new HashSet<Pair>(wanted);
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
                            if (claim.Excluded.Contains(pair)) { gain = -1; break; }
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
                var pair = todo.OrderBy(p => p.A, StringComparer.Ordinal).ThenBy(p => p.B, StringComparer.Ordinal).First();
                best = new SortedDictionary<string, string>(names.ToDictionary(n => n, n => claim.Axes[n][0]), StringComparer.Ordinal);
                foreach (var item in new[] { pair.A, pair.B }) { var at = item.IndexOf('='); best[item[..at]] = item[(at + 1)..]; }
            }
            var newly = Gained(best!, todo);
            todo.ExceptWith(newly);
            rows.Add((best!, newly));
        }
        return rows;
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
