namespace CodoMetis.TypeKit;

/// <summary>Creates identifiers for value objects that wrap a <see cref="Guid"/>.</summary>
public static class GuidValueExtensions
{
    /// <typeparam name="TSelf">The identifier type.</typeparam>
    extension<TSelf>(TSelf)
        where TSelf : IValueObject<TSelf, Guid>, IValueWrapper<TSelf, Guid>
    {
        /// <summary>Creates an identifier from a new version 7 <see cref="Guid"/>.</summary>
        /// <remarks>
        /// <para>
        /// A version 7 Guid starts with its creation time, so identifiers created in sequence also
        /// sort in sequence, which keeps index inserts at the end of the index.
        /// </para>
        /// <para>
        /// Reads the system clock. Use <see cref="New(DateTimeOffset)"/> when a test needs identifiers
        /// in a known order.
        /// </para>
        /// <para>
        /// Callable where the identifier type is named: <c>OrderId.New()</c>. A generic method that
        /// reaches it through its own type parameter fails with CS0704, and calls
        /// <c>TSelf.From(Guid.CreateVersion7())</c> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new identifier.</returns>
        public static TSelf New() => TSelf.From(Guid.CreateVersion7());

        /// <summary>Creates an identifier whose version 7 timestamp is <paramref name="timestamp"/>.</summary>
        /// <remarks>
        /// Only the milliseconds of <paramref name="timestamp"/> reach the identifier, and the remaining
        /// bits are random. Two identifiers created for the same millisecond are distinct but do not
        /// order against each other, so separate them by a millisecond for a stable sequence.
        /// </remarks>
        /// <param name="timestamp">The creation time to embed. Must not be before the Unix epoch.</param>
        /// <returns>A new identifier.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timestamp"/> is before the Unix epoch.</exception>
        public static TSelf New(DateTimeOffset timestamp) => TSelf.From(Guid.CreateVersion7(timestamp));
    }
}
