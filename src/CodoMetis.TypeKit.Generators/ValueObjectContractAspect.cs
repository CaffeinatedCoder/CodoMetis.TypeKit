using System.Numerics;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// <c>IValueObject&lt;TSelf, T&gt;</c> and <c>Value</c>; <c>From</c> for a plain value object,
/// <c>TryFrom</c>, <c>FromKnownGood</c> and <c>Revalidate</c> for a validated one; and the
/// equality-operator interface.
/// </summary>
internal sealed partial class ValueObjectContractAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType = state.ValueType.GetTarget();

        builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IValueObject<,>)).MakeGenericInstance(builder.Target, valueType));

        // How run-time code that holds only a Type recognises the value object. Trimming removes
        // IValueObject<,> from a type's interfaces when nothing uses it, and keeps its attributes.
        builder.IntroduceAttribute(AttributeConstruction.Create(
            TypeFactory.GetNamedType(typeof(GeneratedValueObjectAttribute<,>)).MakeGenericInstance(builder.Target, valueType)));

        builder.IntroduceProperty(
            nameof(Value), IntroductionScope.Instance,
            buildProperty: property =>
            {
                property.Accessibility = Accessibility.Public;
                property.Type          = valueType;
                property.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        if (builder.Target.TypeKind == TypeKind.Class && builder.Target.BaseType is { SpecialType: not SpecialType.Object })
            IntroduceEquality(builder, valueType);

        if (state.Kind == ValueObjectKind.Plain)
        {
            ImplementValueWrapper(builder, valueType, state.PrivateConstructor.GetTarget());
        }
        else
        {
            IntroduceTryFrom(builder, valueType);
            IntroduceFromKnownGood(builder, valueType);
            IntroduceRevalidate(builder);
        }

        builder.ImplementInterface(
            typeof(IEqualityOperators<,,>).ToNamedType().MakeGenericInstance(builder.Target, builder.Target, typeof(bool).ToNamedType()),
            OverrideStrategy.Ignore
        );
    }

    /// <summary>
    /// <c>Equals(TSelf)</c> and <c>GetHashCode()</c> over the wrapped value alone, for a record class
    /// derived from another record, whose synthesized equality calls the base's and so compares the
    /// base's fields too.
    /// </summary>
    /// <remarks>
    /// A base record's state is refused (CMTK1012) and its hand-written equality too (CMTK1011), but in
    /// a referenced assembly its private fields are out of sight, and a reference assembly has none: a
    /// base holding a private identity made two value objects holding 42 unequal, a <c>HashSet</c> hold
    /// both, and a JSON round trip give back a value unequal to the one written, while a
    /// <c>SortedSet</c> held one. The record's synthesized <c>Equals(object)</c>, <c>Equals(TBase)</c>
    /// and <c>==</c> call this one. The record is sealed, so <c>other</c> is of this very type.
    /// <para>
    /// A base's <c>IEquatable&lt;TBase&gt;</c>, which a collection of the base calls, is implemented
    /// again (<c>IReimplementedEquatable&lt;TBase&gt;</c>), by the public <c>Equals(TBase)</c> the
    /// compiler synthesizes in the record derived from that base, which calls <c>Equals(object)</c> and so
    /// this one: a base implementing it explicitly kept that implementation, so a <c>HashSet</c> of the
    /// value object held one of two holding 42 and a <c>HashSet</c> of the base both (reported
    /// 2026-10-14). Metalama implements no interface a base already does, nor explicit members of one it
    /// skipped (LAMA0516), so the interface listed is one derived from it, which C# takes for
    /// implementing it again. Every base's, whatever it declares.
    /// </para>
    /// </remarks>
    private static void IntroduceEquality(IAspectBuilder<INamedType> builder, INamedType valueType)
    {
        for (var type = builder.Target.BaseType; type is { SpecialType: not SpecialType.Object }; type = type.BaseType)
            builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IReimplementedEquatable<>)).MakeGenericInstance(type), OverrideStrategy.Ignore);

        builder.IntroduceMethod(
            nameof(EqualsTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Override,
            buildMethod: method =>
            {
                method.Name               = nameof(Equals);
                method.Accessibility      = Accessibility.Public;
                method.Parameters[0].Type = builder.Target.ToNullable();
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { valueType }
        );

        builder.IntroduceMethod(
            nameof(GetHashCodeTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Override,
            buildMethod: method =>
            {
                method.Name = nameof(GetHashCode);
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { valueType }
        );
    }

    private static void ImplementValueWrapper(IAspectBuilder<INamedType> builder, IType valueType, IConstructor privateConstructor)
    {
        builder.IntroduceMethod(
            nameof(From),
            buildMethod: method =>
            {
                method.Accessibility      = Accessibility.Public;
                method.ReturnType         = builder.Target;
                method.IsStatic           = true;
                method.Parameters[0].Type = valueType;
                method.AddAttribute(CodeAnnotations.AggressiveInlining);
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { constructor = privateConstructor, refusesNull = valueType.IsReferenceType == true }
        );

        builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IPlainValueObject<,>)).MakeGenericInstance(builder.Target, valueType));
    }

    /// <summary>
    /// <c>TryFrom</c> in terms of the type's own <c>Create</c>, so it cannot drift from the rules.
    /// </summary>
    /// <remarks>
    /// <c>OverrideStrategy.Ignore</c>: a type that declares its own <c>TryFrom</c> keeps it, and this
    /// one is not generated.
    /// </remarks>
    private static void IntroduceTryFrom(IAspectBuilder<INamedType> builder, IType valueType) =>
        builder.IntroduceMethod(
            nameof(TryFromTemplate),
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            buildMethod: method =>
            {
                method.Name               = "TryFrom";
                method.Accessibility      = Accessibility.Public;
                method.ReturnType         = TypeFactory.GetNamedType(typeof(Option<>)).MakeGenericInstance(builder.Target);
                method.Parameters[0].Type = valueType;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { target = builder.Target }
        );

    /// <summary>
    /// <c>Create</c> applied to the value an instance holds, for one rebuilt without validation (a
    /// column read through the materializer, a stored document) after a rule changed: which stored
    /// values would today's rules refuse?
    /// </summary>
    /// <remarks>
    /// Returns what <c>Create</c> returns, normalisation included, so a stored value that the rules
    /// would now trim or re-case comes back changed. <c>OverrideStrategy.Ignore</c>, as for
    /// <c>TryFrom</c>.
    /// </remarks>
    private static void IntroduceRevalidate(IAspectBuilder<INamedType> builder)
    {
        // The one marker ValueObjectAspect accepted: IValidatedValue<TSelf, T, TFault>.
        var fault = ValueObjectTypes.Markers(builder.Target)[0].TypeArguments[2];

        builder.IntroduceMethod(
            nameof(RevalidateTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Ignore,
            buildMethod: method =>
            {
                method.Name          = "Revalidate";
                method.Accessibility = Accessibility.Public;
                method.ReturnType    = TypeFactory.GetNamedType(typeof(Result<,>)).MakeGenericInstance(builder.Target, fault);
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { target = builder.Target }
        );
    }

    /// <summary>
    /// The factory for a caller that owns its input, so a refusal is the caller's own bug: a literal
    /// in source, or a value the caller has just produced.
    /// </summary>
    /// <remarks>
    /// Derived from <c>Create</c> like <c>TryFrom</c>, so the fault reaches the exception message.
    /// The second parameter is filled in by the C# compiler with the caller's argument expression,
    /// which lets the message name the offending expression without ever containing the value.
    /// </remarks>
    private static void IntroduceFromKnownGood(IAspectBuilder<INamedType> builder, IType valueType) =>
        builder.IntroduceMethod(
            nameof(FromKnownGoodTemplate),
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            buildMethod: method =>
            {
                method.Name               = "FromKnownGood";
                method.Accessibility      = Accessibility.Public;
                method.ReturnType         = builder.Target;
                method.Parameters[0].Type = valueType;
                method.Parameters[1].AddAttribute(CodeAnnotations.CallerArgumentExpressionOfValue);
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { target = builder.Target }
        );
}
