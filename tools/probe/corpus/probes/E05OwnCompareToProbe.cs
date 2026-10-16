// Claim E05-own-compareto-rejected (README#queries): "Your own CompareTo(TSelf) can't run in the database ... these queries throw an InvalidOperationException naming the value object: a comparison (<, >, <=, >=, CompareTo); an ordering (OrderBy, ThenBy, their descending forms, Order); Min or Max. ... Equality isn't ordering, and stays allowed. A query filter (HasQueryFilter, named or not) that does the same fails when the model is built."
// Oracle: exception. A value object with a descending CompareTo, in each such query, must throw InvalidOperationException naming it (never return rows); equality and ordering by .Value return what memory returns.
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E05OwnCompareToProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E05-own-compareto-rejected");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, Check);

    static readonly string[] Refused = ["lt", "gt", "le", "ge", "CompareTo", "OrderBy", "OrderByDescending", "ThenBy", "Order", "Min", "Max"];

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var h = HandOrdered.Of(r["ef.vo"]);
        var op = r["ef.op"];
        var entity = typeof(EntP<>).MakeGenericType(h.Vo);
        var filter = op is "HasQueryFilter" or "named-query-filter";
        var box = Expression.Constant(Q.NewBox(h.Vo, HandOrdered.Make(h, h.Values[1])));
        Expression Const() => Expression.Field(box, "Value");
        var p = Expression.Parameter(entity, "e");
        var prop = Expression.Property(p, "P");
        LambdaExpression Lam(Expression body) => Expression.Lambda(body, p);
        Expression? predicate = null;
        try
        {
            predicate = op switch
            {
                "lt" => Expression.LessThan(prop, Const()),
                "gt" => Expression.GreaterThan(prop, Const()),
                "le" => Expression.LessThanOrEqual(prop, Const()),
                "ge" => Expression.GreaterThanOrEqual(prop, Const()),
                "CompareTo" => Expression.GreaterThan(Expression.Call(prop, h.Vo.GetMethod("CompareTo", [h.Vo])!, Const()), Expression.Constant(0)),
                "equals" => Expression.Equal(prop, Const()),
                "HasQueryFilter" or "named-query-filter" => Expression.GreaterThan(prop, Const()),
                _ => null,
            };
        }
        catch (InvalidOperationException ex) { r.Note("no-operator", ex.Message); return null; }

        Action<ModelBuilder> cfg = mb =>
        {
            var eb = mb.Entity(entity);
            eb.ToTable("t"); eb.HasKey("Id");
            if (op == "HasQueryFilter") eb.HasQueryFilter(Lam(predicate!));
            if (op == "named-query-filter") eb.HasQueryFilter("AboveTwo", Lam(predicate!));
        };
        await using var db = new EfDb(cfg, typeKit: true);
        object[] Seed() => h.Values.Select(v => HandOrdered.Make(h, v)).ToArray();
        Exception? thrown = null;
        List<string> result = [];
        try
        {
            await db.Context.Database.EnsureCreatedAsync();
            foreach (var v in Seed()) { var row = Activator.CreateInstance(entity)!; entity.GetProperty("P")!.SetValue(row, v); db.Context.Add(row); }
            await db.Context.SaveChangesAsync();
            db.Context.ChangeTracker.Clear();
            var src = Q.SetOf(db.Context, entity);
            object? scalar = null;
            IQueryable? query = null;
            switch (op)
            {
                case "lt" or "gt" or "le" or "ge" or "CompareTo" or "equals": query = Q.Call("Where", [entity], src, Lam(predicate!)); break;
                case "OrderBy": query = Q.Call("OrderBy", [entity, h.Vo], src, Lam(prop)); break;
                case "OrderByDescending": query = Q.Call("OrderByDescending", [entity, h.Vo], src, Lam(prop)); break;
                case "ThenBy": query = Q.Call("ThenBy", [entity, h.Vo], Q.Call("OrderBy", [entity, typeof(int)], src, Lam(Expression.Property(p, "Id"))), Lam(prop)); break;
                case "value-order":
                    var value = h.Vo.GetProperty("Value")!;
                    query = Q.Call("OrderBy", [entity, h.Plain], src, Lam(Expression.Property(prop, value))); break;
                case "Order" or "Min" or "Max":
                    var sel = Q.Call("Select", [entity, h.Vo], src, Lam(prop));
                    var m = typeof(Queryable).GetMethods().First(x => x.Name == op && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 && x.GetGenericArguments().Length == 1).MakeGenericMethod(h.Vo);
                    try { var res = m.Invoke(null, [sel]); if (op == "Order") query = (IQueryable)res!; else scalar = res; }
                    catch (TargetInvocationException tie) { throw tie.InnerException!; }
                    break;
                case "HasQueryFilter" or "named-query-filter":
                    query = src; break;
                default: throw new ArgumentException(op);
            }
            if (query is not null) foreach (var x in (System.Collections.IEnumerable)query) result.Add(Plain(x));
            else result.Add(Plain(scalar));
        }
        catch (Exception ex) { thrown = ex; }

        if (Refused.Contains(op) || filter)
        {
            if (thrown is null) return $"{op} on {h.Vo.Name} returned [{string.Join(",", result)}] instead of throwing";
            if (thrown is not InvalidOperationException) return $"{op}: expected InvalidOperationException, got {thrown.GetType().Name}: {Trim(thrown.Message)}";
            if (!thrown.Message.Contains(h.Vo.Name)) return $"{op}: the exception does not name {h.Vo.Name}: {Trim(thrown.Message)}";
            if (filter && !thrown.Message.Contains("query filter")) return $"{op}: the exception does not say it is the query filter: {Trim(thrown.Message)}";
            if (!filter && !thrown.Message.Contains(".Value")) return $"{op}: the exception does not point to .Value: {Trim(thrown.Message)}";
            return null;
        }
        if (thrown is not null) return $"{op} (allowed) threw {thrown.GetType().Name}: {Trim(thrown.Message)}";
        // allowed: equals keeps the one equal row; value-order sorts by the wrapped value, ascending
        var expected = op == "equals" ? new[] { Plain(h.Values[1]) } : h.Values.OrderBy(v => v, Comparer<object>.Create((a, b) => ((IComparable)a).CompareTo(b))).Select(Plain).ToArray();
        return result.SequenceEqual(expected) ? null : $"{op}: got [{string.Join(",", result)}], memory gives [{string.Join(",", expected)}]";
    }
    static string Plain(object? x)
    {
        if (x is null) return "<null>";
        var t = x.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EntP<>)) return Plain(t.GetProperty("P")!.GetValue(x));
        if (t.Namespace == "Probes.Ef" && t.GetProperty("Value") is { } v) return Plain(v.GetValue(x));
        return Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>";
    }
    static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
