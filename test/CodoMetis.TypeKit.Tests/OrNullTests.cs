using Microsoft.CodeAnalysis;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>OrNull()</c> unwraps an option of either kind into a nullable: an <c>Option&lt;int&gt;</c> into an
/// <c>int?</c>, an <c>Option&lt;string&gt;</c> into a <c>string?</c>.
/// </summary>
/// <remarks>
/// <para>
/// It existed only for a reference type, while the JSON refusal told an <c>Option&lt;int&gt;</c> to
/// convert with <c>OrNull()</c>, which did not compile (CS0452). The value-type one lives in
/// <see cref="ValueTypeOptionExtensions"/>, since the two lower to the same signature, and the
/// constraint picks between them.
/// </para>
/// <para>
/// The resolution is measured by compiling a consumer's code: which class each call binds to, for a
/// closed option of each kind and for an option inside generic code constrained each way, and that
/// nothing is ambiguous.
/// </para>
/// </remarks>
public sealed class OrNullTests
{
    private const string ForValueTypes = nameof(ValueTypeOptionExtensions);

    private const string ForReferenceTypes = nameof(Option);

    public static TheoryData<string, string, string> Calls => new()
    {
        { "Option<int>", "int? Unwrap(Option<int> option) => option.OrNull();", ForValueTypes },
        { "Option<DayOfWeek>", "System.DayOfWeek? Unwrap(Option<System.DayOfWeek> option) => option.OrNull();", ForValueTypes },
        { "Option<string>", "string? Unwrap(Option<string> option) => option.OrNull();", ForReferenceTypes },
        { "Option<object>", "object? Unwrap(Option<object> option) => option.OrNull();", ForReferenceTypes },
        { "Option<IComparable>", "System.IComparable? Unwrap(Option<System.IComparable> option) => option.OrNull();", ForReferenceTypes },
        { "T : struct", "T? Unwrap<T>(Option<T> option) where T : struct => option.OrNull();", ForValueTypes },
        { "T : unmanaged", "T? Unwrap<T>(Option<T> option) where T : unmanaged => option.OrNull();", ForValueTypes },
        { "T : class", "T? Unwrap<T>(Option<T> option) where T : class => option.OrNull();", ForReferenceTypes },
        { "static form, value type", "int? Unwrap(Option<int> option) => ValueTypeOptionExtensions.OrNull(option);", ForValueTypes },
        { "static form, reference type", "string? Unwrap(Option<string> option) => Option.OrNull(option);", ForReferenceTypes },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public void The_constraint_picks_the_OrNull_of_the_option_s_kind(string @case, string member, string declaringClass)
    {
        var compilation = ConsumerCompilation.Of(Consumer(member));
        compilation.Problems().ShouldBeEmpty(@case);

        var (unwrap, method) = compilation.TheOnlyCall();

        var declaredBy = method.ContainingType.IsExtension ? method.ContainingType.ContainingType : method.ContainingType;
        declaredBy.Name.ShouldBe(declaringClass, @case);
        SymbolEqualityComparer.IncludeNullability.Equals(method.ReturnType, unwrap.ReturnType)
                              .ShouldBeTrue($"{@case}: OrNull() returns {method.ReturnType}, not {unwrap.ReturnType}.");
    }

    /// <summary>
    /// An option whose kind generic code does not know has no <c>OrNull()</c>, as before: neither
    /// constraint holds, and the compiler reports the same CS0452 it did with one candidate, not an
    /// ambiguity (CS0121). Measured 2026-09-28.
    /// </summary>
    [Fact]
    public void An_option_of_either_kind_has_no_OrNull_and_nothing_is_ambiguous()
    {
        var errors = ConsumerCompilation.Of(Consumer("object? Unwrap<T>(Option<T> option) where T : notnull => option.OrNull();")).ErrorIds();

        errors.ShouldBe(["CS0452"]);
    }

    [Fact]
    public void OrNull_unwraps_both_kinds_and_round_trips_with_ToOption()
    {
        Option.Some(5).OrNull().ShouldBe(5);
        Option.None<int>().OrNull().ShouldBeNull();
        Option.Some(5).OrNull().ToOption().ShouldBe(Option.Some(5));

        Option.Some("value").OrNull().ShouldBe("value");
        Option.None<string>().OrNull().ShouldBeNull();
        Option.Some("value").OrNull().ToOption().ShouldBe(Option.Some("value"));
    }

    private static string Consumer(string member) =>
        $$"""
          using CodoMetis.TypeKit;

          public static class Consumer
          {
              public static {{member}}
          }
          """;
}
