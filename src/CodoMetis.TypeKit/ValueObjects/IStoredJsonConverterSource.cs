using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// Implemented by the JSON converter that CodoMetis.TypeKit.Generators generates for a value object:
/// hands out the same converter in the mode that reads without the value object's rules, for
/// <see cref="StoredJsonConverterFactory"/>.
/// </summary>
/// <remarks>
/// It is how the factory reaches that mode without reflecting over a private member, which a trimmed
/// or Native AOT application may have removed: the factory creates the converter the value object's
/// <c>[JsonConverter]</c> names, through the public parameterless constructor that attribute keeps,
/// and asks it for its twin. Nothing else calls it.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IStoredJsonConverterSource
{
    /// <summary>The converter that reads JSON the application stored itself, <b>without validation</b>.</summary>
    /// <param name="factory">The factory asking for it. <see langword="null"/> is refused.</param>
    /// <returns>A converter for the same value object that skips its rules on read and writes exactly as this one does.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    JsonConverter CreateStoredJsonConverter(StoredJsonConverterFactory factory);
}
