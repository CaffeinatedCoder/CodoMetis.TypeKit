// Claim E08-option-result-not-columns (docs/ef-core.md#option-and-result): "EF can't map an Option or a Result property, and building the model fails with an error that says so."
// Oracle: claim. Building the model (UseTypeKit on) of an entity with an Option/Result as property, key, list element or owned property throws, and the message names the Option/Result type (not a silent column).
// Obligations (claims.json), both on every row, against the docs page's section (Baseline.Docs): model-fails ("EF can't map an `Option`
// or a `Result` property": InvalidOperationException, never a column) and error-says-so ("building the model fails with an error that
// says so": the message names the type). A model that builds, or a message that does not name the type, is a violation.
using Microsoft.EntityFrameworkCore;
using CodoMetis.TypeKit;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Probes.Ef;
using Probes.Shapes;
namespace Probes;
public sealed class E08OptionResultNotColumnsProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("E08OptionResultNotColumnsProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static Type PlainOf(string w) => w switch { "string" => typeof(string), "int" => typeof(int), "Guid" => typeof(Guid), _ => throw new ArgumentException(w) };

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var w = r["shape.wrapped"]; var kind = r["ef.type"]; var pos = r["ef.position"];
        var plain = PlainOf(w);
        var vo = EfX.Top("record-struct", w).Vo;
        Type t = kind switch
        {
            "Option-int" => typeof(Option<>).MakeGenericType(plain),
            "Option-VO" => typeof(Option<>).MakeGenericType(vo),
            "Result-int-E" => typeof(Result<,>).MakeGenericType(plain, typeof(ShapeFault)),
            "Result-VO-E" => typeof(Result<,>).MakeGenericType(vo, typeof(ShapeFault)),
            "Result-E" => typeof(Result<>).MakeGenericType(plain),
            _ => throw new ArgumentException(kind),
        };
        var entity = pos switch
        {
            "property" => typeof(EntP<>).MakeGenericType(t),
            "key" => typeof(EntK<>).MakeGenericType(t),
            "collection" => typeof(EntL<>).MakeGenericType(t),
            "owned-property" => typeof(EntO<>).MakeGenericType(t),
            _ => throw new ArgumentException(pos),
        };
        Action<Microsoft.EntityFrameworkCore.ModelBuilder> cfg = mb =>
        {
            var eb = mb.Entity(entity); eb.ToTable("t"); eb.HasKey("Id");
            if (pos == "owned-property") eb.OwnsOne(typeof(Own<>).MakeGenericType(t), "O");
        };
        await using var db = new EfDb(cfg, typeKit: true);
        Exception? thrown = null;
        try
        {
            _ = db.Context.GetService<IDesignTimeModel>().Model;
            await db.Context.Database.EnsureCreatedAsync();
        }
        catch (Exception ex) { thrown = ex; }
        var docs = Baseline.Docs("docs/ef-core.md#option-and-result");
        var built = $"the model with {t.Name} as {pos} built and created the table";
        // model-fails: building the model is refused (InvalidOperationException), never a silent column
        var fails = r.Check("model-fails", docs, c =>
        {
            if (thrown is null) return built;
            if (thrown is not InvalidOperationException) return $"model build threw {thrown.GetType().Name}, not InvalidOperationException: {thrown.Message.Split('\n')[0]}";
            c.Note(NoteKeys.Compared, thrown.GetType().Name);
            return null;
        });
        // error-says-so: the refusal names the Option or Result type
        var says = r.Check("error-says-so", docs, c =>
        {
            if (thrown is null) return built + ", with no error to say so";
            c.Note(NoteKeys.RefusalNamesTheType, thrown.Message.Split('\n')[0]);
            var friendly = t.Name.Split('`')[0] + "<";
            return thrown.Message.Contains(friendly) ? null : $"model build failed but the message does not name the type: {thrown.Message.Split('\n')[0]}";
        });
        return fails ?? says;
    }
}
