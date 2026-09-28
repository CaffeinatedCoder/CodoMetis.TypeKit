using System.ComponentModel;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectTypeConverterAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        // TypeConverter only makes sense when round-tripping through string is possible.
        // IParsable<T> is always generated when the underlying type is parsable, so we
        // use its presence as the gate — same rule ValueObjectParsableAspect uses.
        var parsableInterface = typeof(IParsable<>).ToNamedType().MakeGenericInstance(builder.Target);
        if (!builder.Target.IsConvertibleTo(parsableInterface))
        {
            builder.SkipAspect();
            return;
        }

        var typeConverter = builder.IntroduceClass(
            $"{builder.Target.Name}TypeConverter",
            buildType: t =>
            {
                t.Accessibility = Accessibility.Public;
                t.BaseType      = TypeFactory.GetNamedType(typeof(TypeConverter));
                t.IsPartial     = true;
                t.IsStatic      = false;
                t.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        typeConverter.IntroduceMethod(
            nameof(CanConvertFromTemplate),
            whenExists: OverrideStrategy.Override,
            buildMethod: x =>
            {
                x.Name = nameof(TypeConverter.CanConvertFrom);
                x.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        typeConverter.IntroduceMethod(
            nameof(CanConvertToTemplate),
            whenExists: OverrideStrategy.Override,
            buildMethod: x =>
            {
                x.Name = nameof(TypeConverter.CanConvertTo);
                x.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        typeConverter.IntroduceMethod(
            nameof(ConvertFromTemplate),
            whenExists: OverrideStrategy.Override,
            buildMethod: x =>
            {
                x.Name = nameof(TypeConverter.ConvertFrom);
                x.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { valueObjectType = builder.Target }
        );

        typeConverter.IntroduceMethod(
            nameof(ConvertToTemplate),
            whenExists: OverrideStrategy.Override,
            buildMethod: x =>
            {
                x.Name = nameof(TypeConverter.ConvertTo);
                x.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { valueObjectType = builder.Target, valueType = state.ValueType.GetTarget() }
        );

        var typeConverterAttribute = AttributeConstruction.Create(
            typeof(TypeConverterAttribute),
            constructorArguments: [typeConverter.Target]
        );
        builder.IntroduceAttribute(typeConverterAttribute);
    }
}
