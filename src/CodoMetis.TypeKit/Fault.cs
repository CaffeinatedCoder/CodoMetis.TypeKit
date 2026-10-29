using System.Text.Json.Serialization;
using CodoMetis.TypeKit.ClosedSets;

namespace CodoMetis.TypeKit;

/// <summary>
/// A fault and a sentence about this occurrence of it: <c>Result&lt;Order, Fault&lt;OrderFault&gt;&gt;</c>,
/// so what varies per occurrence travels beside a fault that is still one of a fixed set.
/// </summary>
/// <remarks>
/// <para>
/// The reason is an enum member or a closed-set member, which a host maps to a response, keyed on the
/// reason's type, so <c>Fault&lt;OrderFault&gt;</c> is answered as <c>OrderFault</c> is. The detail is
/// the application's sentence, written into the problem's <c>detail</c> as it is: unlike a fault's name,
/// it can say anything, so whether it quotes the request is the application's to decide.
/// </para>
/// <para>
/// <see cref="Fault.Of{TFault}(TFault, string?)"/> makes one and infers the type, with a detail or without:
/// <c>Result.Error(Fault.Of(OrderFault.NotAmendable, "The order shipped on 3 May."))</c>. Where the reason
/// answers for several rules, <c>WithRule</c> names the one that refused, an enum's or a closed set's
/// member, as a key a client branches on: <c>Fault.Of(AccountFault.InvalidEmail).WithRule(EmailFault.NoAt)</c>. A bare reason
/// does not convert to a fault, since a fault compared with it would then be false whenever it carries
/// a detail; compare <see cref="Reason"/> instead. Not a wire type:
/// it is matched to a response, as the <see cref="Result{T, TError}"/> holding it is.
/// </para>
/// </remarks>
/// <typeparam name="TFault">The reason's type: an enum or a closed set.</typeparam>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[RequireCustomInitialization("Use Fault.Of(reason) or Fault.Of(reason, detail).")]
public readonly record struct Fault<TFault> where TFault : notnull
{
    /// <summary>Creates the fault.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is empty or white space: say <see langword="null"/> for no detail.</exception>
    public Fault(TFault reason, string? detail = null)
    {
        Reason = reason is null ? throw new ArgumentNullException(nameof(reason), "A fault's reason is one of its type's values; it is never null.") : reason;
        Detail = detail is not null && string.IsNullOrWhiteSpace(detail)
            ? throw new ArgumentException("A fault's detail is a sentence, or null for none; it is never blank.", nameof(detail))
            : detail;
    }

    /// <summary>Creates the fault, keeping the exception that caused it.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <param name="cause">The exception underneath, kept for the log as <see cref="Cause"/>. Never null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> or <paramref name="cause"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is empty or white space: say <see langword="null"/> for no detail.</exception>
    public Fault(TFault reason, string? detail, Exception cause)
        : this(reason, detail) =>
        Cause = cause ?? throw new ArgumentNullException(nameof(cause), "A fault's cause is the exception underneath; leave it out for none.");

    /// <summary>Which fault.</summary>
    public TFault Reason { get; }

    /// <summary>A sentence about this occurrence, or <see langword="null"/>.</summary>
    public string? Detail { get; }

    /// <summary>
    /// Which rule refused, where the reason answers for several: the rule's type and member,
    /// <c>EmailFault.NoAt</c>, a nested type after its nesting chain; or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Set by <c>WithRule</c> from an enum's member or a closed set's member, so it is always a name
    /// from a fixed set, and never quotes the request. The name has no namespace and no type arguments,
    /// so two rule types can read alike, <c>Rule.Blank</c> from two namespaces; the application names
    /// its rule types apart for a client. Equality compares the rule's type and member, so two such
    /// faults are unequal although their rules read alike; it takes part as the detail does.
    /// </remarks>
    public string? Rule { get; private init; }

    /// <summary>The rule's type, which equality compares beside <see cref="Rule"/>: two types' rules can read alike.</summary>
    private Type? RuleType { get; init; }

    /// <summary>The exception that caused this occurrence, or <see langword="null"/>: for the log, never for the client.</summary>
    /// <remarks>
    /// Set by <see cref="Fault.Of{TFault}(TFault, string?, Exception)"/>, kept by <c>WithRule</c> and by a fault of
    /// several types. CodoMetis.TypeKit.AspNetCore hands it to the host beside the problem and never writes it into
    /// one. Equality leaves it out, so a fault with a cause equals the same fault without one, and the fault prints
    /// without it: log it as the exception it is.
    /// </remarks>
    public Exception? Cause { get; }

    /// <summary>Equal where the reason, the detail and the rule are; the <see cref="Cause"/> takes no part.</summary>
    /// <param name="other">The other fault.</param>
    /// <returns>Whether the two are the same fault.</returns>
    public bool Equals(Fault<TFault> other) =>
        EqualityComparer<TFault>.Default.Equals(Reason, other.Reason)
        && string.Equals(Detail, other.Detail, StringComparison.Ordinal)
        && string.Equals(Rule, other.Rule, StringComparison.Ordinal)
        && RuleType == other.RuleType;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Reason, Detail, Rule, RuleType);

    /// <summary>The same fault with <paramref name="rule"/>, a member of <paramref name="type"/>, as its <see cref="Rule"/>.</summary>
    internal Fault<TFault> Naming(string rule, Type type) => this with { Rule = rule, RuleType = type };

    /// <summary>The reason and the detail, as 2.0 printed them, and the rule where there is one.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Reason = ").Append(Reason).Append(", Detail = ").Append(Detail);
        if (Rule is not null) builder.Append(", Rule = ").Append(Rule);
        return true;
    }

    // A bare reason does not convert to a fault: with the conversion, `fault == OrderFault.NotFound`,
    // `faults.Contains(OrderFault.NotFound)` and `ShouldBeError(OrderFault.NotFound)` compiled and were
    // false whenever the fault carried a detail. Without it the operators below would be CS0019, which
    // does not name Reason, and `fault.Equals(OrderFault.NotFound)` would bind to Equals(object) and
    // compile, always false; so the mixed forms stay declared, and refused where they are written.
    private const string MixedComparison =
        "A Fault<TFault> is never compared with a bare reason: a fault carries a detail beside its reason, and the comparison that is meant is fault.Reason with the reason.";

    /// <summary>Refused: compare <see cref="Reason"/> with the reason.</summary>
    /// <param name="fault">The fault.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator ==(Fault<TFault> fault, TFault reason) => throw new NotSupportedException();

    /// <summary>Refused: compare <see cref="Reason"/> with the reason.</summary>
    /// <param name="fault">The fault.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator !=(Fault<TFault> fault, TFault reason) => throw new NotSupportedException();

    /// <summary>Refused: compare the reason with <see cref="Reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="fault">The fault.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator ==(TFault reason, Fault<TFault> fault) => throw new NotSupportedException();

    /// <summary>Refused: compare the reason with <see cref="Reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="fault">The fault.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator !=(TFault reason, Fault<TFault> fault) => throw new NotSupportedException();

    /// <summary>Refused: compare <see cref="Reason"/> with the reason.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the method does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Equals(TFault reason) => throw new NotSupportedException();
}

/// <summary>Names the rule that refused on a <see cref="Fault{TFault}"/>, an enum's member.</summary>
/// <remarks>A closed set's member takes the same call from <see cref="FaultClosedSetRuleExtensions"/>.</remarks>
public static class FaultRuleExtensions
{
    /// <param name="fault">The fault.</param>
    /// <typeparam name="TFault">The reason's type.</typeparam>
    extension<TFault>(Fault<TFault> fault) where TFault : notnull
    {
        /// <summary>
        /// The same fault, naming <paramref name="rule"/> as the rule that refused: its <see cref="Fault{TFault}.Rule"/>
        /// is the enum's name and the member's, <c>EmailFault.NoAt</c>, a nested enum after its nesting chain.
        /// </summary>
        /// <remarks>
        /// A value no member names, such as a combination of flags or a cast number, throws, since a
        /// client branches on the name and a number is none. A second call replaces the first rule.
        /// </remarks>
        /// <param name="rule">The rule, one of the enum's members.</param>
        /// <typeparam name="TRule">The rule's type, an enum.</typeparam>
        /// <returns>A new fault.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="rule"/> is no member of <typeparamref name="TRule"/>.</exception>
        public Fault<TFault> WithRule<TRule>(TRule rule) where TRule : struct, Enum =>
            Enum.IsDefined(rule)
                ? fault.Naming($"{FaultRuleNames.NestingChain(typeof(TRule))}.{rule}", typeof(TRule))
                : throw new ArgumentOutOfRangeException(nameof(rule), $"A rule is one of {FaultRuleNames.NestingChain(typeof(TRule))}'s members, and no member is this value.");
    }
}

/// <summary>Names the rule that refused on a <see cref="Fault{TFault}"/>, a closed set's member.</summary>
/// <remarks>Separate from the enum's, which takes the same parameters, since C# tells the two apart by their constraints alone.</remarks>
public static class FaultClosedSetRuleExtensions
{
    /// <param name="fault">The fault.</param>
    /// <typeparam name="TFault">The reason's type.</typeparam>
    extension<TFault>(Fault<TFault> fault) where TFault : notnull
    {
        /// <summary>
        /// The same fault, naming <paramref name="rule"/> as the rule that refused: its <see cref="Fault{TFault}.Rule"/>
        /// is the member's <c>QualifiedName</c>, <c>EmailRule.NoAt</c>.
        /// </summary>
        /// <remarks>A second call replaces the first rule.</remarks>
        /// <param name="rule">The rule, one of the closed set's members. Never null.</param>
        /// <typeparam name="TRule">The rule's type, a closed set.</typeparam>
        /// <returns>A new fault.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
        public Fault<TFault> WithRule<TRule>(TRule rule) where TRule : class, IClosedSet<TRule>
        {
            ArgumentNullException.ThrowIfNull(rule);
            return fault.Naming(rule.QualifiedName, typeof(TRule));
        }
    }
}

/// <summary>How a rule's type is named: after its nesting chain, as a closed set's <c>QualifiedName</c> is.</summary>
internal static class FaultRuleNames
{
    /// <summary><c>Billing.Fault</c> for an enum nested in <c>Billing</c>, without a generic arity.</summary>
    public static string NestingChain(Type type) =>
        type.DeclaringType is { } declaring ? $"{NestingChain(declaring)}.{Simple(type)}" : Simple(type);

    private static string Simple(Type type) =>
        type.Name.IndexOf('`') is var tick and >= 0 ? type.Name[..tick] : type.Name;
}

/// <summary>Creates a <see cref="Fault{TFault}"/>, inferring its type.</summary>
public static class Fault
{
    /// <summary>A fault with a sentence about this occurrence.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <typeparam name="TFault">The reason's type, inferred.</typeparam>
    /// <returns>The fault.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is empty or white space.</exception>
    public static Fault<TFault> Of<TFault>(TFault reason, string? detail = null) where TFault : notnull => new(reason, detail);

    /// <summary>A fault and the exception that caused it, kept for the log and never written to a response.</summary>
    /// <remarks>
    /// The pattern is a <c>MapError</c> from a caught exception:
    /// <c>Result.Catch(read).MapError(e =&gt; Fault.Of(ImportFault.Unreadable, "The file could not be read.", e))</c>.
    /// </remarks>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <param name="cause">The exception underneath. Never null.</param>
    /// <typeparam name="TFault">The reason's type, inferred.</typeparam>
    /// <returns>The fault, with <paramref name="cause"/> as its <see cref="Fault{TFault}.Cause"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> or <paramref name="cause"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is empty or white space.</exception>
    public static Fault<TFault> Of<TFault>(TFault reason, string? detail, Exception cause) where TFault : notnull => new(reason, detail, cause);
}

// CI lanes demo (#123): a src/ change, never merged.
