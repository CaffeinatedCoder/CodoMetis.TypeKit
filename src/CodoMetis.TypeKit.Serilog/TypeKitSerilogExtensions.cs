using CodoMetis.TypeKit.Serilog;
using Serilog.Configuration;

// Beside Serilog's own configuration methods, in the namespace every Serilog setup imports.
namespace Serilog;

/// <summary>Adds CodoMetis.TypeKit to a Serilog logger.</summary>
public static class TypeKitSerilogExtensions
{
    /// <summary>
    /// Logs every value object as what it prints, its <c>ToString()</c>, also where Serilog destructures
    /// it: <c>{@Email}</c>, or a record holding one in <c>{@Signup}</c>.
    /// </summary>
    /// <remarks>
    /// A value object that declares <c>ToString()</c> to mask what it wraps is then masked in every log
    /// event, as it is in Microsoft.Extensions.Logging. Every other value object is logged as its value's
    /// text, a string, instead of <c>{ Value: … }</c>. Nothing is registered per type.
    /// </remarks>
    /// <param name="destructure">The logger configuration's <c>Destructure</c>.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration WithTypeKit(this LoggerDestructuringConfiguration destructure)
    {
        ArgumentNullException.ThrowIfNull(destructure);

        return destructure.With(new ValueObjectDestructuringPolicy());
    }
}
