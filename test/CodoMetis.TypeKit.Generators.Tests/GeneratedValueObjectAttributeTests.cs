using System.Reflection;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Every value object carries <see cref="GeneratedValueObjectAttribute{TValueObject,T}"/>, which is
/// how run-time code holding only a <see cref="Type"/> recognises it: trimming removes
/// <c>IValueObject&lt;,&gt;</c> from a type's interfaces when nothing uses it, and keeps attributes.
/// </summary>
public sealed class GeneratedValueObjectAttributeTests
{
    /// <summary>The value objects, found by their interface: the attribute has to agree with it.</summary>
    public static TheoryData<Type, Type> ValueObjects =>
    [
        .. typeof(ProbeId).Assembly.GetTypes()
                          .Select(type => (Type: type, Contract: type.GetInterfaces().SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueObject<,>))))
                          .Where(candidate => candidate.Contract is not null)
                          .Select(candidate => (candidate.Type, candidate.Contract!.GetGenericArguments()[1]))
    ];

    [Fact]
    public void The_value_objects_are_found() => ValueObjects.Count.ShouldBeGreaterThanOrEqualTo(19);

    [Theory]
    [MemberData(nameof(ValueObjects))]
    public void A_value_object_names_itself_and_what_it_wraps(Type valueObject, Type wrapped)
    {
        var attribute = valueObject.GetCustomAttribute<GeneratedValueObjectAttribute>().ShouldNotBeNull($"{valueObject} carries no [GeneratedValueObject]");

        attribute.ValueObjectType.ShouldBe(valueObject);
        attribute.ValueType.ShouldBe(wrapped);
    }

    [Theory]
    [MemberData(nameof(ValueObjects))]
    public void The_attribute_hands_over_both_types_as_type_arguments(Type valueObject, Type wrapped) =>
        valueObject.GetCustomAttribute<GeneratedValueObjectAttribute>()!.Accept(TypeArguments.Instance).ShouldBe((valueObject, wrapped));

    [Fact]
    public void A_type_that_is_not_a_value_object_carries_none() =>
        typeof(ProbeCodeFault).GetCustomAttribute<GeneratedValueObjectAttribute>().ShouldBeNull();

    private sealed class TypeArguments : IValueObjectVisitor<(Type, Type)>
    {
        public static readonly TypeArguments Instance = new();

        public (Type, Type) Visit<TValueObject, T>()
            where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
            where T : notnull =>
            (typeof(TValueObject), typeof(T));
    }
}
