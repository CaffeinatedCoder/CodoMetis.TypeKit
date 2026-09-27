using System.Numerics;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

[CompileTime]
internal enum ValueCompareStrategy
{
    OrdinalString,        // T is string: ordinal, so the ordering agrees with the record's ordinal equality
    GenericComparable,    // T : IComparable<T> — type-safe, no boxing for structs
    NonGenericComparable, // T : IComparable only — boxing unavoidable
    Unsupported
}

[CompileTime]
internal sealed class ComparableImplementationArguments
{
    public required INamedType        ValueType       { get; init; }
    public required INamedType        ValueObjectType { get; init; }
    public required ValueCompareStrategy Strategy     { get; init; }
}

internal sealed partial class ValueObjectComparableAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType = state.ValueType.GetTarget();
        var strategy  = ResolveStrategy(valueType);

        if (strategy == ValueCompareStrategy.Unsupported)
        {
            builder.SkipAspect();
            return;
        }

        // One seam: a hand-written CompareTo(TSelf) is kept (OverrideStrategy.Ignore below), and the
        // object overload, the operators and the interfaces are derived from it, as TryFrom is for the
        // factories. Any other hand-written comparison member would sit beside generated ones that
        // need not agree with it, so it is an error naming the seam. Before this, an operator failed
        // the aspect (LAMA0500, naming no fix) and an object overload was kept silently.
        var besideTheSeam = HandWrittenComparisonMembers(builder.Target).ToList();

        if (besideTheSeam.Count > 0)
        {
            builder.Diagnostics.Report(AspectDiagnostics.ComparisonMemberBesideTheSeam.WithArguments((builder.Target, string.Join(", ", besideTheSeam))));
            builder.SkipAspect();
            return;
        }

        builder.Tags = new ComparableImplementationArguments
        {
            ValueType       = valueType,
            ValueObjectType = builder.Target,
            Strategy        = strategy
        };

        // IComparable<TWrapper> — type-safe, primary interface
        builder.ImplementInterface(
            typeof(IComparable<>).ToNamedType().MakeGenericInstance(builder.Target),
            OverrideStrategy.Ignore
        );

        builder.IntroduceMethod(
            nameof(GenericCompareTo),
            IntroductionScope.Instance,
            OverrideStrategy.Ignore,
            m =>
            {
                m.Name               = nameof(IComparable<int>.CompareTo);
                m.Parameters[0].Type = OperandType(builder.Target);
                m.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // IComparable — required for Array.Sort, SortedSet, etc. A hand-written object overload is
        // CMTK1008 above, so Fail here is a backstop, never a silent keep.
        builder.ImplementInterface(typeof(IComparable), OverrideStrategy.Ignore);

        builder.IntroduceMethod(
            nameof(ObjectCompareTo),
            IntroductionScope.Instance,
            OverrideStrategy.Fail,
            m =>
            {
                m.Name = nameof(IComparable.CompareTo);
                m.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // Comparison operators — all delegate to CompareTo
        IntroduceComparisonOperator(builder, nameof(LessThanOperator),           OperatorKind.LessThan);
        IntroduceComparisonOperator(builder, nameof(GreaterThanOperator),        OperatorKind.GreaterThan);
        IntroduceComparisonOperator(builder, nameof(LessThanOrEqualOperator),    OperatorKind.LessThanOrEqual);
        IntroduceComparisonOperator(builder, nameof(GreaterThanOrEqualOperator), OperatorKind.GreaterThanOrEqual);

        // Formally declare the IComparisonOperators<,,> contract satisfied
        // by the four operators introduced above.
        builder.ImplementInterface(
            typeof(IComparisonOperators<,,>).ToNamedType()
                .MakeGenericInstance(builder.Target, builder.Target, typeof(bool).ToNamedType()),
            OverrideStrategy.Ignore
        );
    }

    private static ValueCompareStrategy ResolveStrategy(INamedType valueType)
    {
        // string.CompareTo is culture-sensitive, and the record's equality is ordinal.
        if (valueType.SpecialType == SpecialType.String)
            return ValueCompareStrategy.OrdinalString;

        // Probe IComparable<T> first — avoids boxing for struct T.
        var genericComparable = typeof(IComparable<>).ToNamedType().MakeGenericInstance(valueType);
        if (valueType.IsConvertibleTo(genericComparable))
            return ValueCompareStrategy.GenericComparable;

        if (valueType.IsConvertibleTo(typeof(IComparable)))
            return ValueCompareStrategy.NonGenericComparable;

        return ValueCompareStrategy.Unsupported;
    }

    /// <summary>
    /// The comparison members the type declares itself, other than the seam <c>CompareTo(TSelf)</c>:
    /// the object overload and the four ordering operators.
    /// </summary>
    private static IEnumerable<string> HandWrittenComparisonMembers(INamedType target)
    {
        foreach (var method in target.Methods)
        {
            var isObjectOverload = method is { Name: nameof(IComparable.CompareTo), Parameters: [{ Type.SpecialType: SpecialType.Object }] };
            var isOrderingOperator = method.OperatorKind is OperatorKind.LessThan or OperatorKind.GreaterThan
                                                          or OperatorKind.LessThanOrEqual or OperatorKind.GreaterThanOrEqual;

            if (isObjectOverload || isOrderingOperator)
                yield return method.ToDisplayString();
        }
    }

    /// <summary>
    /// The operand type: nullable for a record class, as <c>IComparable&lt;T&gt;.CompareTo(T?)</c> and
    /// the record's own equality operators declare it, so a caller passing null gets flow analysis
    /// rather than a <c>NullReferenceException</c>. The templates then sort null first.
    /// </summary>
    private static IType OperandType(INamedType target) => target.IsReferenceType == true ? target.ToNullable() : target;

    private static void IntroduceComparisonOperator(
        IAspectBuilder<INamedType> builder,
        string                     templateName,
        OperatorKind               operatorKind
    )
    {
        builder.IntroduceMethod(templateName, buildMethod: m =>
        {
            m.Parameters[0].Type = OperandType(builder.Target);
            m.Parameters[1].Type = OperandType(builder.Target);
            m.ReturnType         = typeof(bool).ToNamedType();
            m.OperatorKind       = operatorKind;
            m.AddAttribute(CodeAnnotations.AggressiveInlining);
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
    }
}
