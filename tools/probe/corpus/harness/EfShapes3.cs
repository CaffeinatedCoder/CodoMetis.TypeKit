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
