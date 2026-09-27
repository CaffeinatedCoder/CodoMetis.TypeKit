using System.Diagnostics.CodeAnalysis;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Advising;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using static CodoMetis.TypeKit.Generators.AspectDiagnostics;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// The first aspect on every value object: checks the declaration, introduces the field, the private
/// constructor, the explicit materializer and the entry-point helpers, and adds the other aspects.
/// </summary>
internal sealed partial class ValueObjectImplementationAspect : TypeAspect
{
    private readonly string? _extensionClassNameOwner;
    private readonly string? _wrappedValueObjectRefusal;

    /// <param name="extensionClassNameOwner">
    /// What already owns the name of the companion class, as <see cref="CompanionClass.NameOwner"/>
    /// answered it in the fabric, or <see langword="null"/> when the name is free.
    /// </param>
    /// <param name="wrappedValueObjectRefusal">
    /// Why the value object cannot wrap what it wraps, as
    /// <see cref="ValueObjectTypes.WrappedValueObjectRefusal"/> answered it in the fabric, or
    /// <see langword="null"/> when what it wraps is not a value object.
    /// </param>
    public ValueObjectImplementationAspect(string? extensionClassNameOwner, string? wrappedValueObjectRefusal)
    {
        _extensionClassNameOwner   = extensionClassNameOwner;
        _wrappedValueObjectRefusal = wrappedValueObjectRefusal;
    }

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!TryResolve(builder, _wrappedValueObjectRefusal, out var kind, out var valueType))
        {
            builder.SkipAspect();
            return;
        }

        var target = builder.Target;

        if (!target.IsPartial)
            builder.Diagnostics.Report(MissingPartialKeyword.WithArguments(target));

        if (!target.IsRecord)
            builder.Diagnostics.Report(MissingRecordKeyword.WithArguments(target));

        if (target is { TypeKind: TypeKind.Struct, IsReadOnly: false })
            builder.Diagnostics.Report(MissingReadonlyKeyword.WithArguments(target));

        HideDefaultStructConstructor(builder);

        var valueField = builder.IntroduceField(
            nameof(_value), IntroductionScope.Instance, OverrideStrategy.Fail,
            field =>
            {
                field.Type = valueType;
                field.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        var privateConstructor = builder.IntroduceConstructor(
            nameof(PrivateConstructor),
            buildConstructor: constructor =>
            {
                constructor.Accessibility      = Accessibility.Private;
                constructor.InitializerKind    = ConstructorInitializerKind.None;
                constructor.Parameters[0].Type = valueType;
                constructor.AddAttribute(CodeAnnotations.CompilerGenerated);
                constructor.AddAttribute(CodeAnnotations.DebuggerStepThrough);
            },
            args: new { valueField = valueField.Declaration }
        );

        ImplementMaterializer(builder, valueType, privateConstructor.Declaration);

        var entryPointArgs = new { constructor = privateConstructor.Declaration, target, kind };

        var fromJson    = IntroduceEntryPoint(builder, nameof(FromJsonTemplate),    "__FromJson",    valueType, entryPointArgs);
        var fromText    = IntroduceEntryPoint(builder, nameof(FromTextTemplate),    "__FromText",    valueType, entryPointArgs);
        var tryFromText = IntroduceEntryPoint(builder, nameof(TryFromTextTemplate), "__TryFromText", valueType, entryPointArgs);

        builder.AspectState = new ValueObjectAspectState(
            kind,
            valueType.ToDurableRef(),
            privateConstructor.Declaration.ToDurableRef(),
            fromJson.ToDurableRef(),
            fromText.ToDurableRef(),
            tryFromText.ToDurableRef(),
            CompanionClass.Name(target),
            _extensionClassNameOwner
        );

        builder.Outbound.AddAspect<ValueObjectInterfaceAspect>();
        builder.Outbound.AddAspect<ValueObjectJsonAspect>();
        builder.Outbound.AddAspect<ValueObjectParsableAspect>();
        builder.Outbound.AddAspect<ValueObjectFormattableAspect>();
        builder.Outbound.AddAspect<ValueObjectComparableAspect>();
        builder.Outbound.AddAspect<ValueObjectMinMaxValueAspect>();
        builder.Outbound.AddAspect<ValueObjectTypeConverterAspect>();
        builder.Outbound.AddAspect<ValueObjectConvertibleAspect>();
        builder.Outbound.AddAspect<ValueObjectExtensionsAspect>();
    }

    /// <summary>
    /// The one marker, the kind and the wrapped type, or an error for a declaration that cannot be
    /// generated. Generating it anyway would fail later with a far less useful message.
    /// </summary>
    private static bool TryResolve(IAspectBuilder<INamedType> builder, string? wrappedValueObjectRefusal, out ValueObjectKind kind, [NotNullWhen(true)] out INamedType? valueType)
    {
        var target  = builder.Target;
        var markers = ValueObjectTypes.Markers(target);

        kind      = default;
        valueType = null;

        if (markers.Count != 1)
        {
            builder.Diagnostics.Report(MoreThanOneMarker.WithArguments((target, string.Join(", ", markers.Select(marker => marker.ToDisplayString())))));
            return false;
        }

        var marker = markers[0];
        kind = ValueObjectTypes.KindOf(marker);

        if (kind == ValueObjectKind.Validated && !marker.TypeArguments[0].Equals(target))
        {
            builder.Diagnostics.Report(MarkerNamesAnotherType.WithArguments((target, marker)));
            return false;
        }

        if (target.IsGeneric || target.DeclaringType is { IsGeneric: true })
        {
            builder.Diagnostics.Report(UnsupportedValueObject.WithArguments((target, "a value object cannot be generic or nested in a generic type")));
            return false;
        }

        // A record class that can be derived from is not generated: a derived record compares equal
        // only to its own type, which is not value equality, and a derived value object then failed
        // inside the generated code (LAMA0611), where the error names nothing the user wrote.
        if (target is { TypeKind: TypeKind.Class, IsSealed: false })
        {
            builder.Diagnostics.Report(MissingSealedKeyword.WithArguments(target));
            return false;
        }

        for (var baseType = target.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (!baseType.IsAbstract && ValueObjectTypes.Markers(baseType).Count > 0)
            {
                builder.Diagnostics.Report(UnsupportedValueObject.WithArguments((target, $"it derives from '{baseType.ToDisplayString()}', which is a value object itself")));
                return false;
            }
        }

        if (ValueObjectTypes.UnderlyingType(marker) is not INamedType namedValueType)
        {
            builder.Diagnostics.Report(UnsupportedValueObject.WithArguments((target, $"the wrapped type '{ValueObjectTypes.UnderlyingType(marker).ToDisplayString()}' is not a class, struct or enum")));
            return false;
        }

        // The markers' notnull constraint is only a warning (CS8714), so IValue<int?> reaches here,
        // and the generated JSON converter and parsing then fail to compile, which Metalama reports
        // as a bug in an aspect (LAMA0611/0612). Absence belongs to the value object, not to what
        // it wraps: an optional OrderId is an OrderId?, not an OrderId over a Guid?.
        if (namedValueType.IsNullable == true)
        {
            builder.Diagnostics.Report(UnsupportedValueObject.WithArguments((target,
                $"the wrapped type '{namedValueType.ToDisplayString()}' is nullable; wrap '{namedValueType.ToNonNullable().ToDisplayString()}' and declare the property or parameter as '{target.Name}?' where the value can be absent")));
            return false;
        }

        // Before the field is introduced: over a struct that wraps itself it has no layout (CS0523),
        // which Metalama reports as a bug in this aspect.
        if (wrappedValueObjectRefusal is not null)
        {
            builder.Diagnostics.Report(UnsupportedValueObject.WithArguments((target, wrappedValueObjectRefusal)));
            return false;
        }

        valueType = namedValueType;
        return true;
    }

    private static void HideDefaultStructConstructor(IAspectBuilder<INamedType> builder)
    {
        if (builder.Target.TypeKind != TypeKind.Struct) return;

        var constructor = builder.Target.Constructors.Single(c => c is { Accessibility: Accessibility.Public, Parameters.Count: 0 });
        builder.With(constructor).IntroduceAttribute(CodeAnnotations.DebuggerHidden);
        builder.With(constructor).IntroduceAttribute(CodeAnnotations.EditorNonBrowsable);
        builder.With(constructor).IntroduceAttribute(CodeAnnotations.DebuggerStepThrough);
    }

    /// <summary>
    /// <c>IValueObjectMaterializer&lt;TSelf, T&gt;</c>, implemented explicitly so the validation-free
    /// path is not on the type's public surface and can only be reached through a type parameter
    /// constrained to the interface.
    /// </summary>
    private static void ImplementMaterializer(IAspectBuilder<INamedType> builder, INamedType valueType, IConstructor privateConstructor)
    {
        var materializer = builder.ImplementInterface(
            TypeFactory.GetNamedType(typeof(IValueObjectMaterializer<,>)).MakeGenericInstance(builder.Target, valueType));

        materializer.ExplicitMembers.IntroduceMethod(
            nameof(MaterializeTemplate),
            IntroductionScope.Static,
            OverrideStrategy.Fail,
            method =>
            {
                method.Name               = nameof(IValueObjectMaterializer<,>.Materialize);
                method.ReturnType         = builder.Target;
                method.Parameters[0].Type = valueType;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { constructor = privateConstructor }
        );
    }

    private static IMethod IntroduceEntryPoint(IAspectBuilder<INamedType> builder, string template, string name, INamedType valueType, object args) =>
        builder.IntroduceMethod(
            template,
            IntroductionScope.Static,
            OverrideStrategy.Fail,
            method =>
            {
                method.Name               = name;
                method.Accessibility      = Accessibility.Private;
                method.Parameters[0].Type = valueType;

                if (method.Parameters.Any(parameter => parameter.Name == "result"))
                    method.Parameters["result"].Type = builder.Target;
                else
                    method.ReturnType = builder.Target;

                method.AddAttribute(CodeAnnotations.CompilerGenerated);
                method.AddAttribute(CodeAnnotations.EditorNonBrowsable);
                method.AddAttribute(CodeAnnotations.DebuggerStepThrough);
            },
            args: args
        ).Declaration;
}
