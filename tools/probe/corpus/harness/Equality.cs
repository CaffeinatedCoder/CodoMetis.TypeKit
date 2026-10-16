// Claim L05-equality (README.md#what-typekit-guarantees; Generators README "Equality", "Comparison"; oracle "Equality (metamorphic)" in
// .claude/probe-axes.md): equal value objects have equal hash codes, CompareTo is zero exactly when Equals is true, and both agree with the
// wrapped type's (ordinal for a string), through every base in the inheritance chain, and wherever the value object is held. A closed set's
// members are singletons, so its equality is reference equality (docs/closed-sets.md).
// Oracle: metamorphic. Two value objects over values the wrapped type calls equal in different forms (7.8m and 7.80m, 0.0 and -0.0, one
// instant at two offsets, two string instances), and one over a value it calls unequal and orders after.
using System.Collections;
using System.Reflection;
using Probes.Shapes;
namespace Probes.Json;

public sealed record L05Entry(string Kind, string Wrapped, string Inheritance, string Site, Type Type, Func<object> A1, Func<object> A2, Func<object> B);

public abstract record L05PolyBase;
public sealed record L05PolyDerived<T> : L05PolyBase { public T Value { get; set; } = default!; }

public static partial class L05Registry
{
    static readonly List<L05Entry> All = [];
    static L05Registry() => Fill();
    static partial void Fill();
    static void Add(string kind, string wrapped, string inheritance, string site, Type type, Func<object> a1, Func<object> a2, Func<object> b) =>
        All.Add(new(kind, wrapped, inheritance, site, type, a1, a2, b));
    public static L05Entry? Get(string kind, string wrapped, string inheritance, string site) =>
        All.FirstOrDefault(x => x.Kind == kind && x.Wrapped == wrapped && x.Inheritance == inheritance && x.Site == site);
}

public static class Equality
{
    static IEqualityComparer Comparer(Type t) => (IEqualityComparer)typeof(EqualityComparer<>).MakeGenericType(t).GetProperty("Default")!.GetValue(null)!;
    static int Sign(int x) => Math.Sign(x);

    static object Make(Type v, Type w, object value)
    {
        var m = v.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(x => x.Name is "From" or "FromKnownGood" && x.GetParameters() is [var p, ..] && p.ParameterType == w);
        var args = m.GetParameters().Select((p, i) => i == 0 ? value : Type.Missing).ToArray();
        return m.Invoke(null, BindingFlags.OptionalParamBinding | BindingFlags.Default, null, args, null)!;
    }

    static object? Op(Type t, string name, object a, object b) =>
        t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, [t, t]) is { } m ? m.Invoke(null, [a, b]) : null;

    public static string? Check(Row r)
    {
        var kind = r["shape.kind"]; var inh = r["shape.inheritance"]; var site = r["shape.site"];
        var closed = kind == "closed-set";
        // claims.json excludes the pairs no type can have; a row the covering array made with one anyway runs the nearest shape that exists
        if (site == "in-generic-type") { r.Substituted("shape.site", "top-level", "CMTK1005/CMTK1018: nothing nests in a generic type"); site = "top-level"; }
        if (kind is "record-struct" or "validated-struct" && inh.StartsWith("base-")) { r.Substituted("shape.inheritance", "none", "a struct has no base type"); inh = "none"; }
        var wrapped = closed ? "-" : r["shape.wrapped"];
        var en = L05Registry.Get(kind, wrapped, inh, site);
        if (en is null) { r.Outcome("harness-error", $"no L05 shape for {kind}/{wrapped}/{inh}/{site}: run harness/gen_json_family.py"); return null; }
        var v = en.Type;
        object x1, x2, y; bool eq12;
        Type? w = null; object? a1 = null, b = null;
        if (closed)
        {
            x1 = en.A1(); x2 = en.A2(); y = en.B();
            if (!ReferenceEquals(x1, x2)) return "closed set: the member found in All is not the member itself";
            eq12 = true;
        }
        else
        {
            w = v.GetProperty("Value")!.PropertyType;
            a1 = en.A1(); var a2 = en.A2(); b = en.B();
            var cw = Comparer(w);
            if (!cw.Equals(a1, a2) || cw.Equals(a1, b)) { r.Outcome("harness-error", $"samples of {wrapped}: {a1} and {a2} must be equal, {b} not"); return null; }
            eq12 = true;
            x1 = Make(v, w, a1); x2 = Make(v, w, a2); y = Make(v, w, b);
            if (!cw.Equals(v.GetProperty("Value")!.GetValue(x1), a1)) return $"Value of {v.Name} is not the value it was made from";
        }
        var fails = new List<string>();
        void Expect(bool got, bool want, string what) { if (got != want) fails.Add($"{what}: {got}, expected {want}"); }

        // the value object's own equality
        Expect(x1.Equals(x2), eq12, "Equals(object) of equal values");
        Expect(x1.Equals(y), false, "Equals(object) of unequal values");
        Expect(x1.Equals(null), false, "Equals(null)");
        Expect(Comparer(v).Equals(x1, x2), eq12, "EqualityComparer<V> of equal values");
        Expect(Comparer(v).Equals(x1, y), false, "EqualityComparer<V> of unequal values");
        if (x1!.GetHashCode() != x2!.GetHashCode()) fails.Add($"equal values hash apart: {x1.GetHashCode()} and {x2.GetHashCode()}");
        if (Comparer(v).GetHashCode(x1) != Comparer(v).GetHashCode(x2)) fails.Add("EqualityComparer<V> hashes equal values apart");
        if (Op(v, "op_Equality", x1, x2) is { } e12) Expect((bool)e12, eq12, "== of equal values"); else if (!closed) fails.Add("no operator ==");
        if (Op(v, "op_Equality", x1, y) is { } e1b) Expect((bool)e1b, false, "== of unequal values");
        if (Op(v, "op_Inequality", x1, x2) is { } n12) Expect((bool)n12, !eq12, "!= of equal values");

        // ordering: generated where the wrapped type is comparable, ordinal for a string, zero exactly when equal
        var icv = typeof(IComparable<>).MakeGenericType(v);
        if (!closed)
        {
            var comparableW = typeof(IComparable<>).MakeGenericType(w!).IsAssignableFrom(w) || typeof(IComparable).IsAssignableFrom(w);   // an enum is IComparable only
            if (icv.IsAssignableFrom(v) != comparableW) fails.Add($"IComparable<{v.Name}> is {(icv.IsAssignableFrom(v) ? "" : "not ")}implemented, the wrapped {w!.Name} is {(comparableW ? "" : "not ")}comparable");
        }
        if (icv.IsAssignableFrom(v))
        {
            var cmp = icv.GetMethod("CompareTo")!;
            var c12 = (int)cmp.Invoke(x1, [x2])!; var c1b = (int)cmp.Invoke(x1, [y])!; var cb1 = (int)cmp.Invoke(y, [x1])!;
            Expect(c12 == 0, eq12, "CompareTo of equal values is zero");
            if (c1b == 0) fails.Add("CompareTo of unequal values is zero");
            if (Sign(c1b) != -Sign(cb1)) fails.Add($"CompareTo is not antisymmetric: {c1b} and {cb1}");
            if (!closed)
            {
                var want = w == typeof(string) ? string.CompareOrdinal((string)a1!, (string)b!) : ((IComparer)typeof(Comparer<>).MakeGenericType(w!).GetProperty("Default")!.GetValue(null)!).Compare(a1, b);
                if (Sign(c1b) != Sign(want)) fails.Add($"CompareTo orders {c1b}, the wrapped type {want}");
            }
            if (x1 is IComparable nc) { if (Sign(nc.CompareTo(y)) != Sign(c1b) || nc.CompareTo(x2) != 0 == eq12) fails.Add("CompareTo(object) disagrees with CompareTo(TSelf)"); }
            if (Op(v, "op_LessThan", x1, y) is { } lt) Expect((bool)lt, c1b < 0, "<");
            if (Op(v, "op_GreaterThan", x1, y) is { } gt) Expect((bool)gt, c1b > 0, ">");
            if (Op(v, "op_LessThanOrEqual", x1, x2) is { } le) Expect((bool)le, true, "<= of equal values");
            if (Op(v, "op_GreaterThanOrEqual", x1, x2) is { } ge) Expect((bool)ge, true, ">= of equal values");
            if (Op(v, "op_LessThan", x1, x2) is { } lt2) Expect((bool)lt2, false, "< of equal values");
        }

        // through every base, and every IEquatable<TBase> it implements
        for (var t = v.BaseType; t is not null; t = t.BaseType)
        {
            var c = Comparer(t);
            Expect(c.Equals(x1, x2), eq12, $"EqualityComparer<{t.Name}> of equal values");
            Expect(c.Equals(x1, y), false, $"EqualityComparer<{t.Name}> of unequal values");
            if (c.GetHashCode(x1) != c.GetHashCode(x2)) fails.Add($"EqualityComparer<{t.Name}> hashes equal values apart");
            if (Op(t, "op_Equality", x1, x2) is { } be) Expect((bool)be, eq12, $"{t.Name}'s == of equal values");
            var set = (IEnumerable)Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(t))!;
            var add = set.GetType().GetMethod("Add")!; add.Invoke(set, [x1]); add.Invoke(set, [x2]); add.Invoke(set, [y]);
            var count = set.Cast<object>().Count();
            if (count != 2) fails.Add($"HashSet<{t.Name}> of two equal values and one other holds {count}");
        }
        foreach (var i in v.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEquatable<>)))
        {
            var m = i.GetMethod("Equals")!;
            Expect((bool)m.Invoke(x1, [x2])!, eq12, $"{Jx.Name(i)}.Equals of equal values");
            Expect((bool)m.Invoke(x1, [y])!, false, $"{Jx.Name(i)}.Equals of unequal values");
        }

        // wherever it is held
        var position = r["shape.position"];
        var containing = position == "root" ? "class" : r["shape.containing"];
        var e = new Entry(kind, wrapped, v, v, () => x1, () => null, typeof(L05PolyBase), typeof(L05PolyDerived<>).MakeGenericType(v), typeof(L05PolyBase), typeof(L05PolyDerived<>).MakeGenericType(v));
        var h1 = Pos.Build(position, containing, e, true, x1); var h2 = Pos.Build(position, containing, e, true, x2); var h3 = Pos.Build(position, containing, e, true, y);
        object? Member(Held h) => position == "root" ? h.Value : h.Declared.GetProperty("P") is { } p ? p.GetValue(h.Value) : h.Declared.GetField("F")!.GetValue(h.Value);
        switch (position)
        {
            case "root": break;
            case "list-element" or "array-element" or "immutable-array-element" or "read-only-list-element":
            {
                var l = (IList)Member(h1)!;
                Expect(l.Contains(x2), eq12, $"{Jx.Name(l.GetType())}.Contains of an equal value");
                Expect(l.Contains(y), false, $"{Jx.Name(l.GetType())}.Contains of an unequal value");
                if (l.IndexOf(x2) != 0) fails.Add("IndexOf of an equal value is " + l.IndexOf(x2));
                break;
            }
            case "dictionary-key":
            {
                var d = (IDictionary)Member(h1)!;
                Expect(d.Contains(x2), eq12, "dictionary lookup by an equal key");
                Expect(d.Contains(y), false, "dictionary lookup by an unequal key");
                break;
            }
            case "dictionary-value":
            {
                var d = Member(h1)!; var cv = d.GetType().GetMethod("ContainsValue")!;
                Expect((bool)cv.Invoke(d, [x2])!, eq12, "ContainsValue of an equal value");
                Expect((bool)cv.Invoke(d, [y])!, false, "ContainsValue of an unequal value");
                break;
            }
            default:
            {
                // a container with value equality compares through the member; one with reference equality is compared by its member
                var byValue = containing is "record-class" or "record-struct" or "struct";
                var (p1, p2, p3) = byValue ? (h1.Value, h2.Value, h3.Value) : (Member(h1), Member(h2), Member(h3));
                var what = byValue ? Jx.Name(h1.Declared) : $"the {position} of {Jx.Name(h1.Declared)}";
                Expect(Equals(p1, p2), eq12, what + " holding equal values");
                Expect(Equals(p1, p3), false, what + " holding unequal values");
                if (p1 is not null && p2 is not null && p1.GetHashCode() != p2.GetHashCode()) fails.Add(what + " holding equal values hashes them apart");
                break;
            }
        }
        if (fails.Count > 0) return $"{Jx.Name(v)} ({kind}, {wrapped}, {inh}, {site}, {position} in {containing}): " + string.Join("; ", fails);
        r.Note(NoteKeys.Compared, $"{v.Name} {position}");
        return null;
    }
}
