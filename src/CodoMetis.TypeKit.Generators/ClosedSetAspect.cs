using System.ComponentModel;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.CompilerServices;
using Metalama.Framework.Advising;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;
using Metalama.Framework.Code.SyntaxBuilders;
using Metalama.Framework.Diagnostics;
using static CodoMetis.TypeKit.Generators.AspectDiagnostics;

namespace CodoMetis.TypeKit.Generators;

/// <summary>What the templates read: the set and its members.</summary>
[CompileTime]
internal sealed class ClosedSetArguments
{
    public required INamedType            Set     { get; init; }
    public required IReadOnlyList<IField> Members { get; init; }
}

/// <summary>
/// Generates a closed set: <c>Name</c>, <c>QualifiedName</c>, <c>All</c>, <c>Match</c>, <c>TryParse</c>
/// and <c>Parse</c>, each ordinal and each with an <c>ignoreCase</c> overload, <c>IParsable&lt;TSelf&gt;</c> explicitly, <c>ToString()</c> where none is written by
/// hand, the JSON converter, the type converter, the discovery attribute, and a private constructor
/// where the type declares none. Applied by <see cref="ValueObjectFabric"/>.
/// </summary>
/// <remarks>
/// <para>
/// A member's name is assigned by an initializer that runs after the static field initializers and
/// before any static constructor body, so the declaration needs no <c>nameof</c>. Each member is its
/// own instance (CMTK1019), so no name overwrites another; an instance without one is refused where its
/// name is read. <c>Match</c>'s
/// parameters are added per member; the template declares one <c>Func&lt;TResult&gt;</c> parameter,
/// whose bound type the others copy, since Metalama 2026.1 cannot build a generic instance over the
/// method builder's own type parameter (spikes/ClosedSets).
/// </para>
/// </remarks>
internal sealed partial class ClosedSetAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        var target = builder.Target;
        var members = ClosedSetTypes.Members(target);
        var problems = new List<IDiagnostic>();

        if (!target.IsPartial) problems.Add(ClosedSetNotPartial.WithArguments(target));
        if (!target.IsSealed) problems.Add(ClosedSetNotSealed.WithArguments(target));
        if (target.IsRecord) problems.Add(ClosedSetRecord.WithArguments(target));
        if (target.IsGeneric || target.DeclaringType is { IsGeneric: true }) problems.Add(ClosedSetGeneric.WithArguments(target));
        if (target.Constructors.Any(c => !c.IsImplicitlyDeclared && c.Accessibility != Accessibility.Private)) problems.Add(ClosedSetPublicConstructor.WithArguments(target));
        if (members.Count == 0) problems.Add(ClosedSetNoMembers.WithArguments(target));

        foreach (var member in members.Where(member => !ClosedSetTypes.IsCreatedWhereDeclared(member, target)))
            problems.Add(ClosedSetMemberNotCreated.WithArguments((target, member.Name)));

        // Any static field or property of the set's own type that is not a public readonly field: a
        // writable one may be replaced, and a non-public one is an instance that is no member, whose
        // name throws and which TryParse never finds, while the declaration reads as a member.
        foreach (var field in target.Fields.Where(f => f.IsStatic && f.Type.Equals(target) && (f.Accessibility != Accessibility.Public || f.Writeability != Writeability.ConstructorOnly)))
            problems.Add(ClosedSetMemberNotAField.WithArguments((target, field.Name, field.Accessibility != Accessibility.Public ? "a static field that is not public" : "a static field that is not readonly")));
        foreach (var property in target.Properties.Where(p => p.IsStatic && p.Type.Equals(target)))
            problems.Add(ClosedSetMemberNotAField.WithArguments((target, property.Name, "a static property")));

        // A member named as a generated member failed the aspect (LAMA0506 beside Match, Parse and
        // TryParse, LAMA0611 beside ToString). Two members whose names differ only in case are one
        // name to TryParse(name, ignoreCase: true), and where the difference is the first letter's
        // they gave Match one parameter name twice (LAMA0611, CS0100).
        var generatedNames = ClosedSetTypes.GeneratedNames(target);

        // A set named as a generated member failed inside the generated code (LAMA0611, CS0542: a
        // member cannot have the name of its type), naming nothing the user wrote.
        if (generatedNames.Contains(target.Name)) problems.Add(ClosedSetNamedAsGeneratedMember.WithArguments(target));

        foreach (var member in members.Where(member => generatedNames.Contains(member.Name)))
            problems.Add(ClosedSetMemberNameTaken.WithArguments((target, member.Name, "has the name of a member the generators introduce")));
        foreach (var shared in members.GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var member in shared.Skip(1))
                problems.Add(ClosedSetMemberNameTaken.WithArguments((target, member.Name, $"differs from '{target.Name}.{shared.First().Name}' only in case, which Match's parameters and TryParse(name, ignoreCase: true) cannot tell apart")));
        }

        // A hand-written member the generators introduce failed the aspect with a Metalama error that
        // names no fix; a hand-written ToString() is the seam and is kept instead.
        var declaredByHand = ClosedSetTypes.GeneratedMembersDeclaredByHand(target, members);
        if (declaredByHand.Count > 0)
            problems.Add(HandWrittenGeneratedMember.WithArguments((target, string.Join(", ", declaredByHand),
                (declaredByHand.Count == 1 ? "the generators introduce it themselves; remove it" : "the generators introduce these themselves; remove them")
              + ". The generated member a closed set may declare instead is ToString()")));

        if (problems.Count > 0)
        {
            foreach (var problem in problems) builder.Diagnostics.Report(problem);
            builder.SkipAspect();
            return;
        }

        builder.Tags = new ClosedSetArguments { Set = target, Members = members };

        if (!target.Constructors.Any(c => !c.IsImplicitlyDeclared))
        {
            builder.IntroduceConstructor(nameof(PrivateConstructorTemplate), buildConstructor: c =>
            {
                c.Accessibility = Accessibility.Private;
                c.AddAttribute(CodeAnnotations.CompilerGenerated);
            });
        }

        // Assigned by the initializer below, after every member exists; null! keeps the consumer's
        // nullable analysis quiet about the constructor.
        builder.IntroduceField(nameof(_name), IntroductionScope.Instance, OverrideStrategy.Fail, f =>
        {
            f.Accessibility = Accessibility.Private;
            f.InitializerExpression = ExpressionFactory.Parse("null!");
            f.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
        builder.IntroduceProperty(nameof(NameTemplate), buildProperty: p => { p.Name = "Name"; p.Accessibility = Accessibility.Public; p.AddAttribute(CodeAnnotations.CompilerGenerated); });
        builder.IntroduceProperty(nameof(QualifiedNameTemplate), buildProperty: p => { p.Name = "QualifiedName"; p.Accessibility = Accessibility.Public; p.AddAttribute(CodeAnnotations.CompilerGenerated); });
        if (!ClosedSetTypes.DeclaresToString(target))
            builder.IntroduceMethod(nameof(ToStringTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = "ToString"; m.AddAttribute(CodeAnnotations.CompilerGenerated); });

        builder.AddInitializer(nameof(NamesInitializerTemplate), InitializerKind.BeforeTypeConstructor);

        // Assigned by the initializer below, with the names: as a field initializer it ran with the
        // part it was introduced into, before the fields of a part in another file, which were null in
        // it. The array wrapped, never the array itself: cast to IList<T> or T[], it was writable, and
        // a member replaced there was listed while Parse, which reads the fields, answered the original.
        var listType = TypeFactory.GetNamedType(typeof(IReadOnlyList<>)).WithTypeArguments(target);
        builder.IntroduceField(nameof(_all), IntroductionScope.Static, OverrideStrategy.Fail, f =>
        {
            f.Accessibility = Accessibility.Private;
            f.Type = listType;
            f.Writeability = Writeability.ConstructorOnly;
            f.InitializerExpression = ExpressionFactory.Parse("null!");
            f.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
        builder.IntroduceProperty(nameof(AllTemplate), buildProperty: p =>
        {
            p.Name = "All";
            p.Type = listType;
            p.Accessibility = Accessibility.Public;
            p.IsStatic = true;
            p.AddAttribute(CodeAnnotations.CompilerGenerated);
        });

        builder.IntroduceMethod(nameof(TryParseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "TryParse";
            m.Accessibility = Accessibility.Public;
            m.Parameters[1].Type = target.ToNullable();
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
        builder.IntroduceMethod(nameof(ParseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "Parse";
            m.Accessibility = Accessibility.Public;
            m.ReturnType = target;
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
        });

        // The same two with ignoreCase, opted into at the call: JSON, the column, binding and
        // IParsable stay ordinal, since what is stored or sent is the name as it is.
        builder.IntroduceMethod(nameof(TryParseIgnoreCaseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "TryParse";
            m.Accessibility = Accessibility.Public;
            m.Parameters[2].Type = target.ToNullable();
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
        builder.IntroduceMethod(nameof(ParseIgnoreCaseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "Parse";
            m.Accessibility = Accessibility.Public;
            m.ReturnType = target;
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
        });

        // IParsable<TSelf>, explicitly: Parse(string) and TryParse(string, out) are the set's own
        // surface, and the provider means nothing to a name. Generic code calling T.Parse reaches it.
        var parsable = builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IParsable<>)).WithTypeArguments(target));
        parsable.ExplicitMembers.IntroduceMethod(nameof(ParsableParseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "Parse";
            m.ReturnType = target;
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
        parsable.ExplicitMembers.IntroduceMethod(nameof(ParsableTryParseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "TryParse";
            m.Parameters[2].Type = target;
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
        });

        builder.IntroduceMethod(nameof(MatchTemplate), IntroductionScope.Instance, OverrideStrategy.Fail, m =>
        {
            m.Name = "Match";
            m.Accessibility = Accessibility.Public;
            m.AddAttribute(CodeAnnotations.CompilerGenerated);
            var func = m.Parameters[0].Type;
            m.Parameters[0].Name = ClosedSetTypes.ParameterName(members[0]);
            foreach (var member in members.Skip(1)) m.AddParameter(ClosedSetTypes.ParameterName(member), func);
        });

        var converter = builder.IntroduceClass($"{target.Name}JsonConverter", buildType: t =>
        {
            t.Accessibility = Accessibility.Public;
            t.BaseType = TypeFactory.GetNamedType(typeof(JsonConverter<>)).WithTypeArguments(target);
            t.IsStatic = false;
            t.IsSealed = true;
            t.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
        converter.IntroduceMethod(nameof(JsonReadTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = "Read"; m.ReturnType = target; m.AddAttribute(CodeAnnotations.CompilerGenerated); });
        converter.IntroduceMethod(nameof(JsonWriteTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = "Write"; m.Parameters[1].Type = target; m.AddAttribute(CodeAnnotations.CompilerGenerated); });
        converter.IntroduceMethod(nameof(JsonReadAsPropertyNameTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = "ReadAsPropertyName"; m.ReturnType = target; m.AddAttribute(CodeAnnotations.CompilerGenerated); });
        converter.IntroduceMethod(nameof(JsonWriteAsPropertyNameTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = "WriteAsPropertyName"; m.Parameters[1].Type = target; m.AddAttribute(CodeAnnotations.CompilerGenerated); });

        // The reflection-based configuration binder, MVC and anything else that reads a string through
        // TypeDescriptor find a set by its type converter alone; without one a set bound to null.
        var typeConverter = builder.IntroduceClass($"{target.Name}TypeConverter", buildType: t =>
        {
            t.Accessibility = Accessibility.Public;
            t.BaseType = TypeFactory.GetNamedType(typeof(TypeConverter));
            t.IsStatic = false;
            t.IsSealed = true;
            t.AddAttribute(CodeAnnotations.CompilerGenerated);
        });
        typeConverter.IntroduceMethod(nameof(CanConvertFromTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = nameof(TypeConverter.CanConvertFrom); m.AddAttribute(CodeAnnotations.CompilerGenerated); });
        typeConverter.IntroduceMethod(nameof(CanConvertToTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = nameof(TypeConverter.CanConvertTo); m.AddAttribute(CodeAnnotations.CompilerGenerated); });
        typeConverter.IntroduceMethod(nameof(ConvertFromTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = nameof(TypeConverter.ConvertFrom); m.AddAttribute(CodeAnnotations.CompilerGenerated); });
        typeConverter.IntroduceMethod(nameof(ConvertToTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = nameof(TypeConverter.ConvertTo); m.AddAttribute(CodeAnnotations.CompilerGenerated); });

        builder.IntroduceAttribute(AttributeConstruction.Create(typeof(JsonConverterAttribute), constructorArguments: [converter.Target]));
        builder.IntroduceAttribute(AttributeConstruction.Create(typeof(TypeConverterAttribute), constructorArguments: [typeConverter.Target]));
        builder.IntroduceAttribute(AttributeConstruction.Create(TypeFactory.GetNamedType(typeof(GeneratedClosedSetAttribute<>)).WithTypeArguments(target)));
        builder.IntroduceAttribute(CodeAnnotations.CompilerGenerated);
    }
}
