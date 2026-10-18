// Shared by the EF claims E06, E11, E12, E16 (after EfHarness.cs / EfExtra.cs): a context with ConventionS and the order of UseTypeKit,
// and an application-owned converter ("C:" + the plain value as text) for any type.
#nullable enable
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Probes.Shapes;
namespace Probes.Ef;

public sealed class DynCtx2(DbContextOptions options, Action<ModelBuilder> configure, Action<ModelConfigurationBuilder>? conventions) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => conventions?.Invoke(configurationBuilder);
}

public sealed class EfDb2 : IAsyncDisposable
{
    public SqliteConnection Connection { get; }
    public DynCtx2 Context { get; }
    // order: "none" (no UseTypeKit), "before-provider" or "after-provider".
    public EfDb2(Action<ModelBuilder> configure, string order, Action<ModelConfigurationBuilder>? conventions = null)
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();
        var b = new DbContextOptionsBuilder<DynCtx2>();
        if (order == "before-provider") b.UseTypeKit();
        b.UseSqlite(Connection);
        if (order == "after-provider") b.UseTypeKit();
        b.ReplaceService<IModelCacheKeyFactory, UniqueModelKey>();
        Context = new DynCtx2(b.Options, configure, conventions);
    }
    public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await Connection.DisposeAsync(); }
}

/// <summary>An application-owned conversion of T to text: "C:" + the plain value. T is the value object or its wrapped type.</summary>
public static class OwnConv<T>
{
    public static Func<T, string> ToF = _ => throw new InvalidOperationException("OwnConv not set up");
    public static Func<string, T> FromF = _ => throw new InvalidOperationException("OwnConv not set up");
    public static string To(T v) => ToF(v);
    public static T From(string s) => FromF(s);
    public static void Setup(string wrapped, Func<object, object> make, Func<object, object> plain)
    {
        ToF = v => "C:" + Fmt(plain(v!));
        FromF = s => (T)make(Parse(wrapped, s[2..]));
    }
    public static string Fmt(object p) => Convert.ToString(p, CultureInfo.InvariantCulture)!;
    public static object Parse(string wrapped, string s) => wrapped switch
    {
        "string" => s, "int" => int.Parse(s, CultureInfo.InvariantCulture), "long" => long.Parse(s, CultureInfo.InvariantCulture),
        "Guid" => Guid.Parse(s), "decimal" => decimal.Parse(s, CultureInfo.InvariantCulture), "char" => s[0],
        _ => throw new ArgumentException(wrapped),
    };
}

public sealed class OwnConverter<T>() : ValueConverter<T, string>(v => OwnConv<T>.To(v), s => OwnConv<T>.From(s));

public static class OwnConvs
{
    /// <summary>Sets OwnConv up for the value object (voSide) or for its wrapped type, returns the converter type.</summary>
    public static Type Setup(EfEntry e, bool vo)
    {
        if (vo)
        {
            typeof(OwnConv<>).MakeGenericType(e.Vo).GetMethod("Setup")!.Invoke(null, [e.Wrapped, (Func<object, object>)e.MakeVo, (Func<object, object>)(v => v.GetType().GetProperty("Value")!.GetValue(v)!)]);
            return typeof(OwnConverter<>).MakeGenericType(e.Vo);
        }
        typeof(OwnConv<>).MakeGenericType(e.Plain).GetMethod("Setup")!.Invoke(null, [e.Wrapped, (Func<object, object>)(p => p), (Func<object, object>)(p => p)]);
        return typeof(OwnConverter<>).MakeGenericType(e.Plain);
    }
}

public static class EfX2
{
    public static async Task<string?> RawScalar(SqliteConnection c, string sql)
    {
        await using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        return Convert.ToString(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
