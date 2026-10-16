// Claim C09-base-record-equality (CHANGELOG.md 2.1.0, Fixed): "Over a base record, a value object's equality is its wrapped value's,
// in a collection of the base too, whatever equality the base declares." With Generators README#equality: "the generated Equals(TSelf)
// and GetHashCode() compare the underlying value alone, and never call the base's ... The value object implements each base's
// IEquatable<TBase> again (IReimplementedEquatable<TBase>), so a collection of the base compares it the way the value object does."
// Oracle: claim, differential against the wrapped type's EqualityComparer. Each row's value object (harness/C09Shapes.g.cs, generated
// from this claim's rows) is of shape.kind over shape.wrapped at shape.site, over a chain of base records (shape.inheritance) whose
// topmost declares base-equality by reference. Two value objects over equal wrapped values (distinct instances where the wrapped type is
// a class) are equal, ==, of one hash code and IEquatable-equal, one over another value is not; and the collection (HashSet,
// Dictionary key, Distinct) of the value object and of every base in its chain holds two of the three.
using System.Reflection;
using Probes.Build;
using NoteKeys = Probes.Shapes.NoteKeys;
using Probes.Build.C09;
namespace Probes;

public sealed class C09BaseRecordEqualityProbe
{
    const string Claim = "C09-base-record-equality";

    public static TheoryData<string, int> Data() => Probes.Shapes.Rows.Data(Claim);

    [Theory, MemberData(nameof(Data))]
    public Task Probe(string claim, int idx) => Probes.Shapes.Rec.Run(claim, idx, r => Task.FromResult(Check(r)));

    /// <summary>A sample, an equal one that is another instance where the type is a class, and another value.</summary>
    static (object A, object Same, object Other) Values(string wrapped) => wrapped switch
    {
        "string" => ("alpha", new string(['a', 'l', 'p', 'h', 'a']), "beta"),
        "Guid" => (Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222")),
        "int" => (42, 42, 43), "long" => (4200000000L, 4200000000L, 7L), "byte" => ((byte)7, (byte)7, (byte)8), "ulong" => (4200000000UL, 4200000000UL, 1UL),
        "decimal" => (7.80m, 7.80m, 7.81m), "double" => (1.5, 1.5, 2.5), "bool" => (true, true, false), "char" => ('a', 'a', 'b'),
        "DateTime" => (new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc), new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc), new DateTime(2026, 10, 16, 8, 30, 0, DateTimeKind.Utc)),
        "DateTimeOffset" => (new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero)),
        "DateOnly" => (new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16)),
        "TimeOnly" => (new TimeOnly(8, 30), new TimeOnly(8, 30), new TimeOnly(9, 30)),
        "TimeSpan" => (TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(91)),
        "enum" => (Probes.Shapes.PEnum.A, Probes.Shapes.PEnum.A, Probes.Shapes.PEnum.B),
        "flags-enum" => (Probes.Shapes.PFlags.P, Probes.Shapes.PFlags.P, Probes.Shapes.PFlags.Q),
        "Uri" => (new Uri("https://example.org/a"), new Uri("https://example.org/a"), new Uri("https://example.org/b")),
        "NodaTime.Instant" => (NodaTime.Instant.FromUnixTimeSeconds(1_700_000_000), NodaTime.Instant.FromUnixTimeSeconds(1_700_000_000), NodaTime.Instant.FromUnixTimeSeconds(1_700_000_001)),
        "own-record-class" => (new Probes.Shapes.PRec { A = "a", B = 1 }, new Probes.Shapes.PRec { A = "a", B = 1 }, new Probes.Shapes.PRec { A = "b", B = 2 }),
        _ => throw new ArgumentException(wrapped),
    };

    static string? Check(Probes.Shapes.Row r)
    {
        var c = C09Table.Cases.SingleOrDefault(x => x.Index == r.Index);
        if (c is null || c.Key != Decls.Key(Claim, r.Index))
        { r.Outcome("harness-error", "C09Shapes.g.cs was generated from other rows: regenerate it with the rows"); return null; }
        foreach (var (axis, value) in c.Substituted) r.Substituted(axis, value, "the declaration cannot exist as the row asks (excluded in claims.json)");
        _ = r["shape.kind"]; _ = r["shape.site"]; _ = r["shape.inheritance"]; _ = r["base-equality"];
        var wrapped = r["shape.wrapped"]; var collection = r["collection"];

        var (a, same, other) = Values(wrapped);
        if (!a.GetType().IsValueType && ReferenceEquals(a, same)) { r.Outcome("harness-error", "the equal sample is the same instance"); return null; }
        var comparer = typeof(EqualityComparer<>).MakeGenericType(a.GetType()).GetProperty("Default")!.GetValue(null)!;
        var eq = comparer.GetType().GetMethod("Equals", [a.GetType(), a.GetType()])!;
        if (!(bool)eq.Invoke(comparer, [a, same])! || (bool)eq.Invoke(comparer, [a, other])!) { r.Outcome("harness-error", "the samples do not compare as intended under the wrapped type's equality"); return null; }

        var va = c.Make(a); var vb = c.Make(same); var vc = c.Make(other);
        var fails = new List<string>();
        if (!va.Equals(vb)) fails.Add("Equals(object) is false for equal wrapped values");
        if (va.Equals(vc)) fails.Add("Equals(object) is true for different wrapped values");
        if (va.GetHashCode() != vb.GetHashCode()) fails.Add("equal value objects have different hash codes");
        var op = c.Vo.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static, [c.Vo, c.Vo]);
        if (op is null) fails.Add("no operator ==");
        else if (!(bool)op.Invoke(null, [va, vb])!) fails.Add("== is false for equal wrapped values");

        foreach (var t in c.Bases.Prepend(c.Vo))
        {
            var label = t == c.Vo ? "the value object" : $"the base {t.Name}";
            var f = (string?)typeof(C09BaseRecordEqualityProbe).GetMethod(nameof(Collect), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(t).Invoke(null, [collection, va, vb, vc]);
            if (f is not null) fails.Add($"{label}: {f}");
        }
        r.Note(NoteKeys.Compared, $"{c.Bases.Length + 1} types, against EqualityComparer<{a.GetType().Name}>");
        return fails.Count == 0 ? null : string.Join("; ", fails);
    }

    /// <summary>Three values held as <typeparamref name="T"/>, two of them equal: the collection holds two, and IEquatable agrees.</summary>
    static string? Collect<T>(string collection, object a, object b, object c) where T : notnull
    {
        T x = (T)a, y = (T)b, z = (T)c;
        if (x is IEquatable<T> e && (!e.Equals(y) || e.Equals(z))) return $"IEquatable<{typeof(T).Name}> answers {e.Equals(y)} for equal and {e.Equals(z)} for different values";
        switch (collection)
        {
            case "HashSet":
            {
                var set = new HashSet<T> { x, y, z };
                return set.Count == 2 ? null : $"a HashSet<{typeof(T).Name}> holds {set.Count} of two equal and one other";
            }
            case "Dictionary-key":
            {
                var d = new Dictionary<T, int>();
                var first = d.TryAdd(x, 1); var second = d.TryAdd(y, 2); var third = d.TryAdd(z, 3);
                if (!first || second || !third) return $"a Dictionary<{typeof(T).Name}, int> took the keys {first}, {second}, {third}";
                return d.TryGetValue(y, out var found) && found == 1 ? null : $"a Dictionary<{typeof(T).Name}, int> does not find the equal key";
            }
            case "Distinct":
            {
                var n = new[] { x, y, z }.Distinct().Count();
                return n == 2 ? null : $"Distinct over {typeof(T).Name} keeps {n} of two equal and one other";
            }
            default: throw new ArgumentException("collection " + collection);
        }
    }
}
