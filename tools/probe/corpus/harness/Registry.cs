// Hand half of the shape registry (see gen_shapes.py). Looks an entry up by the row's shape.kind and shape.wrapped.
namespace Probes.Shapes;
public static partial class Registry
{
    public static readonly List<Entry> All = Build();
    static List<Entry> Build() { var e = new List<Entry>(); Fill(e); return e; }
    static partial void Fill(List<Entry> e);
    /// <summary>The entry a row names. record-class (CMTK1006) and over-value-object (CMTK1005) are refused at build, verified; the row's other axes are
    /// still probed on the nearest buildable kind.</summary>
    public static Entry Of(Row r)
    {
        var k = r["shape.kind"];
        if (k is "record-class" or "over-value-object") { k = k == "record-class" ? "sealed-record-class" : "record-struct"; r.Substituted("shape.kind", k, "the generators refuse the requested kind"); }
        var e = Get(k, r["shape.wrapped"]);
        if (r.Is("http.nullability", "nullable"))
        {
            // The parameter or body declared nullable on both sides (T? against wrapped?): the dimension G1 exposed.
            static Type N(Type t) => t.IsValueType && Nullable.GetUnderlyingType(t) is null ? typeof(Nullable<>).MakeGenericType(t) : t;
            r.Note(NoteKeys.BindingNullable);
            e = e with { Vo = N(e.Vo), Plain = N(e.Plain) };
        }
        return e;
    }
    public static Entry Get(string kind, string wrapped) =>
        kind == "closed-set" ? All.First(x => x.Kind == "closed-set")
        : All.First(x => x.Kind == kind && x.Wrapped == wrapped);
}
