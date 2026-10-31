using System.Numerics;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using static CodoMetis.TypeKit.Generators.GeneratedParameter;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// The named members the generators introduce into a value object or a closed set (decision 128). Each aspect
/// introduces a member through the entry made here, which names it, checks its template's signature and marks
/// it; the check of a hand-written member reads the same entries, under the conditions the aspects decide by.
/// What the generators introduce and what the check looks for cannot drift apart, as two name lists kept in step
/// by hand did.
/// </summary>
/// <remarks>
/// <para>
/// Not here: what no check finds by its name. An explicit implementation (the materializer, <c>IConvertible</c>,
/// a closed set's <c>IParsable&lt;TSelf&gt;</c>) is found by the interface mapping, an operator by its kind, a
/// constructor by CMTK1009, and the companion class by CMTK1007; each is marked by
/// <see cref="CodeAnnotations.GeneratedMember"/> where it is introduced. Neither are the members a record
/// synthesizes and the generators replace (<c>Equals</c>, <c>GetHashCode</c>), which the equality checks answer.
/// </para>
/// <para>
/// Every entry is made where it is used, never kept in a static field: its types belong to one compilation
/// (<c>CompileTimeStaticStateTests</c>).
/// </para>
/// </remarks>
[CompileTime]
internal static class GeneratedMembers
{
    // ValueObjectAspect.

    public static GeneratedMember ValueField(INamedType target, IType valueType) =>
        new("_value", GeneratedMemberKind.Field, GeneratedMemberRole.Refused, ValueObject(target, valueType), nameof(IValueObject<,>.Value));

    /// <summary><c>__FromJson(T value, bool materialize)</c>: the JSON converter's way in.</summary>
    public static GeneratedMember FromJson(INamedType target, IType valueType, INamedType marker) =>
        Method("__FromJson", Construction(target, valueType, marker), [Of(valueType), Of(TypeFactory.GetType(SpecialType.Boolean))]);

    /// <summary><c>__FromText(T value)</c>: parsing's and the type converter's way in.</summary>
    public static GeneratedMember FromText(INamedType target, IType valueType, INamedType marker) =>
        Method("__FromText", Construction(target, valueType, marker), [Of(valueType)]);

    /// <summary><c>__TryFromText(T value, out TSelf result)</c>: <c>TryParse</c>'s way in.</summary>
    public static GeneratedMember TryFromText(INamedType target, IType valueType, INamedType marker) =>
        Method("__TryFromText", Construction(target, valueType, marker), [Of(valueType), Out(target)]);

    // ValueObjectContractAspect.

    public static GeneratedMember Value(INamedType target, IType valueType) =>
        new(nameof(IValueObject<,>.Value), GeneratedMemberKind.Property, GeneratedMemberRole.Refused, ValueObject(target, valueType), nameof(IValueObject<,>.Value));

    public static GeneratedMember From(INamedType target, IType valueType) =>
        Method(nameof(IPlainValueObject<,>.From), PlainValueObject(target, valueType), [Of(valueType)], contractMember: nameof(IPlainValueObject<,>.From));

    public static GeneratedMember TryFrom(IType valueType, INamedType marker) =>
        Method("TryFrom", marker, [Of(valueType)], GeneratedMemberRole.Seam);

    /// <summary><c>FromKnownGood(T value, string? source)</c>, whose second parameter the compiler fills with the caller's expression.</summary>
    public static GeneratedMember FromKnownGood(IType valueType, INamedType marker) =>
        Method("FromKnownGood", marker, [Of(valueType), Of(TypeFactory.GetType(SpecialType.String))], GeneratedMemberRole.Seam);

    public static GeneratedMember Revalidate(INamedType marker) =>
        Method("Revalidate", marker, [], GeneratedMemberRole.Seam);

    /// <summary><c>Equals(TSelf?)</c> over the wrapped value, for a record class with a base, which the record synthesizes otherwise.</summary>
    public static GeneratedMember RecordEquals(INamedType target) =>
        Method(nameof(Equals), Equatable(target), [Of(target)], GeneratedMemberRole.Elsewhere, clashesByName: false);

    public static GeneratedMember RecordGetHashCode(INamedType target) =>
        Method(nameof(GetHashCode), Equatable(target), [], GeneratedMemberRole.Elsewhere, clashesByName: false);

    /// <summary><c>new Equals(TBase?)</c>, which takes a deeper base's <c>IEquatable&lt;TBase&gt;</c> over (decision 87).</summary>
    public static GeneratedMember ReimplementedEquals(INamedType hidden) =>
        Method(nameof(Equals), Equatable(hidden), [Of(hidden)], GeneratedMemberRole.Elsewhere, clashesByName: false);

    // ValueObjectParsableAspect and ValueObjectTypeConverterAspect.

    /// <summary><c>Parse(text, IFormatProvider?)</c>, where <paramref name="text"/> is <c>string</c>, <c>ReadOnlySpan&lt;char&gt;</c> or <c>ReadOnlySpan&lt;byte&gt;</c>.</summary>
    public static GeneratedMember Parse(INamedType target, IType text) =>
        Method(nameof(IParsable<>.Parse), Parsable(target, text), [Of(text), Of(Provider)]);

    public static GeneratedMember TryParse(INamedType target, IType text) =>
        Method(nameof(IParsable<>.TryParse), Parsable(target, text), [Of(text), Of(Provider), Out(target)]);

    public static GeneratedMember TypeConverter(INamedType target, INamedType marker) =>
        new($"{target.Name}TypeConverter", GeneratedMemberKind.NestedType, GeneratedMemberRole.Refused, marker, $"{target.Name}TypeConverter");

    // ValueObjectJsonAspect.

    public static GeneratedMember JsonConverter(INamedType target, INamedType marker) =>
        new($"{target.Name}JsonConverter", GeneratedMemberKind.NestedType, GeneratedMemberRole.Refused, marker, $"{target.Name}JsonConverter");

    // ValueObjectFormattableAspect.

    public static GeneratedMember FormattableToString() =>
        Method(nameof(IFormattable.ToString), TypeFactory.GetNamedType(typeof(IFormattable)), [Of(TypeFactory.GetType(SpecialType.String)), Of(Provider)], clashesByName: false);

    /// <summary>The override of the record's <c>ToString()</c>, the seam.</summary>
    public static GeneratedMember ToStringOverride(INamedType target, IType valueType) =>
        Method(nameof(ToString), ValueObject(target, valueType), [], GeneratedMemberRole.Seam, clashesByName: false);

    public static GeneratedMember TryFormat() =>
        Method(nameof(ISpanFormattable.TryFormat), TypeFactory.GetNamedType(typeof(ISpanFormattable)),
               [Of(TypeFactory.GetType(typeof(Span<char>))), Out(TypeFactory.GetType(SpecialType.Int32)), Of(TypeFactory.GetType(typeof(ReadOnlySpan<char>))), Of(Provider)]);

    public static GeneratedMember TryFormatUtf8() =>
        Method(nameof(IUtf8SpanFormattable.TryFormat), TypeFactory.GetNamedType(typeof(IUtf8SpanFormattable)),
               [Of(TypeFactory.GetType(typeof(Span<byte>))), Out(TypeFactory.GetType(SpecialType.Int32)), Of(TypeFactory.GetType(typeof(ReadOnlySpan<char>))), Of(Provider)]);

    // ValueObjectMinMaxValueAspect.

    public static GeneratedMember MinValue(INamedType target) =>
        new(nameof(IMinMaxValue<>.MinValue), GeneratedMemberKind.Property, GeneratedMemberRole.Refused, MinMaxValue(target), nameof(IMinMaxValue<>.MinValue));

    public static GeneratedMember MaxValue(INamedType target) =>
        new(nameof(IMinMaxValue<>.MaxValue), GeneratedMemberKind.Property, GeneratedMemberRole.Refused, MinMaxValue(target), nameof(IMinMaxValue<>.MaxValue));

    // ValueObjectComparableAspect.

    /// <summary><c>CompareTo(TSelf)</c>, the comparison seam.</summary>
    public static GeneratedMember CompareTo(INamedType target) =>
        Method(nameof(IComparable<>.CompareTo), TypeFactory.GetNamedType(typeof(IComparable<>)).WithTypeArguments(target), [Of(target)], GeneratedMemberRole.Seam);

    /// <summary><c>CompareTo(object?)</c>; a hand-written one is CMTK1008.</summary>
    public static GeneratedMember ObjectCompareTo() =>
        Method(nameof(IComparable.CompareTo), TypeFactory.GetNamedType(typeof(IComparable)), [Of(TypeFactory.GetType(SpecialType.Object))], GeneratedMemberRole.Elsewhere);

    /// <summary>
    /// The named members the generators introduce into <paramref name="target"/>, under the conditions the aspects
    /// introduce them by: each aspect's own predicate, called here as there.
    /// </summary>
    public static IReadOnlyList<GeneratedMember> OfValueObject(INamedType target, INamedType valueType, ValueObjectKind kind) =>
        Catalogue(target, valueType, ValueObjectTypes.Markers(target)[0], kind, everyCondition: false);

    /// <summary>
    /// The names of the members the generators introduce into a value object for some marker or wrapped type,
    /// which a value object cannot have (CS0542). Not those <see cref="object"/> declares, <c>ToString</c> and
    /// <c>Equals</c>, which a record synthesizes, so that a value object of that name is C#'s own error.
    /// </summary>
    public static IReadOnlyList<string> NamesInAnyValueObject(INamedType target, INamedType marker)
    {
        var objectMembers = new HashSet<string>(TypeFactory.GetNamedType(typeof(object)).Methods.Select(method => method.Name));
        var valueType     = ValueObjectTypes.WrappedType(marker);

        return
        [
            .. new[] { ValueObjectKind.Plain, ValueObjectKind.Validated }
               .SelectMany(kind => Catalogue(target, valueType, marker, kind, everyCondition: true))
               .Select(member => member.Name)
               .Where(name => !objectMembers.Contains(name))
               .Distinct()
        ];
    }

    private static IReadOnlyList<GeneratedMember> Catalogue(INamedType target, IType valueType, INamedType marker, ValueObjectKind kind, bool everyCondition)
    {
        var named = valueType as INamedType;

        bool When(Func<INamedType, bool> condition) => everyCondition || (named is not null && condition(named));

        List<GeneratedMember> members = [ValueField(target, valueType), FromJson(target, valueType, marker), FromText(target, valueType, marker), TryFromText(target, valueType, marker), Value(target, valueType), JsonConverter(target, marker)];

        if (kind == ValueObjectKind.Plain)
            members.Add(From(target, valueType));
        else
            members.AddRange([TryFrom(valueType, marker), FromKnownGood(valueType, marker), Revalidate(marker)]);

        if (When(type => ValueObjectParsableAspect.ResolveStrategy(type) != ValueParseStrategy.Unsupported))
        {
            members.AddRange([Parse(target, Text), TryParse(target, Text), TypeConverter(target, marker)]);

            if (When(ValueObjectParsableAspect.SupportsSpan))
                members.AddRange([Parse(target, Span), TryParse(target, Span)]);

            if (When(ValueObjectParsableAspect.SupportsUtf8))
                members.AddRange([Parse(target, Utf8), TryParse(target, Utf8)]);
        }

        if (everyCondition || !ValueObjectDeclaration.DeclaresToString(target))
        {
            members.AddRange([FormattableToString(), ToStringOverride(target, valueType)]);

            if (When(type => ValueObjectFormattableAspect.ResolveStrategy(type) == ValueFormatStrategy.SpanFormattable))
                members.Add(TryFormat());

            if (When(ValueObjectFormattableAspect.SupportsUtf8))
                members.Add(TryFormatUtf8());
        }

        if (kind == ValueObjectKind.Plain && When(ValueObjectMinMaxValueAspect.HasMinMaxValue))
            members.AddRange([MinValue(target), MaxValue(target)]);

        if (When(type => ValueObjectComparableAspect.ResolveStrategy(type) != ValueCompareStrategy.Unsupported))
            members.AddRange([CompareTo(target), ObjectCompareTo()]);

        return members;
    }

    // ClosedSetAspect.

    public static GeneratedMember NameField(INamedType set) =>
        new("_name", GeneratedMemberKind.Field, GeneratedMemberRole.Refused, ClosedSet(set), nameof(IClosedSet<>.Name));

    public static GeneratedMember Name(INamedType set) =>
        new(nameof(IClosedSet<>.Name), GeneratedMemberKind.Property, GeneratedMemberRole.Refused, ClosedSet(set), nameof(IClosedSet<>.Name));

    public static GeneratedMember QualifiedName(INamedType set) =>
        new(nameof(IClosedSet<>.QualifiedName), GeneratedMemberKind.Property, GeneratedMemberRole.Refused, ClosedSet(set), nameof(IClosedSet<>.QualifiedName));

    public static GeneratedMember AllField(INamedType set) =>
        new("_all", GeneratedMemberKind.Field, GeneratedMemberRole.Refused, ClosedSet(set), nameof(IClosedSet<>.All));

    public static GeneratedMember All(INamedType set) =>
        new(nameof(IClosedSet<>.All), GeneratedMemberKind.Property, GeneratedMemberRole.Refused, ClosedSet(set), nameof(IClosedSet<>.All));

    /// <summary><c>TryParse(string?, out TSelf)</c>, or with <c>bool ignoreCase</c> before the result.</summary>
    public static GeneratedMember SetTryParse(INamedType set, bool ignoreCase) =>
        Method(nameof(IClosedSet<>.TryParse), ClosedSet(set), ignoreCase ? [Of(Text), Of(Boolean), Out(set)] : [Of(Text), Out(set)]);

    /// <summary><c>Parse(string)</c>, or <c>Parse(string, bool ignoreCase)</c>.</summary>
    public static GeneratedMember SetParse(INamedType set, bool ignoreCase) =>
        Method("Parse", ClosedSet(set), ignoreCase ? [Of(Text), Of(Boolean)] : [Of(Text)]);

    /// <summary><c>Match&lt;TResult&gt;</c>: one <c>Func&lt;TResult&gt;</c> of its own type parameter per member.</summary>
    public static GeneratedMember Match(INamedType set, int memberCount) =>
        Method("Match", ClosedSet(set), [.. Enumerable.Repeat(Matching(IsFuncOfOwnTypeParameter), memberCount)], arity: 1);

    /// <summary>The set's <c>ToString()</c>, the seam.</summary>
    public static GeneratedMember SetToString(INamedType set) =>
        Method(nameof(ToString), ClosedSet(set), [], GeneratedMemberRole.Seam);

    public static GeneratedMember SetJsonConverter(INamedType set) =>
        new($"{set.Name}JsonConverter", GeneratedMemberKind.NestedType, GeneratedMemberRole.Refused, ClosedSet(set), $"{set.Name}JsonConverter");

    public static GeneratedMember SetTypeConverter(INamedType set) =>
        new($"{set.Name}TypeConverter", GeneratedMemberKind.NestedType, GeneratedMemberRole.Refused, ClosedSet(set), $"{set.Name}TypeConverter");

    /// <summary>
    /// The named members <see cref="ClosedSetAspect"/> introduces into <paramref name="set"/> of
    /// <paramref name="memberCount"/> members: <c>ToString()</c> unless the set declares it, when
    /// <paramref name="everyCondition"/> is false.
    /// </summary>
    public static IReadOnlyList<GeneratedMember> OfClosedSet(INamedType set, int memberCount, bool everyCondition = false)
    {
        List<GeneratedMember> members =
        [
            NameField(set), Name(set), QualifiedName(set), AllField(set), All(set),
            SetTryParse(set, ignoreCase: false), SetParse(set, ignoreCase: false), SetTryParse(set, ignoreCase: true), SetParse(set, ignoreCase: true),
            Match(set, memberCount), SetJsonConverter(set), SetTypeConverter(set)
        ];

        if (everyCondition || !ClosedSetTypes.DeclaresToString(set))
            members.Add(SetToString(set));

        return members;
    }

    /// <remarks>
    /// The method's type parameter is read by its kind and position: in the method the aspect builds, the
    /// parameters' <c>Func&lt;TResult&gt;</c> is bound to the template's <c>TResult</c>, which Metalama 2026.1 copies
    /// rather than rebinding to the builder's own (spikes/ClosedSets).
    /// </remarks>
    private static bool IsFuncOfOwnTypeParameter(IType type, IMethod method) =>
        type is INamedType { IsGeneric: true } func
     && func.Definition.Equals(TypeFactory.GetNamedType(typeof(Func<>)))
     && method.TypeParameters.Count == 1
     && func.TypeArguments[0] is ITypeParameter { TypeParameterKind: TypeParameterKind.Method, Index: 0 };

    private static GeneratedMember Method(string name, INamedType contract, IReadOnlyList<GeneratedParameter> parameters, GeneratedMemberRole role = GeneratedMemberRole.Refused, string? contractMember = null, int arity = 0, bool clashesByName = true) =>
        new(name, GeneratedMemberKind.Method, role, contract, contractMember ?? name, arity, parameters, clashesByName);

    private static IType Text => TypeFactory.GetType(SpecialType.String);

    private static IType Span => TypeFactory.GetType(typeof(ReadOnlySpan<char>));

    private static IType Utf8 => TypeFactory.GetType(typeof(ReadOnlySpan<byte>));

    private static IType Boolean => TypeFactory.GetType(SpecialType.Boolean);

    private static IType Provider => TypeFactory.GetType(typeof(IFormatProvider));

    private static INamedType ValueObject(INamedType target, IType valueType) =>
        TypeFactory.GetNamedType(typeof(IValueObject<,>)).WithTypeArguments(target, valueType);

    private static INamedType PlainValueObject(INamedType target, IType valueType) =>
        TypeFactory.GetNamedType(typeof(IPlainValueObject<,>)).WithTypeArguments(target, valueType);

    /// <summary>What the entry points construct by: <c>From</c> of a plain value object, <c>Create</c> of a validated one.</summary>
    public static INamedType Construction(INamedType target, IType valueType, INamedType marker) =>
        ValueObjectTypes.KindOf(marker) == ValueObjectKind.Plain ? PlainValueObject(target, valueType) : marker;

    /// <summary><c>From</c> or <c>Create</c>, the member of <see cref="Construction"/> the entry points and the private constructor serve.</summary>
    public static string ConstructionMember(INamedType marker) =>
        ValueObjectTypes.KindOf(marker) == ValueObjectKind.Plain ? nameof(IPlainValueObject<,>.From) : nameof(IValidatedValue<,,>.Create);

    private static INamedType Equatable(INamedType type) => TypeFactory.GetNamedType(typeof(IEquatable<>)).WithTypeArguments(type);

    private static INamedType MinMaxValue(INamedType target) => TypeFactory.GetNamedType(typeof(IMinMaxValue<>)).WithTypeArguments(target);

    private static INamedType ClosedSet(INamedType set) => TypeFactory.GetNamedType(typeof(IClosedSet<>)).WithTypeArguments(set);

    /// <summary><c>IParsable&lt;TSelf&gt;</c>, <c>ISpanParsable&lt;TSelf&gt;</c> or <c>IUtf8SpanParsable&lt;TSelf&gt;</c>, by the text it parses.</summary>
    private static INamedType Parsable(INamedType target, IType text) =>
        TypeFactory.GetNamedType(text.Equals(Text) ? typeof(IParsable<>) : text.Equals(Span) ? typeof(ISpanParsable<>) : typeof(IUtf8SpanParsable<>)).WithTypeArguments(target);
}
