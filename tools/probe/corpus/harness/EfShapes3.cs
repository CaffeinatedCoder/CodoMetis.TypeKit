// Lookup of the top-level shapes of EfShapes3.g.cs as EfEntry (so MakeVo/Companion work): the complete kind x wrapped grid.
#nullable enable
namespace Probes.Ef3;
public static class E3
{
    public static Probes.Ef.EfEntry Find(string kind, string wrapped)
    {
        var (vo, plain) = Reg3.All[(kind, wrapped)];
        var src = Probes.Ef.EfRegistry.All.Where(x => x.Wrapped == wrapped && x.Kind == (kind.StartsWith("validated") ? "validated-struct" : "record-struct")).FirstOrDefault()
            ?? Probes.Ef.EfRegistry.All.First(x => x.Wrapped == wrapped);
        return new Probes.Ef.EfEntry(kind, wrapped, "top-level", vo, plain, src.Valid);
    }
}
public static class E4
{
    /// <summary>The shape at a site; in-generic-type (CMTK1005, refused at build) runs at top-level and says so.</summary>
    public static Probes.Ef.EfEntry At(Probes.Shapes.Row r, string kind, string wrapped, string site)
    {
        if (site == "in-generic-type") { r.Substituted("shape.site", "top-level", "CMTK1005: a value object nested in a generic type does not build"); site = "top-level"; }
        if (site == "top-level") return E3.Find(kind, wrapped);
        var (vo, plain) = Probes.Ef4.Reg4.All[(kind, wrapped, site)];
        var src = Probes.Ef.EfRegistry.All.Where(x => x.Wrapped == wrapped && x.Kind == (kind.StartsWith("validated") ? "validated-struct" : "record-struct")).FirstOrDefault()
            ?? Probes.Ef.EfRegistry.All.First(x => x.Wrapped == wrapped);
        return new Probes.Ef.EfEntry(kind, wrapped, site, vo, plain, src.Valid);
    }
}
