using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// <c>ToString</c>, <c>IFormattable</c> and the span formats. A value object formats as its value,
/// unlike <c>Option</c>/<c>Result</c>, whose <c>ToString</c> never prints their content.
/// </summary>
public sealed class FormattingTests
{
    [Fact]
    public void ToString_is_the_value_in_the_invariant_culture()
    {
        ProbeName.From("text").ToString().ShouldBe("text");
        ProbeAmount.From(1234.5m).ToString().ShouldBe("1234.5");
    }

    [Fact]
    public void IFormattable_honours_the_format_and_the_culture() =>
        ProbeAmount.From(1234.5m).ToString("N1", CultureInfo.GetCultureInfo("de-DE")).ShouldBe("1.234,5");

    [Fact]
    public void A_span_formattable_value_formats_into_a_span_and_into_UTF_8()
    {
        var count = ProbeCount.From(42);

        Span<char> chars = stackalloc char[8];
        count.TryFormat(chars, out var charsWritten, default, CultureInfo.InvariantCulture).ShouldBeTrue();
        chars[..charsWritten].ToString().ShouldBe("42");

        Span<byte> bytes = stackalloc byte[8];
        ((IUtf8SpanFormattable)count).TryFormat(bytes, out var bytesWritten, default, CultureInfo.InvariantCulture).ShouldBeTrue();
        Encoding.UTF8.GetString(bytes[..bytesWritten]).ShouldBe("42");
    }
}

/// <summary>Comparison, generated when the wrapped type is comparable.</summary>
public sealed class ComparisonTests
{
    [Fact]
    public void Value_objects_order_by_their_values()
    {
        ProbeCount[] counts = [ProbeCount.From(3), ProbeCount.From(1), ProbeCount.From(2)];

        counts.Order().Select(count => count.Value).ShouldBe([1, 2, 3]);
        (ProbeCount.From(1) < ProbeCount.From(2)).ShouldBeTrue();
        (ProbeCount.From(2) >= ProbeCount.From(2)).ShouldBeTrue();
        typeof(ProbeCount).GetInterfaces().ShouldContain(typeof(IComparisonOperators<ProbeCount, ProbeCount, bool>));
    }

    [Fact]
    public void The_non_generic_CompareTo_orders_null_first_and_refuses_another_type()
    {
        IComparable count = ProbeCount.From(1);

        count.CompareTo(null).ShouldBe(1);
        Should.Throw<ArgumentException>(() => count.CompareTo(1));
    }

    [Fact]
    public void A_value_object_over_an_incomparable_type_is_not_comparable() =>
        typeof(IComparable).IsAssignableFrom(typeof(ProbeUri)).ShouldBeFalse();
}

/// <summary><c>MinValue</c>/<c>MaxValue</c> for a plain value object whose wrapped type has them.</summary>
public sealed class MinMaxValueTests
{
    [Fact]
    public void A_plain_number_gets_the_bounds_of_its_wrapped_type()
    {
        ProbeCount.MinValue.Value.ShouldBe(int.MinValue);
        ProbeCount.MaxValue.Value.ShouldBe(int.MaxValue);
        typeof(ProbeCount).GetInterfaces().ShouldContain(typeof(IMinMaxValue<ProbeCount>));
    }

    [Fact]
    public void A_type_without_bounds_gets_none() => typeof(ProbeName).GetProperty("MinValue").ShouldBeNull();
}

/// <summary>The nested <c>TypeConverter</c>, which delegates to the generated parsing and formatting.</summary>
public sealed class TypeConverterTests
{
    [Fact]
    public void The_type_carries_its_generated_converter()
    {
        var converter = TypeDescriptor.GetConverter(typeof(ProbeCount));

        converter.ShouldBeOfType<ProbeCount.ProbeCountTypeConverter>();
        converter.CanConvertFrom(typeof(string)).ShouldBeTrue();
        converter.CanConvertTo(typeof(string)).ShouldBeTrue();
    }

    [Fact]
    public void It_converts_from_and_to_a_string()
    {
        var converter = TypeDescriptor.GetConverter(typeof(ProbeCount));

        converter.ConvertFromInvariantString("42").ShouldBe(ProbeCount.From(42));
        converter.ConvertToInvariantString(ProbeCount.From(42)).ShouldBe("42");
    }
}

/// <summary><c>IConvertible</c>, implemented explicitly when the wrapped type is convertible.</summary>
public sealed class ConvertibleTests
{
    [Fact]
    public void A_convertible_value_converts_like_its_value()
    {
        Convert.ToInt64(ProbeCount.From(42), CultureInfo.InvariantCulture).ShouldBe(42L);
        Convert.ToString(ProbeCount.From(42), CultureInfo.InvariantCulture).ShouldBe("42");
        ((IConvertible)ProbeCount.From(42)).GetTypeCode().ShouldBe(TypeCode.Int32);
    }

    [Fact]
    public void A_value_object_over_a_non_convertible_type_is_not_convertible() =>
        typeof(IConvertible).IsAssignableFrom(typeof(ProbeId)).ShouldBeFalse();
}

/// <summary>The <c>GetValue</c>/<c>ValueOrNull</c> companions a query translates to the column.</summary>
public sealed class ExtensionsTests
{
    [Fact]
    public void GetValue_and_ValueOrNull_unwrap()
    {
        ProbeCount.From(42).GetValue().ShouldBe(42);
        ((ProbeCount?)ProbeCount.From(42)).ValueOrNull().ShouldBe(42);
        ((ProbeCount?)null).ValueOrNull().ShouldBeNull();
        ((ProbeLabel?)null).ValueOrNull().ShouldBeNull();
    }

    [Fact]
    public void Both_are_marked_for_translation()
    {
        foreach (var name in new[] { "GetValue", "ValueOrNull" })
            typeof(ProbeCountExtensions).GetMethod(name).ShouldNotBeNull().GetCustomAttribute<TranslatedAsUnderlyingValueAttribute>().ShouldNotBeNull();
    }
}
