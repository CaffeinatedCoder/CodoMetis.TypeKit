using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectComparableAspect
{
    // IComparable<TWrapper>.CompareTo(TWrapper other)
    [Template]
    public int GenericCompareTo(dynamic other)
    {
        var tag = (ComparableImplementationArguments)meta.Tags.Source!;

        if (meta.CompileTime(tag.Strategy == ValueCompareStrategy.GenericComparable))
        {
            // ExpressionFactory.Parse is necessary: an explicit IComparable<T> cast
            // is required to avoid boxing value types through dynamic dispatch.
            // Metalama's IMethod.Invoke has no equivalent for typed instance method
            // calls through a generic interface constraint.
            // The wrapped type is never an open generic (the implementation aspect refuses those).
            string typeName = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));
            return (int)ExpressionFactory.Parse(
                $"((global::System.IComparable<{typeName}>)this.Value).CompareTo(other.Value)",
                TypeFactory.GetType(SpecialType.Int32),
                false
            ).Value!;
        }

        // NonGenericComparable: boxing is inherent to the interface; no workaround.
        return ((IComparable)meta.This.Value).CompareTo(other.Value);
    }

    // IComparable.CompareTo(object? obj)
    [Template]
    public int ObjectCompareTo(object? obj)
    {
        var tag = (ComparableImplementationArguments)meta.Tags.Source!;

        if (obj is null) return 1;

        // ExpressionFactory.Parse is necessary: 'is TWrapper other' pattern binding
        // has no first-class Metalama API equivalent — same category as TryParse's
        // out parameter workaround.
        string typeName = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueObjectType));
        var isMatch = ExpressionFactory.Parse(
            $"obj is {typeName} __comparand",
            TypeFactory.GetType(SpecialType.Boolean),
            false
        );

        if (!(bool)isMatch.Value!)
            throw new ArgumentException(
                $"Object must be of type {meta.CompileTime(tag.ValueObjectType.Name)}.",
                nameof(obj)
            );

        // Delegate to the type-safe generic overload — avoids duplicating comparison logic.
        return meta.This.CompareTo(ExpressionFactory.Parse("__comparand", tag.ValueObjectType, false).Value!);
    }

    // All four operators delegate to CompareTo, keeping comparison logic in one place.
    // AggressiveInlining is applied at the introduction site, so the operator calls
    // disappear entirely in release builds.

    [Template]
    public static bool LessThanOperator(dynamic left, dynamic right) => left.CompareTo(right) < 0;

    [Template]
    public static bool GreaterThanOperator(dynamic left, dynamic right) => left.CompareTo(right) > 0;

    [Template]
    public static bool LessThanOrEqualOperator(dynamic left, dynamic right) => left.CompareTo(right) <= 0;

    [Template]
    public static bool GreaterThanOrEqualOperator(dynamic left, dynamic right) => left.CompareTo(right) >= 0;
}
