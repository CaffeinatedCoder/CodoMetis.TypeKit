using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;
using Spike.Abstractions;

namespace Spike.ValueObjects;

/// <summary>Deliberately internal: the spike checks a fabric can apply a non-public aspect across projects.</summary>
internal sealed class ValueObjectAspect : TypeAspect
{
    private readonly ValueObjectKind kind;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ValueObjectAspect(ValueObjectKind kind) => this.kind = kind;

#pragma warning disable CS0649
    // No [Durable] here: that attribute is Metalama 2027.0+. On 2027.0 this placeholder needs it
    // (LAMA0870); it is never assigned, so adding it back is a one-attribute change.
    [Template] private readonly dynamic? _value;
#pragma warning restore CS0649

    [Template] public dynamic Value => meta.This._value;

    [Template]
    public void PrivateConstructor(dynamic? value, [CompileTime] IField valueField) => valueField.Value = value;

    [Template]
    public static dynamic From(dynamic? value, [CompileTime] IConstructor constructor) => constructor.Invoke(value);

    [Template]
    public static dynamic TryFromTemplate(dynamic? value, [CompileTime] INamedType target) =>
        ExpressionFactory.Parse($"{target.FullName}.Create(value).IsOk").Value!;

    /// <summary>The EF materializer candidate: explicitly implemented static, validation-free.</summary>
    [Template]
    public static dynamic Materialize(dynamic? value, [CompileTime] IConstructor constructor) => constructor.Invoke(value);

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        var valueType = ValueObjectTypes.GetUnderlyingType(builder.Target)!;

        var field = builder.IntroduceField(nameof(_value), IntroductionScope.Instance, OverrideStrategy.Fail, f => f.Type = valueType);

        var ctor = builder.IntroduceConstructor(
            nameof(PrivateConstructor),
            buildConstructor: c =>
            {
                c.Accessibility      = Accessibility.Private;
                c.Parameters[0].Type = valueType;
            },
            args: new { valueField = field.Declaration });

        // IDurableRef already exists in 2026.1; only 2027.0 enforces it. Using it now keeps the
        // aspect state forward-compatible.
        builder.AspectState = new ValueObjectAspectState(valueType.ToDurableRef(), field.Declaration.ToDurableRef());

        builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IValueObject<,>)).MakeGenericInstance(builder.Target, valueType));
        builder.IntroduceProperty(nameof(Value), buildProperty: p => { p.Accessibility = Accessibility.Public; p.Type = valueType; });

        if (kind == ValueObjectKind.SimpleValue)
        {
            builder.IntroduceMethod(nameof(From), buildMethod: m =>
            {
                m.Accessibility = Accessibility.Public; m.IsStatic = true;
                m.ReturnType = builder.Target; m.Parameters[0].Type = valueType;
            }, args: new { constructor = ctor.Declaration });
            builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IValueWrapper<,>)).MakeGenericInstance(builder.Target, valueType));
        }
        else
        {
            builder.IntroduceMethod(nameof(TryFromTemplate), IntroductionScope.Static, OverrideStrategy.Ignore, buildMethod: m =>
            {
                m.Name = "IsValid"; m.Accessibility = Accessibility.Public;
                m.ReturnType = typeof(bool).ToNamedType(); m.Parameters[0].Type = valueType;
            }, args: new { target = builder.Target });
        }
    }
}

internal sealed class ValueObjectAspectState : IAspectState
{
    // ReSharper disable once ConvertToPrimaryConstructor
    public ValueObjectAspectState(IDurableRef<INamedType> valueType, IDurableRef<IField> valueField)
    {
        ValueType  = valueType;
        ValueField = valueField;
    }

    public IDurableRef<INamedType> ValueType  { get; }
    public IDurableRef<IField>     ValueField { get; }
}

[CompileTime]
internal static class TypeExtensions
{
    public static INamedType ToNamedType(this Type type) => TypeFactory.GetNamedType(type);
}
