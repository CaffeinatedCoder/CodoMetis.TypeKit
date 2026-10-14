namespace CodoMetis.TypeKit;

/// <summary>The accessibility a <see cref="RequireParameterlessConstructorAttribute"/> asks of the constructor.</summary>
/// <remarks>
/// The members mirror C#'s accessibilities in declaration order, and the analyzer reads them by
/// value, so the order is contract: a member is never reordered or removed.
/// </remarks>
public enum ConstructorAccessibility
{
    /// <summary><c>public</c>, which is also what a class that declares no constructor has.</summary>
    Public,

    /// <summary><c>internal</c>.</summary>
    Internal,

    /// <summary><c>protected</c>.</summary>
    Protected,

    /// <summary><c>protected internal</c>.</summary>
    ProtectedInternal,

    /// <summary><c>private protected</c>.</summary>
    PrivateProtected,

    /// <summary><c>private</c>.</summary>
    Private
}
