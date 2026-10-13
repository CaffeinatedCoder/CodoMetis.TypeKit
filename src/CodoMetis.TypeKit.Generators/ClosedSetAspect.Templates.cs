using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ClosedSetAspect
{
#pragma warning disable CS0649, CS8618 // template fields: the generated ones are assigned by generated code
    [Template] public string _name;
    [Template] public static IReadOnlyList<object> _all;
#pragma warning restore CS0649, CS8618

    [Template]
    public void PrivateConstructorTemplate()
    {
    }

    /// <summary>
    /// An instance the set's own code made beside its members has no name, and neither has a member
    /// read before the names initializer ran: refused, since a null name is written as JSON null and
    /// stored as a null column, and read back as nothing.
    /// </summary>
    [Template]
    public string NameTemplate
    {
        get
        {
            var args = (ClosedSetArguments)meta.Tags.Source!;
            return meta.This._name ?? throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NotAMember(args.Set.Name);
        }
    }

    /// <summary>The set's name, with its containing types for a nested set, and the member's: two <c>Status</c> sets stay apart.</summary>
    [Template]
    public string QualifiedNameTemplate
    {
        get => ClosedSetTypes.NestingChain(meta.Target.Type) + "." + meta.This.Name;
    }

    [Template]
    public string ToStringTemplate() => meta.This.Name;

    /// <summary>
    /// After the static field initializers of every part, before any static constructor body: each
    /// member learns its name, and <c>All</c> is filled, since a field initializer ran with its own
    /// part only and listed a member declared in another file as null.
    /// </summary>
    [Template]
    public static void NamesInitializerTemplate()
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var member in args.Members)
        {
            member.Value!._name = member.Name;
        }

        meta.ThisType._all = ExpressionFactory.Parse(ClosedSetTypes.AllExpression(args.Set, args.Members)).Value;
    }

    [Template]
    public static dynamic? AllTemplate
    {
        get => meta.ThisType._all;
    }

    [Template]
    public static bool TryParseTemplate([NotNullWhen(true)] string? name, [NotNullWhen(true)] out dynamic? member)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var candidate in args.Members)
        {
            if (name == candidate.Name)
            {
                member = candidate.Value!;
                return true;
            }
        }

        member = null;
        return false;
    }

    /// <summary>
    /// The member of that name, compared ordinally, or ignoring case when asked: a header or a query
    /// string typed by a client. Two members whose names differ only in case are refused where the
    /// set is declared (CMTK1021), so there is one member to find.
    /// </summary>
    [Template]
    public static bool TryParseIgnoreCaseTemplate([NotNullWhen(true)] string? name, bool ignoreCase, [NotNullWhen(true)] out dynamic? member)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var candidate in args.Members)
        {
            if (string.Equals(name, candidate.Name, comparison))
            {
                member = candidate.Value!;
                return true;
            }
        }

        member = null;
        return false;
    }

    /// <summary>A <see cref="FormatException"/> naming the set, never the input.</summary>
    [Template]
    public static dynamic? ParseTemplate(string? name)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var candidate in args.Members)
        {
            if (name == candidate.Name) return candidate.Value;
        }

        throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMember(args.Set.Name);
    }

    /// <summary>The member of that name, compared ordinally or ignoring case when asked; a <see cref="FormatException"/> naming the set, never the input.</summary>
    [Template]
    public static dynamic? ParseIgnoreCaseTemplate(string? name, bool ignoreCase)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var candidate in args.Members)
        {
            if (string.Equals(name, candidate.Name, comparison)) return candidate.Value;
        }

        throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMember(args.Set.Name);
    }

    /// <summary><c>IParsable&lt;TSelf&gt;.Parse</c>: the set's own <c>Parse</c>; the provider means nothing to a name.</summary>
    [Template]
    public static dynamic? ParsableParseTemplate(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return meta.ThisType.Parse(s);
    }

    /// <summary><c>IParsable&lt;TSelf&gt;.TryParse</c>: the member of that name, compared ordinally, as the set's own <c>TryParse</c>.</summary>
    [Template]
    public static bool ParsableTryParseTemplate([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out dynamic result)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var candidate in args.Members)
        {
            if (s == candidate.Name)
            {
                result = candidate.Value!;
                return true;
            }
        }

        result = null!;
        return false;
    }

    [Template]
    public bool CanConvertFromTemplate(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || meta.Base.CanConvertFrom(context, sourceType);

    [Template]
    public bool CanConvertToTemplate(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || meta.Base.CanConvertTo(context, destinationType);

    /// <summary>A name is read with the set's own <c>Parse</c>: a <see cref="FormatException"/> naming the set, never the input.</summary>
    [Template]
    public object? ConvertFromTemplate(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            var args = (ClosedSetArguments)meta.Tags.Source!;
            return ExpressionFactory.Parse($"{ValueObjectTypes.SourceName(args.Set)}.Parse(s)").Value;
        }

        return meta.Base.ConvertFrom(context, culture, value);
    }

    /// <summary>A member is written as its name, which throws for an instance that is no member.</summary>
    [Template]
    public object? ConvertToTemplate(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is not null)
        {
            var args = (ClosedSetArguments)meta.Tags.Source!;
            return ExpressionFactory.Parse($"(({ValueObjectTypes.SourceName(args.Set)})value).Name").Value;
        }

        return meta.Base.ConvertTo(context, culture, value, destinationType);
    }

    /// <summary>
    /// Every delegate is checked before a branch is picked, as <c>Option</c> and <c>Result</c> check
    /// theirs: a null for a member not matched passed until that member first arrived.
    /// </summary>
    [Template]
    public TResult MatchTemplate<TResult>(Func<TResult> first)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var parameter in meta.Target.Parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter.Value, parameter.Name);
        }

        var index = meta.CompileTime(0);
        foreach (var member in args.Members)
        {
            if (ReferenceEquals(meta.This, member.Value)) return meta.Target.Parameters[index].Value!.Invoke();
            index++;
        }

        throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NotAMember(args.Set.Name);
    }

    /// <summary>
    /// A <see cref="JsonException"/> naming the set, never the input. A JSON null never reaches this: a
    /// set is a class, so the serializer reads null as null, and its <c>RespectNullableAnnotations</c>
    /// refuses it for a property that is not nullable. Handling null here would refuse it for an
    /// <c>OrderStatus?</c> property too, which the converter cannot tell apart.
    /// </summary>
    [Template]
    public dynamic? JsonReadTemplate(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        var name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (name is not null)
        {
            foreach (var candidate in args.Members)
            {
                if (name == candidate.Name) return candidate.Value;
            }
        }

        throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMemberInJson(args.Set.Name);
    }

    [Template]
    public void JsonWriteTemplate(Utf8JsonWriter writer, dynamic? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue((string)value!.Name);
    }

    /// <summary>
    /// A dictionary key is the member's name too, which the serializer refused for a converter without
    /// these two (NotSupportedException, both ways). Written as it is, as the value is: a
    /// <c>DictionaryKeyPolicy</c> renames only on writing, so a renamed key would not read back.
    /// </summary>
    [Template]
    public dynamic? JsonReadAsPropertyNameTemplate(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        var name = reader.GetString();
        foreach (var candidate in args.Members)
        {
            if (name == candidate.Name) return candidate.Value;
        }

        throw global::CodoMetis.TypeKit.CompilerServices.GeneratedClosedSets.NoMemberInJson(args.Set.Name);
    }

    [Template]
    public void JsonWriteAsPropertyNameTemplate(Utf8JsonWriter writer, dynamic? value, JsonSerializerOptions options)
    {
        writer.WritePropertyName((string)value!.Name);
    }
}
