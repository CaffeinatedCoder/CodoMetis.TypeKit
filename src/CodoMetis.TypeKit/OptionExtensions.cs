using System.Runtime.CompilerServices;

namespace CodoMetis.TypeKit;

/// <summary>
/// Creates <see cref="Option{T}"/> instances, and adds the LINQ vocabulary, the zips and the
/// conversions to and from nullables, sequences and results.
/// </summary>
public static class Option
{
    /// <summary>Creates an option that holds <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>An option holding <paramref name="value"/>.</returns>
    public static Option<T> Some<T>(T value) where T : notnull => new(value, true);

    /// <summary>Creates an empty option.</summary>
    /// <typeparam name="T">The type of the value the option could have held.</typeparam>
    /// <returns>An empty option.</returns>
    public static Option<T> None<T>() where T : notnull => new(default, false);

    /// <param name="a">The option.</param>
    /// <typeparam name="T">The type of the option's value.</typeparam>
    extension<T>(in Option<T> a) where T : notnull
    {
        /// <summary>Turns absence into an error.</summary>
        /// <param name="onSuccess">Transforms the value if there is one.</param>
        /// <param name="error">The error for <c>None</c>.</param>
        /// <typeparam name="TSuccess">The type of the result's value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with the transformed value, or an error with <paramref name="error"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TSuccess, TError> ToResult<TSuccess, TError>(Func<T, TSuccess> onSuccess, TError error) =>
            a.Match<Result<TSuccess, TError>>(x => onSuccess(x), () => Result.Error(error));

        /// <summary>Turns absence into an error, dropping the value.</summary>
        /// <param name="error">The error for <c>None</c>.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success if there is a value, otherwise an error with <paramref name="error"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TError> ToResult<TError>(TError error) where TError : notnull =>
            a.IsSome() ? Result.Ok() : error;

        /// <summary>Transforms the value, if there is one. Enables <c>select</c> in query syntax.</summary>
        /// <param name="map">Called with the value if there is one.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>The transformed value, or <c>None</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> Select<TResult>(Func<T, TResult> map) where TResult : notnull =>
            a.Map(map);

        /// <summary>Chains an operation that may itself produce no value. Same as <see cref="Option{T}.Bind{TResult}"/>.</summary>
        /// <param name="map">Called with the value if there is one.</param>
        /// <typeparam name="TResult">The type of the chained option's value.</typeparam>
        /// <returns>The option <paramref name="map"/> returned, or <c>None</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<TResult> SelectMany<TResult>(Func<T, Option<TResult>> map) where TResult : notnull =>
            a.Bind(map);

        /// <summary>Keeps the value only if it passes a check. Enables <c>where</c> in query syntax.</summary>
        /// <param name="check">Called with the value if there is one.</param>
        /// <returns>The option if its value passes, otherwise <c>None</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Option<T> Where(Func<T, bool> check) => a.Filter(check);

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
            => a.Bind(x => b.Map(y => selector(x, y)));

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
            => a.TryGetValue(out var v1) &&
               b.TryGetValue(out var v2) &&
               c.TryGetValue(out var v3)
                   ? Some(selector(v1, v2, v3))
                   : None<TResult>();

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
            => a.TryGetValue(out var v1) &&
               b.TryGetValue(out var v2) &&
               c.TryGetValue(out var v3) &&
               d.TryGetValue(out var v4)
                   ? Some(selector(v1, v2, v3, v4))
                   : None<TResult>();

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
            => a.TryGetValue(out var v1) &&
               b.TryGetValue(out var v2) &&
               c.TryGetValue(out var v3) &&
               d.TryGetValue(out var v4) &&
               e.TryGetValue(out var v5)
                   ? Some(selector(v1, v2, v3, v4, v5))
                   : None<TResult>();

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
            => a.TryGetValue(out var v1) &&
               b.TryGetValue(out var v2) &&
               c.TryGetValue(out var v3) &&
               d.TryGetValue(out var v4) &&
               e.TryGetValue(out var v5) &&
               f.TryGetValue(out var v6)
                   ? Some(selector(v1, v2, v3, v4, v5, v6))
                   : None<TResult>();
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
        /// <summary>Unwraps the value, substituting <see langword="null"/> for <c>None</c>.</summary>
        /// <returns>The value, or <see langword="null"/>.</returns>
        public T? OrNull() => option.TryGetValue(out var value) ? value : null;
    }

    /// <param name="source">The sequence.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    extension<T>(IEnumerable<T> source) where T : notnull
    {
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

    /// <param name="instance">The object to cast.</param>
    extension(object? instance)
    {
        /// <summary>Casts to <typeparamref name="TResult"/>, if the run-time type allows it.</summary>
        /// <typeparam name="TResult">The type to cast to.</typeparam>
        /// <returns>The cast instance, or <c>None</c> if it is not a <typeparamref name="TResult"/> or is <see langword="null"/>.</returns>
        public Option<TResult> TryCast<TResult>() where TResult : notnull =>
            instance is TResult result
                ? Some(result)
                : None<TResult>();
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
