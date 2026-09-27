using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace Spike.ValueObjects;

// Four more aspect classes, so a consumer sees five in total: probes the free-tier limit of 3.
internal sealed class ExtraAspect1 : TypeAspect { [Introduce] public static string Probe1() => "1"; }
internal sealed class ExtraAspect2 : TypeAspect { [Introduce] public static string Probe2() => "2"; }
internal sealed class ExtraAspect3 : TypeAspect { [Introduce] public static string Probe3() => "3"; }
internal sealed class ExtraAspect4 : TypeAspect { [Introduce] public static string Probe4() => "4"; }
