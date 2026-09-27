using System.Reflection;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Declared in this project, which reaches CodoMetis.TypeKit.Generators only through the probes
/// library: being generated at all is the transitive-fabric check.
/// </summary>
public readonly partial record struct TestLocalId : IValue<int>;

/// <summary>
/// The fabric reaches every value object it should. A type it skips compiles cleanly, and the
/// analyzer stays quiet because the generators are referenced, so only a test like this notices.
/// </summary>
public sealed class FabricTests
{
    public static TheoryData<Type> DeclaredValueObjects =>
        [.. ValueObjectDeclarations(typeof(ProbeId).Assembly), .. ValueObjectDeclarations(typeof(TestLocalId).Assembly)];

    /// <summary>The floor for the theory below, which would pass on an empty list.</summary>
    [Fact]
    public void The_declarations_are_discovered()
    {
        ValueObjectDeclarations(typeof(ProbeId).Assembly).Count().ShouldBeGreaterThanOrEqualTo(19);
        ValueObjectDeclarations(typeof(TestLocalId).Assembly).ShouldContain(typeof(TestLocalId));
    }

    [Theory]
    [MemberData(nameof(DeclaredValueObjects))]
    public void Every_declared_value_object_is_generated(Type type)
    {
        var marker     = type.GetInterfaces().Single(IsMarker);
        var plain      = marker.GetGenericTypeDefinition() == typeof(IValue<>);
        var underlying = plain ? marker.GetGenericArguments()[0] : marker.GetGenericArguments()[1];

        type.GetInterfaces().ShouldContain(typeof(IValueObject<,>).MakeGenericType(type, underlying));
        type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance).ShouldNotBeNull().PropertyType.ShouldBe(underlying);
        type.GetMethod(plain ? "From" : "TryFrom", BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull();
    }

    /// <summary>
    /// The declarations the fabric could plausibly miss are among those the theory above checks.
    /// They are checked by reflection only, so a fabric that skipped one fails that theory instead
    /// of breaking this project's build.
    /// </summary>
    [Theory]
    [InlineData(typeof(ProbeCustomerId), "a marker reached through another interface")]
    [InlineData(typeof(ProbeContainer.NestedId), "a nested type")]
    [InlineData(typeof(ProbeLabel), "a record class")]
    [InlineData(typeof(ProbeInternalId), "an internal type")]
    [InlineData(typeof(TestLocalId), "a project that references the generators only transitively")]
    public void The_edge_cases_are_covered(Type type, string edgeCase) =>
        DeclaredValueObjects.Select(row => row.Data).ShouldContain(type, edgeCase);

    /// <summary>A public extension class would not compile against an internal value object (CS0051).</summary>
    [Fact]
    public void An_internal_value_object_gets_an_internal_extension_class()
    {
        typeof(ProbeInternalIdExtensions).IsPublic.ShouldBeFalse();
        ProbeInternalId.From(4).GetValue().ShouldBe(4);
    }

    private static IEnumerable<Type> ValueObjectDeclarations(Assembly assembly) =>
        assembly.GetTypes().Where(type => type is { IsAbstract: false, IsInterface: false } && type.GetInterfaces().Any(IsMarker));

    private static bool IsMarker(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IValue<>) || type.GetGenericTypeDefinition() == typeof(IValidatedValue<,,>));
}
