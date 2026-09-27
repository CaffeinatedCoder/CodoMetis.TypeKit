using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Every generated way into a validated value object applies its <c>Create</c>. A way in that
/// skipped it would hand out instances the rules refused, which SECURITY.md lists first among the
/// issues in scope.
/// </summary>
/// <remarks>
/// <para>
/// Three checks per entry point, so none can pass by accident: an invalid input is refused, a valid
/// one is accepted, and an input <c>Create</c> normalises comes out normalised. The last one fails
/// for an entry point that validates and then wraps the raw input.
/// </para>
/// <para>
/// The completeness tests hold the lists to the generated surface: a new public static member that
/// produces an instance fails them until it has a case here. The one exception is the explicit
/// <c>IValueObjectMaterializer</c>, which skips validation by contract and is not public.
/// </para>
/// </remarks>
public sealed class EntryPointValidationTests
{
    private enum Outcome { Refused, Accepted }

    private static readonly Dictionary<string, Func<string, (Outcome Outcome, object? Value)>> PercentageEntryPoints = new()
    {
        ["Create(Int32)"]                = input => FromResult(ProbePercentage.Create(int.Parse(input)).TryGetValue(out var v, out _), v),
        ["TryFrom(Int32)"]               = input => FromResult(ProbePercentage.TryFrom(int.Parse(input)).TryGetValue(out var v), v),
        ["FromKnownGood(Int32)"]         = input => Catching<InvalidOperationException>(() => ProbePercentage.FromKnownGood(int.Parse(input))),
        ["Parse(String)"]                = input => Catching<FormatException>(() => ProbePercentage.Parse(input, null)),
        ["TryParse(String)"]             = input => FromResult(ProbePercentage.TryParse(input, null, out var v), v),
        ["Parse(ReadOnlySpan<Char>)"]    = input => Catching<FormatException>(() => ProbePercentage.Parse(input.AsSpan(), null)),
        ["TryParse(ReadOnlySpan<Char>)"] = input => FromResult(ProbePercentage.TryParse(input.AsSpan(), null, out var v), v),
        ["Parse(ReadOnlySpan<Byte>)"]    = input => Catching<FormatException>(() => ProbePercentage.Parse(Encoding.UTF8.GetBytes(input), null)),
        ["TryParse(ReadOnlySpan<Byte>)"] = input => FromResult(ProbePercentage.TryParse(Encoding.UTF8.GetBytes(input), null, out var v), v),
        ["JSON value"]                   = input => Catching<JsonException>(() => JsonSerializer.Deserialize<ProbePercentage>(input)),
        ["JSON property name"]           = input => Catching<JsonException>(() => JsonSerializer.Deserialize<Dictionary<ProbePercentage, int>>($"{{\"{input}\":1}}")!.Keys.Single()),
        ["TypeConverter"]                = input => Catching<FormatException>(() => TypeDescriptor.GetConverter(typeof(ProbePercentage)).ConvertFromInvariantString(input)),
    };

    private static readonly Dictionary<string, Func<string, (Outcome Outcome, object? Value)>> CodeEntryPoints = new()
    {
        ["Create(String)"]        = input => FromResult(ProbeCode.Create(input).TryGetValue(out var v, out _), v),
        ["TryFrom(String)"]       = input => FromResult(ProbeCode.TryFrom(input).TryGetValue(out var v), v),
        ["FromKnownGood(String)"] = input => Catching<InvalidOperationException>(() => ProbeCode.FromKnownGood(input)),
        ["Parse(String)"]         = input => Catching<FormatException>(() => ProbeCode.Parse(input, null)),
        ["TryParse(String)"]      = input => FromResult(ProbeCode.TryParse(input, null, out var v), v),
        ["JSON value"]            = input => Catching<JsonException>(() => JsonSerializer.Deserialize<ProbeCode>(JsonSerializer.Serialize(input))),
        ["JSON property name"]    = input => Catching<JsonException>(() => JsonSerializer.Deserialize<Dictionary<ProbeCode, int>>($"{{{JsonSerializer.Serialize(input)}:1}}")!.Keys.Single()),
        ["TypeConverter"]         = input => Catching<FormatException>(() => TypeDescriptor.GetConverter(typeof(ProbeCode)).ConvertFromInvariantString(input)),
    };

    public static TheoryData<string> PercentageEntryPointNames => [.. PercentageEntryPoints.Keys];

    public static TheoryData<string> CodeEntryPointNames => [.. CodeEntryPoints.Keys];

    [Theory]
    [MemberData(nameof(PercentageEntryPointNames))]
    public void A_number_the_rules_refuse_is_refused_by_every_entry_point(string entryPoint) =>
        PercentageEntryPoints[entryPoint]("101").Outcome.ShouldBe(Outcome.Refused, $"{entryPoint} accepted 101, which ProbePercentage.Create refuses");

    [Theory]
    [MemberData(nameof(PercentageEntryPointNames))]
    public void A_number_the_rules_accept_is_accepted_by_every_entry_point(string entryPoint)
    {
        var (outcome, value) = PercentageEntryPoints[entryPoint]("50");

        outcome.ShouldBe(Outcome.Accepted, $"{entryPoint} refused 50");
        value.ShouldBe(ProbePercentage.FromKnownGood(50));
    }

    [Theory]
    [MemberData(nameof(CodeEntryPointNames))]
    public void Text_the_rules_refuse_is_refused_by_every_entry_point(string entryPoint) =>
        CodeEntryPoints[entryPoint]("ab").Outcome.ShouldBe(Outcome.Refused, $"{entryPoint} accepted \"ab\", which ProbeCode.Create refuses");

    /// <summary><c>Create</c> trims, so an entry point that checks and then wraps the raw input keeps the spaces.</summary>
    [Theory]
    [MemberData(nameof(CodeEntryPointNames))]
    public void Every_entry_point_returns_what_Create_made_of_the_input(string entryPoint)
    {
        var (outcome, value) = CodeEntryPoints[entryPoint]("  ABC  ");

        outcome.ShouldBe(Outcome.Accepted, $"{entryPoint} refused \"  ABC  \"");
        value.ShouldBeOfType<ProbeCode>().Value.ShouldBe("ABC");
    }

    [Fact]
    public void Every_public_way_to_a_percentage_has_a_case() =>
        PublicFactories(typeof(ProbePercentage)).ShouldBe(PercentageEntryPoints.Keys.Where(key => key.Contains('(')).Order(), ignoreOrder: true);

    [Fact]
    public void Every_public_way_to_a_code_has_a_case() =>
        PublicFactories(typeof(ProbeCode)).ShouldBe(CodeEntryPoints.Keys.Where(key => key.Contains('(')).Order(), ignoreOrder: true);

    /// <summary>The refusal names the rule, never the refused value: input can be a secret.</summary>
    [Fact]
    public void A_JSON_refusal_names_the_type_and_the_fault_but_not_the_value()
    {
        var exception = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ProbeCode>("\"s3cr3t\""));

        exception.Message.ShouldContain(nameof(ProbeCode));
        exception.Message.ShouldContain(nameof(ProbeCodeFault.NotUpperCase));
        exception.Message.ShouldNotContain("s3cr3t");
    }

    [Fact]
    public void A_parse_refusal_names_the_type_and_the_fault_but_not_the_value()
    {
        var exception = Should.Throw<FormatException>(() => ProbeCode.Parse("s3cr3t", null));

        exception.Message.ShouldContain(nameof(ProbeCode));
        exception.Message.ShouldContain(nameof(ProbeCodeFault.NotUpperCase));
        exception.Message.ShouldNotContain("s3cr3t");
    }

    /// <summary>
    /// <c>int.MinValue</c> is not a percentage. The plain probes do get <c>MinValue</c>
    /// (<see cref="MinMaxValueTests"/>), so its absence here is the rule, not an accident.
    /// </summary>
    [Fact]
    public void A_validated_number_gets_no_MinValue_or_MaxValue()
    {
        typeof(ProbePercentage).GetProperty("MinValue").ShouldBeNull();
        typeof(ProbePercentage).GetProperty("MaxValue").ShouldBeNull();
        typeof(ProbePercentage).GetInterfaces().ShouldNotContain(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(System.Numerics.IMinMaxValue<>));
    }

    private static (Outcome, object?) FromResult(bool accepted, object? value) => accepted ? (Outcome.Accepted, value) : (Outcome.Refused, null);

    private static (Outcome, object?) Catching<TException>(Func<object?> entryPoint) where TException : Exception
    {
        try
        {
            return (Outcome.Accepted, entryPoint());
        }
        catch (TException)
        {
            return (Outcome.Refused, null);
        }
    }

    /// <summary>
    /// Every public static method that returns an instance, an Option or a Result of one, or hands
    /// one out through an out parameter, as <c>Name(FirstParameterType)</c>.
    /// </summary>
    private static IEnumerable<string> PublicFactories(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && Produces(method, type))
            .Select(method => $"{method.Name}({Display(method.GetParameters()[0].ParameterType)})")
            .Order();

    private static bool Produces(MethodInfo method, Type type) =>
        method.ReturnType == type
     || method.ReturnType is { IsGenericType: true } returned && returned.GetGenericArguments()[0] == type
     || method.GetParameters().Any(parameter => parameter.IsOut && parameter.ParameterType.GetElementType() == type);

    private static string Display(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>" : type.Name;
}
