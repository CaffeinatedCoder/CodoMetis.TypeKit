using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CodoMetis.TypeKit.Attributes;

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
    /// <param name="fnSome">Called with the value if there is one.</param>
    /// <param name="fnNone">Called if there is no value.</param>
    /// <typeparam name="TResult">The type of the produced result.</typeparam>
    /// <returns>What the called function returned.</returns>
    public TResult Match<TResult>(Func<T, TResult> fnSome, Func<TResult> fnNone)
    {
        ArgumentNullException.ThrowIfNull(fnSome);
        ArgumentNullException.ThrowIfNull(fnNone);

        return _hasValue ? fnSome(_value!) : fnNone();
    }

    /// <summary>Chains an operation that may itself produce no value.</summary>
    /// <param name="fn">Called with the value if there is one.</param>
    /// <typeparam name="TResult">The type of the chained option's value.</typeparam>
    /// <returns>The option <paramref name="fn"/> returned, or <c>None</c> without calling it.</returns>
    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> fn) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(fn);

        return _hasValue ? fn(_value!) : Option.None<TResult>();
    }

    /// <summary>Transforms the value, if there is one.</summary>
    /// <param name="map">Called with the value if there is one.</param>
    /// <typeparam name="TResult">The type of the transformed value.</typeparam>
    /// <returns>The transformed value, or <c>None</c> without calling <paramref name="map"/>.</returns>
    public Option<TResult> Map<TResult>(Func<T, TResult> map) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(map);

        return _hasValue ? Option.Some(map(_value!)) : Option.None<TResult>();
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

    /// <summary>Keeps the value only if it passes a check.</summary>
    /// <param name="check">Called with the value if there is one.</param>
    /// <returns>This option if it holds a value that passes, otherwise <c>None</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Option<T> Filter(Func<T, bool> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        return _hasValue && check(_value!) ? this : Option.None<T>();
    }

    /// <summary>Unwraps the value, substituting a fallback for <c>None</c>.</summary>
    /// <param name="alternateValue">Returned if there is no value.</param>
    /// <returns>The value, or <paramref name="alternateValue"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Coalesce(T alternateValue) => TryGetValue(out var value) ? value : alternateValue;

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
}
