// Claim E15-composite-key (docs/ef-core.md#keys): "A composite key of value objects is HasKey(x => new { x.Order, x.Line }), as for primitives."
// Oracle: differential against the same composite key declared over the wrapped types: same model description (key, column types, generation),
// same Find / equality-query / ordered results, same refusal of a duplicate key; and the claim's own ground truth for the lookups.
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E15CompositeKeyProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E15-composite-key");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, Check);

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var w = r["shape.wrapped"];
        var first = r["ef.key-position"] == "first";
        var kind = EfX.Kinds[r.Index % EfX.Kinds.Length];
        var ew = KeyRegistry.All.First(x => x.Kind == kind && x.Wrapped == w);
        var ei = KeyRegistry.All.First(x => x.Kind == kind && x.Wrapped == "int");
        // the wrapped-type key under test sits at the requested position; the other element is a value object over int (a "Line")
        object WPlain(int i) => EfX.Sample(w, i);
        object IPlain(int i) => 1 + i;

        async Task<(string desc, string truth)> Side(bool vo)
        {
            Type tw = vo ? ew.Vo : ew.Plain, ti = vo ? ei.Vo : ei.Plain;
            var (ta, tb) = first ? (tw, ti) : (ti, tw);
            var entity = typeof(EntComp<,>).MakeGenericType(ta, tb);
            object W(int i) => vo ? ew.MakeVo(WPlain(i)) : WPlain(i);
            object I(int i) => vo ? ei.MakeVo(IPlain(i)) : IPlain(i);
            (object a, object b) Key(int wi, int ii) => first ? (W(wi), I(ii)) : (I(ii), W(wi));
            Action<Microsoft.EntityFrameworkCore.ModelBuilder> cfg = mb =>
            {
                var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("A", "B");
            };
            await using var db = new EfDb(cfg, typeKit: vo);
            await db.Context.Database.EnsureCreatedAsync();
            var model = EfModel.Describe(db.Context);
            var pk = string.Join(",", db.Context.Model.FindEntityType(entity)!.FindPrimaryKey()!.Properties.Select(p => p.Name));
            (int wi, int ii)[] seeds = [(0, 0), (0, 1), (1, 0)];
            foreach (var (wi, ii) in seeds)
            {
                var e = EfX.New(entity); var (a, b) = Key(wi, ii);
                EfX.Set(e, "A", a); EfX.Set(e, "B", b); EfX.Set(e, "Tag", $"t{wi}{ii}");
                db.Context.Add(e);
            }
            await db.Context.SaveChangesAsync();
            db.Context.ChangeTracker.Clear();
            var lines = new List<string> { "pk=" + pk, model };
            // Find by the whole key
            foreach (var (wi, ii) in new[] { (0, 0), (0, 1), (1, 0), (1, 1) })
            {
                var (a, b) = Key(wi, ii);
                var found = await db.Context.FindAsync(entity, a, b);
                lines.Add($"find{wi}{ii}=" + (found is null ? "null" : EfX.Get(found, "Tag")));
                db.Context.ChangeTracker.Clear();
            }
            // equality query on the first key column, and on the second
            foreach (var col in new[] { "A", "B" })
            {
                var (a, b) = Key(0, 0);
                var box = Expression.Constant(Q.NewBox(col == "A" ? ta : tb, col == "A" ? a : b));
                var p = Expression.Parameter(entity, "e");
                var pred = Expression.Equal(Expression.Property(p, col), Expression.Field(box, "Value"));
                var q = Q.Call("Where", [entity], Q.SetOf(db.Context, entity), Expression.Lambda(pred, p));
                lines.Add($"where{col}=" + string.Join("|", EfX.Enumerate(q).Select(x => (string)EfX.Get(x, "Tag")!).OrderBy(x => x, StringComparer.Ordinal)));
            }
            db.Context.ChangeTracker.Clear();
            // a duplicate whole key is refused
            var dup = EfX.New(entity); var (da, dbk) = Key(0, 1);
            EfX.Set(dup, "A", da); EfX.Set(dup, "B", dbk); EfX.Set(dup, "Tag", "dup");
            db.Context.Add(dup);
            try { await db.Context.SaveChangesAsync(); lines.Add("dup=accepted"); } catch (Exception ex) { lines.Add("dup=" + ex.GetType().Name); }
            db.Context.ChangeTracker.Clear();
            var all = string.Join(",", EfX.Enumerate(Q.SetOf(db.Context, entity)).Select(x => (string)EfX.Get(x, "Tag")!).OrderBy(x => x, StringComparer.Ordinal));
            lines.Add("rows=" + all);
            var truth = string.Join(";", lines.Where(l => l.StartsWith("find") || l.StartsWith("where") || l.StartsWith("dup") || l.StartsWith("rows")));
            return (string.Join("\n", lines), truth);
        }
        var v = await Side(true);
        var p = await Side(false);
        const string Expected = "find00=t00;find01=t01;find10=t10;find11=null;whereA=t00|t01;whereB=t00|t10;dup=DbUpdateException;rows=t00,t01,t10";
        // whereA/whereB depend on the position: A is the first key element
        var expected = first ? Expected : Expected.Replace("whereA=t00|t01;whereB=t00|t10", "whereA=t00|t10;whereB=t00|t01");
        if (v.truth != expected) return $"VO side: {v.truth}; expected {expected}";
        if (v.desc != p.desc) return "model/results differ from the plain composite key: " + EfModel.FirstDiff(v.desc, p.desc);
        return null;
    }
}
