using System.Text.Json;
using System.Text.Json.Serialization;
using Metalama.Framework.Advising;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;
using Metalama.Framework.Code.SyntaxBuilders;
using Metalama.Framework.Diagnostics;

namespace Spike.Generators;

[CompileTime]
internal sealed class ClosedSetArguments
{
    public required INamedType           Set     { get; init; }
    public required IReadOnlyList<IField> Members { get; init; }
}

internal sealed class ClosedSetAspect : TypeAspect
{
    private static readonly DiagnosticDefinition<INamedType> NotPartial        = new("SPIKE001", Severity.Error, "'{0}' must be partial: the generators add its members.");
    private static readonly DiagnosticDefinition<INamedType> NotSealed         = new("SPIKE002", Severity.Error, "'{0}' must be sealed: a closed set has no subtypes.");
    private static readonly DiagnosticDefinition<INamedType> NoMembers         = new("SPIKE003", Severity.Error, "'{0}' declares no member: a public static readonly field of its own type.");
    private static readonly DiagnosticDefinition<INamedType> PublicConstructor = new("SPIKE004", Severity.Error, "'{0}' has a public constructor, so instances beside its members can be made.");
    private static readonly DiagnosticDefinition<(INamedType, string)> MemberIsNotAField = new("SPIKE005", Severity.Error, "'{0}.{1}' is a public static member of the set's type but not a readonly field; a member is a public static readonly field.");

#pragma warning disable CS0649, CS8618
    [Template] public string _name;
    [Template] public static IReadOnlyList<object> _all;
#pragma warning restore CS0649, CS8618

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        var target = builder.Target;
        var errors = false;

        void Report<T>(DiagnosticDefinition<T> definition, T args) { builder.Diagnostics.Report(definition.WithArguments(args)); errors = true; }

        if (!target.IsPartial) Report(NotPartial, target);
        if (!target.IsSealed) Report(NotSealed, target);

        foreach (var constructor in target.Constructors.Where(c => !c.IsImplicitlyDeclared && c.Accessibility == Accessibility.Public))
            Report(PublicConstructor, target);

        var members = target.Fields
                            .Where(f => f.IsStatic && f.Accessibility == Accessibility.Public && f.Type.Equals(target))
                            .ToList();

        foreach (var field in members.Where(f => f.Writeability != Writeability.ConstructorOnly))
            Report(MemberIsNotAField, (target, field.Name));
        foreach (var property in target.Properties.Where(p => p.IsStatic && p.Accessibility == Accessibility.Public && p.Type.Equals(target)))
            Report(MemberIsNotAField, (target, property.Name));

        if (members.Count == 0) Report(NoMembers, target);
        if (errors) { builder.SkipAspect(); return; }

        var args = new ClosedSetArguments { Set = target, Members = members };
        builder.Tags = args;

        // A set that declares no constructor has the implicit public one; a private parameterless one
        // replaces it, so that `= new()` in a member keeps compiling and nothing outside can.
        if (!target.Constructors.Any(c => !c.IsImplicitlyDeclared))
            builder.IntroduceConstructor(nameof(PrivateConstructorTemplate), buildConstructor: c => c.Accessibility = Accessibility.Private);

        builder.IntroduceField(nameof(_name), IntroductionScope.Instance, OverrideStrategy.Fail, f => f.Accessibility = Accessibility.Private);
        builder.IntroduceProperty(nameof(NameTemplate), buildProperty: p => { p.Name = "Name"; p.Accessibility = Accessibility.Public; });
        builder.IntroduceProperty(nameof(QualifiedNameTemplate), buildProperty: p => { p.Name = "QualifiedName"; p.Accessibility = Accessibility.Public; });
        builder.IntroduceMethod(nameof(ToStringTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => m.Name = "ToString");

        // The names: assigned once, after the static field initializers, before any static constructor body.
        builder.AddInitializer(nameof(NamesInitializerTemplate), InitializerKind.BeforeTypeConstructor);

        // All, in declaration order, as a static field initialized from the members.
        var listType = ((INamedType)TypeFactory.GetType(typeof(IReadOnlyList<>))).WithTypeArguments(target);
        builder.IntroduceField(nameof(_all), IntroductionScope.Static, OverrideStrategy.Fail, f =>
        {
            f.Accessibility = Accessibility.Private;
            f.Type = listType;
            f.Writeability = Writeability.ConstructorOnly;
            f.InitializerExpression = ExpressionFactory.Parse($"new global::{target.FullName}[] {{ {string.Join(", ", members.Select(m => $"global::{target.FullName}.{m.Name}"))} }}");
        });
        builder.IntroduceProperty(nameof(AllTemplate), buildProperty: p => { p.Name = "All"; p.Type = listType; p.Accessibility = Accessibility.Public; p.IsStatic = true; });

        // TryParse and Parse by name.
        builder.IntroduceMethod(nameof(TryParseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "TryParse";
            m.Accessibility = Accessibility.Public;
            m.Parameters[1].Type = target.ToNullable();
        });
        builder.IntroduceMethod(nameof(ParseTemplate), IntroductionScope.Static, OverrideStrategy.Fail, m =>
        {
            m.Name = "Parse";
            m.Accessibility = Accessibility.Public;
            m.ReturnType = target;
        });

        // Match: one required delegate per member, named after it.
        // The template is generic itself, so TResult is a real type parameter; the parameters, one per
        // member, are Func<TResult> built from it.
        // Func<TResult> cannot be built from the builder's own type parameter (Metalama 2026.1 asserts:
        // "the reference to ITypeSymbol being null is not supported yet"), so the template declares one
        // such parameter, whose bound type the other members' parameters copy; the first is renamed.
        builder.IntroduceMethod(nameof(MatchTemplate), IntroductionScope.Instance, OverrideStrategy.Fail, m =>
        {
            m.Name = "Match";
            m.Accessibility = Accessibility.Public;
            var func = m.Parameters[0].Type;
            m.Parameters[0].Name = Camel(members[0].Name);
            foreach (var member in members.Skip(1)) m.AddParameter(Camel(member.Name), func);
        });

        // JSON: the name as a string, read back by name; an unknown name refused without quoting it.
        var converter = builder.IntroduceClass($"{target.Name}JsonConverter", buildType: t =>
        {
            t.Accessibility = Accessibility.Public;
            t.BaseType = ((INamedType)TypeFactory.GetType(typeof(JsonConverter<>))).WithTypeArguments(target);
            t.IsStatic = false;
        });
        converter.IntroduceMethod(nameof(JsonReadTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = "Read"; m.ReturnType = target; });
        converter.IntroduceMethod(nameof(JsonWriteTemplate), IntroductionScope.Instance, OverrideStrategy.Override, m => { m.Name = "Write"; m.Parameters[1].Type = target; });
        builder.IntroduceAttribute(AttributeConstruction.Create(typeof(JsonConverterAttribute), constructorArguments: [converter.Target]));
    }

    [CompileTime]
    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    [Template] public void PrivateConstructorTemplate() { }

    [Template] public string NameTemplate { get => meta.This._name; }

    [Template] public string QualifiedNameTemplate { get => meta.Target.Type.Name + "." + meta.This._name; }

    [Template] public string ToStringTemplate() => meta.This._name;

    [Template]
    public static void NamesInitializerTemplate()
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var member in args.Members)
        {
            member.Value!._name = member.Name;
        }
    }

    [Template] public static dynamic? AllTemplate { get => meta.ThisType._all; }

    [Template]
    public static bool TryParseTemplate(string name, out dynamic? member)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var candidate in args.Members)
        {
            if (name == candidate.Name)
            {
                member = candidate.Value;
                return true;
            }
        }

        member = null;
        return false;
    }

    [Template]
    public static dynamic? ParseTemplate(string name)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var candidate in args.Members)
        {
            if (name == candidate.Name) return candidate.Value;
        }

        throw new FormatException($"{args.Set.Name} has no member of that name.");
    }

    [Template]
    public TResult MatchTemplate<TResult>(Func<TResult> first)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        var index = meta.CompileTime(0);
        foreach (var member in args.Members)
        {
            if (ReferenceEquals(meta.This, member.Value)) return meta.Target.Parameters[index].Value!.Invoke();
            index++;
        }

        throw new InvalidOperationException($"{args.Set.Name}: not a member.");
    }

    [Template]
    public dynamic? JsonReadTemplate(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        var name = reader.GetString();
        if (name is not null)
        {
            foreach (var candidate in args.Members)
            {
                if (name == candidate.Name) return candidate.Value;
            }
        }

        throw new JsonException($"{args.Set.Name} has no member of that name.");
    }

    [Template]
    public void JsonWriteTemplate(Utf8JsonWriter writer, dynamic? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue((string)value!.Name);
    }
}
