using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using Serilog.Core;
using Serilog.Events;

namespace CodoMetis.TypeKit.Serilog;

/// <summary>
/// Destructures a value object or a closed set as what it prints: a scalar of its <c>ToString()</c>,
/// never its properties.
/// </summary>
/// <remarks>
/// <para>
/// Without it, <c>{@Email}</c> and every value object inside a destructured record are logged as
/// <c>{ Value: … }</c>, read from the public <c>Value</c>: a value object that masks itself with a
/// hand-written <c>ToString()</c> is logged unmasked there, and nowhere else (spikes/SensitiveValues,
/// finding 4). With it, Serilog logs a value object as Microsoft.Extensions.Logging does, as its text.
/// A closed set was logged by its properties too, its constant data and its hand-written
/// <c>ToString()</c>'s text bypassed alike; it is its name, or what its own <c>ToString()</c> prints.
/// </para>
/// <para>
/// Recognised by the <see cref="GeneratedValueObjectAttribute"/> every value object carries and the
/// <see cref="GeneratedClosedSetAttribute"/> every closed set carries, never by a name or by
/// <see cref="Type.GetInterfaces"/>, and remembered per type.
/// </para>
/// </remarks>
internal sealed class ValueObjectDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, bool> PrintedTypes = new();

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        result = PrintsItself(value.GetType()) ? new ScalarValue(value.ToString()) : null;

        return result is not null;
    }

    /// <summary>A boxed nullable arrives as its underlying type, so the type itself is the value object.</summary>
    private static bool PrintsItself(Type type) =>
        PrintedTypes.GetOrAdd(type, static candidate =>
            (candidate.GetCustomAttribute<GeneratedValueObjectAttribute>(inherit: false) is { } valueObject && valueObject.ValueObjectType == candidate)
         || (candidate.GetCustomAttribute<GeneratedClosedSetAttribute>(inherit: false) is { } set && set.SetType == candidate));
}
