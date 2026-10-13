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
    Plain,

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
    /// A type the fabric hands to ValueObjectAspect: a concrete class or struct with at least
    /// one marker. The aspect then generates it or reports why it cannot.
    /// </summary>
    public static bool IsValueObject(INamedType type) =>
        type.TypeKind is TypeKind.Struct or TypeKind.Class
     && !type.IsAbstract
     && Markers(type).Count > 0;

    /// <summary>
    /// An abstract class with a marker, which the fabric refuses (CMTK1022) rather than skip: it was
    /// generated nothing and reported nothing, and a value held as it, of a sealed type derived from
    /// it, had no <c>Value</c> and was written as <c>{}</c>.
    /// </summary>
    public static bool IsAbstractValueObject(INamedType type) =>
        type.TypeKind == TypeKind.Class
     && type.IsAbstract
     && !type.IsStatic
     && Markers(type).Count > 0;

    /// <summary>
    /// Why <paramref name="valueObject"/> cannot be generated over what it wraps, read from types
    /// other than itself: a value object (<see cref="WrappedValueObjectRefusal"/>) or a closed set of
    /// the same project (<see cref="SameProjectClosedSetRefusal"/>). <see langword="null"/> when it
    /// wraps neither. Called from the fabric only, since it reads types other than the aspect's target
    /// (<see cref="ValueObjectFabric"/>).
    /// </summary>
    public static string? WrappedTypeRefusal(INamedType valueObject) =>
        WrappedValueObjectRefusal(valueObject) ?? SameProjectClosedSetRefusal(valueObject);

    /// <summary>
    /// Why <paramref name="valueObject"/> cannot be generated over the closed set it wraps, or
    /// <see langword="null"/> when what it wraps is no closed set declared in the same project.
    /// </summary>
    /// <remarks>
    /// The closed-set aspect runs after the value-object aspects (<c>AspectOrder</c>), and they do not
    /// see what it introduces: over a set of its own project a value object was generated without
    /// <c>Parse</c>, <c>TryParse</c>, <c>IParsable</c> and its own type converter, while its JSON worked,
    /// and over the same set in a referenced project it got all of them (measured 2026-10-13). A
    /// surface that follows the project layout is refused, as for a value object over a value object.
    /// </remarks>
    private static string? SameProjectClosedSetRefusal(INamedType valueObject) =>
        WrappedNamedType(valueObject) is { } set && ClosedSetTypes.IsClosedSet(set) && !set.DeclaringAssembly.IsExternal
            ? $"it wraps '{set.ToDisplayString()}', a closed set declared in the same project, whose parsing and type converter are generated after the value object's and would be missing from it; "
            + "declare the set in a referenced project, or wrap its name ('string') instead"
            : null;

    /// <summary>
    /// Why <paramref name="valueObject"/> cannot be generated over what it wraps, or
    /// <see langword="null"/> when that is not a value object.
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
    private static string? WrappedValueObjectRefusal(INamedType valueObject)
    {
        List<INamedType> chain = [valueObject];
        var next = WrappedNamedType(valueObject);

        // Ends at the first type that is not a value object, or at one already on the chain.
        while (next is not null && IsValueObject(next) && !chain.Any(type => type.Equals(next)))
        {
            chain.Add(next);
            next = WrappedNamedType(next);
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
    private static INamedType? WrappedNamedType(INamedType type) =>
        Markers(type) is [var marker] ? WrappedType(marker) as INamedType : null;

    public static ValueObjectKind KindOf(INamedType marker) =>
        marker.Definition.Equals(TypeFactory.GetNamedType(typeof(IValue<>)))
            ? ValueObjectKind.Plain
            : ValueObjectKind.Validated;

    /// <summary>The wrapped type: <c>T</c> of <c>IValue&lt;T&gt;</c>, or of <c>IValidatedValue&lt;TSelf, T, TFault&gt;</c>.</summary>
    public static IType WrappedType(INamedType marker) =>
        KindOf(marker) == ValueObjectKind.Plain ? marker.TypeArguments[0] : marker.TypeArguments[1];

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
        /// The state ValueObjectAspect left for the aspects that run after it. Fails only for
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
