using System.ComponentModel;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Listed by a value object that CodoMetis.TypeKit.Generators generates over a base record, once for each
/// base <typeparamref name="T"/>: it implements <see cref="IEquatable{T}"/> of the base again, as the value
/// object's own equality.
/// </summary>
/// <remarks>
/// A collection of the base type calls the base's <see cref="IEquatable{T}"/>, and a base that implements
/// it explicitly kept its implementation in every class derived from it: a <c>HashSet</c> of the value
/// object held one of two holding 42, and a <c>HashSet</c> of the base both. C# implements an interface
/// again only where a class lists it, or one derived from it, and the generators cannot list one a base
/// already implements, so they list this one. Nothing calls it.
/// </remarks>
/// <typeparam name="T">The base record.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IReimplementedEquatable<T> : IEquatable<T>;
