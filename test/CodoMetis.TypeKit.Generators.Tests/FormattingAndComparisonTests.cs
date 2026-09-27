using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using CodoMetis.TypeKit.CompilerServices;
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

    /// <summary>
    /// A null provider means the invariant culture in every generated format, as it does in the
    /// generated parsing. Interpolation, <c>string.Format</c> and <c>Convert.ToString</c> pass null,
    /// which meant the current culture: in de-DE <c>$"{amount}"</c> was "1,5" while
    /// <c>amount.ToString()</c> was "1.5", and <c>Parse($"{amount}", null)</c> read it as 15.
    /// </summary>
    [Fact]
    public void A_null_format_provider_means_the_invariant_culture_in_every_format()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            var amount = ProbeAmount.From(1.5m);

            $"{amount}".ShouldBe("1.5");
            string.Format("{0}", amount).ShouldBe("1.5");
            amount.ToString(null, null).ShouldBe("1.5");
            Convert.ToString(amount).ShouldBe("1.5");
            ProbeAmount.Parse($"{amount}", null).ShouldBe(amount);

            Span<byte> bytes = stackalloc byte[16];
            ((IUtf8SpanFormattable)amount).TryFormat(bytes, out var bytesWritten, default, null).ShouldBeTrue();
            Encoding.UTF8.GetString(bytes[..bytesWritten]).ShouldBe("1.5");

            // A provider that is given is used as given.
            string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{amount}").ShouldBe("1,5");
            Convert.ToString(amount, CultureInfo.GetCultureInfo("de-DE")).ShouldBe("1,5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

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

    /// <summary>
    /// A hand-written <c>CompareTo(TSelf)</c> is the one comparison seam, as <c>TryFrom</c> is for the
    /// factories: it is kept, and the object overload, the operators and the interfaces are derived
    /// from it. A pin of behaviour that was already so, kept beside the CMTK1008 guard for the other
    /// comparison members.
    /// </summary>
    [Fact]
    public void A_hand_written_CompareTo_is_the_seam_and_the_rest_follows_it()
    {
        var one = ProbeDescending.From(1);
        var two = ProbeDescending.From(2);

        one.CompareTo(two).ShouldBePositive();
        (one < two).ShouldBeFalse();
        (one > two).ShouldBeTrue();
        (one <= two).ShouldBeFalse();
        (two >= one).ShouldBeFalse();
        ((IComparable)one).CompareTo(two).ShouldBePositive();
        new SortedSet<ProbeDescending> { one, two }.Select(value => value.Value).ShouldBe([2, 1]);

        typeof(ProbeDescending).GetInterfaces().ShouldContain(typeof(IComparable<ProbeDescending>));
        typeof(ProbeDescending).GetInterfaces().ShouldContain(typeof(IComparable));
        typeof(ProbeDescending).GetInterfaces().ShouldContain(typeof(IComparisonOperators<ProbeDescending, ProbeDescending, bool>));
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

    /// <summary>
    /// The record's equality is ordinal, so the ordering must be too. Culture-sensitive comparison
    /// puts "a" before "B" and treats a zero-width space as nothing, so a sorted set held one of two
    /// values a hash set kept apart.
    /// </summary>
    [Fact]
    public void A_string_value_object_orders_ordinally_so_ordering_agrees_with_equality()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            var plain    = ProbeName.From("ABC");
            var invisible = ProbeName.From("A​BC");

            (plain == invisible).ShouldBeFalse();
            plain.CompareTo(invisible).ShouldNotBe(0);
            new SortedSet<ProbeName> { plain, invisible }.Count.ShouldBe(2);

            ProbeName.From("B").CompareTo(ProbeName.From("a")).ShouldBeLessThan(0);
            (ProbeName.From("B") < ProbeName.From("a")).ShouldBeTrue();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>An enum orders through the non-generic <c>IComparable</c> its base type implements.</summary>
    [Fact]
    public void An_enum_value_object_orders_by_its_value() =>
        (ProbeWeekday.From(DayOfWeek.Monday) < ProbeWeekday.From(DayOfWeek.Tuesday)).ShouldBeTrue();

    /// <summary>
    /// A record class is a reference type, so <c>CompareTo</c> and the operators meet null. The BCL
    /// rule is that null sorts first; both threw <c>NullReferenceException</c> instead.
    /// </summary>
    [Fact]
    public void A_record_class_value_object_sorts_null_first()
    {
        var        label       = ProbeLabel.From("a");
        ProbeLabel none        = null!; // `null!` so this compiles against non-nullable parameters too, and fails at run time rather than at build time without the fix
        ProbeLabel anotherNone = null!;

        label.CompareTo(none).ShouldBePositive();
        (none < label).ShouldBeTrue();
        (label > none).ShouldBeTrue();
        (label < none).ShouldBeFalse();
        (label >= none).ShouldBeTrue();
        (none <= anotherNone).ShouldBeTrue();
        (none < anotherNone).ShouldBeFalse();
    }

    /// <summary>
    /// The generated signatures say so too: <c>IComparable&lt;T&gt;.CompareTo(T?)</c> and the record's
    /// own equality operators take a nullable operand for a reference type, and so do these.
    /// </summary>
    [Fact]
    public void A_record_class_value_object_declares_its_comparison_operands_nullable()
    {
        var nullability = new NullabilityInfoContext();

        var compareTo = typeof(ProbeLabel).GetMethod(nameof(IComparable<>.CompareTo), [typeof(ProbeLabel)]).ShouldNotBeNull();
        nullability.Create(compareTo.GetParameters()[0]).ReadState.ShouldBe(NullabilityState.Nullable);

        var lessThan = typeof(ProbeLabel).GetMethod("op_LessThan", [typeof(ProbeLabel), typeof(ProbeLabel)]).ShouldNotBeNull();
        nullability.Create(lessThan.GetParameters()[0]).ReadState.ShouldBe(NullabilityState.Nullable);
        nullability.Create(lessThan.GetParameters()[1]).ReadState.ShouldBe(NullabilityState.Nullable);

        var structCompareTo = typeof(ProbeCount).GetMethod(nameof(IComparable<>.CompareTo), [typeof(ProbeCount)]).ShouldNotBeNull();
        nullability.Create(structCompareTo.GetParameters()[0]).ReadState.ShouldBe(NullabilityState.NotNull);
    }
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
            typeof(ProbeCountExtensions).GetMethod(name).ShouldNotBeNull().GetCustomAttribute<TranslatedAsWrappedValueAttribute>().ShouldNotBeNull();
    }
}
