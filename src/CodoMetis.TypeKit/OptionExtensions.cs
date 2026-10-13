using System.Runtime.CompilerServices;

namespace CodoMetis.TypeKit;

/// <summary>
/// Creates <see cref="Option{T}"/> instances, and adds the LINQ vocabulary, the zips and the
/// conversions to and from nullables, sequences and results.
/// </summary>
/// <remarks>
/// The continuations of a pending option (<c>MapAsync</c>, <c>BindAsync</c>, <c>FilterAsync</c>,
/// <c>TapAsync</c>) never resume on the caller's <see cref="SynchronizationContext"/>, as
/// <see cref="Result"/>'s do not: a callback runs at once, on the caller's context, when the option was
/// already known, and otherwise wherever its task completed, without that context. In a UI or a Blazor
/// component, await the option and call the synchronous combinator on it, or marshal inside the callback.
/// </remarks>
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
        if (value is null) throw ThrowHelper.NullContent(nameof(value));

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
            if (error is null) throw ThrowHelper.NullContent(nameof(error));

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

    /// <param name="task">An option that is still being produced, such as a repository's lookup.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <remarks>
    /// The continuations of <c>Task&lt;Result&lt;…&gt;&gt;</c> for an option: every delegate, and an
    /// error for <c>ToResultAsync</c>, is checked before the option is known, as before a branch is
    /// picked. A callback that returns a <c>Task</c> is waited for; one that returns a
    /// <c>ValueTask</c> needs <c>.AsTask()</c> or an <c>async</c> lambda, as for a result.
    /// </remarks>
    extension<T>(Task<Option<T>> task) where T : notnull
    {
        /// <summary>Transforms the value once the option is known, if there is one.</summary>
        /// <param name="selector">Called with the value if there is one. It must not return null.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>The transformed value, or <c>None</c> without calling <paramref name="selector"/>.</returns>
        public async Task<Option<TResult>> MapAsync<TResult>(Func<T, TResult> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Map(selector);
        }

        /// <summary>Transforms the value once the option is known, with an asynchronous function.</summary>
        /// <param name="selector">Called with the value if there is one. Its task must not produce null.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>The transformed value, or <c>None</c> without calling <paramref name="selector"/>.</returns>
        public async Task<Option<TResult>> MapAsync<TResult>(Func<T, Task<TResult>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).MapAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Chains an operation that may itself produce no value, once the option is known.</summary>
        /// <param name="selector">Called with the value if there is one.</param>
        /// <typeparam name="TResult">The type of the chained option's value.</typeparam>
        /// <returns>The option <paramref name="selector"/> returned, or <c>None</c> without calling it.</returns>
        public async Task<Option<TResult>> BindAsync<TResult>(Func<T, Option<TResult>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Bind(selector);
        }

        /// <summary>Chains an asynchronous operation that may itself produce no value, once the option is known.</summary>
        /// <param name="selector">Called with the value if there is one.</param>
        /// <typeparam name="TResult">The type of the chained option's value.</typeparam>
        /// <returns>The option <paramref name="selector"/> produced, or <c>None</c> without calling it.</returns>
        public async Task<Option<TResult>> BindAsync<TResult>(Func<T, Task<Option<TResult>>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).BindAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Keeps the value once the option is known, only if it satisfies <paramref name="predicate"/>.</summary>
        /// <param name="predicate">Called with the value if there is one.</param>
        /// <returns>The option if it holds a value that passes, otherwise <c>None</c>.</returns>
        public async Task<Option<T>> FilterAsync(Func<T, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(predicate);

            return (await task.ConfigureAwait(false)).Filter(predicate);
        }

        /// <summary>Runs a side effect on the value once the option is known, if there is one.</summary>
        /// <param name="action">Called with the value if there is one.</param>
        /// <returns>The option, unchanged.</returns>
        public async Task<Option<T>> TapAsync(Action<T> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return (await task.ConfigureAwait(false)).Tap(action);
        }

        /// <summary>Runs an asynchronous side effect on the value once the option is known, if there is one.</summary>
        /// <param name="action">Called with the value if there is one.</param>
        /// <returns>The option, unchanged, once <paramref name="action"/> has completed.</returns>
        public async Task<Option<T>> TapAsync(Func<T, Task> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return await (await task.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);
        }

        /// <summary>Turns absence into <paramref name="error"/> once the option is known: the step from a lookup into a <c>Result</c> pipeline.</summary>
        /// <param name="error">The error for <c>None</c>. Never null.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with the value, or <paramref name="error"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null, whether or not the option has a value.</exception>
        public async Task<Result<T, TError>> ToResultAsync<TError>(TError error) where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            if (error is null) throw ThrowHelper.NullContent(nameof(error));

            return (await task.ConfigureAwait(false)).ToResult(error);
        }
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

        /// <summary>
        /// The only element, or <c>None</c> for an empty sequence. More than one is not absence: it
        /// throws, as <c>SingleOrDefault</c> does, since reading it as <c>None</c> would hide the defect.
        /// </summary>
        /// <returns>The only element, or <c>None</c> if there is none.</returns>
        /// <exception cref="InvalidOperationException">The sequence holds more than one element.</exception>
        /// <exception cref="ArgumentNullException">The only element is null.</exception>
        public Option<T> SingleOrNone()
        {
            ArgumentNullException.ThrowIfNull(source);

            return SingleOf(source);
        }

        /// <summary>The only element that matches, or <c>None</c>; more than one throws, as <c>SingleOrDefault</c> does.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <returns>The only matching element, or <c>None</c> if nothing matches.</returns>
        /// <exception cref="InvalidOperationException">More than one element matches.</exception>
        /// <exception cref="ArgumentNullException">The only matching element is null.</exception>
        public Option<T> SingleOrNone(Func<T, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);

            return SingleOf(source.Where(predicate));
        }

        /// <summary>The element at <paramref name="index"/>, or <c>None</c> past either end.</summary>
        /// <param name="index">The zero-based index; a negative one is <c>None</c>.</param>
        /// <returns>The element, or <c>None</c> if there is none at that index.</returns>
        /// <exception cref="ArgumentNullException">The element is null.</exception>
        public Option<T> ElementAtOrNone(int index)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (index < 0) return None<T>();
            if (source is IReadOnlyList<T> list) return index < list.Count ? Some(list[index]) : None<T>();

            return FirstOf(source.Skip(index));
        }

        /// <summary>The element at <paramref name="index"/>, from the start or from the end (<c>^1</c> is the last), or <c>None</c> past either end.</summary>
        /// <param name="index">The index.</param>
        /// <returns>The element, or <c>None</c> if there is none at that index.</returns>
        /// <exception cref="ArgumentNullException">The element is null.</exception>
        public Option<T> ElementAtOrNone(Index index)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (!index.IsFromEnd) return source.ElementAtOrNone(index.Value);
            if (index.Value == 0) return None<T>();
            if (source is IReadOnlyList<T> list) return index.Value <= list.Count ? Some(list[^index.Value]) : None<T>();

            // The last n elements, of which the first is the one asked for, if there are n.
            var last = source.TakeLast(index.Value).ToList();
            return last.Count == index.Value ? Some(last[0]) : None<T>();
        }

        /// <summary>
        /// The values <paramref name="selector"/> finds: for each element it answers with <c>Some</c>,
        /// that value, in order, and nothing for a <c>None</c>. A filter and a map in one, as F#'s
        /// <c>choose</c>, and lazy, as a LINQ operator is.
        /// </summary>
        /// <param name="selector">Called with each element as the sequence is read.</param>
        /// <typeparam name="TResult">The type of the values.</typeparam>
        /// <returns>The values found, in order.</returns>
        public IEnumerable<TResult> Choose<TResult>(Func<T, Option<TResult>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            return Chosen(source, selector);
        }
    }

    /// <param name="source">The query.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <remarks>
    /// <para>
    /// A query is asked for one element, so the provider runs the condition and the limit where the
    /// query runs: a <c>WHERE</c> and a <c>LIMIT</c> in SQL. Through the <see cref="IEnumerable{T}"/> overloads, an
    /// <see cref="IQueryable{T}"/> was read in full and its condition compiled and run in memory, on
    /// every row of the table.
    /// </para>
    /// <para>
    /// Under Native AOT, EF Core runs only the queries it precompiled where the application calls its
    /// operators, and these compose the query here: they throw "Query wasn't precompiled" (measured
    /// with EF Core 10.0.12 in the smoke test). Write <c>Where(…).Take(1).ToList()</c> at the call site
    /// and take <c>FirstOrNone()</c> of the list.
    /// </para>
    /// </remarks>
    extension<T>(IQueryable<T> source) where T : notnull
    {
        /// <summary>The first element of the query, or <c>None</c>: the provider is asked for one.</summary>
        /// <returns>The first element, or <c>None</c> if the query has none.</returns>
        /// <exception cref="ArgumentNullException">The first element is null.</exception>
        public Option<T> FirstOrNone()
        {
            ArgumentNullException.ThrowIfNull(source);

            return FirstOf(source.Take(1));
        }

        /// <summary>The first element that matches, or <c>None</c>: the provider runs the condition, and is asked for one.</summary>
        /// <param name="predicate">The condition to match, translated by the query provider.</param>
        /// <returns>The first matching element, or <c>None</c> if nothing matches.</returns>
        /// <exception cref="ArgumentNullException">The first matching element is null.</exception>
        public Option<T> FirstOrNone(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);

            return FirstOf(source.Where(predicate).Take(1));
        }

        /// <summary>The only element of the query, or <c>None</c>: the provider is asked for two, and more than one throws.</summary>
        /// <returns>The only element, or <c>None</c> if the query has none.</returns>
        /// <exception cref="InvalidOperationException">The query has more than one element.</exception>
        /// <exception cref="ArgumentNullException">The only element is null.</exception>
        public Option<T> SingleOrNone()
        {
            ArgumentNullException.ThrowIfNull(source);

            return SingleOf(source.Take(2));
        }

        /// <summary>The only element that matches, or <c>None</c>: the provider runs the condition and is asked for two.</summary>
        /// <param name="predicate">The condition to match, translated by the query provider.</param>
        /// <returns>The only matching element, or <c>None</c> if nothing matches.</returns>
        /// <exception cref="InvalidOperationException">More than one element matches.</exception>
        /// <exception cref="ArgumentNullException">The only matching element is null.</exception>
        public Option<T> SingleOrNone(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);

            return SingleOf(source.Where(predicate).Take(2));
        }

        /// <summary>The element at <paramref name="index"/>, or <c>None</c>: the provider skips to it and is asked for one.</summary>
        /// <param name="index">The zero-based index; a negative one is <c>None</c>, without a query, since a query skips nothing for it.</param>
        /// <returns>The element, or <c>None</c> if there is none at that index.</returns>
        /// <exception cref="ArgumentNullException">The element is null.</exception>
        public Option<T> ElementAtOrNone(int index)
        {
            ArgumentNullException.ThrowIfNull(source);

            return index < 0 ? None<T>() : FirstOf(source.Skip(index).Take(1));
        }

        /// <summary>Refused: a query skips to an index from the start, and one from the end would read every row to find it.</summary>
        /// <param name="index">The index.</param>
        /// <returns>Never: the overload does not compile.</returns>
        [Obsolete("A query has no end until it is ordered, and ElementAtOrNone(^n) would read every row to find it. Order the query the other way and use ElementAtOrNone(int), or call AsEnumerable() to read it all on purpose.", error: true)]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public Option<T> ElementAtOrNone(Index index) => throw new NotSupportedException();

        /// <summary>Refused: a query has no last element until it is ordered, and this would read every row to find one.</summary>
        /// <returns>Never: the overload does not compile.</returns>
        [Obsolete("A query has no last element until it is ordered, and LastOrNone would read every row to find one. Order it the other way and use FirstOrNone, or call AsEnumerable() to read it all on purpose.", error: true)]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public Option<T> LastOrNone() => throw new NotSupportedException();

        /// <summary>Refused: a query has no last element until it is ordered, and this would read every row to find one.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <returns>Never: the overload does not compile.</returns>
        [Obsolete("A query has no last element until it is ordered, and LastOrNone would read every row to find one. Order it the other way and use FirstOrNone, or call AsEnumerable() to read it all on purpose.", error: true)]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public Option<T> LastOrNone(System.Linq.Expressions.Expression<Func<T, bool>> predicate) => throw new NotSupportedException();
    }

    /// <summary>The first element, or <c>None</c>; on a query, one limited to one, so it reads one row.</summary>
    private static Option<T> FirstOf<T>(IEnumerable<T> limited) where T : notnull
    {
        using var enumerator = limited.GetEnumerator();

        return enumerator.MoveNext() ? Some(enumerator.Current) : None<T>();
    }

    /// <summary>The only element, <c>None</c> for none, and a refusal for more; on a query, one limited to two.</summary>
    private static Option<T> SingleOf<T>(IEnumerable<T> elements) where T : notnull
    {
        using var enumerator = elements.GetEnumerator();
        if (!enumerator.MoveNext()) return None<T>();

        var single = enumerator.Current;
        if (enumerator.MoveNext()) throw new InvalidOperationException("The sequence holds more than one element, so SingleOrNone has no single one to answer. Use FirstOrNone where any one will do.");

        return Some(single);
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

    /// <param name="options">The options.</param>
    /// <typeparam name="T">The type of the values.</typeparam>
    extension<T>(IEnumerable<Option<T>> options) where T : notnull
    {
        /// <summary>The values of the options that hold one, in order: a sequence of lookups to what was found. Lazy, as a LINQ operator is.</summary>
        /// <returns>The values, in order.</returns>
        public IEnumerable<T> Choose()
        {
            ArgumentNullException.ThrowIfNull(options);

            return Chosen(options, static option => option);
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

    /// <summary>The iterator behind both <c>Choose</c>, apart so the arguments are checked when the call is made, not when the sequence is read.</summary>
    private static IEnumerable<TResult> Chosen<T, TResult>(IEnumerable<T> source, Func<T, Option<TResult>> selector) where TResult : notnull
    {
        foreach (var element in source)
        {
            if (selector(element).TryGetValue(out var value)) yield return value;
        }
    }
}
