// Claim E07-closed-set-stored-as-name (README#closed-sets): "A closed set is stored as a text column of the member's name, and read back as the member itself. That holds in a table, in a primitive collection of names and in a JSON column alike ... A name the set no longer has throws when it's read, rather than reading as null. An instance that isn't a member throws when it's written."
// Obligations (claims.json), against the README's section (Baseline.Docs): stored-as-name (round-trip), in-collections-and-json
// (round-trip in a primitive collection or a JSON column), unknown-name-throws (unknown-name-on-read), non-member-throws (non-member-on-write).
// Oracle: claim. Round trip: the stored text is the member's name and the read value is the member itself (reference-equal); an unknown stored name throws on read (never null/default); a non-member instance throws on write.
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E07ClosedSetNameProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E07ClosedSetNameProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var site = r["ef.site"]; var ev = r["ef.event"];
        var set = typeof(PSet);
        Type entity; string column; Action<Microsoft.EntityFrameworkCore.ModelBuilder> cfg;
        switch (site)
        {
            case "table":
                entity = typeof(EntP<PSet>); column = "P";
                cfg = mb => { var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id"); };
                break;
            case "primitive-collection":
                entity = typeof(EntL<PSet>); column = "Items";
                cfg = mb => { var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id"); };
                break;
            case "json-column":
                entity = typeof(EntO<PSet>); column = "O";
                cfg = mb => { var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id"); eb.OwnsOne(typeof(Own<PSet>), "O", ob => ob.ToJson()); };
                break;
            default: throw new ArgumentException(site);
        }
        PSet NonMember() => (PSet)Activator.CreateInstance(set, nonPublic: true)!;
        object Entity(params PSet[] members)
        {
            var e = EfX.New(entity);
            switch (site)
            {
                case "table": EfX.Set(e, "P", members[0]); break;
                case "primitive-collection": EfX.Set(e, "Items", members.ToList()); break;
                default: var o = new Own<PSet> { P = members[0] }; EfX.Set(e, "O", o); break;
            }
            return e;
        }
        string Members(object e) => site switch
        {
            "table" => ((PSet)EfX.Get(e, "P")!).Name(),
            "primitive-collection" => string.Join(",", ((List<PSet>)EfX.Get(e, "Items")!).Select(x => x.Name())),
            _ => ((Own<PSet>)EfX.Get(e, "O")!).P.Name(),
        };

        var docs = Baseline.Docs("src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#closed-sets");
        await using var db = new EfDb(cfg, typeKit: true);
        await db.Context.Database.EnsureCreatedAsync();
        switch (ev)
        {
            case "round-trip":
            {
                var e = site == "primitive-collection" ? Entity(PSet.Beta, PSet.Alpha) : Entity(PSet.Beta);
                db.Context.Add(e); await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
                var stored = await EfX.RawScalar(db, $"SELECT {column} FROM t");
                var expectStored = site switch { "table" => "Beta", "primitive-collection" => "[\"Beta\",\"Alpha\"]", _ => "{\"P\":\"Beta\"}" };
                // stored-as-name: a table's text is the member's name, and every site reads back the member itself
                var named = r.Check("stored-as-name", docs, c =>
                {
                    if (site == "table" && stored != expectStored) return $"stored text is {stored}, expected {expectStored}";
                    object back;
                    try { back = EfX.Enumerate(Q.SetOf(db.Context, entity)).Single(); }
                    catch (Exception ex) when (ex is not Xunit.Sdk.XunitException) { return $"reading the stored {stored} back threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"; }
                    if (site == "primitive-collection")
                    {
                        var l = (List<PSet>)EfX.Get(back, "Items")!;
                        if (!(l.Count == 2 && ReferenceEquals(l[0], PSet.Beta) && ReferenceEquals(l[1], PSet.Alpha))) return "read back " + Members(back);
                    }
                    else
                    {
                        var got = site == "table" ? (PSet)EfX.Get(back, "P")! : ((Own<PSet>)EfX.Get(back, "O")!).P;
                        if (!ReferenceEquals(got, PSet.Beta)) return "read back is not the member itself: " + got;
                    }
                    c.Note(NoteKeys.Compared, site == "table" ? "the stored name and the member read back" : "the members read back");
                    return null;
                });
                if (site == "table") return named;
                // in-collections-and-json: a primitive collection holds the names as a JSON array, a JSON column the name as the property's value
                var inJson = r.Check("in-collections-and-json", docs, c =>
                {
                    if (stored != expectStored) return $"stored text is {stored}, expected {expectStored}";
                    c.Note(NoteKeys.Compared, stored);
                    return null;
                });
                return named ?? inJson;
            }
            case "unknown-name-on-read":
            {
                var e = site == "primitive-collection" ? Entity(PSet.Beta, PSet.Alpha) : Entity(PSet.Alpha);
                db.Context.Add(e); await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
                var bad = site switch { "table" => "'Gamma'", "primitive-collection" => "'[\"Alpha\",\"Gamma\"]'", _ => "'{\"P\":\"Gamma\"}'" };
                await EfX.Raw(db, $"UPDATE t SET {column} = {bad}");
                return r.Check("unknown-name-throws", docs, c =>
                {
                    try
                    {
                        var back = EfX.Enumerate(Q.SetOf(db.Context, entity)).ToList();
                        return "an unknown stored name read without throwing: " + (back.Count == 1 ? Safe(() => Members(back[0])) : back.Count + " rows");
                    }
                    catch (Exception ex) when (ex is not Xunit.Sdk.XunitException) { c.Note(NoteKeys.Compared, ex.GetType().Name); return null; }
                });
            }
            case "non-member-on-write":
            {
                var e = Entity(NonMember());
                db.Context.Add(e);
                return await r.Check("non-member-throws", docs, async c =>
                {
                    try { await db.Context.SaveChangesAsync(); }
                    catch (Exception ex) when (ex is not Xunit.Sdk.XunitException) { c.Note(NoteKeys.Compared, ex.GetType().Name); return null; }
                    var stored = await EfX.RawScalar(db, $"SELECT {column} FROM t");
                    return "a non-member instance was written without throwing; stored: " + stored;
                });
            }
            default: throw new ArgumentException(ev);
        }
    }
    static string Safe(Func<string> f) { try { return f(); } catch (Exception ex) { return "(" + ex.GetType().Name + ")"; } }
}
static class PSetNames { public static string Name(this PSet? s) => s is null ? "null" : s == PSet.Alpha ? "Alpha" : s == PSet.Beta ? "Beta" : "other"; }
