using System.ComponentModel;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-003: a type <see cref="TypeDescriptor.GetConverter(Type)"/> described before
/// <see cref="TypeDescriptor.RegisterType{T}"/> stays unregistered, and
/// <see cref="TypeDescriptor.GetConverterFromRegisteredType(Type)"/> refuses it for the rest of the process.
/// </summary>
/// <remarks>
/// TypeDescriptor's state is per process, so each test has a type of its own that nothing else touches.
/// </remarks>
public sealed class KB003
{
    [TypeConverter(typeof(RegisteredFirstConverter))]
    public sealed class RegisteredFirst;

    public sealed class RegisteredFirstConverter : TypeConverter;

    [TypeConverter(typeof(DescribedFirstConverter))]
    public sealed class DescribedFirst;

    public sealed class DescribedFirstConverter : TypeConverter;

    [Fact]
    public void A_type_registered_first_is_registered_and_its_converter_found()
    {
        TypeDescriptor.RegisterType<RegisteredFirst>();

        TypeDescriptor.GetProvider(typeof(RegisteredFirst)).IsRegisteredType(typeof(RegisteredFirst)).ShouldBeTrue();
        TypeDescriptor.GetConverterFromRegisteredType(typeof(RegisteredFirst)).ShouldBeOfType<RegisteredFirstConverter>();
    }

    [Fact]
    public void A_type_described_first_cannot_be_registered_and_the_registered_lookup_refuses_it()
    {
        TypeDescriptor.GetConverter(typeof(DescribedFirst)).ShouldBeOfType<DescribedFirstConverter>();

        TypeDescriptor.RegisterType<DescribedFirst>();

        TypeDescriptor.GetProvider(typeof(DescribedFirst)).IsRegisteredType(typeof(DescribedFirst)).ShouldBeFalse("RegisterType did nothing");
        var refused = Should.Throw<InvalidOperationException>(() => TypeDescriptor.GetConverterFromRegisteredType(typeof(DescribedFirst)));
        refused.Message.ShouldContain("is not registered");

        TypeDescriptor.GetConverter(typeof(DescribedFirst)).ShouldBeOfType<DescribedFirstConverter>("the reflection path still answers, with the attribute's converter");
    }
}
