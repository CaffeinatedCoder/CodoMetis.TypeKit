using System.Text.Json;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>A value object over an interface.</summary>
public readonly partial record struct OverDisposable : IValue<IDisposable>;

/// <summary>A value object over an abstract class.</summary>
public readonly partial record struct OverStream : IValue<Stream>;

/// <summary>
/// An interface or an abstract class is written and read as the serializer writes and reads it (its
/// declared members, polymorphic where the type declares it, refused on reading where the serializer
/// refuses it), so it is wrapped. A guard against the refusal of <c>object</c> reaching further.
/// </summary>
/// <remarks>
/// The value objects are declared in this project, so a refusal (CMTK1005) or a failure inside the
/// generated code (LAMA0611) fails its build. A consumer build cannot be asked for the second by name:
/// its message names what the generated code calls, such as the wrapped type, not the value object.
/// </remarks>
public sealed class AbstractionWrappingTests
{
    [Fact]
    public void An_interface_is_wrapped_and_written_by_its_declared_members()
    {
        var lease = new Lease();

        OverDisposable.From(lease).Value.ShouldBeSameAs(lease);
        JsonSerializer.Serialize(OverDisposable.From(lease)).ShouldBe(JsonSerializer.Serialize<IDisposable>(lease));
    }

    [Fact]
    public void An_abstract_class_is_wrapped()
    {
        using var stream = new MemoryStream();

        OverStream.From(stream).Value.ShouldBeSameAs(stream);
    }

    /// <summary>Neither has a <c>Parse</c> to delegate to.</summary>
    [Theory]
    [InlineData(typeof(OverDisposable))]
    [InlineData(typeof(OverStream))]
    public void Neither_is_parsable(Type type) =>
        type.GetInterfaces().ShouldNotContain(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IParsable<>));

    private sealed class Lease : IDisposable
    {
        public int Id => 7;

        public void Dispose() { }
    }
}
