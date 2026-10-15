using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace CodoMetis.TypeKit.AspNetCore
{
    /// <summary>Members of a fault type an endpoint never answers with, left out of its document.</summary>
    internal interface IFaultExclusion<TFault> : IFaultDeclaration where TFault : notnull
    {
        bool Matches(TFault fault, FaultType<TFault> type);

        /// <summary>Why the metadata names something no member is, or <see langword="null"/> where every name is a member's.</summary>
        string? Problem(FaultType<TFault> type);
    }

    internal sealed class FaultExclusionMetadata<TFault>(TFault[] faults) : IFaultExclusion<TFault> where TFault : notnull
    {
        public Type FaultType => typeof(TFault);

        public bool Matches(TFault candidate, FaultType<TFault> type) => faults.Contains(candidate, EqualityComparer<TFault>.Default);

        public string? Problem(FaultType<TFault> type) =>
            faults.Any(fault => !type.Members.Contains(fault)) ? $"{Describe()} names a value no member of {type.TypeName} has" : null;

        public string Describe() => $"WithoutFaults({string.Join(", ", faults.Select(fault => $"{FaultTypes.NameOf(typeof(TFault))}.{fault}"))})";
    }

    /// <summary>What reads the members an endpoint leaves out.</summary>
    internal static class FaultExclusions
    {
        public static IFaultExclusion<TFault>? Excluding<TFault>(IReadOnlyList<object> metadata, TFault fault, FaultType<TFault> type) where TFault : notnull
        {
            foreach (var item in metadata)
            {
                if (item is IFaultExclusion<TFault> exclusion && exclusion.Matches(fault, type)) return exclusion;
            }

            return null;
        }

        public static TFault[] Checked<TFault>(TFault[] faults, string parameter) where TFault : notnull
        {
            ArgumentNullException.ThrowIfNull(faults, parameter);
            if (faults.Length == 0) throw new ArgumentException("Name the members the endpoint never answers with; an empty list leaves nothing out.", parameter);
            if (faults.Any(fault => fault is null)) throw new ArgumentNullException(parameter, "A member left out is one of the fault type's values; it is never null.");

            return [.. faults];
        }
    }

    /// <summary>
    /// Members of <typeparamref name="TFault"/> an MVC action or controller never answers with, left out of its
    /// document: <c>[WithoutFaults&lt;OrderFault&gt;(nameof(OrderFault.NotFound))]</c>. A minimal API endpoint or
    /// route group says it with <c>.WithoutFaults(OrderFault.NotFound)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operation lists no status for a member left out, and drops a status no member is left at. Reporting
    /// one anyway is refused when the response is written, with an <see cref="InvalidOperationException"/>
    /// naming the endpoint and the member, rather than answered with a status the document does not list.
    /// </para>
    /// <para>
    /// The members are named, since an attribute takes only constants. A name no member has, a member an
    /// endpoint also gives a status with <c>FaultStatus</c>, and a scope none of whose actions answers with
    /// <typeparamref name="TFault"/> stop the host from starting. On a controller it applies to the actions that
    /// answer with <typeparamref name="TFault"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TFault">The fault type, an enum or a closed set.</typeparam>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class WithoutFaultsAttribute<TFault> : Attribute, IFaultExclusion<TFault> where TFault : notnull
    {
        /// <summary>Leaves the members named <paramref name="faults"/> out of this action's document.</summary>
        /// <param name="faults">The members' names, as <c>nameof(OrderFault.NotFound)</c> gives them.</param>
        /// <exception cref="ArgumentException"><paramref name="faults"/> is empty, or a name is blank.</exception>
        public WithoutFaultsAttribute(params string[] faults)
        {
            ArgumentNullException.ThrowIfNull(faults);
            if (faults.Length == 0) throw new ArgumentException("Name the members the action never answers with; an empty list leaves nothing out.", nameof(faults));
            foreach (var fault in faults) ArgumentException.ThrowIfNullOrWhiteSpace(fault, nameof(faults));

            Faults = [.. faults];
        }

        /// <summary>The members' names.</summary>
        public IReadOnlyList<string> Faults { get; }

        Type IFaultDeclaration.FaultType => typeof(TFault);

        bool IFaultExclusion<TFault>.Matches(TFault fault, FaultType<TFault> type) => Faults.Contains(type.NameOf(fault), StringComparer.Ordinal);

        string? IFaultExclusion<TFault>.Problem(FaultType<TFault> type) =>
            Faults.Where(name => !type.TryFind(name, out _)).ToList() is { Count: > 0 } unknown
                ? $"{((IFaultDeclaration)this).Describe()} names no member {string.Join(", ", unknown.Select(name => $"\"{name}\""))} of {type.TypeName}"
                : null;

        string IFaultDeclaration.Describe() =>
            $"[WithoutFaults<{FaultTypes.NameOf(typeof(TFault))}>({string.Join(", ", Faults.Select(name => $"\"{name}\""))})]";
    }
}

// Beside WithMetadata and the other endpoint conventions.
namespace Microsoft.AspNetCore.Builder
{
    /// <summary>Members of a fault type an endpoint, or a group of them, never answers with.</summary>
    public static class TypeKitWithoutFaultsEndpointConventionBuilderExtensions
    {
        /// <summary>
        /// Leaves <paramref name="faults"/> out of this endpoint's document, or out of the documents of the
        /// endpoints of this group that answer with <typeparamref name="TFault"/>: a create route lists no
        /// <c>404</c> for <c>OrderFault.NotFound</c> it never reports.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The operation lists no status for a member left out, and drops a status no member is left at.
        /// Reporting one anyway is refused when the response is written, with an
        /// <see cref="InvalidOperationException"/> naming the endpoint and the member, rather than answered with
        /// a status the document does not list.
        /// </para>
        /// <para>
        /// A value no member names, a member the endpoint also gives a status with <c>WithFaultStatus</c>, and a
        /// scope none of whose endpoints answers with <typeparamref name="TFault"/> stop the host from starting, as
        /// for <c>WithFaultStatus</c>. Call it once per fault type: <c>.WithoutFaults(OrderFault.NotFound).WithoutFaults(ConcurrencyFault.PreconditionFailed)</c>.
        /// </para>
        /// </remarks>
        /// <param name="builder">The endpoint, or a group or convention of endpoints.</param>
        /// <param name="faults">The members, enum members or a closed set's members.</param>
        /// <typeparam name="TBuilder">The builder.</typeparam>
        /// <typeparam name="TFault">The fault type.</typeparam>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentException"><paramref name="faults"/> is empty.</exception>
        /// <exception cref="ArgumentNullException">A member is null.</exception>
        public static TBuilder WithoutFaults<TBuilder, TFault>(this TBuilder builder, params TFault[] faults)
            where TBuilder : IEndpointConventionBuilder
            where TFault : notnull
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.WithMetadata(new FaultExclusionMetadata<TFault>(FaultExclusions.Checked(faults, nameof(faults))));
        }
    }
}
