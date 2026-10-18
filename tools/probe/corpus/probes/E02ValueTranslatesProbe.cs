// Claim E02-value-translates-to-column (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#queries): ".Value, GetValue() and ValueOrNull() translate to the column itself" (column parity: same SQL as the query over the wrapped property).
// Oracle: differential. For each query shape the SQL (ToQueryString) of the query over the value object's accessor equals the SQL of the same query over the wrapped-type property, and both return the same rows.
// Obligations (claims.json), each against the wrapped twin (Baseline.Twin):
//   value, get-value, value-or-null (by ef.accessor): "`.Value`, `GetValue()` and `ValueOrNull()` translate to the column itself.": the rows the query answers
//   column-not-cast (every row): "A table's column is re-typed, not cast, so an index on it still serves the query.": the SQL
// Expected: no failure for any row; where a side refuses the query, EfSides says how the check ends. Where SQLite cannot compare or
// order the wrapped type, every obligation declares itself inapplicable (claims.json, fact KB-039). where-value-compare is >= as C# writes
// it, over the underlying type for an enum; a string, a bool and a Uri have none, and claims.json excludes the pair.
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E02ValueTranslatesProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E02ValueTranslatesProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static MethodInfo? Unwrap(Type vo, string name) =>
        vo.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1);

    /// <summary>The type C# compares an enum (or a nullable enum) over: its underlying type, nullable where it is; null for any other type.</summary>
    static Type? Underlying(Type t) => Nullable.GetUnderlyingType(t) is { IsEnum: true } e ? typeof(Nullable<>).MakeGenericType(Enum.GetUnderlyingType(e))
                                      : t.IsEnum ? Enum.GetUnderlyingType(t) : null;

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
                    // C# compares two values of an enum with >= over its underlying type, and an expression tree it compiles says so
                    // with a conversion on each side; Expression.GreaterThanOrEqual defines no >= for the enum itself.
                    catch (InvalidOperationException) when (Underlying(at) is { } u) { cmp = Expression.GreaterThanOrEqual(Expression.Convert(access, u), Expression.Convert(param, u)); }
                    // string, bool and Uri have no >= in C# (excluded in claims.json): no row asks for one
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

        // One side's query: its SQL, or the exception translating it; the rows it answers, or the exception running it.
        async Task<(string? Sql, Exception? Translating, List<string>? Rows, Exception? Running)> Side(Type entity, bool vo)
        {
            await using var db = new EfDb(EfModel.Configure(mapping, entity, null, false), typeKit: vo);
            IQueryable query;
            try
            {
                await db.Context.Database.EnsureCreatedAsync();
                var row = Activator.CreateInstance(entity)!;
                var value = vo ? e.MakeVo(e.Valid()) : e.Valid();
                entity.GetProperty("P")!.SetValue(row, value);
                db.Context.Add(row);
                await db.Context.SaveChangesAsync();
                db.Context.ChangeTracker.Clear();
                query = Build(db.Context, entity, vo);
            }
            catch (Exception ex) { return (null, ex, null, ex); }
            string sql;
            try { sql = query.ToQueryString(); }
            catch (Exception ex) { return (null, ex, null, ex); }
            try
            {
                var list = new List<string>();
                foreach (var x in (System.Collections.IEnumerable)query) list.Add(Norm(x));
                return (sql, null, list, null);
            }
            catch (Exception ex) { return (sql, null, null, ex); }
        }
        var v = await Side(voEntity, true);
        var pl = await Side(plainEntity, false);

        // The accessor's obligation: the query over it answers what the same query over the wrapped property answers.
        var answered = r.Check(acc switch { "Value" => "value", "GetValue" => "get-value", _ => "value-or-null" }, Baseline.Twin, c =>
        {
            if (EfSides.Threw(c, v.Running, pl.Running, "the query", out var refused)) return refused;
            if (!v.Rows!.SequenceEqual(pl.Rows!)) return $"rows differ: VO [{string.Join(",", v.Rows!)}] plain [{string.Join(",", pl.Rows!)}]";
            c.Note(NoteKeys.Compared, $"{v.Rows!.Count} row(s)");
            return null;
        });
        // column-not-cast: the SQL is the wrapped property's, so the column is re-typed and not cast.
        var sqlOf = r.Check("column-not-cast", Baseline.Twin, c =>
        {
            if (EfSides.Threw(c, v.Translating, pl.Translating, "translating the query", out var refused)) return refused;
            if (v.Sql != pl.Sql) return $"SQL differs.\nVO:\n{v.Sql}\nplain:\n{pl.Sql}";
            c.Note(NoteKeys.Compared, "the SQL");
            return null;
        });
        return answered ?? sqlOf;
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
}
