using Metalama.Framework.Aspects;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectMinMaxValueAspect
{
    // ExpressionFactory.Parse is necessary: BCL types expose MinValue/MaxValue as
    // static fields (int, DateTime, decimal...), not properties. There is no unified
    // Metalama API covering both. FullName is safe: types with MinValue/MaxValue
    // are never open generics.

    [Template]
    public static dynamic MinValueTemplate
    {
        get
        {
            var    tag      = (MinMaxValueImplementationArguments)meta.Tags.Source!;
            string typeName = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));
            var    minExpr  = ExpressionFactory.Parse($"{typeName}.MinValue", tag.ValueType, false);
            return tag.Constructor.Invoke(minExpr.Value!)!;
        }
    }

    [Template]
    public static dynamic MaxValueTemplate
    {
        get
        {
            var    tag      = (MinMaxValueImplementationArguments)meta.Tags.Source!;
            string typeName = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));
            var    maxExpr  = ExpressionFactory.Parse($"{typeName}.MaxValue", tag.ValueType, false);
            return tag.Constructor.Invoke(maxExpr.Value!)!;
        }
    }
}
