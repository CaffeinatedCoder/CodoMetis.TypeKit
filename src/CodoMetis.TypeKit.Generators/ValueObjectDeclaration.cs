using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// What <see cref="ValueObjectAspect"/> reads from a value object's own declaration before anything is
/// introduced: the seams it keeps, and what it declares that would collide with a generated member.
/// </summary>
/// <remarks>
/// Every lookup enumerates a collection and compares names, never <c>OfName</c> (design/aspects.md).
/// </remarks>
[CompileTime]
internal static class ValueObjectDeclaration
{
    /// <summary>
    /// A hand-written parameterless <c>ToString()</c>, the formatting seam: it is kept, and none of the
    /// formatting interfaces is generated, so interpolation, <c>string.Format</c> and
    /// <c>Convert.ToString</c> reach it. The record's own synthesized one is implicitly declared.
    /// </summary>
    /// <remarks>
    /// A base record's <c>sealed</c> one is the seam too: C# keeps it in every derived record, which then
    /// synthesizes none, so it is what the value object prints, and it cannot declare its own. Generating
    /// one failed the aspect (LAMA0502). A base record's one that is not sealed is refused instead
    /// (<see cref="UnsealedInheritedToString"/>).
    /// </remarks>
    public static bool DeclaresToString(INamedType type) =>
        type.Methods.Any(IsHandWrittenToString) || InheritedToString(type) is { IsSealed: true };

    /// <summary>
    /// The <c>ToString()</c> a base record declares by hand and does not seal, where the value object
    /// declares none: the generated one replaced it without a word, as a record's synthesized one does,
    /// and printed the value a base that returned <c>"***"</c> hid (decision 28's leak, one level up).
    /// </summary>
    public static IMethod? UnsealedInheritedToString(INamedType target) =>
        target.Methods.Any(IsHandWrittenToString) ? null : InheritedToString(target) is { IsSealed: false } inherited ? inherited : null;

    /// <summary>The nearest base record's hand-written, non-abstract <c>ToString()</c>.</summary>
    private static IMethod? InheritedToString(INamedType target)
    {
        foreach (var type in SelfAndBases(target).Skip(1))
        {
            var declared = type.Methods.FirstOrDefault(method => IsHandWrittenToString(method) && !method.IsAbstract);
            if (declared is not null) return declared;
        }

        return null;
    }

    private static bool IsHandWrittenToString(IMethod method) =>
        method is { Name: nameof(ToString), IsStatic: false, IsExplicitInterfaceImplementation: false, Parameters.Count: 0 } && IsByValueAndNotGeneric(method) && IsHandWritten(method);

    /// <summary>
    /// A value object and the records it derives from, <see cref="object"/> left out. A struct has
    /// none: its base is <see cref="ValueType"/>, whose <c>ToString()</c> is nobody's seam.
    /// </summary>
    internal static IEnumerable<INamedType> SelfAndBases(INamedType target)
    {
        yield return target;

        if (target.TypeKind != TypeKind.Class) yield break;

        for (var type = target.BaseType; type is not null && type.SpecialType != SpecialType.Object; type = type.BaseType)
            yield return type;
    }

    /// <summary>
    /// A <c>file</c>-local type, or one nested in a <c>file</c>-local type, which cannot be named outside
    /// its file either. Metalama's code model reports it as internal and has no flag for it, so the
    /// declaration's modifiers are read from its source.
    /// </summary>
    /// <remarks>
    /// The source is read without its leading trivia, as its span's text: <c>SourceReference.GetText</c>
    /// includes it (and its <c>ToString()</c> is a location, whatever its documentation says), and a
    /// <c>#region</c> or <c>#pragma</c> line above the declaration ended the
    /// modifiers before <c>file</c>, as the text an <c>#if</c> leaves out would, and Metalama then crashed
    /// (LAMA0001). A type nested in a <c>file</c> class crashed it the same way.
    /// </remarks>
    public static bool IsFileLocal(INamedType type)
    {
        for (INamedType? declaration = type; declaration is not null; declaration = declaration.DeclaringType)
        {
            if (declaration.Sources.Any(source => Modifiers(source.Span.GetText()).Contains("file"))) return true;
        }

        return false;
    }

    /// <summary>
    /// The modifiers of a type declaration: the words between its attribute lists and its
    /// <c>class</c>/<c>struct</c>/<c>record</c> keyword, comments and directive lines skipped.
    /// </summary>
    internal static IReadOnlyList<string> Modifiers(string declaration)
    {
        List<string> modifiers = [];
        var i = 0;

        while (i < declaration.Length)
        {
            var c = declaration[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '#' || (c == '/' && i + 1 < declaration.Length && declaration[i + 1] == '/'))
            {
                // A line comment, or a directive between the attribute lists and the modifiers.
                while (i < declaration.Length && declaration[i] != '\n') i++;
            }
            else if (c == '/' && i + 1 < declaration.Length && declaration[i + 1] == '*')
            {
                var end = declaration.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? declaration.Length : end + 2;
            }
            else if (c == '[')
            {
                i = SkipAttributeList(declaration, i);
            }
            else if (char.IsLetter(c) || c == '_' || c == '@')
            {
                var start = i;
                while (i < declaration.Length && (char.IsLetterOrDigit(declaration[i]) || declaration[i] == '_' || declaration[i] == '@')) i++;

                var word = declaration.Substring(start, i - start);
                if (word is "class" or "struct" or "record" or "interface" or "enum") return modifiers;

                modifiers.Add(word);
            }
            else
            {
                return modifiers;
            }
        }

        return modifiers;
    }

    private static int SkipAttributeList(string text, int start)
    {
        var depth = 0;

        for (var i = start; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[':
                    depth++;
                    break;
                case ']' when --depth == 0:
                    return i + 1;
                case '"':
                    for (i++; i < text.Length && text[i] != '"'; i++)
                    {
                        if (text[i] == '\\') i++;
                    }

                    break;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// The names of the members the generators introduce into a value object, besides <c>Value</c>, for
    /// some marker or wrapped type: a value object of one of these names failed inside the generated
    /// code (LAMA0611, CS0542: a member cannot have the name of its type), naming nothing the user wrote
    /// (measured 2026-10-13 for <c>From</c>, <c>TryFrom</c>, <c>Parse</c>, <c>TryParse</c>,
    /// <c>TryFormat</c>, <c>MinValue</c> and <c>CompareTo</c>). Refused whatever it wraps, so the rule is
    /// one list rather than one per wrapped type. What a record synthesizes (<c>ToString</c>,
    /// <c>Equals</c>) is C#'s own error, and an explicit implementation (<c>IConvertible</c>'s) does not
    /// collide.
    /// </summary>
    public static IReadOnlyList<string> GeneratedMemberNames { get; } =
        ["From", "TryFrom", "FromKnownGood", "Revalidate", "Parse", "TryParse", "TryFormat", "MinValue", "MaxValue", "CompareTo", "_value", "__FromJson", "__FromText", "__TryFromText"];

    /// <summary>
    /// What the declaration writes by hand that a generator introduces itself, which would fail inside
    /// the generated code (LAMA0500, LAMA0503, LAMA0512, LAMA0521, LAMA0531, LAMA0611) naming nothing
    /// the user wrote, or, for a member the generators skip when it exists, would be kept silently as
    /// an entry point or a format the generated code never agreed to. The seams are not in it:
    /// <c>TryFrom</c>, <c>FromKnownGood</c>, <c>Revalidate</c>, <c>CompareTo(TSelf)</c>, <c>ToString()</c>
    /// and, for a validated value object, <c>Create</c>. Neither are the comparison interfaces, which
    /// ValueObjectComparableAspect answers (CMTK1008).
    /// </summary>
    public static IReadOnlyList<string> GeneratedMembersDeclaredByHand(INamedType target, INamedType valueType, ValueObjectKind kind)
    {
        List<string> found = [];

        // The interfaces the generators implement for this declaration, whose explicit implementations
        // are refused below. ValueObjectContractAspect: the equality operators.
        List<INamedType> generatedInterfaces = [TypeFactory.GetNamedType(typeof(IEqualityOperators<,,>))];

        // A base record's count as the value object's own (#197). Beside a generated one, its public
        // Value, From, Parse, TryParse, ToString(string?, IFormatProvider?) or TryFormat failed the aspect
        // (LAMA0500), naming nothing the user wrote, and its static MinValue and MaxValue were kept as the
        // value object's, with no MinValue or MaxValue generated. One the value object cannot see, a base's
        // private member, stands in for nothing: C# hides it, and the generated one sits beside it (#230).
        IEnumerable<string> Declared(Func<INamedType, IEnumerable<string>> find) =>
            SelfAndBases(target).SelectMany(type => find(type).Select(member => type.Equals(target) ? member : $"{member} in its base type {type.ToDisplayString()}"));

        // ValueObjectAspect: the field, the entry points; ValueObjectContractAspect: Value; the JSON aspect's nested converter.
        found.AddRange(Declared(type => MembersNamed(type, target, "_value", "Value", "__FromJson", "__FromText", "__TryFromText", $"{target.Name}JsonConverter")));

        if (kind == ValueObjectKind.Plain)
        {
            found.AddRange(Declared(type => MethodsWithSignature(type, target, "From", (valueType, false))));
            found.AddRange(Declared(type => MembersNamed(type, target, methods: false, "From")));
        }

        // A generated method's parameters, and whether each is out: an overload that takes one by value where the
        // generated one takes it by reference, or the other way round, is another method (#226).
        (IType, bool) stringType = (TypeFactory.GetType(SpecialType.String), false);
        (IType, bool) provider   = (TypeFactory.GetType(typeof(IFormatProvider)), false);
        (IType, bool) result     = (target, true);

        // ValueObjectParsableAspect and, where parsing is generated, ValueObjectTypeConverterAspect.
        var parse = ValueObjectParsableAspect.ResolveStrategy(valueType);

        if (parse != ValueParseStrategy.Unsupported)
        {
            found.AddRange(Declared(type => MethodsWithSignature(type, target, "Parse", stringType, provider)));
            found.AddRange(Declared(type => MethodsWithSignature(type, target, "TryParse", stringType, provider, result)));
            found.AddRange(Declared(type => MembersNamed(type, target, $"{target.Name}TypeConverter")));
            generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IParsable<>)));

            if (ValueObjectParsableAspect.SupportsSpan(valueType))
            {
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(ISpanParsable<>)));

                (IType, bool) span = (TypeFactory.GetType(typeof(ReadOnlySpan<char>)), false);
                found.AddRange(Declared(type => MethodsWithSignature(type, target, "Parse", span, provider)));
                found.AddRange(Declared(type => MethodsWithSignature(type, target, "TryParse", span, provider, result)));
            }

            if (ValueObjectParsableAspect.SupportsUtf8(valueType))
            {
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IUtf8SpanParsable<>)));

                (IType, bool) utf8 = (TypeFactory.GetType(typeof(ReadOnlySpan<byte>)), false);
                found.AddRange(Declared(type => MethodsWithSignature(type, target, "Parse", utf8, provider)));
                found.AddRange(Declared(type => MethodsWithSignature(type, target, "TryParse", utf8, provider, result)));
            }
        }

        // ValueObjectFormattableAspect, unless ToString() is hand-written and it generates nothing.
        if (!DeclaresToString(target))
        {
            found.AddRange(Declared(type => MethodsWithSignature(type, target, nameof(IFormattable.ToString), stringType, provider)));
            generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IFormattable)));

            if (ValueObjectFormattableAspect.ResolveStrategy(valueType) == ValueFormatStrategy.SpanFormattable)
            {
                found.AddRange(Declared(type => MethodsWithSignature(type, target, nameof(ISpanFormattable.TryFormat), (TypeFactory.GetType(typeof(Span<char>)), false), (TypeFactory.GetType(SpecialType.Int32), true), (TypeFactory.GetType(typeof(ReadOnlySpan<char>)), false), provider)));
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(ISpanFormattable)));
            }

            if (ValueObjectFormattableAspect.SupportsUtf8(valueType))
            {
                found.AddRange(Declared(type => MethodsWithSignature(type, target, nameof(IUtf8SpanFormattable.TryFormat), (TypeFactory.GetType(typeof(Span<byte>)), false), (TypeFactory.GetType(SpecialType.Int32), true), (TypeFactory.GetType(typeof(ReadOnlySpan<char>)), false), provider)));
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IUtf8SpanFormattable)));
            }
        }

        // ValueObjectMinMaxValueAspect.
        if (kind == ValueObjectKind.Plain && ValueObjectMinMaxValueAspect.HasMinMaxValue(valueType))
        {
            found.AddRange(Declared(type => MembersNamed(type, target, nameof(IMinMaxValue<int>.MinValue), nameof(IMinMaxValue<int>.MaxValue))));
            generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IMinMaxValue<>)));
        }

        // An explicit implementation of an interface the generators implement sat beside the generated
        // member, which it hides from every caller through the interface: a generic T.Parse bypassed
        // Create, and interpolation printed through a hand-written IFormattable while ToString() did not.
        // An interface's was replaced by the generated member instead, as silently.
        found.AddRange(ImplementationsOf(target, generatedInterfaces));

        // ValueObjectConvertibleAspect implements every member of IConvertible explicitly, so declaring
        // the interface at all, with public or explicit members, failed the aspect (LAMA0041: it cannot
        // introduce explicit members for an interface it was told to ignore). So did declaring an
        // interface derived from it.
        var convertible = valueType.IsConvertibleTo(typeof(IConvertible));
        var iconvertible = TypeFactory.GetNamedType(typeof(IConvertible));

        // The interfaces implemented with OverrideStrategy.Fail, and IConvertible. The marker's own are implied by it.
        // Declared through an interface of the user's, as an IEntityId<TSelf> : IValueObject<TSelf, Guid> declares
        // IValueObject, each failed the aspect as declaring it does (LAMA0512, LAMA0041), naming nothing the user
        // wrote (#192). Neither marker derives from one of them, so an interface derived from a marker is not one.
        bool Implemented(INamedType @interface) =>
            @interface.Definition.Equals(TypeFactory.GetNamedType(typeof(IValueObject<,>)))
         || @interface.Definition.Equals(TypeFactory.GetNamedType(typeof(IValueObjectMaterializer<,>)))
         || @interface.Definition.Equals(TypeFactory.GetNamedType(typeof(IPlainValueObject<,>)))
         || (convertible && @interface.Equals(iconvertible));

        foreach (var implemented in target.ImplementedInterfaces)
        {
            if (Implemented(implemented))
                found.Add($"the interface {implemented.ToDisplayString()}");
            else
                found.AddRange(implemented.AllImplementedInterfaces.Where(Implemented).Select(inherited => $"the interface {inherited.ToDisplayString()} in its interface {implemented.ToDisplayString()}"));
        }

        // One a base record implements, itself or through an interface, is implemented already, and the aspect
        // failed the same way.
        foreach (var type in SelfAndBases(target).Skip(1))
        {
            found.AddRange(type.ImplementedInterfaces.SelectMany(implemented => implemented.AllImplementedInterfaces.Prepend(implemented).Where(Implemented))
                               .Distinct()
                               .Select(inherited => $"the interface {inherited.ToDisplayString()} in its base type {type.ToDisplayString()}"));
        }

        // The attributes the aspects put on the type.
        foreach (var attribute in target.Attributes)
        {
            var type = attribute.Type;

            if (type.Equals(TypeFactory.GetNamedType(typeof(JsonConverterAttribute)))
             || type.Equals(TypeFactory.GetNamedType(typeof(CompilerGeneratedAttribute)))
             || type.Definition.Equals(TypeFactory.GetNamedType(typeof(GeneratedValueObjectAttribute<,>)))
             || type.Equals(TypeFactory.GetNamedType(typeof(HandWrittenOrderingAttribute)))
             || (parse != ValueParseStrategy.Unsupported && type.Equals(TypeFactory.GetNamedType(typeof(TypeConverterAttribute)))))
                found.Add($"the attribute {(type.Name.EndsWith(nameof(Attribute), StringComparison.Ordinal) ? type.Name.Substring(0, type.Name.Length - nameof(Attribute).Length) : type.Name)}");
        }

        return found.Distinct().ToList();
    }

    /// <summary>
    /// The implementations that <paramref name="target"/> reaches for a member of one of
    /// <paramref name="interfaces"/>, compared by definition, other than its own public members, which the
    /// checks for hand-written members answer: its own explicit implementation, and a base record's or an
    /// interface's, explicit or public, in this assembly or another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A base record's count too. The generated member replaced one of <c>IFormattable</c> or
    /// <c>IEqualityOperators</c> without a word, and one of a static interface (<c>IParsable</c>,
    /// <c>IMinMaxValue</c>) or of <c>IComparable</c> kept answering the interface: a generic
    /// <c>T.Parse</c> reached a generic base's, skipping <c>Create</c>, and <c>Comparer&lt;T&gt;.Default</c>
    /// disagreed with <c>&lt;</c> (measured 2026-09-28).
    /// </para>
    /// <para>
    /// So does an interface's, the default every type that declares the interface reaches until it declares
    /// a public member of its own. The generated one replaced it without a word: <c>Max&lt;Bounded&gt;()</c>
    /// read the generated <c>MaxValue</c> where <c>IBounded&lt;TSelf&gt;</c>'s threw, and an <c>IFormattable</c>
    /// call printed <c>1</c> where <c>IFormats</c> answered its own text (#191, measured 2026-10-30 for every
    /// interface the generators implement). An interface's <c>==</c> is not one: the record's own replaces it,
    /// generated or not, and that is the implementation found.
    /// </para>
    /// <para>
    /// Found by the interface mapping, never by listing members: Metalama lists no private member of a type in
    /// another assembly, and an explicit implementation is private, so a base record's and an interface's from
    /// a referenced project were replaced or kept without a word (measured 2026-10-30).
    /// </para>
    /// <para>
    /// A base record's public implementation counts as the value object's own. Its public static <c>MaxValue</c>
    /// was the value object's, and no <c>MaxValue</c> was generated; its public <c>ToString(string?,
    /// IFormatProvider?)</c>, <c>CompareTo(object)</c> or <c>Parse</c> failed the aspect (LAMA0500), naming
    /// nothing the user wrote (#197, measured 2026-10-30).
    /// </para>
    /// <para>
    /// An interface's explicit implementation of a member of an interface in the same library is not refused:
    /// .NET's <c>INumberBase&lt;TSelf&gt;</c> bridges UTF-8 formatting and parsing to the UTF-16 members, which
    /// every numeric type of .NET replaces with its own and the generators generate both of. They are the only
    /// such members in .NET 10's interfaces (scanned 2026-10-30).
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> ImplementationsOf(INamedType target, IReadOnlyList<INamedType> interfaces)
    {
        foreach (var implemented in target.AllImplementedInterfaces)
        {
            if (!interfaces.Any(@interface => implemented.Definition.Equals(@interface))) continue;

            foreach (var member in implemented.Methods.Concat<IMember>(implemented.Properties).Concat(implemented.Events))
            {
                if (!target.TryFindImplementationForInterfaceMember(member, out var implementation) || !IsHandWritten(implementation)) continue;

                var declaring = implementation.DeclaringType;

                if (declaring.TypeKind == TypeKind.Interface && declaring.DeclaringAssembly.Equals(implemented.DeclaringAssembly)) continue;

                if (Elsewhere(target, implementation) is { } where)
                    yield return (IsPublic(implementation) ? PublicName(implementation, member) : Describe(implementation)) + where;
                else if (implementation.IsExplicitInterfaceImplementation)
                    yield return Describe(implementation);
            }
        }
    }

    /// <summary>
    /// <paramref name="target"/>, a base record or an interface it implements implements a member of
    /// <paramref name="interface"/> explicitly, compared as the instance it is, type arguments included: what a
    /// generic base implements for <c>IAdditionOperators&lt;TSelf, TSelf, TSelf&gt;</c> is read for the value
    /// object it derives.
    /// </summary>
    /// <remarks>
    /// An interface's explicit implementation is the default every implementing type reaches until it declares
    /// its own public member: a generated <c>+</c> replaced <c>IMoneyMath&lt;TSelf&gt;</c>'s, and generic
    /// addition read 3 where the interface's answered 1.
    /// </remarks>
    internal static bool ImplementsExplicitly(INamedType target, INamedType @interface) =>
        SelfAndBases(target).Concat(target.AllImplementedInterfaces)
                            .Any(type => type.Methods.Any(method => method is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: true }
                                                                 && method.ExplicitInterfaceImplementations.Any(implemented => implemented.DeclaringType.Equals(@interface))));

    /// <summary>
    /// Where <paramref name="implementation"/> is declared, as an error names it, when it is not
    /// <paramref name="target"/> itself: <c>" in its base type X"</c> or <c>" in its interface X"</c>.
    /// </summary>
    private static string? Elsewhere(INamedType target, IMember implementation) =>
        implementation.DeclaringType switch
        {
            var declaring when declaring.Equals(target)     => null,
            { TypeKind: TypeKind.Interface } declaring       => $" in its interface {declaring.ToDisplayString()}",
            var declaring                                    => $" in its base type {declaring.ToDisplayString()}"
        };

    /// <summary>
    /// A public member, which an explicit implementation never is. From another assembly, a public static member
    /// that implements a static abstract one reads as an explicit implementation too, since the compiler records
    /// every such implementation in metadata as one, so it is told apart by its accessibility.
    /// </summary>
    private static bool IsPublic(IMember member) => member.Accessibility == Accessibility.Public;

    /// <summary>
    /// A public implementation as an error names it: <c>BoundedBase&lt;Level&gt;.MaxValue</c>. One from another
    /// assembly that reads as an explicit implementation displays as <c>BoundedBase&lt;Level&gt;.IMinMaxValue.MaxValue</c>,
    /// so it is named by its type and the member it implements.
    /// </summary>
    private static string PublicName(IMember implementation, IMember implemented)
    {
        if (!implementation.IsExplicitInterfaceImplementation) return implementation.ToDisplayString();

        var shown = implemented.ToDisplayString();
        var at    = shown.IndexOf("." + implemented.Name, StringComparison.Ordinal);

        return at < 0 ? implementation.ToDisplayString() : implementation.DeclaringType.ToDisplayString() + shown.Substring(at);
    }

    /// <summary>
    /// The factory seams of a validated value object that a base record declares rather than the value object:
    /// <c>TryFrom(T)</c> and <c>Revalidate()</c>. The generated one is introduced only where none exists, and a
    /// base's counted, so it stood in for the value object's without a word: <c>TryFrom</c> answered whatever the
    /// base did, which need not call <c>Create</c>, and <c>Revalidate</c> threw where the base's did (#194,
    /// measured 2026-10-30). A base's <c>FromKnownGood(T)</c> is hidden by the generated one, whose caller
    /// expression parameter makes it another signature, so it is not one, and a base's private one is hidden from the
    /// value object, which C# never calls it for (#230).
    /// </summary>
    public static IReadOnlyList<string> InheritedSeams(INamedType target, INamedType valueType, ValueObjectKind kind) =>
        kind != ValueObjectKind.Validated
            ? []
            : [.. SelfAndBases(target).Skip(1).SelectMany(type => type.Methods
                                                                     .Where(method => IsHandWritten(method) && !method.IsExplicitInterfaceImplementation && IsByValueAndNotGeneric(method) && method.IsAccessibleFrom(target)
                                                                                   && (method is { Name: "TryFrom", IsStatic: true, Parameters: [{ } value] } && value.Type.ToNonNullable().Equals(valueType.ToNonNullable())
                                                                                    || method is { Name: "Revalidate", IsStatic: false, Parameters.Count: 0 }))
                                                                     .Select(method => $"{method.ToDisplayString()} in its base type {type.ToDisplayString()}"))];

    /// <summary>
    /// How a validated value object declares <c>Create</c>: as an explicit implementation of the
    /// marker's, and as a static method the generated code can call as <c>{TSelf}.Create(value)</c>.
    /// Alone, the explicit one cannot be reached from there (CS1929 inside the generated code); beside a
    /// callable one it is a second rule set, which generic code calling <c>T.Create</c> reached while
    /// every generated entry point applied the other.
    /// </summary>
    /// <remarks>
    /// A base record's or an interface's counts, found by the interface mapping: <paramref name="declaration"/>
    /// names it, and <paramref name="inherited"/> says it is not the value object's own. Where the value object
    /// lists the marker only through a base record of another project, generic <c>T.Create</c> reached the base's
    /// explicit implementation, which refused what the value object's own <c>Create</c> accepted (#195, measured
    /// 2026-10-30). A base's public one is one rule set alone, which the generated code reaches as
    /// <c>{TSelf}.Create</c>, and a second beside the value object's own.
    /// </remarks>
    public static (bool Explicitly, bool Callable) CreateDeclarations(INamedType target, INamedType marker, out string declaration, out bool inherited)
    {
        var explicitly = false;
        var callable   = false;

        declaration = $"an explicit implementation of {marker.ToDisplayString()}";
        inherited   = false;

        foreach (var method in target.Methods)
        {
            if (!method.IsStatic || method.IsImplicitlyDeclared) continue;

            if (method.IsExplicitInterfaceImplementation)
            {
                explicitly |= method.ExplicitInterfaceImplementations.Any(implemented => implemented.Name == nameof(IValidatedValue<,,>.Create)
                                                                                      && implemented.DeclaringType.Definition.Equals(TypeFactory.GetNamedType(typeof(IValidatedValue<,,>))));
            }
            else if (method.Name == nameof(IValidatedValue<,,>.Create))
            {
                callable = true;
            }
        }

        if (!explicitly
         && marker.Methods.FirstOrDefault(method => method.Name == nameof(IValidatedValue<,,>.Create)) is { } create
         && target.TryFindImplementationForInterfaceMember(create, out var implementation)
         && Elsewhere(target, implementation) is { } where
         && (!IsPublic(implementation) || callable))
        {
            explicitly  = true;
            inherited   = true;
            declaration = IsPublic(implementation) ? PublicName(implementation, create) + where : declaration + where;
        }

        return (explicitly, callable);
    }

    /// <summary>
    /// A hand-written <c>PrintMembers(StringBuilder)</c>, the hook a record's synthesized <c>ToString()</c>
    /// calls, where the generated <c>ToString()</c> replaces that one and never calls it. What it hid was
    /// printed: <c>"1234"</c>, and <c>PinHolder { Pin = 1234 }</c> in a record that holds it. With a
    /// hand-written <c>ToString()</c>, the seam, nothing is generated in its place, so it is allowed there.
    /// </summary>
    public static IReadOnlyList<string> PrintMembersDeclaredByHand(INamedType target)
    {
        if (DeclaresToString(target)) return [];

        var stringBuilder = TypeFactory.GetType(typeof(System.Text.StringBuilder));

        // A base record's too: a derived record's synthesized ToString() reaches it through its own
        // PrintMembers, and the generated one does not.
        return
        [
            .. SelfAndBases(target).SelectMany(type => type.Methods
                                                           .Where(method => method is { Name: "PrintMembers", IsStatic: false, Parameters: [{ } builder] } && builder.Type.Equals(stringBuilder) && IsByValueAndNotGeneric(method) && IsHandWritten(method))
                                                           .Select(method => Describe(method) + (type.Equals(target) ? "" : $" in its base type {type.ToDisplayString()}")))
        ];
    }

    /// <summary>
    /// A hand-written equality: <c>Equals(TSelf)</c>, <c>GetHashCode()</c>, or an explicit
    /// <c>IEquatable&lt;TSelf&gt;.Equals</c>. The record kept it, while the generated ordering, the JSON
    /// dictionary keys and the EF Core column went on comparing the wrapped value: a case-insensitive
    /// <c>Equals</c> made "abc" and "ABC" equal with <c>CompareTo</c> 32, so a <c>HashSet</c> held one and
    /// a <c>SortedSet</c> two. An <c>operator ==</c> or <c>!=</c> between two of the type collides with
    /// the record's own, which C# reports as CS0111 naming only <c>op_Equality</c>; the error says why.
    /// </summary>
    /// <remarks>
    /// An <c>Equals</c> of a base record's type counts too. It implemented <c>IEquatable&lt;TBase&gt;</c>, so a
    /// <c>HashSet</c> of the base compared by it while <c>==</c> compared the wrapped value: two holding "a" were
    /// equal, and the set held both (#196, measured 2026-10-30). One of the direct base's type is C#'s own CS0111,
    /// beside the override the record synthesizes.
    /// </remarks>
    public static IReadOnlyList<string> EqualityDeclaredByHand(INamedType target)
    {
        var equatable = TypeFactory.GetNamedType(typeof(IEquatable<>));
        var ownTypes  = SelfAndBases(target).ToList();

        return
        [
            .. target.Methods
                     .Where(method => method is { IsStatic: false, IsImplicitlyDeclared: false } && IsByValueAndNotGeneric(method)
                                   && (method.IsExplicitInterfaceImplementation
                                           ? method.ExplicitInterfaceImplementations.Any(implemented => implemented.DeclaringType.Definition.Equals(equatable))
                                           : (method is { Name: nameof(Equals), Parameters: [{ } other] } && ownTypes.Any(type => other.Type.ToNonNullable().Equals(type)))
                                          || method is { Name: nameof(GetHashCode), Parameters.Count: 0 }))
                     .Select(Describe),
            .. target.Methods
                     .Where(method => method is { IsStatic: true, IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false, OperatorKind: OperatorKind.Equality or OperatorKind.Inequality, Parameters: [{ } left, { } right] }
                                   && left.Type.ToNonNullable().Equals(target) && right.Type.ToNonNullable().Equals(target))
                     .Select(Describe)
        ];
    }

    /// <summary>
    /// A base record's hand-written equality: <c>Equals(TBase)</c>, <c>GetHashCode()</c>, or an explicit
    /// <c>IEquatable&lt;TBase&gt;.Equals</c>, in this assembly or another. The derived record's
    /// synthesized equality called them, so a base comparing by reference made two value objects holding
    /// 42 unequal while <c>CompareTo</c> answered 0. Over a base record the equality is generated, the
    /// wrapped value's (<see cref="ValueObjectContractAspect"/>), and it does not call them: CMTK1023
    /// says so, rather than replace them without a word.
    /// </summary>
    public static IReadOnlyList<string> InheritedEquality(INamedType target)
    {
        var equatable = TypeFactory.GetNamedType(typeof(IEquatable<>));

        return
        [
            .. SelfAndBases(target).Skip(1).SelectMany(type => type.Methods
                                                                   .Where(method => method is { IsStatic: false } && IsByValueAndNotGeneric(method) && IsHandWritten(method)
                                                                                 && (method.IsExplicitInterfaceImplementation
                                                                                         ? method.ExplicitInterfaceImplementations.Any(implemented => implemented.DeclaringType.Definition.Equals(equatable))
                                                                                         : (method is { Name: nameof(Equals), Parameters: [{ } other] } && other.Type.Equals(type))
                                                                                        || method is { Name: nameof(GetHashCode), Parameters.Count: 0 }))
                                                                   .Select(method => $"{Describe(method)} in its base type {type.ToDisplayString()}"))
        ];
    }

    /// <summary>
    /// The instance state <paramref name="target"/> declares or inherits besides the wrapped value: a
    /// field, an auto-property, a <c>required</c> member or a field-like event. The generated JSON,
    /// parsing, type converter and materializer carry the wrapped value alone, so such state was lost
    /// on every round trip, while the record's equality compared it: a <c>Currency</c> written as
    /// <c>10</c> read back as its initializer's <c>"EUR"</c>, and a lazily filled cache field made two
    /// equal instances unequal once it was read. A <c>required</c> member failed inside the generated
    /// code instead (LAMA0611, CS9035). A computed property, and anything static, holds nothing.
    /// </summary>
    /// <remarks>
    /// The generated field does not exist yet when this runs, and a hand-written <c>_value</c> or
    /// <c>Value</c> is CMTK1011, answered before this. A base type is read too, since the record's
    /// equality compares its state as well; in a referenced assembly its private fields are out of
    /// sight and its properties' implementation unknown, so there a writable property counts. What the
    /// compiler synthesized there is not the user's: a base record's <c>EqualityContract</c> read as an
    /// auto-property, and a value object over any record from another project was refused.
    /// </remarks>
    public static IReadOnlyList<string> InstanceStateBesideTheValue(INamedType target)
    {
        List<string> found = [];

        for (var type = target; type is not null && type.SpecialType != SpecialType.Object; type = type.BaseType)
        {
            var inherited = type.Equals(target) ? "" : " (inherited)";

            foreach (var field in type.Fields)
                if (!field.IsStatic && IsHandWritten(field)) found.Add($"the field {field.ToDisplayString()}{inherited}");

            foreach (var property in type.Properties)
            {
                if (property.IsStatic || !IsHandWritten(property)) continue;

                if (property.IsRequired)
                    found.Add($"the required property {property.ToDisplayString()}{inherited}");
                else if (property.IsAutoPropertyOrField == true)
                    found.Add($"the auto-property {property.ToDisplayString()}{inherited}");
                else if (property.IsAutoPropertyOrField is null && property.Writeability != Writeability.None)
                    found.Add($"the property {property.ToDisplayString()}{inherited}");
            }

            foreach (var @event in type.Events)
                if (@event is { IsStatic: false, RaiseMethod: not null } && IsHandWritten(@event)) found.Add($"the event {@event.ToDisplayString()}{inherited}");
        }

        return found;
    }

    /// <summary>
    /// A member as an error names it. An explicit interface implementation names the interface member
    /// it implements, type arguments included, which its own display name drops
    /// (<c>Rank.IComparable.CompareTo(Rank)</c>).
    /// </summary>
    internal static string Describe(IMember member)
    {
        IMember? implemented = member switch
        {
            IMethod { IsExplicitInterfaceImplementation: true } method       => method.ExplicitInterfaceImplementations.FirstOrDefault(),
            IProperty { IsExplicitInterfaceImplementation: true } property => property.ExplicitInterfaceImplementations.FirstOrDefault(),
            IEvent { IsExplicitInterfaceImplementation: true } @event       => @event.ExplicitInterfaceImplementations.FirstOrDefault(),
            _                                                               => null
        };

        return implemented is null ? member.ToDisplayString() : $"the explicit implementation of {implemented.ToDisplayString()}";
    }

    /// <summary>
    /// Written by the user, not synthesized by the compiler: a record in a referenced assembly reports its
    /// synthesized members as declared, and marks them <c>[CompilerGenerated]</c>.
    /// </summary>
    internal static bool IsHandWritten(IMember member) =>
        !member.IsImplicitlyDeclared && !member.Attributes.Any(attribute => attribute.Type.Equals(TypeFactory.GetNamedType(typeof(CompilerGeneratedAttribute))));

    /// <summary>
    /// A method without type parameters whose parameters are all by value, as every generated method is but
    /// <c>TryParse</c> and <c>TryFormat</c> (<see cref="MethodsWithSignature"/>): C# overloads by arity and by a
    /// parameter's ref kind, so a generic <c>CompareTo&lt;T&gt;(object?)</c> or a <c>TryFrom(in int)</c> is another
    /// method, which the generated one sits beside, and no seam (#226).
    /// </summary>
    internal static bool IsByValueAndNotGeneric(IMethod method) =>
        method.TypeParameters.Count == 0 && method.Parameters.All(parameter => parameter.RefKind == RefKind.None);

    /// <summary>
    /// Non-implicit members of <paramref name="target"/> with one of <paramref name="names"/>, of any kind, that
    /// <paramref name="seenFrom"/> can see: a base's private member is hidden from the value object (#230).
    /// </summary>
    private static IEnumerable<string> MembersNamed(INamedType target, INamedType seenFrom, params string[] names) => MembersNamed(target, seenFrom, methods: true, names);

    /// <summary>Non-implicit members of <paramref name="target"/> with one of <paramref name="names"/>.</summary>
    internal static IEnumerable<string> MembersNamed(INamedType target, bool methods, params string[] names) => MembersNamed(target, target, methods, names);

    /// <remarks>
    /// An explicit implementation is <see cref="ImplementationsOf"/>'s, which names it, and so is a public static
    /// member of another assembly that implements a static abstract one, which reads as explicit there.
    /// </remarks>
    private static IEnumerable<string> MembersNamed(INamedType target, INamedType seenFrom, bool methods, params string[] names)
    {
        foreach (var field in target.Fields)
            if (!field.IsImplicitlyDeclared && Enumerable.Contains(names, field.Name) && field.IsAccessibleFrom(seenFrom)) yield return field.ToDisplayString();

        foreach (var property in target.Properties)
            if (property is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false } && Enumerable.Contains(names, property.Name) && property.IsAccessibleFrom(seenFrom)) yield return property.ToDisplayString();

        foreach (var @event in target.Events)
            if (@event is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false } && Enumerable.Contains(names, @event.Name) && @event.IsAccessibleFrom(seenFrom)) yield return @event.ToDisplayString();

        foreach (var nested in target.Types)
            if (Enumerable.Contains(names, nested.Name) && nested.IsAccessibleFrom(seenFrom)) yield return $"the nested type {nested.ToDisplayString()}";

        if (!methods) yield break;

        foreach (var method in target.Methods)
            if (!method.IsImplicitlyDeclared && !method.IsExplicitInterfaceImplementation && Enumerable.Contains(names, method.Name) && method.IsAccessibleFrom(seenFrom)) yield return method.ToDisplayString();
    }

    /// <summary>
    /// Methods named <paramref name="name"/> whose parameter types are <paramref name="parameters"/>,
    /// whatever they return, that <paramref name="seenFrom"/> can see: a generated member with that signature is
    /// either refused by Metalama or skipped in favour of the hand-written one.
    /// </summary>
    /// <remarks>
    /// The signature C# overloads by (#226): no type parameters, as the generated method has none, and each
    /// parameter by reference where the generated one is <c>out</c>, by value where it is by value. A generic
    /// <c>Parse&lt;TFormat&gt;(string, IFormatProvider?)</c>, a <c>From(in int)</c> and a <c>TryParse</c> whose
    /// last parameter is by value are other methods, which the generated ones sit beside. <c>ref</c> or <c>in</c>
    /// against <c>out</c> is no overload in C# (CS0663), so it is the same method, as nullability is.
    /// </remarks>
    private static IEnumerable<string> MethodsWithSignature(INamedType target, INamedType seenFrom, string name, params (IType Type, bool Out)[] parameters) =>
        target.Methods
              .Where(method => method is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false, TypeParameters.Count: 0 }
                            && method.Name == name
                            && method.Parameters.Count == parameters.Length
                            && method.Parameters.Zip(parameters, (declared, expected) => declared.Type.Equals(expected.Type) && declared.RefKind != RefKind.None == expected.Out).All(same => same)
                            && method.IsAccessibleFrom(seenFrom))
              .Select(method => method.ToDisplayString());
}
