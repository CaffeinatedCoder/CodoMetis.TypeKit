namespace CodoMetis.TypeKit.Attributes;

/// <summary>
/// Marks a struct whose <c>default</c> value is not a valid instance, so it must be created through
/// one of its own factories.
/// </summary>
/// <remarks>
/// The CodoMetis.TypeKit analyzer reports <c>default</c>, <c>default(T)</c>, <c>new()</c> and
/// <c>new T()</c> of a type that carries this attribute.
/// </remarks>
/// <param name="errorMessage">
/// An optional message the analyzer reports in place of its generic one, typically naming the
/// factory to call instead.
/// </param>
[AttributeUsage(AttributeTargets.Struct)]
public class RequireCustomInitializationAttribute(string? errorMessage = null) : Attribute
{
    /// <summary>The message the analyzer reports instead of its generic one, if any.</summary>
    public string? ErrorMessage { get; } = errorMessage;
}
