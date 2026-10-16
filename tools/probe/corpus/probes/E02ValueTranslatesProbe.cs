// Claim E02-value-translates-to-column (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#queries): ".Value, GetValue() and ValueOrNull() translate to the column itself" (column parity: same SQL as the query over the wrapped property).
// Oracle: differential. For each query shape the SQL (ToQueryString) of the query over the value object's accessor equals the SQL of the same query over the wrapped-type property, and both return the same rows.
// Expected: no failure for any row; both sides refusing is an expected refusal.
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E02ValueTranslatesProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E02-value-translates-to-column");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, Check);

    static MethodInfo? Unwrap(Type vo, string name) =>
        vo.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1);

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var e = Probes.Ef3.E4.At(r, r["shape.kind"], r["shape.wrapped"], r["shape.site"]);
        var acc = r["ef.accessor"];
        var q = r["ef.query"];
        var nullable = acc == "ValueOrNull";
        var mapping = nullable ? "nullable" : "property";
        MethodInfo? unwrap = acc == "Value" ? null : e.Companion(acc);
        if (acc != "Value" && unwrap is null) return $"no static {acc}(...) on {e.Vo}";
        var voProp = nullable && e.Vo.IsValueType ? typeof(Nullable<>).MakeGenericType(e.Vo) : e.Vo;
        var plProp = nullable && e.Plain.IsValueType ? typeof(Nullable<>).MakeGenericType(e.Plain) : e.Plain;
        var voEntity = EfModel.Entity(mapping, e.Vo);
        var plainEntity = EfModel.Entity(mapping, e.Plain);
        // the type the accessor yields; the plain side is the property itself
        Type accessType = acc == "Value" ? e.Vo.GetProperty("Value")!.PropertyType : unwrap!.ReturnType;
        object constant = nullable && !e.Plain.IsValueType ? e.Valid() : e.Valid();

        IQueryable Build(DbContext db, Type entity, bool vo)
        {
            var p = Expression.Parameter(entity, "e");
            Expression prop = Expression.Property(p, "P");
            Expression access = !vo ? prop : acc == "Value" ? Expression.Property(prop, e.Vo.GetProperty("Value")!) : Expression.Call(unwrap!, prop);
            var at = access.Type;
            var src = Q.SetOf(db, entity);
            var box = Expression.Constant(Q.NewBox(at, constant));
            Expression param = Expression.Field(box, "Value");
            LambdaExpression Lam(Expression body) => Expression.Lambda(body, p);
            switch (q)
            {
                case "where-equals": return Q.Call("Where", [entity], src, Lam(Expression.Equal(access, param)));
                case "where-value-compare":
                    BinaryExpression cmp;
                    try { cmp = Expression.GreaterThanOrEqual(access, param); }
                    catch (InvalidOperationException) { r.Substituted("ef.query", "where-equals", "the wrapped type has no >= operator"); cmp = Expression.Equal(access, param); }
                    return Q.Call("Where", [entity], src, Lam(cmp));
                case "order-by": return Q.Call("OrderBy", [entity, at], src, Lam(access));
                case "select": return Q.Call("Select", [entity, at], src, Lam(access));
                case "contains":
                    var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(at))!;
                    list.GetType().GetMethod("Add")!.Invoke(list, [constant]);
                    var lbox = Expression.Constant(Q.NewBox(list.GetType(), list));
                    var contains = typeof(Enumerable).GetMethods().First(m => m.Name == "Contains" && m.GetParameters().Length == 2).MakeGenericMethod(at);
                    return Q.Call("Where", [entity], src, Lam(Expression.Call(contains, Expression.Field(lbox, "Value"), access)));
                case "group-by":
                    var grouped = Q.Call("GroupBy", [entity, at], src, Lam(access));
                    var gt = typeof(IGrouping<,>).MakeGenericType(at, entity);
                    var g = Expression.Parameter(gt, "g");
                    return Q.Call("Select", [gt, at], grouped, Expression.Lambda(Expression.Property(g, "Key"), g));
                default: throw new ArgumentException(q);
            }
        }

        async Task<(string? Sql, List<string>? Rows, Exception? Ex)> Side(Type entity, bool vo)
        {
            await using var db = new EfDb(EfModel.Configure(mapping, entity, null, false), typeKit: vo);
            try
            {
                await db.Context.Database.EnsureCreatedAsync();
                var row = Activator.CreateInstance(entity)!;
                var value = vo ? e.MakeVo(e.Valid()) : e.Valid();
                entity.GetProperty("P")!.SetValue(row, value);
                db.Context.Add(row);
                await db.Context.SaveChangesAsync();
                db.Context.ChangeTracker.Clear();
                var query = Build(db.Context, entity, vo);
                var sql = query.ToQueryString();
                var list = new List<string>();
                foreach (var x in (System.Collections.IEnumerable)query) list.Add(Norm(x));
                return (sql, list, null);
            }
            catch (Exception ex) { return (null, null, ex); }
        }
        var v = await Side(voEntity, true);
        var pl = await Side(plainEntity, false);
        if (v.Ex is not null && pl.Ex is not null) { r.Note("both-threw", v.Ex.GetType().Name); return null; }
        if (pl.Ex is not null) { r.Note("plain-refused", pl.Ex.GetType().Name); return null; }
        if (v.Ex is not null) return $"the query over the value object threw {v.Ex.GetType().Name}: {Trim(v.Ex.Message)}; the same query over the wrapped type did not";
        if (v.Sql != pl.Sql) return $"SQL differs.\nVO:\n{v.Sql}\nplain:\n{pl.Sql}";
        if (!v.Rows!.SequenceEqual(pl.Rows!)) return $"rows differ: VO [{string.Join(",", v.Rows!)}] plain [{string.Join(",", pl.Rows!)}]";
        return null;
    }
    /// <summary>A result as comparable text: an entity by its P, a value object by the value it wraps.</summary>
    static string Norm(object? x)
    {
        if (x is null) return "<null>";
        var t = x.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() is var d && (d == typeof(EntP<>) || d == typeof(EntNC<>))) return Norm(t.GetProperty("P")!.GetValue(x));
        if (t.GetProperty("Value") is { } v && v.DeclaringType == t && t.Namespace == "Probes.Ef" || t.Name == "V" || t.Name.StartsWith("E_")) return Norm(t.GetProperty("Value")!.GetValue(x));
        return Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>";
    }
    static string Trim(string s) => s.Length > 400 ? s[..400] : s;
}
