using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using Serilog.Core;
using Serilog.Events;

namespace SensitiveValues;

/// <summary>
/// The prototype: Serilog destructures a value object as what it prints, a scalar, never through its
/// public Value. Recognised by the attribute the generators put on every value object, as the satellites do.
/// </summary>
public sealed class ValueObjectDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        result = value.GetType().GetCustomAttributes(inherit: false).Any(a => a.GetType().IsGenericType && a.GetType().GetGenericTypeDefinition() == typeof(GeneratedValueObjectAttribute<,>))
            ? new ScalarValue(value.ToString())
            : null;
        return result is not null;
    }
}
