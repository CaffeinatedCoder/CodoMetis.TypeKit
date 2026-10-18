// Claim E10-read-back-skips-validation (docs/ef-core.md#reading-back-without-validation): "EF reads a column back without validation, through the value object's Materialize rather than Create." Revalidate() finds the rows today's rules reject.
// Oracle: claim. A row holding a value the value object's Create refuses (written by a plain-typed twin of the entity into the same table) is read back by EF with UseTypeKit: no exception, .Value is the stored value, Revalidate() is an Error.
// Obligations (claims.json), both on every row, against the docs page (Baseline.Docs): reads-without-create (the row reads back,
// .Value as stored) and revalidate-finds (Revalidate() is an Error).
// Expected: no failure for any row.
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E10ReadBackSkipsValidationProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E10ReadBackSkipsValidationProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

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
        Exception? voModel = null, plainModel = null, written = null;
        try { await vo.Context.Database.EnsureCreatedAsync(); }
        catch (Exception ex)
        {
            voModel = ex;
            // the wrapped twin's table on its own, to tell a model the framework refuses from one the value object breaks
            await using var alone = new EfDb(EfModel.Configure(mapping, plainEntity, null, false), typeKit: false);
            try { await alone.Context.Database.EnsureCreatedAsync(); } catch (Exception pex) { plainModel = pex; }
        }
        object? read = null; string? readFailed = null;
        if (voModel is null)
        {
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
                try { await twin.SaveChangesAsync(); } catch (Exception ex) { written = ex; }
            }
            if (written is null)
                try
                {
                    var list = EfX.Enumerate(Q.SetOf(vo.Context, voEntity)).ToList();
                    if (list.Count != 1) readFailed = $"expected 1 row, read {list.Count}";
                    else read = list[0];
                }
                catch (Exception ex) { readFailed = $"reading the row back threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"; }
        }
        object? vobj = read is null ? null : mapping switch
        {
            "key" => EfX.Get(read, "Id"),
            "owned" => EfX.Get(EfX.Get(read, "O")!, "P"),
            "complex" => EfX.Get(EfX.Get(read, "C")!, "P"),
            _ => EfX.Get(read, "P"),
        };
        var docs = Baseline.Docs("docs/ef-core.md#reading-back-without-validation");
        // Where the row could not be written, neither check has anything to read: the model refused (compared with the twin's), or the
        // twin's write of the refused value (nothing documents why a plain value would be refused).
        string? Unwritten(Probes.Shapes.Check c, out bool ended)
        {
            ended = true;
            if (voModel is not null) return EfSides.Threw(c, voModel, plainModel, "creating the table", out var refused) ? refused : null;
            if (written is not null) return c.Inconclusive($"the plain twin could not write the value Create refuses: {written.GetType().Name}: {written.Message.Split('\n')[0]}");
            ended = false;
            return null;
        }

        // reads-without-create: the stored value Create refuses is read back, as it is stored
        var reads = r.Check("reads-without-create", docs, c =>
        {
            var unwritten = Unwritten(c, out var ended);
            if (ended) return unwritten;
            if (readFailed is not null) return readFailed;
            if (vobj is null) return "the value object read back is null";
            var stored = vobj.GetType().GetProperty("Value")!.GetValue(vobj);
            if (!Equals(stored, refused)) return $"read-back Value is {stored}, the row holds {refused}";
            c.Note(NoteKeys.StoredFactoryReadUnvalidated, $"{refused}");
            return null;
        });
        // revalidate-finds: Revalidate() of what was read back is the refusal Create gives today
        var revalidates = r.Check("revalidate-finds", docs, c =>
        {
            var unwritten = Unwritten(c, out var ended);
            if (ended) return unwritten;
            if (vobj is null) return c.Inconclusive("nothing was read back to revalidate: " + (readFailed ?? "the value object read back is null"));
            var reval = vobj.GetType().GetMethod("Revalidate", Type.EmptyTypes);
            if (reval is null) return "no Revalidate() on " + vobj.GetType().Name;
            var res = reval.Invoke(vobj, null)!;
            var state = res.GetType().GetProperty("State")!.GetValue(res)!.ToString();
            if (state != "Error") return $"Revalidate() of the stored refused value is {state}, expected Error";
            c.Note(NoteKeys.Compared, "Revalidate() is an Error");
            return null;
        });
        return reads ?? revalidates;
    }
}
