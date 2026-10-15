// Claim E12-convention-by-vo-type (src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#model-wide-conventions-follow-the-propertys-type): "EF applies ConfigureConventions by CLR type. So Properties<decimal>().HavePrecision(18, 2) reaches decimal properties, and not an Amount that wraps one. Configure the value object instead: Properties<Amount>().HavePrecision(18, 2)."
// Oracle: differential + claim. A convention on the value object type reaches the value object's property exactly as the same convention on the wrapped type reaches a plain property; a convention on the wrapped type does not reach the value object's property (its model equals the model without the convention) while it does reach the plain one.
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E12ConventionByTypeProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E12-convention-by-vo-type");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, Check);

    static string PLine(EfDb2 db) => EfModel.Describe(db.Context).Split('\n').First(l => l.StartsWith("t.P: "));

    static Action<ModelConfigurationBuilder> Conv(string facet, Type target, string w) => cb =>
    {
        var pb = cb.Properties(target);
        switch (facet)
        {
            case "HaveMaxLength": pb.HaveMaxLength(5); break;
            case "HavePrecision": pb.HavePrecision(18, 2); break;
            case "HaveColumnType": pb.HaveColumnType(EfModel.ColumnTypes[w]); break;
        }
    };

    static async Task<string> Line(Type t, string order, Action<ModelConfigurationBuilder>? conv)
    {
        var entity = typeof(EntP<>).MakeGenericType(t);
        await using var db = new EfDb2(mb => { var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id"); }, order, conv);
        return PLine(db);
    }

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var facet = r["ef.convention"]; var ct = r["ef.convention-type"]; var w = r["shape.wrapped"];
        var e = EfX.Top(EfX.Kinds[r.Index % EfX.Kinds.Length], w);
        var plainNone = await Line(e.Plain, "none", null);
        var plainConv = await Line(e.Plain, "none", Conv(facet, e.Plain, w));
        if (plainConv == plainNone)
        {
            // the facet changes nothing on the wrapped type's own property (HavePrecision on a string): it cannot show anything on the value object either
            var fallback = w is "string" or "Uri" ? "HaveMaxLength" : w == "decimal" ? "HavePrecision" : "HaveColumnType";
            r.Substituted("ef.convention", fallback, $"{facet} has no effect on a plain {w} property");
            facet = fallback;
            plainConv = await Line(e.Plain, "none", Conv(facet, e.Plain, w));
            if (plainConv == plainNone) { r.Outcome("inapplicable", $"{facet} changes nothing on a plain {w}"); return null; }
        }
        var voNone = await Line(e.Vo, "after-provider", null);
        if (ct == "underlying")
        {
            var reached = await Line(e.Vo, "after-provider", Conv(facet, e.Plain, w));
            return (reached == voNone ? null : $"a convention on {w} reached the value object's property: {reached} (without it: {voNone}); the plain property got {plainConv}");
        }
        var voConv = await Line(e.Vo, "after-provider", Conv(facet, e.Vo, w));
        if (voConv == voNone) return ($"a convention on the value object type {e.Vo.Name} did not reach its property: {voConv}");
        if (voConv != plainConv) return ($"the convention on the value object gave {voConv}; on the plain {w} {plainConv}");
        return null;
    }
}
