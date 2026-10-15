// Entity shapes and helpers for the EF claims E07, E13, E14, E15 (reuse EfHarness.cs: EfDb, EfModel, Q, EfRegistry, KeyRegistry).
#nullable enable
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Probes.Shapes;
namespace Probes.Ef;

public class EntL<T> where T : notnull { public int Id { get; set; } public List<T> Items { get; set; } = []; }
public class EntComp<TA, TB> where TA : notnull where TB : notnull { public TA A { get; set; } = default!; public TB B { get; set; } = default!; public string Tag { get; set; } = ""; }
public abstract class RootE<T> where T : notnull { public T Id { get; set; } = default!; public string Name { get; set; } = ""; }
public class LeafA<T> : RootE<T> where T : notnull { public int X { get; set; } }
public class LeafB<T> : RootE<T> where T : notnull { public int Y { get; set; } }

public static class EfX
{
    /// <summary>The i-th distinct plain value of a wrapped type.</summary>
    public static object Sample(string wrapped, int i) => wrapped switch
    {
        "string" => "val-" + (char)('a' + i),
        "int" => 10 + i,
        "long" => 4_000_000_000L + i,
        "Guid" => new Guid(i + 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11),
        "decimal" => 1.5m + i,
        _ => throw new ArgumentException(wrapped),
    };
    public static EfEntry Top(string kind, string wrapped) => EfRegistry.All.FirstOrDefault(x => x.Kind == kind && x.Wrapped == wrapped && x.Site == "top-level")
        ?? EfRegistry.All.First(x => (x.Kind is "record-struct" or "sealed-record-class") && x.Wrapped == wrapped && x.Site == "top-level");
    public static readonly string[] Kinds = ["record-struct", "sealed-record-class", "validated-class", "validated-struct"];
    public static object New(Type t) => Activator.CreateInstance(t)!;
    public static void Set(object o, string prop, object? v) => o.GetType().GetProperty(prop)!.SetValue(o, v);
    public static object? Get(object o, string prop) => o.GetType().GetProperty(prop)!.GetValue(o);
    public static string Plainify(object? k)
    {
        if (k is not null && k.GetType().Namespace == "Probes.Ef") k = k.GetType().GetProperty("Value")!.GetValue(k);
        return Convert.ToString(k, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
    }
    public static IEnumerable<object> Enumerate(IQueryable q) { foreach (var o in q) yield return o!; }
    public static async Task<string?> RawScalar(EfDb db, string sql)
    {
        await using var cmd = db.Connection.CreateCommand(); cmd.CommandText = sql;
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
    public static async Task Raw(EfDb db, string sql)
    {
        await using var cmd = db.Connection.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync();
    }
}
