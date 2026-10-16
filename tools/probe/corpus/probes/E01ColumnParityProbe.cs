// Claim E01-column-parity (docs/ef-core.md#the-model): "Every property maps as a column of the underlying type, nullable where the property is. A facet you configure on it is kept: a precision, a length, a column type."
// Oracle: differential. The EF model and the CREATE script of an entity declared with the value object (UseTypeKit) equal those of the same entity declared with the wrapped type (plain EF), in every mapping, shape and facet.
// Expected: no failure for any row; a model both refuse is an expected refusal; a plain type EF cannot map is inapplicable.
using Microsoft.EntityFrameworkCore;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E01ColumnParityProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E01ColumnParityProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var e = Probes.Ef3.E4.At(r, r["shape.kind"], r["shape.wrapped"], r["shape.site"]);
        var mapping = r["ef.mapping"];
        var facet = r["ef.facet"];
        Action<Microsoft.EntityFrameworkCore.Metadata.IMutableProperty>? apply = null;
        switch (facet)
        {
            case "HasMaxLength" when e.Wrapped is "string" or "Uri": apply = p => p.SetMaxLength(5); break;
            case "HavePrecision" when e.Wrapped == "decimal": apply = p => { p.SetPrecision(18); p.SetScale(2); }; break;
            case "HasColumnType": var ct = EfModel.ColumnTypes[e.Wrapped]; apply = p => p.SetColumnType(ct); break;
            case "none": break;
            default: r.Substituted("ef.facet", "none", "the facet does not apply to the wrapped type"); break;
        }
        var integerKey = mapping == "key" && e.Wrapped is "int" or "long" or "ulong" && facet != "HasColumnType";
        var voEntity = EfModel.Entity(mapping, e.Vo);
        var plainEntity = EfModel.Entity(mapping, e.Plain);
        string? voModel = null, voScript = null, plainModel = null, plainScript = null;
        Exception? voEx = null, plainEx = null;
        await using (var vo = new EfDb(EfModel.Configure(mapping, voEntity, apply, integerKey), typeKit: true))
            try { voModel = EfModel.Describe(vo.Context); voScript = vo.Context.Database.GenerateCreateScript(); } catch (Exception ex) { voEx = ex; }
        await using (var plain = new EfDb(EfModel.Configure(mapping, plainEntity, apply, false), typeKit: false))
            try { plainModel = EfModel.Describe(plain.Context); plainScript = plain.Context.Database.GenerateCreateScript(); } catch (Exception ex) { plainEx = ex; }
        if (voEx is not null && plainEx is not null) { r.Note(NoteKeys.BothThrew, voEx.GetType().Name); return null; }
        if (plainEx is not null) { r.Note(NoteKeys.PlainRefused, plainEx.GetType().Name); return null; }
        if (voEx is not null) return $"model/script with the value object threw {voEx.GetType().Name}: {Short(voEx.Message)}; the wrapped type's did not";
        if (mapping == "key" && e.Wrapped is "char" or "Guid")
        {
            // README#keys: "A key over `char` is yours to assign. It's never generated, on any provider" and "A key over a `Guid` is yours to assign" (EF's own plain char and Guid keys are generated on add).
            if (!voModel!.Contains("pk=True gen=Never")) return "a key over char or Guid is not ValueGeneratedNever: " + voModel;
            voScript = voScript!.Replace(" AUTOINCREMENT", ""); plainScript = plainScript!.Replace(" AUTOINCREMENT", "");
            voModel = voModel.Replace("gen=Never", "gen=*"); plainModel = plainModel!.Replace("gen=OnAdd", "gen=*").Replace("gen=Never", "gen=*");
            r.Note(NoteKeys.DocumentedCharGuidKeyNeverGeneratedChecked, "generation compared apart: not-promised char-key-not-generated, guid-key-not-generated");
        }
        if (voModel != plainModel) return "model differs: " + EfModel.FirstDiff(voModel!, plainModel!);
        if (voScript != plainScript) return "create script differs: " + EfModel.FirstDiff(voScript!, plainScript!);
        return null;
    }
    static string Short(string s) => s.Length > 300 ? s[..300] : s;
}
