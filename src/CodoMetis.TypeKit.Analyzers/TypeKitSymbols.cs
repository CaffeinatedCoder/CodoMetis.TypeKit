using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// The CodoMetis.TypeKit symbols one compilation can see, resolved once per compilation.
/// </summary>
/// <remarks>
/// <para>
/// Everything is compared by symbol. A consumer's own type with the same name in another namespace
/// is not ours, and a value object that implements a marker through a derived interface still is.
/// </para>
/// <para>
/// The metadata names below are this package's own, and the analyzer tests compile against the real
/// CodoMetis.TypeKit assembly. If one of them drifted from the real type, the positive tests would
/// go silent and fail.
/// </para>
/// </remarks>
internal sealed class TypeKitSymbols
{
    /// <summary>
    /// The assembly whose fabric generates value objects. A compilation references it exactly when
    /// the fabric can run, directly or through a transitive project or package reference. This is
    /// the package's own identity, which a consumer cannot rename.
    /// </summary>
    private const string GeneratorsAssembly = "CodoMetis.TypeKit.Generators";

    private readonly ImmutableArray<INamedTypeSymbol> _valueMarkers;
    private readonly ImmutableArray<INamedTypeSymbol> _validatedMarkers;
    private readonly ImmutableArray<INamedTypeSymbol> _requireCustomInitialization;

    private TypeKitSymbols(
        ImmutableArray<INamedTypeSymbol> valueMarkers,
        ImmutableArray<INamedTypeSymbol> validatedMarkers,
        ImmutableArray<INamedTypeSymbol> requireCustomInitialization,
        bool                             referencesGenerators
    )
    {
        _valueMarkers                = valueMarkers;
        _validatedMarkers            = validatedMarkers;
        _requireCustomInitialization = requireCustomInitialization;
        ReferencesGenerators         = referencesGenerators;
    }

    /// <summary>Whether the compilation references CodoMetis.TypeKit.Generators.</summary>
    public bool ReferencesGenerators { get; }

    /// <summary>
    /// The symbols the compilation can see, or <see langword="null"/> when it does not reference
    /// CodoMetis.TypeKit at all, in which case no rule has anything to look at.
    /// </summary>
    /// <remarks>
    /// <c>GetTypesByMetadataName</c> rather than <c>GetTypeByMetadataName</c>: the latter returns
    /// <see langword="null"/> when two assemblies define the name, which would switch the rules off
    /// silently. Accepting every match can only make them report more.
    /// </remarks>
    public static TypeKitSymbols? Resolve(Compilation compilation)
    {
        var valueMarkers     = compilation.GetTypesByMetadataName("CodoMetis.TypeKit.ValueObjects.IValue`1");
        var validatedMarkers = compilation.GetTypesByMetadataName("CodoMetis.TypeKit.ValueObjects.IValidatedValue`3");
        var attributes       = compilation.GetTypesByMetadataName("CodoMetis.TypeKit.Attributes.RequireCustomInitializationAttribute");

        if (valueMarkers.IsEmpty && validatedMarkers.IsEmpty && attributes.IsEmpty) return null;

        var referencesGenerators = compilation.ReferencedAssemblyNames.Any(identity =>
            string.Equals(identity.Name, GeneratorsAssembly, StringComparison.OrdinalIgnoreCase));

        return new TypeKitSymbols(valueMarkers, validatedMarkers, attributes, referencesGenerators);
    }

    /// <summary>
    /// The value-object marker <paramref name="type"/> implements, directly or through another
    /// interface, as the constructed interface (e.g. <c>IValue&lt;Guid&gt;</c>). For a type
    /// parameter, the marker its constraints require.
    /// </summary>
    public INamedTypeSymbol? FindMarker(ITypeSymbol type, out bool validated)
    {
        foreach (var candidate in InterfacesOf(type))
        {
            if (_validatedMarkers.Contains(candidate.OriginalDefinition, SymbolEqualityComparer.Default))
            {
                validated = true;
                return candidate;
            }

            if (_valueMarkers.Contains(candidate.OriginalDefinition, SymbolEqualityComparer.Default))
            {
                validated = false;
                return candidate;
            }
        }

        validated = false;
        return null;
    }

    /// <summary>
    /// The interfaces of <paramref name="type"/>. A type parameter has none of its own: what it is
    /// guaranteed to implement comes from its constraints, which can be interfaces, a base class or
    /// another type parameter.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> InterfacesOf(ITypeSymbol type)
    {
        if (type is not ITypeParameterSymbol parameter) return type.AllInterfaces;

        return parameter.ConstraintTypes.SelectMany(constraint =>
            constraint is INamedTypeSymbol { TypeKind: TypeKind.Interface } constraintInterface
                ? constraintInterface.AllInterfaces.Insert(0, constraintInterface)
                : InterfacesOf(constraint));
    }

    /// <summary>The <c>[RequireCustomInitialization]</c> on <paramref name="type"/>, if it carries ours.</summary>
    public AttributeData? FindRequireCustomInitialization(ITypeSymbol type) =>
        type.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass is { } attributeClass
                                      && _requireCustomInitialization.Contains(attributeClass, SymbolEqualityComparer.Default));
}
