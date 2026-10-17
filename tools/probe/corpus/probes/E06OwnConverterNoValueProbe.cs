// Claim E06-own-converter-no-value (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#queries): ".Value translates only where the column holds the underlying value ... A property your application converts itself (HasConversion(c => "C:" + c.Value, ...), a converter type, a conversion in ConfigureConventions) doesn't hold .Value. So a query that reads .Value there fails with EF's 'could not be translated', rather than comparing against what the column really holds. Comparing the value object itself (o.Code == code) goes through that converter and still works."
// Obligations (claims.json), one per row by ef.op: value-not-translated (value-access), against the README (Baseline.Docs), and
// value-object-comparison (vo-equality), against the row memory matches (Baseline.Model).
// Oracle: claim. With the application's own converter (three ways to declare it) the column holds the converter's text; a query on .Value throws InvalidOperationException "could not be translated" (never returns rows or an empty result); a query comparing the value object returns exactly the matching row.
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E06OwnConverterNoValueProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E06OwnConverterNoValueProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static Action<ModelBuilder> Model<T>(string form) where T : notnull => mb => mb.Entity<EntP<T>>(eb =>
    {
        eb.ToTable("t"); eb.HasKey(x => x.Id);
        switch (form)
        {
            case "HasConversion-lambda": eb.Property(x => x.P).HasConversion(v => OwnConv<T>.To(v), s => OwnConv<T>.From(s)); break;
            case "converter-type": eb.Property(x => x.P).HasConversion<OwnConverter<T>>(); break;
        }
    });
    static Action<ModelConfigurationBuilder>? Conventions<T>(string form) where T : notnull =>
        form == "ConfigureConventions" ? cb => cb.Properties<T>().HaveConversion<OwnConverter<T>>() : null;

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var form = r["ef.converter"]; var op = r["ef.op"]; var w = r["shape.wrapped"];
        // value-access: the README's "could not be translated" (no twin can show it); vo-equality: the one row memory matches
        var valueAccess = op == "value-access";
        return await r.Check(valueAccess ? "value-not-translated" : "value-object-comparison",
            valueAccess ? Baseline.Docs("src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#queries") : Baseline.Model, async c =>
        {
            var e = EfX.Top(EfX.Kinds[r.Index % EfX.Kinds.Length], w);
            OwnConvs.Setup(e, vo: true);
            var entity = typeof(EntP<>).MakeGenericType(e.Vo);
            var cfg = (Action<ModelBuilder>)typeof(E06OwnConverterNoValueProbe).GetMethod(nameof(Model), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(e.Vo).Invoke(null, [form])!;
            var conv = (Action<ModelConfigurationBuilder>?)typeof(E06OwnConverterNoValueProbe).GetMethod(nameof(Conventions), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(e.Vo).Invoke(null, [form]);
            await using var db = new EfDb2(cfg, "after-provider", conv);
            await db.Context.Database.EnsureCreatedAsync();
            foreach (var i in new[] { 0, 1 }) { var x = EfX.New(entity); EfX.Set(x, "P", e.MakeVo(EfX.Sample(w, i))); db.Context.Add(x); }
            await db.Context.SaveChangesAsync();
            db.Context.ChangeTracker.Clear();

            var stored = await EfX2.RawScalar(db.Connection, "SELECT group_concat(P, ',') FROM (SELECT P FROM t ORDER BY Id)");
            var expected = "C:" + OwnConv<int>.Fmt(EfX.Sample(w, 0)) + ",C:" + OwnConv<int>.Fmt(EfX.Sample(w, 1));
            if (stored != expected) return $"{form}: the column holds {stored}, expected the application's converter output {expected}";

            var p = Expression.Parameter(entity, "e");
            var prop = Expression.Property(p, "P");
            var src = Q.SetOf(db.Context, entity);
            if (op == "vo-equality")
            {
                var box = Q.NewBox(e.Vo, e.MakeVo(EfX.Sample(w, 1)));
                var pred = Expression.Lambda(Expression.Equal(prop, Expression.Field(Expression.Constant(box), "Value")), p);
                List<object> got;
                // a refusal here is the claim failing, not the harness: comparing the value object goes through the application's converter
                try { got = EfX.Enumerate(Q.Call("Where", [entity], src, pred)).ToList(); }
                catch (InvalidOperationException ex) { return $"{form}: comparing the value object failed with {ex.GetType().Name}: {ex.Message[..Math.Min(200, ex.Message.Length)]}"; }
                if (got.Count != 1 || EfX.Plainify(EfX.Get(got[0], "P")) != EfX.Plainify(EfX.Sample(w, 1))) return $"{form}: comparing the value object returned {got.Count} row(s), expected the one that matches";
                return null;
            }
            var pbox = Q.NewBox(e.Plain, EfX.Sample(w, 1));
            var vpred = Expression.Lambda(Expression.Equal(Expression.Property(prop, "Value"), Expression.Field(Expression.Constant(pbox), "Value")), p);
            try
            {
                var got = EfX.Enumerate(Q.Call("Where", [entity], src, vpred)).ToList();
                return $"{form}: a query on .Value of a property the application converts itself returned {got.Count} row(s) instead of failing as untranslatable";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("could not be translated")) { return null; }
            catch (Exception ex) { return $"{form}: the .Value query failed with {ex.GetType().Name}: {ex.Message[..Math.Min(200, ex.Message.Length)]}, not EF's 'could not be translated'"; }
        });
    }
}
