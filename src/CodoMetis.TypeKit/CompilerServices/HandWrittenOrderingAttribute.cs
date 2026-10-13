using System.ComponentModel;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Put by CodoMetis.TypeKit.Generators on a value object that declares its own <c>CompareTo(TSelf)</c>:
/// its ordering, and the operators and interfaces derived from it, is that method, not the order of
/// the value it wraps.
/// </summary>
/// <remarks>
/// <para>
/// Code that orders values somewhere <c>CompareTo</c> cannot run reads it, from the value object's
/// <see cref="Type"/>, as it reads <see cref="GeneratedValueObjectAttribute"/>: the EF Core satellite
/// refuses a query that compares, orders or takes the minimum or maximum of such a value object,
/// since the database would order the column by the wrapped value, the opposite of the in-memory
/// answer for a descending <c>CompareTo</c>, and say nothing.
/// </para>
/// <para>
/// Nothing to write by hand: a hand-written one is CMTK1011, as for every attribute the generators put
/// on a value object.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HandWrittenOrderingAttribute : Attribute;
