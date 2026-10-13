using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace CodoMetis.TypeKit.AspNetCore
{
    /// <summary>A status an endpoint answers some fault with in place of the host's: what the startup check reads without knowing the type.</summary>
    internal interface IFaultStatus
    {
        Type FaultType { get; }

        /// <summary>How the metadata reads in a message: <c>WithFaultStatus(OrderFault.NameTaken, 422)</c>.</summary>
        string Describe();
    }

    /// <summary>The typed form, which the response, the document and the startup check read.</summary>
    internal interface IFaultStatus<TFault> : IFaultStatus where TFault : notnull
    {
        int Status { get; }

        bool Matches(TFault fault, FaultType<TFault> type);

        /// <summary>Why the metadata names no member, or <see langword="null"/> where it names one.</summary>
        string? Problem(FaultType<TFault> type);
    }

    internal sealed class FaultStatusMetadata<TFault>(TFault fault, int status) : IFaultStatus<TFault> where TFault : notnull
    {
        public Type FaultType => typeof(TFault);

        public int Status => status;

        public bool Matches(TFault candidate, FaultType<TFault> type) => EqualityComparer<TFault>.Default.Equals(candidate, fault);

        public string? Problem(FaultType<TFault> type) =>
            type.Members.Contains(fault) ? null : $"{Describe()} names a value no member of {type.TypeName} has";

        public string Describe() => $"WithFaultStatus({typeof(TFault).Name}.{fault}, {status})";
    }

    /// <summary>The ordered override: the last status the endpoint's metadata sets for a member, as ASP.NET reads metadata.</summary>
    internal static class FaultStatuses
    {
        public static int? Override<TFault>(IReadOnlyList<object> metadata, TFault fault, FaultType<TFault> type) where TFault : notnull
        {
            int? status = null;
            foreach (var item in metadata)
            {
                if (item is IFaultStatus<TFault> candidate && candidate.Matches(fault, type)) status = candidate.Status;
            }

            return status;
        }

        public static void ThrowIfNoErrorStatus(int status, string parameter)
        {
            if (status is < 400 or > 599)
                throw new ArgumentOutOfRangeException(parameter, status, $"{status} is not an error status (400 to 599): a fault is answered as problem details." + FaultRefusals.Mapping);
        }
    }

    /// <summary>
    /// An MVC action's status for one fault, in place of the host's: <c>[FaultStatus&lt;OrderFault&gt;(nameof(OrderFault.NameTaken), 422)]</c>.
    /// A minimal API endpoint says it with <c>.WithFaultStatus(OrderFault.NameTaken, 422)</c>.
    /// </summary>
    /// <remarks>
    /// The member is named, since an attribute takes only constants, and <c>nameof</c> keeps the name
    /// in step with the member. A name no member has stops the host from starting, as an endpoint
    /// whose fault type is not mapped does. The response, the operation's document entry and the
    /// startup check all read it.
    /// </remarks>
    /// <typeparam name="TFault">The fault type, an enum or a closed set.</typeparam>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class FaultStatusAttribute<TFault> : Attribute, IFaultStatus<TFault> where TFault : notnull
    {
        /// <summary>Answers the member named <paramref name="fault"/> with <paramref name="status"/> on this action.</summary>
        /// <param name="fault">The member's name, as <c>nameof(OrderFault.NameTaken)</c> gives it.</param>
        /// <param name="status">The HTTP status, 400 to 599.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is not an error status.</exception>
        public FaultStatusAttribute(string fault, int status)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fault);
            FaultStatuses.ThrowIfNoErrorStatus(status, nameof(status));

            Fault = fault;
            Status = status;
        }

        /// <summary>The member's name.</summary>
        public string Fault { get; }

        /// <summary>The HTTP status.</summary>
        public int Status { get; }

        Type IFaultStatus.FaultType => typeof(TFault);

        bool IFaultStatus<TFault>.Matches(TFault fault, FaultType<TFault> type) => string.Equals(type.NameOf(fault), Fault, StringComparison.Ordinal);

        string? IFaultStatus<TFault>.Problem(FaultType<TFault> type) =>
            type.TryFind(Fault, out _) ? null : $"{((IFaultStatus)this).Describe()} names no member of {type.TypeName}";

        string IFaultStatus.Describe() => $"[FaultStatus<{typeof(TFault).Name}>(\"{Fault}\", {Status})]";
    }
}

// Beside WithMetadata and the other endpoint conventions.
namespace Microsoft.AspNetCore.Builder
{
    /// <summary>A status for one fault on one endpoint, in place of the host's.</summary>
    public static class TypeKitFaultStatusEndpointConventionBuilderExtensions
    {
        /// <summary>
        /// Answers <paramref name="fault"/> with <paramref name="status"/> on this endpoint, in place of the
        /// status the host maps it to: the same fault can be a 404 on one route and a 409 on another.
        /// </summary>
        /// <remarks>
        /// The response, the operation's document entry and the startup check all read it. A status for
        /// a fault type the endpoint does not answer with stops the host from starting, so a stale one is
        /// found before it misleads.
        /// </remarks>
        /// <param name="builder">The endpoint, or a group or convention of endpoints.</param>
        /// <param name="fault">The fault, an enum member or a closed set's member.</param>
        /// <param name="status">The HTTP status, 400 to 599.</param>
        /// <typeparam name="TBuilder">The builder.</typeparam>
        /// <typeparam name="TFault">The fault type.</typeparam>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is not an error status.</exception>
        public static TBuilder WithFaultStatus<TBuilder, TFault>(this TBuilder builder, TFault fault, int status)
            where TBuilder : IEndpointConventionBuilder
            where TFault : notnull
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (fault is null) throw new ArgumentNullException(nameof(fault));
            FaultStatuses.ThrowIfNoErrorStatus(status, nameof(status));

            return builder.WithMetadata(new FaultStatusMetadata<TFault>(fault, status));
        }
    }
}
