// Claim E09-or-none-in-database (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#an-option-for-nothing-found): "These ask the database for one row and let it run the condition: FirstOrNoneAsync, FirstOrNone, FirstOrError read one row (WHERE ... LIMIT); SingleOrNone(Async) ask for two rows and throw for more than one; ElementAtOrNone skips to its row (OFFSET)."
// Oracle: claim. Over a table of value-object keys the operator returns what an in-memory evaluation of the same condition gives (none / the first match / the error), the executed SELECT carries the condition (WHERE) and LIMIT (OFFSET for ElementAtOrNone), and SingleOrNone throws InvalidOperationException for more than one match.
// Expected: no failure for any row. The condition is written as a lambda at the call or held as an Expression<Func<T,bool>> variable.
#nullable enable
#pragma warning disable CMTK0014, CS8618, CS1591, CMTK0005, CMTK1000, CMTK0003, CMTK0006, CA1000
using System.Data.Common;
using System.Linq.Expressions;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;

public sealed class SqlCap : DbCommandInterceptor
{
    public readonly List<string> Sql = [];
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Sql.Add(command.CommandText); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
}

public sealed class E09OrNoneInDbProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E09-or-none-in-database");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, Check);

    static Task<string?> Check(Probes.Shapes.Row r) => r["shape.wrapped"] switch { "int" => Run_int(r), "Guid" => Run_Guid(r), "string" => Run_string(r), var w => throw new ArgumentException(w) };

    static string Flat(string s) => s.Replace("\n", " ");
    static string? Verify(string op, string data, string got, string? thrown, List<int> matching, SqlCap cap)
    {
        var expect = op switch
        {
            "FirstOrNoneAsync" or "FirstOrNone" => matching.Count == 0 ? "none" : "some:" + matching[0],
            "FirstOrError" => matching.Count == 0 ? "err" : "ok:" + matching[0],
            "SingleOrNoneAsync" or "SingleOrNone" => matching.Count > 1 ? "throws" : matching.Count == 0 ? "none" : "some:" + matching[0],
            "ElementAtOrNone" => matching.Count > 1 ? "some:" + matching[1] : "none",
            _ => throw new ArgumentException(op),
        };
        var actual = thrown is not null ? (thrown == "InvalidOperationException" ? "throws" : "threw " + thrown) : got;
        if (actual != expect) return $"{op} over {data}: got {actual}, expected {expect}";
        var selects = cap.Sql.Where(s => s.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && s.Contains("FROM \"t\"")).ToList();
        if (selects.Count == 0) return $"{op}: no SELECT was executed";
        var last = selects[^1];
        if (!last.Contains("WHERE")) return $"{op}: the condition did not reach the database: {Flat(last)}";
        if (!last.Contains("LIMIT")) return $"{op}: the SELECT has no LIMIT (reads more than the rows asked for): {Flat(last)}";
        if (op == "ElementAtOrNone" && !last.Contains("OFFSET")) return $"{op}: the SELECT has no OFFSET: {Flat(last)}";
        return null;
    }

    public sealed class Ent_int { public int Id { get; set; } public PInt Key { get; set; } }
    static async Task<string?> Run_int(Probes.Shapes.Row r)
    {
        var op = r["ef.operator"]; var data = r["ef.data"]; var cond = r["ef.condition"]; _ = r["shape.wrapped"];
        var cap = new SqlCap();
        await using var db = new EfDb(mb => { var eb = mb.Entity<Ent_int>(); eb.ToTable("t"); eb.HasKey(x => x.Id); }, typeKit: true, b => b.AddInterceptors(cap));
        await db.Context.Database.EnsureCreatedAsync();
        PInt[] keys = data == "empty" ? [] : data == "one-match" ? [PInt.From(10), PInt.From(11), PInt.From(12)] : [PInt.From(10), PInt.From(11), PInt.From(11), PInt.From(12)];
        for (var i = 0; i < keys.Length; i++) db.Context.Add(new Ent_int { Id = i + 1, Key = keys[i] });
        await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
        var target = PInt.From(11);
        var matching = Enumerable.Range(0, keys.Length).Where(i => keys[i] == target).Select(i => i + 1).ToList();
        Expression<Func<Ent_int, bool>> held = o => o.Key == target;
        IQueryable<Ent_int> q = db.Context.Set<Ent_int>().OrderBy(o => o.Id);
        cap.Sql.Clear();
        var lam = cond == "lambda";
        string got = ""; string? thrown = null;
        try
        {
            switch (op)
            {
                case "FirstOrNoneAsync": got = Show(lam ? await q.FirstOrNoneAsync(o => o.Key == target) : await q.FirstOrNoneAsync(held)); break;
                case "FirstOrNone": got = Show(lam ? q.FirstOrNone(o => o.Key == target) : q.FirstOrNone(held)); break;
                case "SingleOrNoneAsync": got = Show(lam ? await q.SingleOrNoneAsync(o => o.Key == target) : await q.SingleOrNoneAsync(held)); break;
                case "SingleOrNone": got = Show(lam ? q.SingleOrNone(o => o.Key == target) : q.SingleOrNone(held)); break;
                case "ElementAtOrNone": got = Show(lam ? q.Where(o => o.Key == target).ElementAtOrNone(1) : q.Where(held).ElementAtOrNone(1)); break;
                case "FirstOrError":
                    var res = lam ? q.FirstOrError(o => o.Key == target, ShapeFault.Refused) : q.FirstOrError(held, ShapeFault.Refused);
                    got = res.Match(x => "ok:" + x.Id, _ => "err"); break;
                default: throw new ArgumentException(op);
            }
        }
        catch (Exception ex) { thrown = ex.GetType().Name; }
        return Verify(op, data, got, thrown, matching, cap);
    }
    static string Show(Option<Ent_int> o) => o.Match(x => "some:" + x.Id, () => "none");

    public sealed class Ent_Guid { public int Id { get; set; } public PGuid Key { get; set; } }
    static async Task<string?> Run_Guid(Probes.Shapes.Row r)
    {
        var op = r["ef.operator"]; var data = r["ef.data"]; var cond = r["ef.condition"]; _ = r["shape.wrapped"];
        var cap = new SqlCap();
        await using var db = new EfDb(mb => { var eb = mb.Entity<Ent_Guid>(); eb.ToTable("t"); eb.HasKey(x => x.Id); }, typeKit: true, b => b.AddInterceptors(cap));
        await db.Context.Database.EnsureCreatedAsync();
        PGuid[] keys = data == "empty" ? [] : data == "one-match" ? [PGuid.From(new Guid(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)), PGuid.From(new Guid(2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)), PGuid.From(new Guid(3, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11))] : [PGuid.From(new Guid(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)), PGuid.From(new Guid(2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)), PGuid.From(new Guid(2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)), PGuid.From(new Guid(3, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11))];
        for (var i = 0; i < keys.Length; i++) db.Context.Add(new Ent_Guid { Id = i + 1, Key = keys[i] });
        await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
        var target = PGuid.From(new Guid(2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11));
        var matching = Enumerable.Range(0, keys.Length).Where(i => keys[i] == target).Select(i => i + 1).ToList();
        Expression<Func<Ent_Guid, bool>> held = o => o.Key == target;
        IQueryable<Ent_Guid> q = db.Context.Set<Ent_Guid>().OrderBy(o => o.Id);
        cap.Sql.Clear();
        var lam = cond == "lambda";
        string got = ""; string? thrown = null;
        try
        {
            switch (op)
            {
                case "FirstOrNoneAsync": got = Show(lam ? await q.FirstOrNoneAsync(o => o.Key == target) : await q.FirstOrNoneAsync(held)); break;
                case "FirstOrNone": got = Show(lam ? q.FirstOrNone(o => o.Key == target) : q.FirstOrNone(held)); break;
                case "SingleOrNoneAsync": got = Show(lam ? await q.SingleOrNoneAsync(o => o.Key == target) : await q.SingleOrNoneAsync(held)); break;
                case "SingleOrNone": got = Show(lam ? q.SingleOrNone(o => o.Key == target) : q.SingleOrNone(held)); break;
                case "ElementAtOrNone": got = Show(lam ? q.Where(o => o.Key == target).ElementAtOrNone(1) : q.Where(held).ElementAtOrNone(1)); break;
                case "FirstOrError":
                    var res = lam ? q.FirstOrError(o => o.Key == target, ShapeFault.Refused) : q.FirstOrError(held, ShapeFault.Refused);
                    got = res.Match(x => "ok:" + x.Id, _ => "err"); break;
                default: throw new ArgumentException(op);
            }
        }
        catch (Exception ex) { thrown = ex.GetType().Name; }
        return Verify(op, data, got, thrown, matching, cap);
    }
    static string Show(Option<Ent_Guid> o) => o.Match(x => "some:" + x.Id, () => "none");

    public sealed class Ent_string { public int Id { get; set; } public PStr Key { get; set; } }
    static async Task<string?> Run_string(Probes.Shapes.Row r)
    {
        var op = r["ef.operator"]; var data = r["ef.data"]; var cond = r["ef.condition"]; _ = r["shape.wrapped"];
        var cap = new SqlCap();
        await using var db = new EfDb(mb => { var eb = mb.Entity<Ent_string>(); eb.ToTable("t"); eb.HasKey(x => x.Id); }, typeKit: true, b => b.AddInterceptors(cap));
        await db.Context.Database.EnsureCreatedAsync();
        PStr[] keys = data == "empty" ? [] : data == "one-match" ? [PStr.From("val-a"), PStr.From("val-b"), PStr.From("val-c")] : [PStr.From("val-a"), PStr.From("val-b"), PStr.From("val-b"), PStr.From("val-c")];
        for (var i = 0; i < keys.Length; i++) db.Context.Add(new Ent_string { Id = i + 1, Key = keys[i] });
        await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
        var target = PStr.From("val-b");
        var matching = Enumerable.Range(0, keys.Length).Where(i => keys[i] == target).Select(i => i + 1).ToList();
        Expression<Func<Ent_string, bool>> held = o => o.Key == target;
        IQueryable<Ent_string> q = db.Context.Set<Ent_string>().OrderBy(o => o.Id);
        cap.Sql.Clear();
        var lam = cond == "lambda";
        string got = ""; string? thrown = null;
        try
        {
            switch (op)
            {
                case "FirstOrNoneAsync": got = Show(lam ? await q.FirstOrNoneAsync(o => o.Key == target) : await q.FirstOrNoneAsync(held)); break;
                case "FirstOrNone": got = Show(lam ? q.FirstOrNone(o => o.Key == target) : q.FirstOrNone(held)); break;
                case "SingleOrNoneAsync": got = Show(lam ? await q.SingleOrNoneAsync(o => o.Key == target) : await q.SingleOrNoneAsync(held)); break;
                case "SingleOrNone": got = Show(lam ? q.SingleOrNone(o => o.Key == target) : q.SingleOrNone(held)); break;
                case "ElementAtOrNone": got = Show(lam ? q.Where(o => o.Key == target).ElementAtOrNone(1) : q.Where(held).ElementAtOrNone(1)); break;
                case "FirstOrError":
                    var res = lam ? q.FirstOrError(o => o.Key == target, ShapeFault.Refused) : q.FirstOrError(held, ShapeFault.Refused);
                    got = res.Match(x => "ok:" + x.Id, _ => "err"); break;
                default: throw new ArgumentException(op);
            }
        }
        catch (Exception ex) { thrown = ex.GetType().Name; }
        return Verify(op, data, got, thrown, matching, cap);
    }
    static string Show(Option<Ent_string> o) => o.Match(x => "some:" + x.Id, () => "none");
}
public readonly partial record struct PInt : IValue<int>;
public readonly partial record struct PGuid : IValue<Guid>;
public readonly partial record struct PStr : IValue<string>;
