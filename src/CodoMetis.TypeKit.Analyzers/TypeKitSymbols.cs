using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    private const string Materialize = "Materialize";

    private readonly ImmutableArray<INamedTypeSymbol> _valueMarkers;
    private readonly ImmutableArray<INamedTypeSymbol> _validatedMarkers;
    private readonly ImmutableArray<INamedTypeSymbol> _requireCustomInitialization;
    private readonly ImmutableArray<INamedTypeSymbol> _materializers;
    private readonly ImmutableArray<INamedTypeSymbol> _converters;
    private readonly ImmutableArray<INamedTypeSymbol> _outcomes;

    private TypeKitSymbols(
        ImmutableArray<INamedTypeSymbol> valueMarkers,
        ImmutableArray<INamedTypeSymbol> validatedMarkers,
        ImmutableArray<INamedTypeSymbol> requireCustomInitialization,
        ImmutableArray<INamedTypeSymbol> materializers,
        ImmutableArray<INamedTypeSymbol> converters,
        ImmutableArray<INamedTypeSymbol> outcomes,
        bool                             referencesGenerators
    )
    {
        _outcomes                    = outcomes;
        _valueMarkers                = valueMarkers;
        _validatedMarkers            = validatedMarkers;
        _requireCustomInitialization = requireCustomInitialization;
        _materializers               = materializers;
        _converters                  = converters;
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
        var attributes       = compilation.GetTypesByMetadataName("CodoMetis.TypeKit.RequireCustomInitializationAttribute");
        var materializers    = compilation.GetTypesByMetadataName("CodoMetis.TypeKit.ValueObjects.IValueObjectMaterializer`2");
        var converters       = compilation.GetTypesByMetadataName("CodoMetis.TypeKit.EntityFrameworkCore.ValueObjectConverter`2");
        var outcomes         = compilation.GetTypesByMetadataName("CodoMetis.TypeKit.Result`1")
                                          .AddRange(compilation.GetTypesByMetadataName("CodoMetis.TypeKit.Result`2"))
                                          .AddRange(compilation.GetTypesByMetadataName("CodoMetis.TypeKit.Option`1"));

        if (valueMarkers.IsEmpty && validatedMarkers.IsEmpty && attributes.IsEmpty && materializers.IsEmpty) return null;

        var referencesGenerators = compilation.ReferencedAssemblyNames.Any(identity =>
            string.Equals(identity.Name, GeneratorsAssembly, StringComparison.OrdinalIgnoreCase));

        return new TypeKitSymbols(valueMarkers, validatedMarkers, attributes, materializers, converters, outcomes, referencesGenerators);
    }

    /// <summary>
    /// <c>Result</c> or <c>Option</c> if <paramref name="type"/> is one of ours (a <c>Nullable</c> of one
    /// included), otherwise <see langword="null"/>.
    /// </summary>
    public string? OutcomeName(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) type = nullable.TypeArguments[0];

        return type is INamedTypeSymbol named && _outcomes.Contains(named.OriginalDefinition, SymbolEqualityComparer.Default)
                   ? named.Name
                   : null;
    }

    /// <summary>
    /// The validated value object <paramref name="invocation"/> calls <paramref name="member"/> on, as
    /// in <c>Email.TryFrom(input)</c>, whether or not the call binds.
    /// </summary>
    /// <remarks>
    /// Metalama runs analyzers on the source before weaving, where a member the generators introduce
    /// into a value object of the same project does not exist yet: the call does not bind, and an
    /// operation-based rule never sees it (measured 2026-09-28: CMTK0003 reported an ignored
    /// <c>Create</c>, which is hand-written, and not an ignored <c>TryFrom</c>). The receiver still
    /// binds, so the call is recognised by the generated member's name on a value object's type. The
    /// names are the generators' own surface, which <c>GeneratedSurface.verified.txt</c> pins.
    /// </remarks>
    public INamedTypeSymbol? GeneratedMemberCall(InvocationExpressionSyntax invocation, string member, SemanticModel model, System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access || access.Name.Identifier.ValueText != member) return null;

        return model.GetSymbolInfo(access.Expression, cancellationToken).Symbol is INamedTypeSymbol type
            && FindMarker(type, out var validated) is not null && validated
                   ? type
                   : null;
    }

    /// <summary>
    /// Whether a <c>default</c> <paramref name="type"/> is an instance that passed no factory: a struct
    /// value object, an <c>Option</c>, a <c>Result</c> or a <c>[RequireCustomInitialization]</c>
    /// struct, or a type parameter constrained to be one. A class's default is null, which nullable
    /// analysis already follows.
    /// </summary>
    public bool IsNoDefaultStruct(ITypeSymbol type)
    {
        var isStruct = type.IsValueType || type is ITypeParameterSymbol { HasValueTypeConstraint: true };
        if (!isStruct || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return false;

        return FindRequireCustomInitialization(type) is not null || FindMarker(type, out _) is not null;
    }

    /// <summary>
    /// Whether <paramref name="method"/> rebuilds a value object without validation: the
    /// materializer's <c>Materialize</c>, called through a type parameter; the EF converter's public
    /// <c>Materialize</c>; or a hand-written value object's own public implementation of the former.
    /// </summary>
    /// <param name="method">The method called or referenced.</param>
    /// <param name="valueObject">The value object it rebuilds, as the call names it.</param>
    public bool IsMaterialize(IMethodSymbol method, out ITypeSymbol? valueObject)
    {
        valueObject = null;

        if (!method.IsStatic || method.Name != Materialize || method.ContainingType is not { } owner) return false;

        if (_materializers.Contains(owner.OriginalDefinition, SymbolEqualityComparer.Default)
         || _converters.Contains(owner.OriginalDefinition, SymbolEqualityComparer.Default))
        {
            valueObject = owner.TypeArguments.FirstOrDefault();
            return true;
        }

        // The generators implement the interface explicitly, so their Materialize cannot be called by
        // name. A value object declared by hand can implement it implicitly, as a public static method.
        foreach (var candidate in owner.AllInterfaces)
        {
            if (!_materializers.Contains(candidate.OriginalDefinition, SymbolEqualityComparer.Default)) continue;

            var contract = candidate.GetMembers(Materialize).FirstOrDefault();
            if (contract is not null && SymbolEqualityComparer.Default.Equals(owner.FindImplementationForInterfaceMember(contract), method.OriginalDefinition))
            {
                valueObject = owner;
                return true;
            }
        }

        return false;
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
