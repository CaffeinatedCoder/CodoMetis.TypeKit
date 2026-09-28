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
/// Every lookup enumerates a collection and compares names, never <c>OfName</c> (plan §5).
/// </remarks>
[CompileTime]
internal static class ValueObjectDeclaration
{
    /// <summary>
    /// A hand-written parameterless <c>ToString()</c>, the formatting seam: it is kept, and none of the
    /// formatting interfaces is generated, so interpolation, <c>string.Format</c> and
    /// <c>Convert.ToString</c> reach it. The record's own synthesized one is implicitly declared.
    /// </summary>
    public static bool DeclaresToString(INamedType type) =>
        type.Methods.Any(method => method is { Name: nameof(ToString), IsStatic: false, IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false, Parameters.Count: 0 });

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

        // ValueObjectAspect: the field, the entry points; ValueObjectContractAspect: Value; the JSON aspect's nested converter.
        found.AddRange(MembersNamed(target, "_value", "Value", "__FromJson", "__FromText", "__TryFromText", $"{target.Name}JsonConverter"));

        if (kind == ValueObjectKind.Plain)
        {
            found.AddRange(MethodsWithSignature(target, "From", valueType));
            found.AddRange(MembersNamed(target, methods: false, "From"));
        }

        var stringType = TypeFactory.GetType(SpecialType.String);
        var provider   = TypeFactory.GetType(typeof(IFormatProvider));

        // ValueObjectParsableAspect and, where parsing is generated, ValueObjectTypeConverterAspect.
        var parse = ValueObjectParsableAspect.ResolveStrategy(valueType);

        if (parse != ValueParseStrategy.Unsupported)
        {
            found.AddRange(MethodsWithSignature(target, "Parse", stringType, provider));
            found.AddRange(MethodsWithSignature(target, "TryParse", stringType, provider, target));
            found.AddRange(MembersNamed(target, $"{target.Name}TypeConverter"));
            generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IParsable<>)));

            if (parse == ValueParseStrategy.SpanParsable)
            {
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(ISpanParsable<>)));

                var span = TypeFactory.GetType(typeof(ReadOnlySpan<char>));
                found.AddRange(MethodsWithSignature(target, "Parse", span, provider));
                found.AddRange(MethodsWithSignature(target, "TryParse", span, provider, target));
            }

            if (ValueObjectParsableAspect.SupportsUtf8(valueType))
            {
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IUtf8SpanParsable<>)));

                var utf8 = TypeFactory.GetType(typeof(ReadOnlySpan<byte>));
                found.AddRange(MethodsWithSignature(target, "Parse", utf8, provider));
                found.AddRange(MethodsWithSignature(target, "TryParse", utf8, provider, target));
            }
        }

        // ValueObjectFormattableAspect, unless ToString() is hand-written and it generates nothing.
        if (!DeclaresToString(target))
        {
            found.AddRange(MethodsWithSignature(target, nameof(IFormattable.ToString), stringType, provider));
            generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IFormattable)));

            if (ValueObjectFormattableAspect.ResolveStrategy(valueType) == ValueFormatStrategy.SpanFormattable)
            {
                found.AddRange(MethodsWithSignature(target, nameof(ISpanFormattable.TryFormat), TypeFactory.GetType(typeof(Span<char>)), TypeFactory.GetType(SpecialType.Int32), TypeFactory.GetType(typeof(ReadOnlySpan<char>)), provider));
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(ISpanFormattable)));
            }

            if (ValueObjectFormattableAspect.SupportsUtf8(valueType))
            {
                found.AddRange(MethodsWithSignature(target, nameof(IUtf8SpanFormattable.TryFormat), TypeFactory.GetType(typeof(Span<byte>)), TypeFactory.GetType(SpecialType.Int32), TypeFactory.GetType(typeof(ReadOnlySpan<char>)), provider));
                generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IUtf8SpanFormattable)));
            }
        }

        // ValueObjectMinMaxValueAspect.
        if (kind == ValueObjectKind.Plain && ValueObjectMinMaxValueAspect.HasMinMaxValue(valueType))
        {
            found.AddRange(MembersNamed(target, nameof(IMinMaxValue<int>.MinValue), nameof(IMinMaxValue<int>.MaxValue)));
            generatedInterfaces.Add(TypeFactory.GetNamedType(typeof(IMinMaxValue<>)));
        }

        // An explicit implementation of an interface the generators implement sat beside the generated
        // member, which it hides from every caller through the interface: a generic T.Parse bypassed
        // Create, and interpolation printed through a hand-written IFormattable while ToString() did not.
        found.AddRange(ExplicitImplementationsOf(target, generatedInterfaces));

        // ValueObjectConvertibleAspect implements every member of IConvertible explicitly, so declaring
        // the interface at all, with public or explicit members, failed the aspect (LAMA0041: it cannot
        // introduce explicit members for an interface it was told to ignore).
        var convertible = valueType.IsConvertibleTo(typeof(IConvertible));

        // The interfaces implemented with OverrideStrategy.Fail, and IConvertible. The marker's own are implied by it.
        foreach (var implemented in target.ImplementedInterfaces)
        {
            if (implemented.Definition.Equals(TypeFactory.GetNamedType(typeof(IValueObject<,>)))
             || implemented.Definition.Equals(TypeFactory.GetNamedType(typeof(IValueObjectMaterializer<,>)))
             || implemented.Definition.Equals(TypeFactory.GetNamedType(typeof(IPlainValueObject<,>)))
             || (convertible && implemented.Equals(TypeFactory.GetNamedType(typeof(IConvertible)))))
                found.Add($"the interface {implemented.ToDisplayString()}");
        }

        // The attributes the aspects put on the type.
        foreach (var attribute in target.Attributes)
        {
            var type = attribute.Type;

            if (type.Equals(TypeFactory.GetNamedType(typeof(JsonConverterAttribute)))
             || type.Equals(TypeFactory.GetNamedType(typeof(CompilerGeneratedAttribute)))
             || type.Definition.Equals(TypeFactory.GetNamedType(typeof(GeneratedValueObjectAttribute<,>)))
             || (parse != ValueParseStrategy.Unsupported && type.Equals(TypeFactory.GetNamedType(typeof(TypeConverterAttribute)))))
                found.Add($"the attribute {(type.Name.EndsWith(nameof(Attribute), StringComparison.Ordinal) ? type.Name.Substring(0, type.Name.Length - nameof(Attribute).Length) : type.Name)}");
        }

        return found.Distinct().ToList();
    }

    /// <summary>
    /// The members of <paramref name="target"/> that explicitly implement a member of one of
    /// <paramref name="interfaces"/>, compared by definition, as <see cref="Describe"/> names them.
    /// </summary>
    internal static IEnumerable<string> ExplicitImplementationsOf(INamedType target, IReadOnlyList<INamedType> interfaces)
    {
        bool OfOne(IMember implemented) => interfaces.Any(@interface => implemented.DeclaringType.Definition.Equals(@interface));

        foreach (var method in target.Methods)
            if (method is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: true } && method.ExplicitInterfaceImplementations.Any(OfOne)) yield return Describe(method);

        foreach (var property in target.Properties)
            if (property is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: true } && property.ExplicitInterfaceImplementations.Any(OfOne)) yield return Describe(property);

        foreach (var @event in target.Events)
            if (@event is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: true } && @event.ExplicitInterfaceImplementations.Any(OfOne)) yield return Describe(@event);
    }

    /// <summary>
    /// The <c>Create</c> of a validated value object, when it is declared only as an explicit interface
    /// implementation: the generated code calls <c>{TSelf}.Create(value)</c>, which cannot reach it
    /// (CS1929 inside the generated code). <see langword="false"/> when a callable one is declared too.
    /// </summary>
    public static bool DeclaresCreateOnlyExplicitly(INamedType target)
    {
        var declaresExplicitly = false;

        foreach (var method in target.Methods)
        {
            if (!method.IsStatic || method.IsImplicitlyDeclared) continue;

            if (method.IsExplicitInterfaceImplementation)
            {
                declaresExplicitly |= method.ExplicitInterfaceImplementations.Any(implemented => implemented.Name == nameof(IValidatedValue<,,>.Create)
                                                                                              && implemented.DeclaringType.Definition.Equals(TypeFactory.GetNamedType(typeof(IValidatedValue<,,>))));
            }
            else if (method.Name == nameof(IValidatedValue<,,>.Create))
            {
                return false;
            }
        }

        return declaresExplicitly;
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

        return
        [
            .. target.Methods
                     .Where(method => method is { Name: "PrintMembers", IsStatic: false, IsImplicitlyDeclared: false, Parameters: [{ } builder] } && builder.Type.Equals(stringBuilder))
                     .Select(Describe)
        ];
    }

    /// <summary>
    /// A hand-written equality: <c>Equals(TSelf)</c>, <c>GetHashCode()</c>, or an explicit
    /// <c>IEquatable&lt;TSelf&gt;.Equals</c>. The record kept it, while the generated ordering, the JSON
    /// dictionary keys and the EF Core column went on comparing the wrapped value: a case-insensitive
    /// <c>Equals</c> made "abc" and "ABC" equal with <c>CompareTo</c> 32, so a <c>HashSet</c> held one and
    /// a <c>SortedSet</c> two.
    /// </summary>
    public static IReadOnlyList<string> EqualityDeclaredByHand(INamedType target)
    {
        var equatable = TypeFactory.GetNamedType(typeof(IEquatable<>));

        return
        [
            .. target.Methods
                     .Where(method => method is { IsStatic: false, IsImplicitlyDeclared: false }
                                   && (method.IsExplicitInterfaceImplementation
                                           ? method.ExplicitInterfaceImplementations.Any(implemented => implemented.DeclaringType.Definition.Equals(equatable))
                                           : (method is { Name: nameof(Equals), Parameters: [{ } other] } && other.Type.Equals(target))
                                          || method is { Name: nameof(GetHashCode), Parameters.Count: 0 }))
                     .Select(Describe)
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
    /// sight and its properties' implementation unknown, so there a writable property counts.
    /// </remarks>
    public static IReadOnlyList<string> InstanceStateBesideTheValue(INamedType target)
    {
        List<string> found = [];

        for (var type = target; type is not null && type.SpecialType != SpecialType.Object; type = type.BaseType)
        {
            var inherited = type.Equals(target) ? "" : " (inherited)";

            foreach (var field in type.Fields)
                if (field is { IsStatic: false, IsImplicitlyDeclared: false }) found.Add($"the field {field.ToDisplayString()}{inherited}");

            foreach (var property in type.Properties)
            {
                if (property.IsStatic || property.IsImplicitlyDeclared) continue;

                if (property.IsRequired)
                    found.Add($"the required property {property.ToDisplayString()}{inherited}");
                else if (property.IsAutoPropertyOrField == true)
                    found.Add($"the auto-property {property.ToDisplayString()}{inherited}");
                else if (property.IsAutoPropertyOrField is null && property.Writeability != Writeability.None)
                    found.Add($"the property {property.ToDisplayString()}{inherited}");
            }

            foreach (var @event in type.Events)
                if (@event is { IsStatic: false, IsImplicitlyDeclared: false, RaiseMethod: not null }) found.Add($"the event {@event.ToDisplayString()}{inherited}");
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

    /// <summary>Non-implicit members of <paramref name="target"/> with one of <paramref name="names"/>, of any kind.</summary>
    private static IEnumerable<string> MembersNamed(INamedType target, params string[] names) => MembersNamed(target, methods: true, names);

    private static IEnumerable<string> MembersNamed(INamedType target, bool methods, params string[] names)
    {
        foreach (var field in target.Fields)
            if (!field.IsImplicitlyDeclared && Enumerable.Contains(names, field.Name)) yield return field.ToDisplayString();

        foreach (var property in target.Properties)
            if (!property.IsImplicitlyDeclared && Enumerable.Contains(names, property.Name)) yield return property.ToDisplayString();

        foreach (var @event in target.Events)
            if (!@event.IsImplicitlyDeclared && Enumerable.Contains(names, @event.Name)) yield return @event.ToDisplayString();

        foreach (var nested in target.Types)
            if (Enumerable.Contains(names, nested.Name)) yield return $"the nested type {nested.ToDisplayString()}";

        if (!methods) yield break;

        foreach (var method in target.Methods)
            if (!method.IsImplicitlyDeclared && !method.IsExplicitInterfaceImplementation && Enumerable.Contains(names, method.Name)) yield return method.ToDisplayString();
    }

    /// <summary>
    /// Methods named <paramref name="name"/> whose parameter types are <paramref name="parameters"/>,
    /// whatever they return: a generated member with that signature is either refused by Metalama or
    /// skipped in favour of the hand-written one.
    /// </summary>
    private static IEnumerable<string> MethodsWithSignature(INamedType target, string name, params IType[] parameters) =>
        target.Methods
              .Where(method => method is { IsImplicitlyDeclared: false, IsExplicitInterfaceImplementation: false }
                            && method.Name == name
                            && method.Parameters.Count == parameters.Length
                            && method.Parameters.Select(parameter => parameter.Type).Zip(parameters, (declared, expected) => declared.Equals(expected)).All(same => same))
              .Select(method => method.ToDisplayString());
}
