using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>Which marker a value object declares, and so which factories it gets.</summary>
[CompileTime]
internal enum ValueObjectKind
{
    /// <summary><c>IValue&lt;T&gt;</c>: wraps any value, and gets <c>From</c>.</summary>
    SimpleValue,

    /// <summary><c>IValidatedValue&lt;,,&gt;</c>: every way in applies <c>Create</c>.</summary>
    Validated
}

/// <summary>Compile-time knowledge about value objects, shared by the fabric and the aspects.</summary>
[CompileTime]
internal static class ValueObjectTypes
{
    /// <summary>
    /// The value-object markers <paramref name="type"/> implements, directly or through another
    /// interface. Matched by type definition, so an interface that merely derives from a marker is
    /// not counted as a second one.
    /// </summary>
    public static IReadOnlyList<INamedType> Markers(INamedType type) =>
        [.. type.AllImplementedInterfaces.Where(IsMarker)];

    public static ValueObjectKind KindOf(INamedType marker) =>
        marker.Definition.Equals(TypeFactory.GetNamedType(typeof(IValue<>)))
            ? ValueObjectKind.SimpleValue
            : ValueObjectKind.Validated;

    /// <summary>The wrapped type: <c>T</c> of <c>IValue&lt;T&gt;</c>, or of <c>IValidatedValue&lt;TSelf, T, TFault&gt;</c>.</summary>
    public static IType UnderlyingType(INamedType marker) =>
        KindOf(marker) == ValueObjectKind.SimpleValue ? marker.TypeArguments[0] : marker.TypeArguments[1];

    /// <summary>A reference to <paramref name="type"/> that is valid in any generated code, whatever the file imports.</summary>
    public static string SourceName(INamedType type) => $"global::{type.FullName}";

    private static bool IsMarker(INamedType candidate) =>
        candidate.Definition.Equals(TypeFactory.GetNamedType(typeof(IValue<>))) ||
        candidate.Definition.Equals(TypeFactory.GetNamedType(typeof(IValidatedValue<,,>)));

    extension(Type type)
    {
        public INamedType ToNamedType() => TypeFactory.GetNamedType(type);
    }

    extension(ImmutableArray<AspectPredecessor> predecessors)
    {
        /// <summary>
        /// The state the implementation aspect left for the aspects that run after it. Fails only for
        /// an aspect applied without it, which <see cref="ValueObjectFabric"/> never does.
        /// </summary>
        public bool TryGetState<T>([NotNullWhen(returnValue: true)] out T? state) where T : IAspectState
        {
            foreach (var predecessor in predecessors)
            {
                if (predecessor.Instance is IAspectInstance { AspectState: T aspectState })
                {
                    state = aspectState;
                    return true;
                }
            }

            state = default;
            return false;
        }
    }
}
