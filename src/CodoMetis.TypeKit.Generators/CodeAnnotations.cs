using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CodoMetis.TypeKit.CompilerServices;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

/// <summary>The attributes the aspects put on what they generate.</summary>
[CompileTime]
internal static class CodeAnnotations
{
    public static readonly AttributeConstruction AggressiveInlining =
        AttributeConstruction.Create(typeof(MethodImplAttribute), [MethodImplOptions.AggressiveInlining]);

    /// <summary>
    /// On the <c>source</c> parameter of the generated <c>FromKnownGood</c>, whose first parameter is
    /// always named <c>value</c>.
    /// </summary>
    public static readonly AttributeConstruction CallerArgumentExpressionOfValue =
        AttributeConstruction.Create(typeof(CallerArgumentExpressionAttribute), ["value"]);

    public static readonly AttributeConstruction CompilerGenerated =
        AttributeConstruction.Create(typeof(CompilerGeneratedAttribute));

    public static readonly AttributeConstruction DebuggerHidden =
        AttributeConstruction.Create(typeof(DebuggerHiddenAttribute));

    public static readonly AttributeConstruction DebuggerStepThrough =
        AttributeConstruction.Create(typeof(DebuggerStepThroughAttribute));

    public static readonly AttributeConstruction EditorNonBrowsable =
        AttributeConstruction.Create(typeof(EditorBrowsableAttribute), [EditorBrowsableState.Never]);

    public static readonly AttributeConstruction TranslatedAsWrappedValue =
        AttributeConstruction.Create(typeof(TranslatedAsWrappedValueAttribute));
}
