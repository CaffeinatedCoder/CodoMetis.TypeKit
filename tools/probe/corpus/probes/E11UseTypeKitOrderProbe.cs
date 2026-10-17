// Claim E11-usetypekit-order (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#setup): "UseTypeKit() adds plugins ... It replaces nothing. So it works alongside a provider ... and it doesn't matter whether you call it before or after UseNpgsql or UseSqlite."
// Obligation (claims.json): order-does-not-matter, on every row, held to the README's sentence (Baseline.Docs) by the differential below.
// Oracle: differential between the two orders (and, where a plain type maps the same way, the wrapped type): the model, the CREATE script, the raw stored row and the values read back are equal in every mapping.
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E11UseTypeKitOrderProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E11UseTypeKitOrderProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static readonly string[] Ws = ["string", "int", "decimal", "Guid", "long"];

    static async Task<string> Raw(EfDb2 db)
    {
        await using var cmd = db.Connection.CreateCommand(); cmd.CommandText = "SELECT * FROM t ORDER BY 1";
        await using DbDataReader rd = await cmd.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await rd.ReadAsync()) lines.Add(string.Join("|", Enumerable.Range(0, rd.FieldCount).Select(i => rd.IsDBNull(i) ? "NULL" : rd.GetValue(i).GetType().Name + ":" + Convert.ToString(rd.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return string.Join(";", lines);
    }

    sealed record Out(string Model, string Script, string Raw, string Read);

    /// <summary>One order's model, script, stored row and values; a refusal anywhere on the way is that order's result, compared like the others.</summary>
    static async Task<Out> Run(string order, string mapping, Type entity, Func<int, object?> value, Action<ModelBuilder> cfg, string? wrapped)
    {
        try { return await RunUnguarded(order, mapping, entity, value, cfg, wrapped); }
        catch (Exception ex) { return new Out($"threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}", "", "", ""); }
    }

    static async Task<Out> RunUnguarded(string order, string mapping, Type entity, Func<int, object?> value, Action<ModelBuilder> cfg, string? wrapped)
    {
        await using var db = new EfDb2(cfg, order);
        var model = EfModel.Describe(db.Context);
        var script = db.Context.Database.GenerateCreateScript();
        await db.Context.Database.EnsureCreatedAsync();
        var set = new List<object>();
        object Mk() => EfX.New(entity);
        switch (mapping)
        {
            case "property" or "closed-set": { var x = Mk(); EfX.Set(x, "P", value(0)); set.Add(x); break; }
            case "nullable": { var x = Mk(); EfX.Set(x, "P", value(0)); set.Add(x); var y = Mk(); EfX.Set(y, "Id", 2); set.Add(y); break; }
            case "key": { var x = Mk(); EfX.Set(x, "Id", value(0)); EfX.Set(x, "Tag", "a"); set.Add(x); var y = Mk(); EfX.Set(y, "Id", value(1)); EfX.Set(y, "Tag", "b"); set.Add(y); break; }
            case "integer-key": { set.Add(Mk()); set.Add(Mk()); break; }
            case "owned" or "complex":
            {
                var x = Mk(); var prop = mapping == "owned" ? "O" : "C"; var own = EfX.Get(x, prop)!; EfX.Set(own, "P", value(0)); set.Add(x); break;
            }
        }
        foreach (var x in set) db.Context.Add(x);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var raw = await Raw(db);
        var read = string.Join(";", EfX.Enumerate(Q.SetOf(db.Context, entity)).Select(x => mapping switch
        {
            "property" or "nullable" or "closed-set" => EfX.Plainify(EfX.Get(x, "P")) + "/" + EfX.Plainify(EfX.Get(x, "Id")),
            "key" => EfX.Plainify(EfX.Get(x, "Id")) + "/" + EfX.Get(x, "Tag"),
            "integer-key" => EfX.Plainify(EfX.Get(x, "Id")),
            _ => EfX.Plainify(EfX.Get(EfX.Get(x, mapping == "owned" ? "O" : "C")!, "P")),
        }).OrderBy(s => s, StringComparer.Ordinal));
        return new Out(model, script, raw, read);
    }

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        return await r.Check("order-does-not-matter", Baseline.Docs("src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#setup"), async c =>
        {
            var mapping = r["ef.mapping"]; var order = r["ef.order"];
            var other = order == "before-provider" ? "after-provider" : "before-provider";
            string w = mapping switch { "integer-key" => r.Index % 2 == 0 ? "int" : "long", "key" => r.Index % 2 == 0 ? "Guid" : "string", "closed-set" => "-", _ => Ws[r.Index % Ws.Length] };
            Out a, b; Out? plain = null;
            if (mapping == "closed-set")
            {
                var entity = typeof(EntP<PSet>);
                Action<ModelBuilder> cfg = mb => { var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id"); };
                a = await Run(order, mapping, entity, _ => PSet.Beta, cfg, null);
                b = await Run(other, mapping, entity, _ => PSet.Beta, cfg, null);
                if (!a.Raw.Contains("String:Beta")) return "closed set not stored as its name text: " + a.Raw;
            }
            else
            {
                var kind = EfX.Kinds[r.Index % EfX.Kinds.Length];
                var e = mapping is "key" or "integer-key" ? KeyRegistry.All.First(x => x.Kind == kind && x.Wrapped == w) : EfX.Top(kind, w);
                var map = mapping == "integer-key" ? "key" : mapping;
                var entity = EfModel.Entity(map, e.Vo);
                var cfg = EfModel.Configure(map, entity, null, false);
                Func<int, object?> value = i => e.MakeVo(EfX.Sample(w, i));
                a = await Run(order, mapping, entity, value, cfg, w);
                b = await Run(other, mapping, entity, value, cfg, w);
                if (mapping is "property" or "nullable" or "owned" or "complex")
                {
                    var pe = EfModel.Entity(map, e.Plain);
                    plain = await Run("none", mapping, pe, i => EfX.Sample(w, i), EfModel.Configure(map, pe, null, false), w);
                }
                else if (mapping == "integer-key")
                {
                    var ids = a.Read.Split(';');
                    if (ids.Distinct().Count() != 2 || ids.Any(x => x is "0" or "")) return "integer key not generated after " + order + ": " + a.Read;
                }
            }
            if (a.Model != b.Model) return $"model differs between {order} and {other}: " + EfModel.FirstDiff(a.Model, b.Model);
            if (a.Model.StartsWith("threw ", StringComparison.Ordinal)) return $"the value object's entity is refused in both orders: {a.Model}";
            if (a.Script != b.Script) return $"create script differs between {order} and {other}: " + EfModel.FirstDiff(a.Script, b.Script);
            if (a.Raw != b.Raw) return $"stored row differs between {order} and {other}: {a.Raw} | {b.Raw}";
            if (a.Read != b.Read) return $"values read differ between {order} and {other}: {a.Read} | {b.Read}";
            if (plain is not null)
            {
                if (a.Model != plain.Model) return "model differs from the wrapped type's: " + EfModel.FirstDiff(a.Model, plain.Model);
                if (a.Raw != plain.Raw) return $"stored row differs from the wrapped type's: {a.Raw} | {plain.Raw}";
            }
            return null;
        });
    }
}
