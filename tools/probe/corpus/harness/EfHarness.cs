// Shared EF row-applying code for the EF satellite claims (surface ef): the entity shapes, a DbContext built from a
// configuration delegate, the model descriptor and the query helpers. Written once; later EF claims reuse it.
#nullable enable
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Probes.Shapes;
namespace Probes.Ef;

public sealed record EfEntry(string Kind, string Wrapped, string Site, Type Vo, Type Plain, Func<object> Valid)
{
    /// <summary>The value object around a plain value, by its public seams: From, else FromKnownGood.</summary>
    public object MakeVo(object plain)
    {
        var from = Vo.GetMethod("From", BindingFlags.Public | BindingFlags.Static, [Plain]);
        if (from is not null) return from.Invoke(null, [plain])!;
        var known = Vo.GetMethod("FromKnownGood", BindingFlags.Public | BindingFlags.Static, [Plain, typeof(string)]);
        return known is null ? throw new InvalidOperationException($"{Vo} has neither From nor FromKnownGood") : known.Invoke(null, [plain, null])!;
    }

    /// <summary>The generated companion extension GetValue / ValueOrNull of this value object (a static class in its namespace; the first parameter is the value object, or its Nullable for ValueOrNull).</summary>
    public MethodInfo? Companion(string name)
    {
        var first = name == "ValueOrNull" && Vo.IsValueType ? typeof(Nullable<>).MakeGenericType(Vo) : Vo;
        return Vo.Assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed && t.Namespace == Vo.Namespace)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m => m.Name == name && m.GetParameters() is [var p] && p.ParameterType == first);
    }
}

public static partial class EfRegistry
{
    public static readonly List<EfEntry> All = Build();
    static List<EfEntry> Build() { var e = new List<EfEntry>(); Fill(e); return e; }
    static partial void Fill(List<EfEntry> e);
    public static EfEntry Of(Row r) { var k = r["shape.kind"]; var w = r["shape.wrapped"]; var s = r["shape.site"]; return All.First(x => x.Kind == k && x.Wrapped == w && x.Site == s); }
}

public class EntP<T> where T : notnull { public int Id { get; set; } public T P { get; set; } = default!; }
public class EntNC<T> where T : class { public int Id { get; set; } public T? P { get; set; } }
public class EntK<T> where T : notnull { public T Id { get; set; } = default!; public string Tag { get; set; } = ""; }
public class Own<T> where T : notnull { public T P { get; set; } = default!; }
public class EntO<T> where T : notnull { public int Id { get; set; } public Own<T> O { get; set; } = new(); }
public class EntC<T> where T : notnull { public int Id { get; set; } public Own<T> C { get; set; } = new(); }
public sealed class Box<T> { public T Value = default!; }

public sealed class UniqueModelKey : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => new object();
}

public sealed class DynCtx(DbContextOptions options, Action<ModelBuilder> configure) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
}

public sealed class EfDb : IAsyncDisposable
{
    public SqliteConnection Connection { get; }
    public DynCtx Context { get; }
    public EfDb(Action<ModelBuilder> configure, bool typeKit, Action<DbContextOptionsBuilder>? extra = null)
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();
        var b = new DbContextOptionsBuilder<DynCtx>().UseSqlite(Connection).ReplaceService<IModelCacheKeyFactory, UniqueModelKey>();
        if (typeKit) b.UseTypeKit();
        extra?.Invoke(b);
        Context = new DynCtx(b.Options, configure);
    }
    public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await Connection.DisposeAsync(); }
}

public static class EfModel
{
    public static Type Entity(string mapping, Type t) => mapping switch
    {
        "property" => typeof(EntP<>).MakeGenericType(t),
        "nullable" => t.IsValueType ? typeof(EntP<>).MakeGenericType(typeof(Nullable<>).MakeGenericType(t)) : typeof(EntNC<>).MakeGenericType(t),
        "key" => typeof(EntK<>).MakeGenericType(t),
        "owned" => typeof(EntO<>).MakeGenericType(t),
        "complex" => typeof(EntC<>).MakeGenericType(t),
        _ => throw new ArgumentException("mapping " + mapping),
    };

    public static readonly Dictionary<string, string> ColumnTypes = new()
    {
        ["string"] = "VARCHAR(40)", ["Uri"] = "VARCHAR(40)", ["Guid"] = "UNIQUEIDENTIFIER", ["int"] = "BIGINT", ["long"] = "BIGINT", ["byte"] = "TINYINT",
        ["ulong"] = "BIGINT", ["decimal"] = "NUMERIC(10,2)", ["double"] = "FLOAT", ["bool"] = "BIT", ["char"] = "CHAR(1)", ["DateTime"] = "DATETIME",
        ["DateTimeOffset"] = "DATETIMEOFFSET", ["DateOnly"] = "DATE", ["TimeOnly"] = "TIME", ["TimeSpan"] = "TIME", ["enum"] = "SMALLINT", ["flags-enum"] = "SMALLINT",
    };

    /// <summary>Maps the entity of a mapping in table "t", applying a facet to the column property (and UseAutoincrement to an integer key when asked).</summary>
    public static Action<ModelBuilder> Configure(string mapping, Type entity, Action<IMutableProperty>? facet, bool autoincrement) => mb =>
    {
        var eb = mb.Entity(entity);
        eb.ToTable("t");
        var t = entity.GetGenericArguments()[0];
        switch (mapping)
        {
            case "property" or "nullable": eb.HasKey("Id"); if (facet is not null) facet(eb.Property("P").Metadata); break;
            case "key":
                eb.HasKey("Id");
                var pb = eb.Property("Id");
                facet?.Invoke(pb.Metadata);
                if (autoincrement) Microsoft.EntityFrameworkCore.SqlitePropertyBuilderExtensions.UseAutoincrement(pb);
                break;
            case "owned":
                eb.HasKey("Id");
                eb.OwnsOne(typeof(Own<>).MakeGenericType(t), "O", ob => { if (facet is not null) facet(ob.Property("P").Metadata); });
                break;
            case "complex":
                eb.HasKey("Id");
                eb.ComplexProperty("C", cb => { if (facet is not null) facet(cb.Property("P").Metadata); });
                break;
        }
    };

    /// <summary>What the model says about every mapped column: store type, nullability, facets, key and generation.</summary>
    public static string Describe(DbContext db)
    {
        var model = db.GetService<IDesignTimeModel>().Model;
        var lines = new List<string>();
        string Line(string table, string prefix, IProperty p)
        {
            string col = "?"; try { col = ((IReadOnlyProperty)p).GetColumnType() ?? "?"; } catch { try { col = Microsoft.EntityFrameworkCore.RelationalPropertyExtensions.GetRelationalTypeMapping(p).StoreType; } catch { col = "?"; } }
            string nul; try { nul = p.IsColumnNullable().ToString(); } catch { nul = p.IsNullable.ToString(); }
            return $"{table}.{prefix}{p.Name}: type={col} nullable={nul} max={p.GetMaxLength()} prec={p.GetPrecision()} scale={p.GetScale()} unicode={p.IsUnicode()} pk={p.IsPrimaryKey()} gen={p.ValueGenerated}";
        }
        foreach (var et in model.GetEntityTypes())
        {
            var table = et.GetTableName() ?? "(none)";
            foreach (var p in et.GetProperties()) lines.Add(Line(table, "", p));
            foreach (var cp in et.GetComplexProperties()) foreach (var p in cp.ComplexType.GetProperties()) lines.Add(Line(table, cp.Name + ".", p));
        }
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }

    public static string FirstDiff(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
            if ((i < x.Length ? x[i] : "<none>") != (i < y.Length ? y[i] : "<none>")) return $"VO: {(i < x.Length ? x[i] : "<none>")} | plain: {(i < y.Length ? y[i] : "<none>")}";
        return "";
    }
}

public static class Q
{
    public static IQueryable Call(string name, Type[] typeArgs, IQueryable src, params Expression[] args)
    {
        static bool OneParamLambda(ParameterInfo p) =>
            p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(Expression<>) && p.ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2;
        var m = typeof(Queryable).GetMethods().First(x => x.Name == name && x.IsGenericMethodDefinition && x.GetGenericArguments().Length == typeArgs.Length
            && x.GetParameters().Length == args.Length + 1 && x.GetParameters().Skip(1).All(OneParamLambda));
        return (IQueryable)m.MakeGenericMethod(typeArgs).Invoke(null, [src, .. args.Select(a => (object)a)])!;
    }
    public static IQueryable SetOf(DbContext db, Type entity) =>
        (IQueryable)typeof(DbContext).GetMethods().First(m => m.Name == "Set" && m.GetParameters().Length == 0).MakeGenericMethod(entity).Invoke(db, null)!;
    public static object NewBox(Type t, object? value) { var b = Activator.CreateInstance(typeof(Box<>).MakeGenericType(t))!; typeof(Box<>).MakeGenericType(t).GetField("Value")!.SetValue(b, value); return b; }
    public static string Sql(IQueryable q) => q.ToQueryString();
}
