using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

/// <summary>
/// A value that may be absent. There is no public <c>.Value</c>: the content is reached through
/// <see cref="Match{TResult}"/>, <see cref="TryGetValue"/> and the combinators, so absence is always
/// handled where the value is used.
/// </summary>
/// <remarks>
/// Create instances with <see cref="Option.Some{T}"/> and <see cref="Option.None{T}"/>. A
/// <c>default</c> option is <c>None</c>. <see cref="object.ToString"/> never prints the content, so an
/// option can be logged without leaking what it holds. The debugger shows the content.
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
[RequireCustomInitialization]
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly record struct Option<T> where T : notnull
{
    private readonly T?   _value    = default;
    private readonly bool _hasValue = false;

    /// <summary>Creates a <c>None</c>. Use <see cref="Option.None{T}"/> instead.</summary>
    [DebuggerHidden]
    [DebuggerNonUserCode]
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Option()
    {
    }

    internal Option(T? value, bool hasValue)
    {
        _value    = value;
        _hasValue = hasValue;
    }

    private string DebuggerDisplay => _hasValue ? $"Some({_value})" : "None";

    /// <summary>Whether this option holds a value.</summary>
    public bool IsSome() => _hasValue;

    /// <summary>Whether this option is empty.</summary>
    public bool IsNone() => !_hasValue;

    /// <summary>Produces a result from either the value or its absence.</summary>
    /// <param name="onSome">Called with the value if there is one.</param>
    /// <param name="onNone">Called if there is no value.</param>
    /// <typeparam name="TResult">The type of the produced result.</typeparam>
    /// <returns>What the called function returned.</returns>
    public TResult Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone)
    {
        ArgumentNullException.ThrowIfNull(onSome);
        ArgumentNullException.ThrowIfNull(onNone);

        return _hasValue ? onSome(_value!) : onNone();
    }

    /// <summary>Chains an operation that may itself produce no value.</summary>
    /// <param name="selector">Called with the value if there is one.</param>
    /// <typeparam name="TResult">The type of the chained option's value.</typeparam>
    /// <returns>The option <paramref name="selector"/> returned, or <c>None</c> without calling it.</returns>
    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> selector) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        return _hasValue ? selector(_value!) : Option.None<TResult>();
    }

    /// <summary>Transforms the value, if there is one.</summary>
    /// <param name="selector">Called with the value if there is one. It must not return null.</param>
    /// <typeparam name="TResult">The type of the transformed value.</typeparam>
    /// <returns>The transformed value, or <c>None</c> without calling <paramref name="selector"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned null.</exception>
    public Option<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        return _hasValue ? Option.Some(selector(_value!)) : Option.None<TResult>();
    }

    /// <summary>Runs a side effect on the value, if there is one.</summary>
    /// <param name="action">Called with the value if there is one.</param>
    /// <returns>This option, unchanged.</returns>
    public Option<T> Tap(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_hasValue)
            action(_value!);

        return this;
    }

    /// <summary>Keeps the value only if it satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">Called with the value if there is one.</param>
    /// <returns>This option if it holds a value that passes, otherwise <c>None</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Option<T> Filter(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return _hasValue && predicate(_value!) ? this : Option.None<T>();
    }

    /// <summary>
    /// Unwraps the value, substituting <paramref name="fallback"/> for <c>None</c>. The siblings for a
    /// fallback of <c>default</c> or <see langword="null"/> are <c>OrDefault()</c> and <c>OrNull()</c>.
    /// </summary>
    /// <param name="fallback">Returned if there is no value.</param>
    /// <returns>The value, or <paramref name="fallback"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Or(T fallback) => TryGetValue(out var value) ? value : fallback;

    /// <summary>Unwraps the value, if there is one.</summary>
    /// <param name="value">The value if there is one; otherwise <c>default</c>.</param>
    /// <returns>Whether there is a value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue([NotNullWhen(returnValue: true)] out T? value)
    {
        value = _value;
        return _hasValue;
    }

    /// <summary>The value as a sequence of one element, or an empty sequence for <c>None</c>.</summary>
    /// <returns>A sequence with the value, or an empty one.</returns>
    public IEnumerable<T> AsEnumerable()
    {
        if (_hasValue) yield return _value!;
    }

    /// <summary>Converts the <c>Option.None()</c> marker, so a method can <c>return Option.None();</c>.</summary>
    /// <param name="none">The marker.</param>
    public static implicit operator Option<T>(None none) => Option.None<T>();
}
