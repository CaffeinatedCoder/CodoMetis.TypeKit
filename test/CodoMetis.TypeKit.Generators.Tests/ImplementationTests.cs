using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>The core every value object gets: the wrapped value, equality, <c>From</c> and the materializer.</summary>
public sealed class ImplementationTests
{
    [Fact]
    public void From_wraps_and_Value_unwraps()
    {
        var id = Guid.CreateVersion7();

        ProbeId.From(id).Value.ShouldBe(id);
        ProbeName.From("name").Value.ShouldBe("name");
    }

    [Fact]
    public void Value_objects_are_equal_when_their_values_are()
    {
        (ProbeCount.From(1) == ProbeCount.From(1)).ShouldBeTrue();
        (ProbeCount.From(1) != ProbeCount.From(2)).ShouldBeTrue();
        ProbeLabel.From("a").ShouldBe(ProbeLabel.From("a"));
    }

    [Fact]
    public void Both_kinds_implement_the_equality_operators_interface()
    {
        typeof(ProbeCount).GetInterfaces().ShouldContain(typeof(IEqualityOperators<ProbeCount, ProbeCount, bool>));
        typeof(ProbeCode).GetInterfaces().ShouldContain(typeof(IEqualityOperators<ProbeCode, ProbeCode, bool>));
    }

    /// <summary>
    /// <c>From</c> over a reference type refuses null, as <c>Option.Some</c> does. It wrapped a null
    /// that <c>Value</c> promises it never holds: the value object serialized as a JSON null its own
    /// converter refuses to read, and a <c>Uri</c>-backed one threw <c>NullReferenceException</c>
    /// from <c>ToString</c>.
    /// </summary>
    [Fact]
    public void From_refuses_null()
    {
        Should.Throw<ArgumentNullException>(() => ProbeName.From(null!)).ParamName.ShouldBe("value");
        Should.Throw<ArgumentNullException>(() => ProbeUri.From(null!));
        Should.Throw<ArgumentNullException>(() => ProbeLabel.From(null!));
    }

    /// <summary>A validated value object's only factories are the ones that apply its rules.</summary>
    [Fact]
    public void Only_a_plain_value_object_gets_From()
    {
        typeof(ProbeName).GetInterfaces().ShouldContain(typeof(IValueWrapper<ProbeName, string>));
        typeof(ProbeCode).GetInterfaces().ShouldNotContain(typeof(IValueWrapper<ProbeCode, string>));
        typeof(ProbeCode).GetMethod("From", BindingFlags.Public | BindingFlags.Static).ShouldBeNull();
    }

    /// <summary>
    /// The validation-free path exists for persistence only, so it must not be callable as
    /// <c>ProbeCode.Materialize(...)</c>, only through a constrained type parameter.
    /// </summary>
    [Fact]
    public void The_materializer_is_implemented_but_not_on_the_public_surface()
    {
        typeof(ProbeCode).GetInterfaces().ShouldContain(typeof(IValueObjectMaterializer<ProbeCode, string>));
        typeof(ProbeCode).GetMethod("Materialize", BindingFlags.Public | BindingFlags.Static).ShouldBeNull();
    }

    [Fact]
    public void The_materializer_skips_validation()
    {
        ProbeCode.Create("lower").TryGetValue(out _, out _).ShouldBeFalse();

        Materialize<ProbeCode, string>("lower").Value.ShouldBe("lower");
        Materialize<ProbeId, Guid>(Guid.Empty).Value.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void The_parameterless_struct_constructor_is_hidden_from_IntelliSense()
    {
        var constructor = typeof(ProbeId).GetConstructor(Type.EmptyTypes).ShouldNotBeNull();

        constructor.GetCustomAttribute<EditorBrowsableAttribute>().ShouldNotBeNull().State.ShouldBe(EditorBrowsableState.Never);
    }

    /// <summary><c>New()</c> needs the generated <c>From</c>, so this is where it meets a real identifier.</summary>
    [Fact]
    public void A_generated_Guid_identifier_gets_New() => ProbeId.New().Value.Version.ShouldBe(7);

    private static T Materialize<T, TValue>(TValue value) where T : IValueObjectMaterializer<T, TValue> where TValue : notnull => T.Materialize(value);
}
