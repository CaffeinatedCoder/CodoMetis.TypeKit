using System.Linq.Expressions;
using CodoMetis.TypeKit;

// The namespace of EF Core's own FirstOrDefaultAsync, so the same using reaches both.
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// <c>FirstOrNoneAsync</c>: the first element of a query as an <see cref="Option{T}"/>, asked of the
/// database, where <c>FirstOrDefaultAsync</c> hands back a <c>default</c> for nothing found.
/// </summary>
/// <remarks>
/// The condition and the limit run in the query, <c>WHERE … LIMIT @p</c>, as they do for the base
/// package's <c>FirstOrNone</c> on a query. Under Native AOT, EF runs only the queries it precompiled,
/// and it precompiles a query where the application calls one of its own operators, which these are
/// not: they throw "Query wasn't precompiled" there (measured with EF Core 10.0.12), so write
/// <c>Where(…).Take(1).ToListAsync()</c> at the call site instead.
/// </remarks>
public static class TypeKitQueryableExtensions
{
    /// <param name="source">The query.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    extension<T>(IQueryable<T> source) where T : notnull
    {
        /// <summary>The first element of the query, or <c>None</c>: the database is asked for one.</summary>
        /// <param name="cancellationToken">Cancels the query.</param>
        /// <returns>The first element, or <c>None</c> if the query has none.</returns>
        /// <exception cref="ArgumentNullException">The first element is null.</exception>
        public Task<Option<T>> FirstOrNoneAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            return FirstOfAsync(source.Take(1), cancellationToken);
        }

        /// <summary>The first element that matches, or <c>None</c>: the database runs the condition, and is asked for one.</summary>
        /// <param name="predicate">The condition to match, translated to SQL.</param>
        /// <param name="cancellationToken">Cancels the query.</param>
        /// <returns>The first matching element, or <c>None</c> if nothing matches.</returns>
        /// <exception cref="ArgumentNullException">The first matching element is null.</exception>
        public Task<Option<T>> FirstOrNoneAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);

            return FirstOfAsync(source.Where(predicate).Take(1), cancellationToken);
        }
    }

    private static async Task<Option<T>> FirstOfAsync<T>(IQueryable<T> limited, CancellationToken cancellationToken) where T : notnull
    {
        await foreach (var element in limited.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
            return Option.Some(element);

        return Option.None<T>();
    }
}
