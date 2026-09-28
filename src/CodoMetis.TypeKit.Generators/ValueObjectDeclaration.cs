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
    /// A <c>file</c>-local type. Metalama's code model reports it as internal and has no flag for it,
    /// so the declaration's modifiers are read from its source.
    /// </summary>
    public static bool IsFileLocal(INamedType type) =>
        type.Sources.Any(source => Modifiers(source.GetText(normalized: false)).Contains("file"));

    /// <summary>
    /// The modifiers of a type declaration: the words between its attribute lists and its
    /// <c>class</c>/<c>struct</c>/<c>record</c> keyword, comments skipped.
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
            else if (c == '/' && i + 1 < declaration.Length && declaration[i + 1] == '/')
            {
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
    /// and, for a validated value object, <c>Create</c>.
    /// </summary>
    public static IReadOnlyList<string> GeneratedMembersDeclaredByHand(INamedType target, INamedType valueType, ValueObjectKind kind)
    {
        List<string> found = [];

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

            if (parse == ValueParseStrategy.SpanParsable)
            {
                var span = TypeFactory.GetType(typeof(ReadOnlySpan<char>));
                found.AddRange(MethodsWithSignature(target, "Parse", span, provider));
                found.AddRange(MethodsWithSignature(target, "TryParse", span, provider, target));
            }

            if (ValueObjectParsableAspect.SupportsUtf8(valueType))
            {
                var utf8 = TypeFactory.GetType(typeof(ReadOnlySpan<byte>));
                found.AddRange(MethodsWithSignature(target, "Parse", utf8, provider));
                found.AddRange(MethodsWithSignature(target, "TryParse", utf8, provider, target));
            }
        }

        // ValueObjectFormattableAspect, unless ToString() is hand-written and it generates nothing.
        if (!DeclaresToString(target))
        {
            found.AddRange(MethodsWithSignature(target, nameof(IFormattable.ToString), stringType, provider));

            if (ValueObjectFormattableAspect.ResolveStrategy(valueType) == ValueFormatStrategy.SpanFormattable)
                found.AddRange(MethodsWithSignature(target, nameof(ISpanFormattable.TryFormat), TypeFactory.GetType(typeof(Span<char>)), TypeFactory.GetType(SpecialType.Int32), TypeFactory.GetType(typeof(ReadOnlySpan<char>)), provider));

            if (ValueObjectFormattableAspect.SupportsUtf8(valueType))
                found.AddRange(MethodsWithSignature(target, nameof(IUtf8SpanFormattable.TryFormat), TypeFactory.GetType(typeof(Span<byte>)), TypeFactory.GetType(SpecialType.Int32), TypeFactory.GetType(typeof(ReadOnlySpan<char>)), provider));
        }

        // ValueObjectMinMaxValueAspect.
        if (kind == ValueObjectKind.Plain && ValueObjectMinMaxValueAspect.HasMinMaxValue(valueType))
            found.AddRange(MembersNamed(target, nameof(IMinMaxValue<int>.MinValue), nameof(IMinMaxValue<int>.MaxValue)));

        // The interfaces implemented with OverrideStrategy.Fail. The marker's own are implied by it.
        foreach (var implemented in target.ImplementedInterfaces)
        {
            if (implemented.Definition.Equals(TypeFactory.GetNamedType(typeof(IValueObject<,>)))
             || implemented.Definition.Equals(TypeFactory.GetNamedType(typeof(IValueObjectMaterializer<,>)))
             || implemented.Definition.Equals(TypeFactory.GetNamedType(typeof(IPlainValueObject<,>))))
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
