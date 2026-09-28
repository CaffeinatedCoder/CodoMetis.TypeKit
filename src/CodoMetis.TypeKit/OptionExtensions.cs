using System.Runtime.CompilerServices;

namespace CodoMetis.TypeKit;

/// <summary>
/// Creates <see cref="Option{T}"/> instances, and adds the LINQ vocabulary, the zips and the
/// conversions to and from nullables, sequences and results.
/// </summary>
public static class Option
{
    /// <summary>Creates an option that holds <paramref name="value"/>.</summary>
    /// <param name="value">The value. Never null: absence is <see cref="None{T}"/>.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>An option holding <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static Option<T> Some<T>(T value) where T : notnull
    {
        // notnull is an annotation, not a run-time check: without this, Some(null!) reported a value
        // and TryGetValue handed out null despite [NotNullWhen(true)]. `is null` costs nothing for a
        // value type, where the JIT drops it; ThrowIfNull(object?) would box one.
        if (value is null) throw new ArgumentNullException(nameof(value));

        return new(value, true);
    }

    /// <summary>Creates an empty option.</summary>
    /// <typeparam name="T">The type of the value the option could have held.</typeparam>
    /// <returns>An empty option.</returns>
    public static Option<T> None<T>() where T : notnull => new(default, false);

    /// <summary>
    /// The empty marker, for a method typed <see cref="Option{T}"/>: it converts implicitly to an empty
    /// option of any <c>T</c>, so <c>return Option.None();</c> needs no type argument. Where nothing
    /// gives the target type, such as <c>var</c>, use <see cref="None{T}"/>.
    /// </summary>
    /// <returns>A marker that converts implicitly to an empty option.</returns>
    public static None None() => new();

    /// <param name="a">The option.</param>
    /// <typeparam name="T">The type of the option's value.</typeparam>
    extension<T>(in Option<T> a) where T : notnull
    {
        /// <summary>Turns absence into an error, keeping the value.</summary>
        /// <param name="error">The error for <c>None</c>.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with the value, or an error with <paramref name="error"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T, TError> ToResult<TError>(TError error) where TError : notnull
        {
            // Checked whatever the option holds, as the delegates are: a null error passed for every
            // Some until the first None.
            if (error is null) throw new ArgumentNullException(nameof(error));

            return a.TryGetValue(out var value) ? Result<T, TError>.Success(value) : Result<T, TError>.Error(error);
        }

        /// <summary>Transforms the value, if there is one. Enables <c>select</c> in query syntax.</summary>
        /// <param name="selector">Called with the value if there is one.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>The transformed value, or <c>None</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> Select<TResult>(Func<T, TResult> selector) where TResult : notnull =>
            a.Map(selector);

        /// <summary>Chains an operation that may itself produce no value. Same as <see cref="Option{T}.Bind{TResult}"/>.</summary>
        /// <param name="selector">Called with the value if there is one.</param>
        /// <typeparam name="TResult">The type of the chained option's value.</typeparam>
        /// <returns>The option <paramref name="selector"/> returned, or <c>None</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> SelectMany<TResult>(Func<T, Option<TResult>> selector) where TResult : notnull =>
            a.Bind(selector);

        /// <summary>
        /// Chains an operation that may itself produce no value, and combines both values. Enables a
        /// second <c>from</c> in query syntax, where the later steps can use every earlier value.
        /// </summary>
        /// <param name="selector">Called with the value if there is one.</param>
        /// <param name="resultSelector">Called with both values if the chained option holds one too.</param>
        /// <typeparam name="TNext">The type of the chained option's value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>The combined value, or <c>None</c> if either option is empty.</returns>
        public Option<TResult> SelectMany<TNext, TResult>(Func<T, Option<TNext>> selector, Func<T, TNext, TResult> resultSelector)
            where TNext : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentNullException.ThrowIfNull(resultSelector);

            return a.TryGetValue(out var value) && selector(value).TryGetValue(out var next)
                ? Some(resultSelector(value, next))
                : None<TResult>();
        }

        /// <summary>Keeps the value only if it satisfies <paramref name="predicate"/>. Enables <c>where</c> in query syntax.</summary>
        /// <param name="predicate">Called with the value if there is one.</param>
        /// <returns>The option if its value passes, otherwise <c>None</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<T> Where(Func<T, bool> predicate) => a.Filter(predicate);

        /// <summary>Combines two options, if both hold a value.</summary>
        /// <param name="b">The second option.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>The combined value, or <c>None</c> if either option is empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> Zip<T2, TResult>(Option<T2> b, Func<T, T2, TResult> selector)
            where T2 : notnull
            where TResult : notnull
        {
            // Checked here: Bind and Map only see the lambdas around it, so a null passed on every
            // None. The other zips never call it for a None either.
            ArgumentNullException.ThrowIfNull(selector);

            return a.Bind(x => b.Map(y => selector(x, y)));
        }

        /// <summary>Combines three options, if all hold a value.</summary>
        /// <param name="b">The second option.</param>
        /// <param name="c">The third option.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>The combined value, or <c>None</c> if any option is empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> Zip<T2, T3, TResult>(Option<T2> b, Option<T3> c, Func<T, T2, T3, TResult> selector)
            where T2 : notnull
            where T3 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            return a.TryGetValue(out var v1) &&
                   b.TryGetValue(out var v2) &&
                   c.TryGetValue(out var v3)
                       ? Some(selector(v1, v2, v3))
                       : None<TResult>();
        }

        /// <summary>Combines four options, if all hold a value.</summary>
        /// <param name="b">The second option.</param>
        /// <param name="c">The third option.</param>
        /// <param name="d">The fourth option.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>The combined value, or <c>None</c> if any option is empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> Zip<T2, T3, T4, TResult>(
            Option<T2>                   b,
            Option<T3>                   c,
            Option<T4>                   d,
            Func<T, T2, T3, T4, TResult> selector
        )
            where T2 : notnull
            where T3 : notnull
            where T4 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            return a.TryGetValue(out var v1) &&
                   b.TryGetValue(out var v2) &&
                   c.TryGetValue(out var v3) &&
                   d.TryGetValue(out var v4)
                       ? Some(selector(v1, v2, v3, v4))
                       : None<TResult>();
        }

        /// <summary>Combines five options, if all hold a value.</summary>
        /// <param name="b">The second option.</param>
        /// <param name="c">The third option.</param>
        /// <param name="d">The fourth option.</param>
        /// <param name="e">The fifth option.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>The combined value, or <c>None</c> if any option is empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> Zip<T2, T3, T4, T5, TResult>(
            Option<T2>                       b,
            Option<T3>                       c,
            Option<T4>                       d,
            Option<T5>                       e,
            Func<T, T2, T3, T4, T5, TResult> selector
        )
            where T2 : notnull
            where T3 : notnull
            where T4 : notnull
            where T5 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            return a.TryGetValue(out var v1) &&
                   b.TryGetValue(out var v2) &&
                   c.TryGetValue(out var v3) &&
                   d.TryGetValue(out var v4) &&
                   e.TryGetValue(out var v5)
                       ? Some(selector(v1, v2, v3, v4, v5))
                       : None<TResult>();
        }

        /// <summary>Combines six options, if all hold a value.</summary>
        /// <param name="b">The second option.</param>
        /// <param name="c">The third option.</param>
        /// <param name="d">The fourth option.</param>
        /// <param name="e">The fifth option.</param>
        /// <param name="f">The sixth option.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>The combined value, or <c>None</c> if any option is empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> Zip<T2, T3, T4, T5, T6, TResult>(
            Option<T2>                           b,
            Option<T3>                           c,
            Option<T4>                           d,
            Option<T5>                           e,
            Option<T6>                           f,
            Func<T, T2, T3, T4, T5, T6, TResult> selector
        )
            where T2 : notnull
            where T3 : notnull
            where T4 : notnull
            where T5 : notnull
            where T6 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            return a.TryGetValue(out var v1) &&
                   b.TryGetValue(out var v2) &&
                   c.TryGetValue(out var v3) &&
                   d.TryGetValue(out var v4) &&
                   e.TryGetValue(out var v5) &&
                   f.TryGetValue(out var v6)
                       ? Some(selector(v1, v2, v3, v4, v5, v6))
                       : None<TResult>();
        }
    }

    /// <param name="option">The option.</param>
    /// <typeparam name="T">The value type the option holds.</typeparam>
    extension<T>(in Option<T> option) where T : struct
    {
        /// <summary>Unwraps the value, substituting <c>default</c> for <c>None</c>.</summary>
        /// <returns>The value, or <c>default</c>.</returns>
        public T OrDefault() => option.Match(x => x, () => default);
    }

    /// <param name="option">The option.</param>
    /// <typeparam name="T">The reference type the option holds.</typeparam>
    extension<T>(in Option<T> option) where T : class
    {
        /// <summary>
        /// Unwraps the value, substituting <see langword="null"/> for <c>None</c>: the inverse of
        /// <c>ToOption()</c>. For a value type, <see cref="ValueTypeOptionExtensions"/> has the same call.
        /// </summary>
        /// <returns>The value, or <see langword="null"/>.</returns>
        public T? OrNull() => option.TryGetValue(out var value) ? value : null;
    }

    /// <param name="source">The sequence.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    extension<T>(IEnumerable<T> source) where T : notnull
    {
        /// <summary>The first element, or <c>None</c> for an empty sequence.</summary>
        /// <returns>The first element, or <c>None</c> if there is none.</returns>
        /// <exception cref="ArgumentNullException">The first element is null.</exception>
        public Option<T> FirstOrNone()
        {
            ArgumentNullException.ThrowIfNull(source);

            using var enumerator = source.GetEnumerator();

            return enumerator.MoveNext() ? Some(enumerator.Current) : None<T>();
        }

        /// <summary>The last element, or <c>None</c> for an empty sequence.</summary>
        /// <returns>The last element, or <c>None</c> if there is none.</returns>
        /// <exception cref="ArgumentNullException">The last element is null.</exception>
        public Option<T> LastOrNone()
        {
            ArgumentNullException.ThrowIfNull(source);

            if (source is IReadOnlyList<T> list) return list.Count > 0 ? Some(list[^1]) : None<T>();

            using var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext()) return None<T>();

            var last = enumerator.Current;
            while (enumerator.MoveNext()) last = enumerator.Current;

            return Some(last);
        }

        /// <summary>The first element that matches, or <c>None</c>.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <returns>The first matching element, or <c>None</c> if nothing matches.</returns>
        public Option<T> FirstOrNone(Func<T, bool> predicate) =>
            source.Where(predicate)
                  .Select(Some)
                  .DefaultIfEmpty(None<T>())
                  .FirstOrDefault();

        /// <summary>The last element that matches, or <c>None</c>.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <returns>The last matching element, or <c>None</c> if nothing matches.</returns>
        public Option<T> LastOrNone(Func<T, bool> predicate) =>
            source.Where(predicate)
                  .Select(Some)
                  .DefaultIfEmpty(None<T>())
                  .LastOrDefault();
    }

    /// <param name="dictionary">The dictionary.</param>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TValue">The type of the values.</typeparam>
    extension<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary) where TValue : notnull
    {
        /// <summary>The value stored under <paramref name="key"/>, or <c>None</c>: <c>TryGetValue</c> as an option.</summary>
        /// <param name="key">The key to look up.</param>
        /// <returns>The value, or <c>None</c> if the dictionary has no such key.</returns>
        /// <exception cref="ArgumentNullException">The value stored under <paramref name="key"/> is null.</exception>
        public Option<TValue> GetValueOrNone(TKey key)
        {
            ArgumentNullException.ThrowIfNull(dictionary);

            return dictionary.TryGetValue(key, out var value) ? Some(value) : None<TValue>();
        }
    }

    /// <param name="instance">The nullable value.</param>
    /// <typeparam name="T">The underlying value type.</typeparam>
    extension<T>(in T? instance) where T : struct
    {
        /// <summary>Lifts a nullable value into an option.</summary>
        /// <returns>The value, or <c>None</c> for <see langword="null"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<T> ToOption() => instance.HasValue ? Some(instance.Value) : None<T>();
    }

    /// <param name="instance">The nullable reference.</param>
    /// <typeparam name="T">The reference type.</typeparam>
    extension<T>(T? instance) where T : class
    {
        /// <summary>Lifts a nullable reference into an option.</summary>
        /// <returns>The reference, or <c>None</c> for <see langword="null"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<T> ToOption() => instance is not null ? Some(instance) : None<T>();
    }
}
