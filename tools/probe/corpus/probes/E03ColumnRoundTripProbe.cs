// Claim E03-column-round-trip (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#what-maps): "Keys, foreign keys, nullable properties, primitive collections and query parameters all map as plain columns of the underlying type."
// Oracle: metamorphic plus differential. A value object saved in a property / nullable property / key / owned property / complex property and read back by a fresh query equals what was saved (and its Value is the wrapped value);
// the stored column (SQLite type and quoted content) equals what the wrapped type stored by plain EF holds. A nullable property also round-trips null.
// Expected: no failure for any row; a model both sides refuse is an expected refusal.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E03ColumnRoundTripProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E03ColumnRoundTripProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    sealed record Side(string? Raw, List<object?> Read, Exception? Ex);

    static async Task<Side> Run(string mapping, Type entity, Func<object?> value, bool vo)
    {
        var col = mapping switch { "key" => "Id", "owned" => "O_P", "complex" => "C_P", _ => "P" };
        await using var db = new EfDb(EfModel.Configure(mapping, entity, p => { if (mapping == "key") p.ValueGenerated = ValueGenerated.Never; }, false), typeKit: vo);
        try
        {
            await db.Context.Database.EnsureCreatedAsync();
            var n = mapping == "nullable" ? 2 : 1;
            for (var i = 1; i <= n; i++)
            {
                var row = Activator.CreateInstance(entity)!;
                var v = i == 2 ? null : value();
                if (mapping == "key") { EfX.Set(row, "Id", v); EfX.Set(row, "Tag", "t"); }
                else
                {
                    EfX.Set(row, "Id", i);
                    object holder = row;
                    if (mapping is "owned" or "complex") holder = EfX.Get(row, mapping == "owned" ? "O" : "C")!;
                    EfX.Set(holder, "P", v);
                }
                db.Context.Add(row);
            }
            await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
            var read = new List<object?>();
            foreach (var x in EfX.Enumerate(Q.SetOf(db.Context, entity)))
            {
                object? o = mapping switch { "key" => EfX.Get(x, "Id"), "owned" => EfX.Get(EfX.Get(x, "O")!, "P"), "complex" => EfX.Get(EfX.Get(x, "C")!, "P"), _ => EfX.Get(x, "P") };
                read.Add(o);
            }
            var raw = await EfX.RawScalar(db, $"SELECT group_concat(k, '|') FROM (SELECT typeof({col}) || ':' || quote({col}) AS k FROM t ORDER BY rowid)");
            return new Side(raw, read, null);
        }
        catch (Exception ex) { return new Side(null, [], ex); }
    }

    static object? Plainify(object? o) => o is not null && o.GetType().Namespace is "Probes.Ef3" or "Probes.Ef" or "Probes.Ef4" ? o.GetType().GetProperty("Value")!.GetValue(o) : o;

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var w = r["shape.wrapped"]; var kind = r["shape.kind"]; var mapping = r["ef.mapping"];
        var e = Probes.Ef3.E4.At(r, kind, w, r["shape.site"]);
        var original = e.Valid();
        var voSide = await Run(mapping, EfModel.Entity(mapping, e.Vo), () => e.MakeVo(original), vo: true);
        var plainSide = await Run(mapping, EfModel.Entity(mapping, e.Plain), () => original, vo: false);
        if (voSide.Ex is not null && plainSide.Ex is not null) { r.Note(NoteKeys.BothThrew, voSide.Ex.GetType().Name); return null; }
        if (plainSide.Ex is not null) { r.Note(NoteKeys.PlainRefused, plainSide.Ex.GetType().Name); return null; }
        if (voSide.Ex is not null) return $"the value object in a {mapping} failed with {voSide.Ex.GetType().Name}: {voSide.Ex.Message.Split('\n')[0]}; the wrapped type did not";
        if (voSide.Read.Count != plainSide.Read.Count) return $"read {voSide.Read.Count} rows, the wrapped type read {plainSide.Read.Count}";
        for (var i = 0; i < voSide.Read.Count; i++)
        {
            var v = voSide.Read[i]; var p = plainSide.Read[i];
            if (p is null != v is null) return $"row {i}: the value object read back {(v is null ? "null" : "a value")}, the wrapped type {(p is null ? "null" : "a value")}";
            if (v is null) continue;
            if (!Equals(Plainify(v), p)) return $"row {i}: read back Value {Plainify(v)}, the wrapped type reads {p}";
            if (!Equals(v, e.MakeVo(p!))) return $"row {i}: the read-back value object does not equal the one built from the stored value";
        }
        if (voSide.Raw != plainSide.Raw) return $"stored column differs: value object [{voSide.Raw}] vs wrapped type [{plainSide.Raw}]";
        return null;
    }
}
