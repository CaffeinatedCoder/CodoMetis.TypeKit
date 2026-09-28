using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// The SQL a query over value objects becomes, on PostgreSQL, from <c>ToQueryString</c>: no
/// database is needed. Each case pins the <c>WHERE</c> clause, so a translator that stopped
/// matching, or a converter that started casting, shows up as a changed string.
/// </summary>
public sealed class SqlTranslationTests
{
    private static readonly Guid FixedId = Guid.Parse("0199a3f4-1c00-7000-8000-000000000001");

    [Fact]
    public void Equality_on_a_value_object_compares_the_column() =>
        Where(o => o.Id == OrderId.From(FixedId)).ShouldBe("WHERE o.\"Id\" = '0199a3f4-1c00-7000-8000-000000000001'");

    [Fact]
    public void Contains_over_a_list_of_value_objects_is_ANY_over_the_column()
    {
        List<OrderId> ids = [OrderId.From(FixedId)];

        Where(o => ids.Contains(o.Id)).ShouldBe("WHERE o.\"Id\" = ANY (@ids)");
    }

    [Fact]
    public void Value_on_a_value_object_is_the_column() =>
        Where(o => o.Code.Value == "ABC").ShouldBe("WHERE o.\"Code\" = 'ABC'");

    [Fact]
    public void Value_supports_the_wrapped_type_s_own_operations() =>
        Where(o => o.Code.Value.StartsWith("A")).ShouldBe("WHERE o.\"Code\" LIKE 'A%'");

    [Fact]
    public void GetValue_is_the_column() =>
        Where(o => o.Quantity.GetValue() > 2).ShouldBe("WHERE o.\"Quantity\" > 2");

    /// <summary>The optional case: <c>ValueOrNull()</c> on a <c>Nullable&lt;ProbePercentage&gt;</c> is not a member access the member translator sees.</summary>
    [Fact]
    public void ValueOrNull_on_an_optional_value_object_is_the_column() =>
        Where(o => o.Discount.ValueOrNull() > 10).ShouldBe("WHERE o.\"Discount\" > 10");

    /// <summary>
    /// A record-class value object's <c>ValueOrNull()</c> takes the value object itself, not a
    /// <c>Nullable</c>, and returns a reference type: the translator's signature check must accept it too.
    /// </summary>
    [Fact]
    public void ValueOrNull_on_an_optional_record_class_value_object_is_the_column() =>
        Where(o => o.Note.ValueOrNull() == "urgent").ShouldBe("WHERE o.\"Note\" = 'urgent'");

    [Fact]
    public void Contains_on_a_primitive_collection_of_value_objects_is_ANY_over_the_array()
    {
        var tag = Tag.From("x");

        Where(o => o.Tags.Contains(tag)).ShouldBe("WHERE @tag = ANY (o.\"Tags\")");
    }

    /// <summary>
    /// The element of a primitive collection is a column of the collection's own table expression
    /// (<c>unnest</c> here). Re-typed to the wrapped type, it gave EF the wrapped type's mapping as the
    /// collection's element mapping, and the query failed to translate. Such a column is converted.
    /// </summary>
    [Fact]
    public void Value_on_the_element_of_a_primitive_collection_is_converted() =>
        Sql(o => o.Tags.Any(t => t.Value == "x")).ShouldContain("FROM unnest(o.\"Tags\") AS t(value)\n    WHERE t.value::text = 'x')");

    /// <summary>Only a value object's <c>Value</c> is translated; <c>Nullable&lt;int&gt;.Value</c> stays EF's.</summary>
    [Fact]
    public void Value_on_a_nullable_int_is_left_to_EF() =>
        Where(o => o.Priority!.Value > 3).ShouldBe("WHERE o.\"Priority\" > 3");

    /// <summary>
    /// The attribute is public, so a method can carry it by hand. One that does not go from a value
    /// object to what it wraps was translated as the column all the same, re-typed: the length of a
    /// code compared the text column with a number. EF is left to refuse it.
    /// </summary>
    [Fact]
    public void A_hand_marked_method_that_does_not_unwrap_is_not_translated() =>
        Should.Throw<InvalidOperationException>(() => Where(o => o.Code.Length() == 3)).Message.ShouldContain("could not be translated");

    private static string Sql(System.Linq.Expressions.Expression<Func<Order, bool>> predicate)
    {
        using var db = new TestDb(TestDb.NpgsqlWithoutServer());

        return db.Orders.Where(predicate).ToQueryString();
    }

    private static string Where(System.Linq.Expressions.Expression<Func<Order, bool>> predicate)
    {
        using var db = new TestDb(TestDb.NpgsqlWithoutServer());

        return db.Orders.Where(predicate).ToQueryString().Split('\n').Single(line => line.StartsWith("WHERE", StringComparison.Ordinal)).Trim();
    }
}

internal static class HandMarked
{
    /// <summary>Carries the attribute without being the unwrap it promises.</summary>
    [TranslatedAsWrappedValue]
    public static int Length(this ProbeCode code) => code.Value.Length;
}
