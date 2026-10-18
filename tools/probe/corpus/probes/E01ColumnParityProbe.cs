// Claim E01-column-parity (docs/ef-core.md#the-model): "Every property maps as a column of the underlying type, nullable where the property is. A facet you configure on it is kept: a precision, a length, a column type."
// Oracle: differential. The EF model and the CREATE script of an entity declared with the value object (UseTypeKit) equal those of the same entity declared with the wrapped type (plain EF), in every mapping, shape and facet.
// Obligations (claims.json), one check each where the row's axes select it, each against the wrapped twin (Baseline.Twin):
//   column-type (ef.mapping property, key, owned, complex): "Every property maps as a column of the underlying type, nullable where the property is."
//   nullable (ef.mapping nullable): "nullable where the property is"
//   facet-kept (ef.facet HasMaxLength, HavePrecision, HasColumnType): "A facet you configure on it is kept: a precision, a length, a column type."
// Expected: no failure for any row; where a side refuses the model, EfSides says how the check ends.
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
        // The facet as configured, on any wrapped type: EF keeps a length or a precision on a property of any type, and the
        // wrapped twin shows what it keeps for that type.
        var columnType = EfModel.ColumnTypes[e.Wrapped];
        Action<Microsoft.EntityFrameworkCore.Metadata.IMutableProperty>? apply = facet switch
        {
            "HasMaxLength" => p => p.SetMaxLength(5),
            "HavePrecision" => p => { p.SetPrecision(18); p.SetScale(2); },
            "HasColumnType" => p => p.SetColumnType(columnType),
            _ => null,
        };
        var integerKey = mapping == "key" && e.Wrapped is "int" or "long" or "ulong" && facet != "HasColumnType";
        var voEntity = EfModel.Entity(mapping, e.Vo);
        var plainEntity = EfModel.Entity(mapping, e.Plain);
        string? voModel = null, voScript = null, plainModel = null, plainScript = null;
        Exception? voEx = null, plainEx = null;
        await using (var vo = new EfDb(EfModel.Configure(mapping, voEntity, apply, integerKey), typeKit: true))
            try { voModel = EfModel.Describe(vo.Context); voScript = vo.Context.Database.GenerateCreateScript(); } catch (Exception ex) { voEx = ex; }
        await using (var plain = new EfDb(EfModel.Configure(mapping, plainEntity, apply, false), typeKit: false))
            try { plainModel = EfModel.Describe(plain.Context); plainScript = plain.Context.Database.GenerateCreateScript(); } catch (Exception ex) { plainEx = ex; }

        // A key over char or Guid is never generated where EF generates the twin's (README#keys, below): the script's AUTOINCREMENT
        // is that generation, compared apart by column-type, so neither check compares it in the script.
        var ownKey = mapping == "key" && e.Wrapped is "char" or "Guid";
        if (ownKey) { voScript = voScript?.Replace(" AUTOINCREMENT", ""); plainScript = plainScript?.Replace(" AUTOINCREMENT", ""); }

        // The column the facet is configured on, as EfModel.Describe names it, and the fields the facet sets there.
        var column = mapping switch { "key" => "t.Id: ", "complex" => "t.C.P: ", _ => "t.P: " };
        string[] facetFields = facet switch { "HasMaxLength" => ["max"], "HavePrecision" => ["prec", "scale"], "HasColumnType" => ["type"], _ => [] };

        // column-type and nullable: every column of the model and the script, as the wrapped twin's, with the facet's own fields
        // left to facet-kept (and the script too, where the facet is the column type the script spells).
        var mapped = r.Check(mapping == "nullable" ? "nullable" : "column-type", Baseline.Twin, c =>
        {
            if (EfSides.Threw(c, voEx, plainEx, "the model or the create script", out var refused)) return refused;
            string vm = Mask(voModel!, column, facetFields), pm = Mask(plainModel!, column, facetFields), vs = voScript!, ps = plainScript!;
            if (ownKey)
            {
                // README#keys: "A key over `char` is yours to assign. It's never generated, on any provider" and "A key over a `Guid` is yours to assign" (EF's own plain char and Guid keys are generated on add).
                if (!voModel!.Contains("pk=True gen=Never")) return "a key over char or Guid is not ValueGeneratedNever: " + voModel;
                vm = vm.Replace("gen=Never", "gen=*"); pm = pm.Replace("gen=OnAdd", "gen=*").Replace("gen=Never", "gen=*");
                c.Note(NoteKeys.DocumentedCharGuidKeyNeverGeneratedChecked, "generation compared apart: not-promised char-key-not-generated, guid-key-not-generated");
            }
            if (vm != pm) return "model differs: " + EfModel.FirstDiff(vm, pm);
            if (facet != "HasColumnType" && vs != ps) return "create script differs: " + EfModel.FirstDiff(vs, ps);
            c.Note(NoteKeys.Compared, facet == "HasColumnType" ? "the model" : "the model and the create script");
            return null;
        });
        if (facet == "none") return mapped;

        // facet-kept: the facet's own fields of the configured column, and for a column type the script, as the wrapped twin keeps them.
        var kept = r.Check("facet-kept", Baseline.Twin, c =>
        {
            if (EfSides.Threw(c, voEx, plainEx, "the model or the create script", out var refused)) return refused;
            var vl = Line(voModel!, column); var pl = Line(plainModel!, column);
            foreach (var f in facetFields)
                if (Field(vl, f) != Field(pl, f)) return $"{facet} not kept: the value object's column has {f}={Field(vl, f)}, the wrapped type's {f}={Field(pl, f)} ({vl})";
            if (facet == "HasColumnType" && voScript != plainScript) return "create script differs: " + EfModel.FirstDiff(voScript!, plainScript!);
            c.Note(NoteKeys.Compared, string.Join(",", facetFields.Select(f => $"{f}={Field(pl, f)}")));
            return null;
        });
        return mapped ?? kept;
    }

    /// <summary>The line EfModel.Describe writes for <paramref name="column"/>; a model without it is the harness's mistake.</summary>
    static string Line(string model, string column) =>
        model.Split('\n').FirstOrDefault(l => l.StartsWith(column, StringComparison.Ordinal)) ?? throw new InvalidOperationException($"no column {column.TrimEnd(' ', ':')} in the model: {model}");
    static string Field(string line, string field) => System.Text.RegularExpressions.Regex.Match(line, $@"(?:^|\s){field}=(\S*)").Groups[1].Value;
    /// <summary>The model with the given fields of <paramref name="column"/>'s line masked.</summary>
    static string Mask(string model, string column, string[] fields) => string.Join("\n", model.Split('\n').Select(l =>
        !l.StartsWith(column, StringComparison.Ordinal) ? l : fields.Aggregate(l, (x, f) => System.Text.RegularExpressions.Regex.Replace(x, $@"(^|\s){f}=\S*", $"$1{f}=*"))));
}
