// Claim E13-primitive-collections (README#queries): "db.Orders.Where(o => o.Tags.Contains(tag)); db.Orders.Where(o => o.Tags.Any(t => t.Value == "x"));" over a List<ValueObject> primitive collection.
// Oracle: claim plus differential. The ids the query returns equal the ids whose collection holds the value (ground truth computed in memory), and equal what the same query returns over List<wrapped type>.
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E13PrimitiveCollectionProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E13PrimitiveCollectionProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var w = r["shape.wrapped"]; var qk = r["ef.query"];
        var kind = EfX.Kinds[r.Index % 2];   // record-struct and sealed-record-class: a validated kind's rule may refuse a sample
        var e = EfX.Top(kind, w);
        int[][] data = [[0, 1], [1, 2], [2], []];
        var needles = new[] { 0, 1, 2, 3 };   // 3 is in no collection

        async Task<(string res, string? err)> Side(bool vo)
        {
            var el = vo ? e.Vo : e.Plain;
            var entity = typeof(EntL<>).MakeGenericType(el);
            object Make(int i) { var p = EfX.Sample(w, i); return vo ? e.MakeVo(p) : p; }
            Action<Microsoft.EntityFrameworkCore.ModelBuilder> cfg = mb => { var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id"); };
            await using var db = new EfDb(cfg, typeKit: vo);
            try
            {
                await db.Context.Database.EnsureCreatedAsync();
                for (var i = 0; i < data.Length; i++)
                {
                    var ent = EfX.New(entity); EfX.Set(ent, "Id", i + 1);
                    var list = EfX.Get(ent, "Items")!;
                    foreach (var k in data[i]) list.GetType().GetMethod("Add")!.Invoke(list, [Make(k)]);
                    db.Context.Add(ent);
                }
                await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
                var lines = new List<string>();
                foreach (var n in needles)
                {
                    var p = Expression.Parameter(entity, "e");
                    var items = Expression.Property(p, "Items");
                    Expression body;
                    if (qk == "contains")
                    {
                        var box = Expression.Constant(Q.NewBox(el, Make(n)));
                        body = Expression.Call(items, typeof(List<>).MakeGenericType(el).GetMethod("Contains")!, Expression.Field(box, "Value"));
                    }
                    else
                    {
                        var t = Expression.Parameter(el, "t");
                        var plainBox = Expression.Constant(Q.NewBox(e.Plain, EfX.Sample(w, n)));
                        var lhs = vo ? Expression.Property(t, "Value") : (Expression)t;
                        var inner = Expression.Lambda(Expression.Equal(lhs, Expression.Field(plainBox, "Value")), t);
                        var any = typeof(Enumerable).GetMethods().First(m => m.Name == "Any" && m.GetParameters().Length == 2).MakeGenericMethod(el);
                        body = Expression.Call(any, items, inner);
                    }
                    var q = Q.Call("Where", [entity], Q.SetOf(db.Context, entity), Expression.Lambda(body, p));
                    lines.Add($"{n}:" + string.Join(",", EfX.Enumerate(q).Select(x => (int)EfX.Get(x, "Id")!).OrderBy(x => x)));
                    db.Context.ChangeTracker.Clear();
                }
                return (string.Join(" ", lines), null);
            }
            catch (Exception ex) { return ("", ex.GetType().Name + ": " + ex.Message.Split('\n')[0]); }
        }
        var truth = string.Join(" ", needles.Select(n => $"{n}:" + string.Join(",", Enumerable.Range(0, data.Length).Where(i => data[i].Contains(n)).Select(i => i + 1))));
        var v = await Side(true);
        if (v.err is null) return v.res == truth ? null : $"{qk} returned [{v.res}], the collections hold [{truth}]";
        var p = await Side(false);
        if (p.err is not null) { r.Note(NoteKeys.BothThrew, v.err.Split(':')[0] + "/" + p.err.Split(':')[0]); return null; }
        return $"{qk} over List<{w} value object> threw ({v.err}) where List<{w}> returned [{p.res}]";
    }
}
