using Metalama.Framework.Advising;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectConvertibleAspect : TypeAspect
{
    // The 14 typed IConvertible conversions, which share one delegation shape.
    private static readonly (string MethodName, Type ReturnType)[] TypedConversionMethods =
    [
        (nameof(IConvertible.ToBoolean), typeof(bool)),
        (nameof(IConvertible.ToByte), typeof(byte)),
        (nameof(IConvertible.ToChar), typeof(char)),
        (nameof(IConvertible.ToDateTime), typeof(DateTime)),
        (nameof(IConvertible.ToDecimal), typeof(decimal)),
        (nameof(IConvertible.ToDouble), typeof(double)),
        (nameof(IConvertible.ToInt16), typeof(short)),
        (nameof(IConvertible.ToInt32), typeof(int)),
        (nameof(IConvertible.ToInt64), typeof(long)),
        (nameof(IConvertible.ToSByte), typeof(sbyte)),
        (nameof(IConvertible.ToSingle), typeof(float)),
        (nameof(IConvertible.ToUInt16), typeof(ushort)),
        (nameof(IConvertible.ToUInt32), typeof(uint)),
        (nameof(IConvertible.ToUInt64), typeof(ulong)),
    ];

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType = state.ValueType.GetTarget();

        // Only emit when the underlying type itself is IConvertible.
        // For all others (Guid, Uri, custom types) the interface would be misleading.
        if (!valueType.IsConvertibleTo(typeof(IConvertible)))
        {
            builder.SkipAspect();
            return;
        }

        var interfaceType = builder.ImplementInterface(typeof(IConvertible), OverrideStrategy.Ignore);

        // GetTypeCode — unique shape, dedicated template.
        interfaceType.ExplicitMembers.IntroduceMethod(
            nameof(GetTypeCodeTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Ignore,
            m =>
            {
                m.Name = nameof(IConvertible.GetTypeCode);
                m.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // ToType — universal escape hatch, dedicated template.
        interfaceType.ExplicitMembers.IntroduceMethod(
            nameof(ToTypeTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Ignore,
            m =>
            {
                m.Name = nameof(IConvertible.ToType);
                m.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // ToString(IFormatProvider) — already covered by IFormattable, dedicated template
        // to satisfy the explicit IConvertible contract without duplicating logic.
        interfaceType.ExplicitMembers.IntroduceMethod(
            nameof(ToStringTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Ignore,
            m =>
            {
                m.Name = nameof(IConvertible.ToString);
                m.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // One template, introduced once per typed conversion with its name and return type.
        foreach (var (methodName, returnType) in TypedConversionMethods)
        {
            var capturedMethodName = methodName;
            var capturedReturnType = returnType;

            interfaceType.ExplicitMembers.IntroduceMethod(
                nameof(TypedConversionTemplate),
                IntroductionScope.Instance,
                OverrideStrategy.Ignore,
                m =>
                {
                    m.Name       = capturedMethodName;
                    m.ReturnType = capturedReturnType.ToNamedType();
                    m.AddAttribute(CodeAnnotations.CompilerGenerated);
                },
                args: new
                      {
                          methodName = capturedMethodName,
                          returnType = capturedReturnType.ToNamedType()
                      }
            );
        }
    }
}
