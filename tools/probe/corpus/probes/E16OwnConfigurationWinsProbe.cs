// Claim E16-own-configuration-wins (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#keys): "A configuration you write yourself always wins over these defaults." (the key defaults: generated on add for an integer, never for char and Guid)
// Obligations (claims.json), one per row by ef.config, against the wrapped twin (Baseline.Twin): never, on-add, column-type, conversion, optional.
// Oracle: differential against the same key declared with the wrapped type under the same configuration, plus the claim itself: ValueGeneratedNever / ValueGeneratedOnAdd are what the model says; HasConversion stores what the application's converter writes;
// HasColumnType is the column's type; IsRequired(false) is refused for a key as for a plain key, and on a non-key property of the same value object makes the column nullable and null round-trips.
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E16OwnConfigurationWinsProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E16OwnConfigurationWinsProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static object Sample(string w, int i) => w == "char" ? (char)('a' + i) : EfX.Sample(w, i);
    static string Norm(object? k)
    {
        if (k is not null && k.GetType().Namespace == "Probes.Ef") k = k.GetType().GetProperty("Value")!.GetValue(k);
        return k switch { null => "null", Guid g => g == Guid.Empty ? "empty" : "guid", string s when Guid.TryParse(s, out _) => "guid", _ => Convert.ToString(k, System.Globalization.CultureInfo.InvariantCulture)! };
    }

    sealed record Res(string Model, string Script, string DefaultAdd, string Assigned, string Raw);

    static async Task<Res> Side(EfEntry e, bool vo, string config)
    {
        var w = e.Wrapped;
        var kt = vo ? e.Vo : e.Plain;
        var entity = typeof(EntK<>).MakeGenericType(kt);
        string err = "";
        Action<ModelBuilder> cfg = mb =>
        {
            var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id");
            var pb = eb.Property("Id");
            try
            {
                switch (config)
                {
                    case "ValueGeneratedNever": pb.ValueGeneratedNever(); break;
                    case "ValueGeneratedOnAdd": pb.ValueGeneratedOnAdd(); break;
                    case "HasColumnType": pb.HasColumnType(EfModel.ColumnTypes[w]); break;
                    case "HasConversion": pb.HasConversion(OwnConvs.Setup(e, vo)); break;
                    case "IsRequired-false": pb.IsRequired(false); break;
                }
            }
            catch (Exception ex) { err = "threw-in-config:" + ex.GetType().Name; }
        };
        async Task<string> Safe(Func<Task<string>> f) { try { return await f(); } catch (Exception ex) { return "threw:" + ex.GetType().Name; } }
        string model = "", script = "";
        try
        {
            await using var d0 = new EfDb2(cfg, vo ? "after-provider" : "none");
            model = EfModel.Describe(d0.Context); script = d0.Context.Database.GenerateCreateScript();
        }
        catch (Exception ex) { return new Res("threw:" + ex.GetType().Name, err, err, err, err); }
        if (err != "") return new Res(err, err, err, err, err);
        var def = await Safe(async () =>
        {
            await using var db = new EfDb2(cfg, vo ? "after-provider" : "none");
            await db.Context.Database.EnsureCreatedAsync();
            var rows = new[] { Activator.CreateInstance(entity)!, Activator.CreateInstance(entity)! };
            foreach (var x in rows) db.Context.Add(x);
            await db.Context.SaveChangesAsync();
            return "keys=[" + string.Join(",", rows.Select(x => Norm(EfX.Get(x, "Id")))) + "]";
        });
        string raw = "";
        var assigned = await Safe(async () =>
        {
            await using var db = new EfDb2(cfg, vo ? "after-provider" : "none");
            await db.Context.Database.EnsureCreatedAsync();
            object K(int i) { var p = Sample(w, i); return vo ? e.MakeVo(p) : p; }
            foreach (var i in new[] { 0, 1 }) { var x = EfX.New(entity); EfX.Set(x, "Id", K(i)); EfX.Set(x, "Tag", "t" + i); db.Context.Add(x); }
            await db.Context.SaveChangesAsync();
            db.Context.ChangeTracker.Clear();
            raw = await EfX2.RawScalar(db.Connection, "SELECT group_concat(Id, ',') FROM (SELECT Id FROM t ORDER BY Tag)") ?? "null";
            var read = EfX.Enumerate(Q.SetOf(db.Context, entity)).Select(x => Norm(EfX.Get(x, "Id")) + "/" + EfX.Get(x, "Tag")).OrderBy(s => s, StringComparer.Ordinal);
            db.Context.ChangeTracker.Clear();
            var found = await db.Context.FindAsync(entity, K(1));
            return string.Join(",", read) + " find1=" + (found is null ? "null" : (string)EfX.Get(found, "Tag")!);
        });
        return new Res(model, script, def, assigned, raw);
    }

    // where it throws (the table, or the second insert) is EF's and SQLite's; that it can or cannot generate is the outcome
    static string Kind(string s) => s.StartsWith("threw") ? "threw" : s;
    static string StripScript(string s) => s.Replace(" AUTOINCREMENT", "");

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var config = r["ef.config"]; var w = r["shape.wrapped"];
        var kind = EfX.Kinds[r.Index % EfX.Kinds.Length];
        var e = KeyRegistry.All.First(x => x.Kind == kind && x.Wrapped == w);
        var v = await Side(e, true, config);
        var p = await Side(e, false, config);
        var obligation = config switch
        {
            "ValueGeneratedNever" => "never", "ValueGeneratedOnAdd" => "on-add", "HasColumnType" => "column-type", "HasConversion" => "conversion",
            _ => "optional",
        };
        // each configuration's obligation, against the same key declared with the wrapped type under the same configuration
        return await r.Check(obligation, Baseline.Twin, async c =>
        {
            if (config == "IsRequired-false")
            {
                // the key attempt: EF refuses an optional key, and must refuse the same way for the value object
                if (v.Model.StartsWith("threw") != p.Model.StartsWith("threw")) return $"IsRequired(false) on a key: VO {v.Model.Split('\n')[0]} | plain {p.Model.Split('\n')[0]}";
                if (v.Model.StartsWith("threw") && v.Model != p.Model) return $"IsRequired(false) on a key is refused differently: VO {v.Model} | plain {p.Model}";
                return await OptionalProperty(r, w);
            }

            if (v.Model.StartsWith("threw") || p.Model.StartsWith("threw"))
            {
                if (v.Model == p.Model) return c.Inconclusive("both refuse the model alike: " + v.Model);
                return $"model refused for one side only: VO {v.Model} | plain {p.Model}";
            }
            // the claim: the configuration is what the model says
            if (config == "ValueGeneratedNever" && !v.Model.Contains("pk=True gen=Never")) return "ValueGeneratedNever not kept: " + v.Model;
            if (config == "ValueGeneratedOnAdd" && !v.Model.Contains("pk=True gen=OnAdd")) return "ValueGeneratedOnAdd not kept: " + v.Model;
            if (config == "HasColumnType" && !v.Model.Contains("type=" + EfModel.ColumnTypes[w])) return $"HasColumnType({EfModel.ColumnTypes[w]}) not kept: " + v.Model;
            if (config == "HasConversion")
            {
                if (!v.Model.Contains("type=TEXT")) return "HasConversion to text: the column is not text: " + v.Model;
                var expect = "C:" + OwnConv<int>.Fmt(Sample(w, 0)) + ",C:" + OwnConv<int>.Fmt(Sample(w, 1));
                if (v.Raw != expect) return $"stored values are not the application's converter output: {v.Raw}, expected {expect}";
            }
            var vm = v.Model; var pm = p.Model;
            if (config is "HasColumnType" or "HasConversion") { vm = vm.Replace("gen=Never", "gen=*").Replace("gen=OnAdd", "gen=*"); pm = pm.Replace("gen=Never", "gen=*").Replace("gen=OnAdd", "gen=*"); r.Note(NoteKeys.DocumentedCharGuidDefaultGenerationNormalized, "not-promised char-key-not-generated, guid-key-not-generated"); }
            if (vm != pm) return "model differs from the wrapped type's: " + EfModel.FirstDiff(vm, pm);
            if (StripScript(v.Script) != StripScript(p.Script)) return "create script differs from the wrapped type's: " + EfModel.FirstDiff(StripScript(v.Script), StripScript(p.Script));
            if (p.Assigned.StartsWith("threw") && !v.Assigned.StartsWith("threw"))
            {
                // the wrapped type's own key fails on SQLite (EF writes AUTOINCREMENT on a BIGINT / TEXT key): the value object's must still do what it was configured to
                var expectRead = Norm(Sample(w, 0)) + "/t0," + Norm(Sample(w, 1)) + "/t1 find1=t1";
                var sorted = string.Join(",", expectRead.Split(" find1=")[0].Split(',').OrderBy(x => x, StringComparer.Ordinal)) + " find1=t1";
                if (v.Assigned != sorted) return $"assigned keys read back as {v.Assigned}, expected {sorted} (the plain key threw: {p.Assigned})";
                r.Note(NoteKeys.PlainKeyThrowsOnSqliteVoCheckedAgainstExpected);
            }
            else
            {
                if (v.Assigned != p.Assigned) return $"assigned keys differ: VO {v.Assigned} | plain {p.Assigned}";
                if (v.Raw != p.Raw) return $"stored keys differ: VO {v.Raw} | plain {p.Raw}";
            }
            if (config is "ValueGeneratedNever" or "ValueGeneratedOnAdd" && Kind(v.DefaultAdd) != Kind(p.DefaultAdd)) return $"default keys differ under {config}: VO {v.DefaultAdd} | plain {p.DefaultAdd}";
            return null;
        });
    }

    // IsRequired(false) cannot apply to a key at all; on a non-key property of the same wrapped type it must win over the required default.
    static async Task<string?> OptionalProperty(Probes.Shapes.Row r, string w)
    {
        r.Substituted("ef.config", "IsRequired-false", "applied to a non-key property: EF refuses an optional key (checked above for parity)");
        var kind = r.Index % 2 == 0 ? "sealed-record-class" : "validated-class";
        var e = KeyRegistry.All.First(x => x.Kind == kind && x.Wrapped == w);
        var entity = typeof(EntP<>).MakeGenericType(e.Vo);
        Action<ModelBuilder> cfg = mb => { var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id"); eb.Property("P").IsRequired(false); };
        await using var db = new EfDb2(cfg, "after-provider");
        var model = EfModel.Describe(db.Context);
        if (!model.Contains("t.P: ") || !model.Split('\n').First(l => l.StartsWith("t.P: ")).Contains("nullable=True")) return "IsRequired(false) not kept on a non-key value object property: " + model;
        await db.Context.Database.EnsureCreatedAsync();
        var a = EfX.New(entity); EfX.Set(a, "P", null);
        var b = EfX.New(entity); EfX.Set(b, "P", e.MakeVo(Sample(w, 0)));
        db.Context.Add(a); db.Context.Add(b);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var read = EfX.Enumerate(Q.SetOf(db.Context, entity)).Select(x => EfX.Get(x, "P") is null ? "null" : Norm(EfX.Get(x, "P"))).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var expect = new[] { "null", Norm(Sample(w, 0)) }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        if (!read.SequenceEqual(expect)) return $"null did not round-trip: read [{string.Join(",", read)}], expected [{string.Join(",", expect)}]";
        return null;
    }
}
