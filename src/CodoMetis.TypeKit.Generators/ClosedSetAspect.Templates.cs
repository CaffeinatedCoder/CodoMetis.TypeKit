using System.Text.Json;
using Metalama.Framework.Aspects;

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

    [Template]
    public string NameTemplate
    {
        get => meta.This._name;
    }

    [Template]
    public string QualifiedNameTemplate
    {
        get => meta.Target.Type.Name + "." + meta.This._name;
    }

    [Template]
    public string ToStringTemplate() => meta.This._name;

    /// <summary>After the static field initializers, before any static constructor body: each member learns its name.</summary>
    [Template]
    public static void NamesInitializerTemplate()
    {
        var args = (ClosedSetArguments)meta.Tags.Source!;
        foreach (var member in args.Members)
        {
            member.Value!._name = member.Name;
        }
    }

    [Template]
    public static dynamic? AllTemplate
    {
        get => meta.ThisType._all;
    }

    [Template]
    public static bool TryParseTemplate(string? name, out dynamic? member)
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

        throw new InvalidOperationException(args.Set.Name + ": not a member of the set.");
    }

    /// <summary>A <see cref="JsonException"/> naming the set, never the input; a JSON null is the serializer's own refusal.</summary>
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
}
