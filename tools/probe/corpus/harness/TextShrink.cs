// Registers the text claims' checks with the shrinker (ShrinkProbe check names L02, L03, L04).
using System.Runtime.CompilerServices;
using Probes.Shapes;
namespace Probes;
public static class TextShrinkInit
{
    [ModuleInitializer]
    public static void Init()
    {
        ShrinkChecks.All["L04"] = r => Task.FromResult(TextClaims.RoundTrip(r, Registry.Of(r)));
        ShrinkChecks.All["L02"] = r => Task.FromResult(TextClaims.NoQuote(r, Registry.Of(r)));
        ShrinkChecks.All["L03"] = r => Task.FromResult(TextClaims.OneRule(r, Registry.Of(r)));
    }
}
