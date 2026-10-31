using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CodoMetis.TypeKit.CompilerServices;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

/// <summary>The attributes the aspects put on what they generate.</summary>
/// <remarks>
/// Each is built where it is used, never kept in a static field: an <see cref="AttributeConstruction"/>
/// belongs to one compilation, and a static field would keep every compilation the IDE ever made alive
/// (<c>CompileTimeStaticStateTests</c>).
/// </remarks>
[CompileTime]
internal static class CodeAnnotations
{
    public static AttributeConstruction AggressiveInlining =>
        AttributeConstruction.Create(typeof(MethodImplAttribute), [MethodImplOptions.AggressiveInlining]);

    /// <summary>
    /// On the <c>source</c> parameter of the generated <c>FromKnownGood</c>, whose first parameter is
    /// always named <c>value</c>.
    /// </summary>
    public static AttributeConstruction CallerArgumentExpressionOfValue =>
        AttributeConstruction.Create(typeof(CallerArgumentExpressionAttribute), ["value"]);

    public static AttributeConstruction CompilerGenerated =>
        AttributeConstruction.Create(typeof(CompilerGeneratedAttribute));

    public static AttributeConstruction DebuggerHidden =>
        AttributeConstruction.Create(typeof(DebuggerHiddenAttribute));

    public static AttributeConstruction DebuggerStepThrough =>
        AttributeConstruction.Create(typeof(DebuggerStepThroughAttribute));

    public static AttributeConstruction EditorNonBrowsable =>
        AttributeConstruction.Create(typeof(EditorBrowsableAttribute), [EditorBrowsableState.Never]);

    /// <summary>
    /// <c>[GeneratedMember(typeof(contract), member)]</c>, on every member the generators introduce. A named member
    /// gets it from its <see cref="Generators.GeneratedMember"/> entry; what no check finds by name (an explicit
    /// implementation, an operator, a constructor, a companion's method) gets it here, where it is introduced.
    /// </summary>
    public static AttributeConstruction GeneratedMember(IType contract, string member) =>
        AttributeConstruction.Create(typeof(GeneratedMemberAttribute), [contract, member]);

    public static AttributeConstruction TranslatedAsWrappedValue =>
        AttributeConstruction.Create(typeof(TranslatedAsWrappedValueAttribute));

    public static AttributeConstruction TranslatedAsWrappedArithmetic =>
        AttributeConstruction.Create(typeof(TranslatedAsWrappedArithmeticAttribute));
}
