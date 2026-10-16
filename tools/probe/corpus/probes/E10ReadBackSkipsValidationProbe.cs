// Claim E10-read-back-skips-validation (docs/ef-core.md#reading-back-without-validation): "EF reads a column back without validation, through the value object's Materialize rather than Create." Revalidate() finds the rows today's rules reject.
// Oracle: claim. A row holding a value the value object's Create refuses (written by a plain-typed twin of the entity into the same table) is read back by EF with UseTypeKit: no exception, .Value is the stored value, Revalidate() is an Error.
// Expected: no failure for any row.
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E10ReadBackSkipsValidationProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E10-read-back-skips-validation");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, Check);

    static object Refused(string w) => w switch
    {
        "string" => "SECRET-7f3a", "int" => -1, "Guid" => Guid.Empty, "decimal" => -1m, "DateTime" => DateTime.MinValue, "Uri" => new Uri("https://example.com/refused"),
        _ => throw new ArgumentException(w),
    };

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var w = r["shape.wrapped"]; var kind = r["shape.kind"]; var mapping = r["ef.mapping"];
        var e = Probes.Ef3.E3.Find(kind, w);
        var refused = Refused(w);
        // the premise: Create refuses this value
        var create = e.Vo.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, [e.Plain])!;
        var created = create.Invoke(null, [refused])!;
        if (created.GetType().GetProperty("State")!.GetValue(created)!.ToString() != "Error") { r.Outcome("harness-error", "Create accepts the supposedly refused value"); return null; }
        var voEntity = EfModel.Entity(mapping, e.Vo);
        var plainEntity = EfModel.Entity(mapping, e.Plain);
        await using var vo = new EfDb(EfModel.Configure(mapping, voEntity, null, false), typeKit: true);
        try { await vo.Context.Database.EnsureCreatedAsync(); } catch (Exception ex) { r.Note(NoteKeys.BothThrew, "model " + ex.GetType().Name); return null; }
        // a plain-typed twin on the same connection writes the row (nothing validates a plain value)
        var twinCfg = EfModel.Configure(mapping, plainEntity, p => { if (mapping == "key") p.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never; }, false);
        var twinOpts = new DbContextOptionsBuilder<DynCtx>().UseSqlite(vo.Connection).ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, UniqueModelKey>().Options;
        await using (var twin = new DynCtx(twinOpts, twinCfg))
        {
            var row = Activator.CreateInstance(plainEntity)!;
            if (mapping == "key") { EfX.Set(row, "Id", refused); EfX.Set(row, "Tag", "t"); }
            else
            {
                EfX.Set(row, "Id", 1);
                object? holder = row;
                if (mapping is "owned" or "complex") { var prop = mapping == "owned" ? "O" : "C"; holder = EfX.Get(row, prop); }
                EfX.Set(holder!, "P", refused);
            }
            twin.Add(row);
            try { await twin.SaveChangesAsync(); } catch (Exception ex) { r.Note(NoteKeys.PlainRefused, ex.GetType().Name); return null; }
        }
        object? read;
        try
        {
            var list = EfX.Enumerate(Q.SetOf(vo.Context, voEntity)).ToList();
            if (list.Count != 1) return $"expected 1 row, read {list.Count}";
            read = list[0];
        }
        catch (Exception ex) { return $"reading the row back threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"; }
        object? vobj = mapping switch
        {
            "key" => EfX.Get(read, "Id"),
            "owned" => EfX.Get(EfX.Get(read, "O")!, "P"),
            "complex" => EfX.Get(EfX.Get(read, "C")!, "P"),
            _ => EfX.Get(read, "P"),
        };
        if (vobj is null) return "the value object read back is null";
        var stored = vobj.GetType().GetProperty("Value")!.GetValue(vobj);
        if (!Equals(stored, refused)) return $"read-back Value is {stored}, the row holds {refused}";
        var reval = vobj.GetType().GetMethod("Revalidate", Type.EmptyTypes);
        if (reval is null) return "no Revalidate() on " + vobj.GetType().Name;
        var res = reval.Invoke(vobj, null)!;
        var state = res.GetType().GetProperty("State")!.GetValue(res)!.ToString();
        return state == "Error" ? null : $"Revalidate() of the stored refused value is {state}, expected Error";
    }
}
