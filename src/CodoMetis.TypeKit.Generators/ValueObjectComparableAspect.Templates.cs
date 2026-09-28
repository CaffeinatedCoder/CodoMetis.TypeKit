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

        // A record class meets null here, and null sorts first, as in the BCL. Without this every
        // strategy below dereferenced other.Value and threw NullReferenceException.
        if (meta.CompileTime(tag.ValueObjectType.IsReferenceType == true))
        {
            if ((bool)ExpressionFactory.Parse("other is null", TypeFactory.GetType(SpecialType.Boolean), false).Value!)
                return 1;
        }

        if (meta.CompileTime(tag.Strategy == ValueCompareStrategy.OrdinalString))
        {
            // Not string.CompareTo, which compares by the current culture: under it "a" sorts before
            // "B" and a zero-width space counts for nothing, so "A​BC" compared equal to "ABC"
            // while the record's ordinal equality told them apart, and a SortedSet dropped a value
            // a HashSet kept. Ordinal agrees with the equality.
            return (int)ExpressionFactory.Parse(
                "global::System.String.CompareOrdinal(this.Value, other.Value)",
                TypeFactory.GetType(SpecialType.Int32),
                false
            ).Value!;
        }

        // Comparer<T>.Default, which the JIT specialises per wrapped type: a constrained call to
        // IComparable<T> for a struct, and for an enum a comparer of the underlying values. Casting
        // the value to IComparable<T> or IComparable boxed it, and for an enum both operands: sorting
        // 1,000 enum-backed value objects allocated 367 KB and took eight times as long as sorting
        // the enums (measured 2026-09-28). It reaches an explicit implementation too.
        // The wrapped type is never an open generic (ValueObjectAspect refuses those).
        string typeName = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));
        return (int)ExpressionFactory.Parse(
            $"global::System.Collections.Generic.Comparer<{typeName}>.Default.Compare(this.Value, other.Value)",
            TypeFactory.GetType(SpecialType.Int32),
            false
        ).Value!;
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
    // disappear entirely in release builds. For a record class the left operand can be null,
    // which CompareTo cannot see, so the operator sorts it first itself.

    private static IExpression Compared(ComparableImplementationArguments tag, string relation)
    {
        var comparison = tag.ValueObjectType.IsReferenceType == true
                             ? "(left is null ? (right is null ? 0 : -1) : left.CompareTo(right))"
                             : "left.CompareTo(right)";

        return ExpressionFactory.Parse($"{comparison} {relation} 0", TypeFactory.GetType(SpecialType.Boolean), false);
    }

    [Template]
    public static bool LessThanOperator(dynamic left, dynamic right)
    {
        var tag = (ComparableImplementationArguments)meta.Tags.Source!;
        return (bool)Compared(tag, "<").Value!;
    }

    [Template]
    public static bool GreaterThanOperator(dynamic left, dynamic right)
    {
        var tag = (ComparableImplementationArguments)meta.Tags.Source!;
        return (bool)Compared(tag, ">").Value!;
    }

    [Template]
    public static bool LessThanOrEqualOperator(dynamic left, dynamic right)
    {
        var tag = (ComparableImplementationArguments)meta.Tags.Source!;
        return (bool)Compared(tag, "<=").Value!;
    }

    [Template]
    public static bool GreaterThanOrEqualOperator(dynamic left, dynamic right)
    {
        var tag = (ComparableImplementationArguments)meta.Tags.Source!;
        return (bool)Compared(tag, ">=").Value!;
    }
}
