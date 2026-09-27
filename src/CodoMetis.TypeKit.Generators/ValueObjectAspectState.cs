using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// What <see cref="ValueObjectImplementationAspect"/> leaves for the aspects that run after it.
/// </summary>
/// <remarks>
/// <para>
/// The references are <see cref="IDurableRef{T}"/> rather than <see cref="IRef{T}"/>: aspect state
/// outlives the compilation it was built from, and Metalama 2027.0 rejects a plain reference there.
/// </para>
/// <para>
/// <see cref="FromJson"/>, <see cref="FromText"/> and <see cref="TryFromText"/> are the only way the
/// JSON, parsing and type-converter aspects create an instance. They apply <c>Create</c> for a
/// validated value object, so no generated entry point can bypass its rules.
/// </para>
/// </remarks>
internal sealed class ValueObjectAspectState : IAspectState
{
    // ReSharper disable once ConvertToPrimaryConstructor
    public ValueObjectAspectState(
        ValueObjectKind           kind,
        IDurableRef<INamedType>   valueType,
        IDurableRef<IConstructor> privateConstructor,
        IDurableRef<IMethod>      fromJson,
        IDurableRef<IMethod>      fromText,
        IDurableRef<IMethod>      tryFromText,
        string                    extensionClassName,
        string?                   extensionClassNameOwner
    )
    {
        Kind                    = kind;
        ValueType               = valueType;
        PrivateConstructor      = privateConstructor;
        FromJson                = fromJson;
        FromText                = fromText;
        TryFromText             = tryFromText;
        ExtensionClassName      = extensionClassName;
        ExtensionClassNameOwner = extensionClassNameOwner;
    }

    public ValueObjectKind Kind { get; }

    public IDurableRef<INamedType> ValueType { get; }

    /// <summary>No validation. For values that are already an instance's own, never for input.</summary>
    public IDurableRef<IConstructor> PrivateConstructor { get; }

    /// <summary>
    /// <c>static TSelf __FromJson(T value, bool materialize)</c>: a refusal throws <c>JsonException</c>,
    /// unless the converter is in the materializing mode for stored JSON.
    /// </summary>
    public IDurableRef<IMethod> FromJson { get; }

    /// <summary><c>static TSelf __FromText(T value)</c>: a refusal throws <c>FormatException</c>.</summary>
    public IDurableRef<IMethod> FromText { get; }

    /// <summary><c>static bool __TryFromText(T value, out TSelf result)</c>: a refusal returns <see langword="false"/>.</summary>
    public IDurableRef<IMethod> TryFromText { get; }

    /// <summary>The name of the <c>GetValue()</c>/<c>ValueOrNull()</c> companion class (<see cref="CompanionClass.Name"/>).</summary>
    public string ExtensionClassName { get; }

    /// <summary>
    /// What already owns that name, answered by the fabric before any introduction, or
    /// <see langword="null"/> when the name is free (<see cref="CompanionClass.NameOwner"/>).
    /// </summary>
    public string? ExtensionClassNameOwner { get; }
}
