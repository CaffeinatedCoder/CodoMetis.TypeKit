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

    /// <summary>
    /// A type the fabric hands to the implementation aspect: a concrete class or struct with at least
    /// one marker. The aspect then generates it or reports why it cannot.
    /// </summary>
    public static bool IsValueObject(INamedType type) =>
        type.TypeKind is TypeKind.Struct or TypeKind.Class
     && !type.IsAbstract
     && Markers(type).Count > 0;

    /// <summary>
    /// Why <paramref name="valueObject"/> cannot be generated over what it wraps, or
    /// <see langword="null"/> when that is not a value object. Called from the fabric only, since it
    /// reads types other than the aspect's target (<see cref="ValueObjectFabric"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value object that reaches itself through what it wraps has no finite form: its JSON
    /// converter serializes the wrapped value through the options, which is its own converter again,
    /// a schema for it never ends, and as a struct it has no layout (CS0523, which Metalama reports as
    /// a bug in the aspect). As a record class it compiled without a word.
    /// </para>
    /// <para>
    /// One that wraps a different value object terminates, but its surface depended on where that one
    /// was declared (measured 2026-09-27). Over a value object from a referenced project it got every
    /// interface; over one from its own project it silently lacked parsing, comparison, span
    /// formatting, <c>IConvertible</c>, <c>MinValue</c> and the type converter, and sorting it threw,
    /// because the aspects of one layer do not see what their sibling instances introduce. It wraps
    /// what the other one wraps instead, and a validated one applies the other's rules in its
    /// <c>Create</c>.
    /// </para>
    /// </remarks>
    public static string? WrappedValueObjectRefusal(INamedType valueObject)
    {
        List<INamedType> chain = [valueObject];
        var next = WrappedType(valueObject);

        // Ends at the first type that is not a value object, or at one already on the chain.
        while (next is not null && IsValueObject(next) && !chain.Any(type => type.Equals(next)))
        {
            chain.Add(next);
            next = WrappedType(next);
        }

        if (next is not null && next.Equals(valueObject))
            return $"it wraps itself ({string.Join(" -> ", chain.Append(next).Select(type => type.ToDisplayString()))})";

        if (chain.Count == 1)
            return null;

        var refusal = $"it wraps '{chain[1].ToDisplayString()}', which is a value object itself";

        // Past a cycle further down the chain there is nothing to suggest.
        return next is null || IsValueObject(next)
                   ? refusal
                   : $"{refusal}; wrap '{next.ToDisplayString()}' instead";
    }

    /// <summary>The wrapped type of a type with exactly one marker, where it is a named type.</summary>
    private static INamedType? WrappedType(INamedType type) =>
        Markers(type) is [var marker] ? UnderlyingType(marker) as INamedType : null;

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
