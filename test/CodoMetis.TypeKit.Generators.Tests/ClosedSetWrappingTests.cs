using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>A closed set of this project, which the value objects below wrap.</summary>
public sealed partial class LocalStatus : IClosedSet<LocalStatus>
{
    public static readonly LocalStatus Open = new();
    public static readonly LocalStatus Shipped = new();
    public static readonly LocalStatus Cancelled = new();
}

/// <summary>A nested closed set of this project.</summary>
public static class LocalOrder
{
    public sealed partial class Stage : IClosedSet<Stage>
    {
        public static readonly Stage Draft = new();
        public static readonly Stage Shipped = new();
    }
}

/// <summary>A struct value object over a set of its own project.</summary>
public readonly partial record struct StatusOfOrder : IValue<LocalStatus>;

/// <summary>A record-class value object over a set of its own project.</summary>
public sealed partial record StatusLabel : IValue<LocalStatus>;

/// <summary>A value object over a nested set of its own project.</summary>
public readonly partial record struct StageOfOrder : IValue<LocalOrder.Stage>;

public enum ShippableFault { Cancelled }

/// <summary>A validated value object over a set of its own project: a cancelled order ships nothing.</summary>
public readonly partial record struct ShippableStatus : IValidatedValue<ShippableStatus, LocalStatus, ShippableFault>
{
    public static Result<ShippableStatus, ShippableFault> Create(LocalStatus value) =>
        ReferenceEquals(value, LocalStatus.Cancelled) ? Result.Error(ShippableFault.Cancelled) : new ShippableStatus(value);
}

/// <summary>A value object over a closed set of a referenced project.</summary>
public readonly partial record struct OverProbeStatus : IValue<ProbeStatus>;

/// <summary>
/// A value object over a closed set is generated in full wherever the set is declared (decision 60).
/// <c>ClosedSetAspect</c> ran last, after the value-object aspects, which then did not see the set's
/// <c>IParsable</c>, <c>Parse</c> and type converter: over a set of its own project a value object had no
/// parsing and the base <c>TypeConverter</c>, while over the same set in a referenced project it had both.
/// It runs first now.
/// </summary>
/// <remarks>
/// Everything that would be missing is reached through reflection or a generic constraint checked at
/// run time, so with the old order these fail rather than stop the project compiling.
/// </remarks>
public sealed class ClosedSetWrappingTests
{
    public static TheoryData<Type, string, object> Wrappers =>
    [
        (typeof(StatusOfOrder), "Shipped", LocalStatus.Shipped),
        (typeof(StatusLabel), "Shipped", LocalStatus.Shipped),
        (typeof(StageOfOrder), "Shipped", LocalOrder.Stage.Shipped),
        (typeof(ShippableStatus), "Shipped", LocalStatus.Shipped),
        (typeof(OverProbeStatus), "Shipped", ProbeStatus.Shipped),
    ];

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void It_parses_the_members_name_through_IParsable_and_its_own_Parse(Type type, string name, object member)
    {
        Value(Parse(type, name)).ShouldBeSameAs(member);
        Value(StaticParse(type, name)).ShouldBeSameAs(member);
        TryParse(type, name, out var parsed).ShouldBeTrue();
        Value(parsed!).ShouldBeSameAs(member);

        // As the set itself parses: ordinally, and with no span parsing, which the set does not have.
        TryParse(type, name.ToUpperInvariant(), out _).ShouldBeFalse();
        type.GetInterfaces().ShouldNotContain(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(ISpanParsable<>));
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void Its_own_type_converter_reads_and_writes_the_members_name(Type type, string name, object member)
    {
        var converter = TypeDescriptor.GetConverter(type);

        converter.GetType().ShouldNotBe(typeof(TypeConverter), "the value object's own type converter, not the base one");
        var converted = converter.ConvertFromInvariantString(name).ShouldNotBeNull();
        Value(converted).ShouldBeSameAs(member);
        converter.ConvertToInvariantString(converted).ShouldBe(name);
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void Its_JSON_is_the_members_name_as_a_value_and_as_a_key(Type type, string name, object member)
    {
        var json = $"\"{name}\"";
        var read = JsonSerializer.Deserialize(json, type).ShouldNotBeNull();
        Value(read).ShouldBeSameAs(member);
        JsonSerializer.Serialize(read, type).ShouldBe(json);

        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(type, typeof(int));
        var keys = JsonSerializer.Deserialize($"{{{json}:1}}", dictionaryType).ShouldNotBeNull();
        JsonSerializer.Serialize(keys, dictionaryType).ShouldBe($"{{{json}:1}}");
    }

    /// <summary>Every way in applies <c>Create</c>: a validated value object refuses the member its rules refuse.</summary>
    [Fact]
    public void Every_way_in_applies_Create()
    {
        var parse = Should.Throw<FormatException>(() => Parse(typeof(ShippableStatus), "Cancelled"));
        parse.Message.ShouldContain(nameof(ShippableFault.Cancelled));

        Should.Throw<FormatException>(() => StaticParse(typeof(ShippableStatus), "Cancelled"));
        TryParse(typeof(ShippableStatus), "Cancelled", out _).ShouldBeFalse();
        Should.Throw<Exception>(() => TypeDescriptor.GetConverter(typeof(ShippableStatus)).ConvertFromInvariantString("Cancelled"));
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ShippableStatus>("\"Cancelled\""));
    }

    /// <summary>A name that is no member is refused without quoting it, by every way in.</summary>
    [Theory]
    [MemberData(nameof(Wrappers))]
    public void A_name_that_is_no_member_is_refused_without_quoting_it(Type type, string name, object member)
    {
        _ = (name, member);
        const string secret = "Secret-Not-A-Member";

        var parse = Should.Throw<FormatException>(() => Parse(type, secret));
        var converter = Should.Throw<Exception>(() => TypeDescriptor.GetConverter(type).ConvertFromInvariantString(secret));
        var json = Should.Throw<JsonException>(() => JsonSerializer.Deserialize($"\"{secret}\"", type));

        foreach (var refusal in new[] { parse, converter, json })
        {
            for (Exception? exception = refusal; exception is not null; exception = exception.InnerException)
                exception.Message.ShouldNotContain(secret);
        }

        parse.Message.ShouldStartWith($"{type.Name} could not read the input as ");
    }

    private static object Value(object valueObject) =>
        valueObject.GetType().GetProperty("Value")!.GetValue(valueObject)!;

    /// <summary><c>T.Parse</c> in generic code, constrained to <c>IParsable&lt;T&gt;</c>; the constraint is checked when the method is made.</summary>
    private static object Parse(Type type, string text) =>
        Unwrapped(() => typeof(ClosedSetWrappingTests).GetMethod(nameof(ParseAs), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [text])!);

    private static bool TryParse(Type type, string text, out object? result)
    {
        object?[] arguments = [text, null];
        var parsed = (bool)Unwrapped(() => typeof(ClosedSetWrappingTests).GetMethod(nameof(TryParseAs), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, arguments)!);
        result = arguments[1];
        return parsed;
    }

    /// <summary>The value object's own public <c>Parse(string, IFormatProvider?)</c>.</summary>
    private static object StaticParse(Type type, string text) =>
        Unwrapped(() => type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(IFormatProvider)]).ShouldNotBeNull($"{type.Name} has no Parse(string, IFormatProvider?)").Invoke(null, [text, null])!);

    private static T ParseAs<T>(string text) where T : IParsable<T> => T.Parse(text, null);

    private static bool TryParseAs<T>(string text, out T? result) where T : IParsable<T> => T.TryParse(text, null, out result);

    private static T Unwrapped<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
