using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// Every value object in the model maps as a scalar column of the type it wraps, found by
/// <c>UseTypeKit()</c> alone: no per-type registration, no scan.
/// </summary>
public sealed class MappingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public static TheoryData<string, Type, Type> ValueObjectProperties => new()
    {
        { nameof(Order.Id), typeof(OrderId), typeof(Guid) },
        { nameof(Order.CustomerId), typeof(CustomerId), typeof(Guid) },
        { nameof(Order.Code), typeof(ProbeCode), typeof(string) },
        { nameof(Order.Discount), typeof(ProbePercentage), typeof(int) },
        { nameof(Order.Quantity), typeof(ProbeCount), typeof(int) },
        { nameof(Order.Amount), typeof(ProbeAmount), typeof(decimal) },
        { nameof(Order.Urgent), typeof(ProbeFlag), typeof(bool) },
        { nameof(Order.PlacedAt), typeof(ProbeTimestamp), typeof(DateTime) },
        { nameof(Order.DueOn), typeof(ProbeDate), typeof(DateOnly) },
        { nameof(Order.ConfirmedAt), typeof(ProbeMoment), typeof(DateTimeOffset) },
        { nameof(Order.Slot), typeof(ProbeTime), typeof(TimeOnly) },
        { nameof(Order.Note), typeof(ProbeLabel), typeof(string) },
    };

    [Theory]
    [MemberData(nameof(ValueObjectProperties))]
    public void A_value_object_property_maps_through_the_value_object_converter(string property, Type valueObject, Type wrapped)
    {
        using var db = new TestDb(TestDb.Sqlite(_connection));

        var mapped = db.Model.FindEntityType(typeof(Order))!.FindProperty(property).ShouldNotBeNull($"{property} is not mapped as a property");

        mapped.GetTypeMapping().Converter.ShouldBeOfType(typeof(ValueObjectConverter<,>).MakeGenericType(valueObject, wrapped));
    }

    [Fact]
    public void Keys_and_foreign_keys_can_be_value_objects()
    {
        using var db = new TestDb(TestDb.Sqlite(_connection));
        var order = db.Model.FindEntityType(typeof(Order))!;

        order.FindProperty(nameof(Order.Id))!.IsPrimaryKey().ShouldBeTrue();
        order.FindProperty(nameof(Order.CustomerId))!.IsForeignKey().ShouldBeTrue();
        order.FindNavigation(nameof(Order.Customer)).ShouldNotBeNull();
    }

    [Fact]
    public void Optional_value_objects_are_nullable_columns()
    {
        using var db = new TestDb(TestDb.Sqlite(_connection));
        var order = db.Model.FindEntityType(typeof(Order))!;

        order.FindProperty(nameof(Order.Discount))!.IsNullable.ShouldBeTrue();
        order.FindProperty(nameof(Order.Note))!.IsNullable.ShouldBeTrue();
        order.FindProperty(nameof(Order.Code))!.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void A_primitive_collection_converts_each_element()
    {
        using var db = new TestDb(TestDb.Sqlite(_connection));

        var tags = db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Tags))!;

        tags.IsPrimitiveCollection.ShouldBeTrue();
        tags.GetElementType().ShouldNotBeNull().GetTypeMapping().Converter.ShouldBeOfType<ValueObjectConverter<Tag, string>>();
    }

    [Fact]
    public void A_property_that_is_not_a_value_object_is_left_alone()
    {
        using var db = new TestDb(TestDb.Sqlite(_connection));

        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Priority))!.GetTypeMapping().Converter.ShouldBeNull();
    }

    /// <summary>The control: without <c>UseTypeKit()</c>, EF cannot map the model at all.</summary>
    [Fact]
    public void Without_UseTypeKit_the_model_does_not_build()
    {
        using var db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options);

        Should.Throw<InvalidOperationException>(() => db.Model);
    }

    [Fact]
    public void UseTypeKit_works_before_the_provider_too()
    {
        using var db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseTypeKit().UseSqlite(_connection).Options);

        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Id))!.GetTypeMapping().Converter.ShouldBeOfType<ValueObjectConverter<OrderId, Guid>>();
    }

    /// <summary>
    /// A strongly-typed-id library commonly replaces EF's converter selector. The mapping is a plugin
    /// and replaces nothing, so both work; a replaced selector of ours would have lost to it.
    /// </summary>
    [Fact]
    public void UseTypeKit_coexists_with_a_library_that_replaces_the_converter_selector()
    {
        using var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
                                  .UseSqlite(_connection)
                                  .UseTypeKit()
                                  .ReplaceService<IValueConverterSelector, OtherLibrarySelector>()
                                  .Options);

        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Id))!.GetTypeMapping().Converter.ShouldBeOfType<ValueObjectConverter<OrderId, Guid>>();
    }

    [Fact]
    public void A_configured_column_facet_is_kept()
    {
        using var db = new TestDb(TestDb.NpgsqlWithoutServer());
        var code = db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Code))!;

        code.GetColumnType().ShouldBe("character varying(10)");
        code.GetTypeMapping().Converter.ShouldBeOfType<ValueObjectConverter<ProbeCode, string>>();
    }

    [Fact]
    public void An_application_that_builds_the_internal_service_provider_uses_AddEntityFrameworkTypeKit()
    {
        var internalServices = new ServiceCollection().AddEntityFrameworkSqlite().AddEntityFrameworkTypeKit().BuildServiceProvider();
        using var db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).UseInternalServiceProvider(internalServices).Options);

        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Id))!.GetTypeMapping().Converter.ShouldBeOfType<ValueObjectConverter<OrderId, Guid>>();
        db.Orders.Where(o => o.Code.Value == "ABC").ToQueryString().ShouldContain("\"o\".\"Code\" = 'ABC'");
    }

    /// <summary>
    /// No per-type comparer: a value object's record equality is EF's default comparer, and it must
    /// agree with comparing the wrapped values, or change tracking would miss or invent changes.
    /// </summary>
    [Fact]
    public void The_default_comparer_agrees_with_comparing_the_wrapped_values()
    {
        using var db = new TestDb(TestDb.Sqlite(_connection));
        var comparer = db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Code))!.GetValueComparer();

        var a = ProbeCode.FromKnownGood("ABC");
        var b = ProbeCode.FromKnownGood("ABC");
        var c = ProbeCode.FromKnownGood("XYZ");

        comparer.Equals(a, b).ShouldBe(a.Value == b.Value);
        comparer.Equals(a, c).ShouldBe(a.Value == c.Value);
        comparer.GetHashCode(a).ShouldBe(comparer.GetHashCode(b));
        comparer.Snapshot(a).ShouldBe(a);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class OtherLibrarySelector(ValueConverterSelectorDependencies dependencies) : ValueConverterSelector(dependencies);
}
