namespace CodoMetis.TypeKit.Testing;

/// <summary>
/// An <see cref="Option{T}"/>, a <c>Result</c> or a <see cref="Patch{T}"/> is not the outcome the test expected. Every test
/// framework reports an exception from a test as its failure, so this package needs none of them.
/// </summary>
public sealed class TypeKitAssertionException : Exception
{
    /// <summary>Creates the exception with the message that says what the outcome was instead.</summary>
    /// <param name="message">What was expected, and what the outcome was.</param>
    public TypeKitAssertionException(string message) : base(message) { }
}
