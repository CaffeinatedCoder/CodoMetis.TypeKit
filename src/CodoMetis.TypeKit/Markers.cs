using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

// The markers let a method return an Option or a Result without spelling out its type arguments:
// `return Option.None();`, `return Result.Success(value);`, `return Result.Error(fault);`. Each
// converts implicitly to the target type, so nobody writes a marker's name. None of them is a wire
// type, and the two that carry content keep it internal, as Option and Result do.

/// <summary>
/// The marker <c>Option.None()</c> returns. It converts implicitly to an empty
/// <see cref="Option{T}"/> of any <c>T</c>, so a method can <c>return Option.None();</c>.
/// </summary>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
public readonly record struct None;

/// <summary>
/// The marker <c>Result.Success()</c> returns. It converts implicitly to a successful
/// <see cref="Result{TError}"/>.
/// </summary>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
public readonly record struct Success;

/// <summary>
/// The marker <c>Result.Success(value)</c> returns. It converts implicitly to a successful
/// <see cref="Result{T,TError}"/>, and has no public <c>.Value</c> of its own.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
[RequireCustomInitialization("Use Result.Success(value).")]
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
public readonly record struct Success<T> where T : notnull
{
    internal T Value { get; }

    internal Success(T value)
    {
        Value = value;
    }
}

/// <summary>
/// The marker <c>Result.Error(error)</c> returns. It converts implicitly to a failed
/// <see cref="Result{T,TError}"/> of any value type, or <see cref="Result{TError}"/>, and has no
/// public <c>.Value</c> of its own.
/// </summary>
/// <typeparam name="T">The type of the error.</typeparam>
[RequireCustomInitialization("Use Result.Error(error).")]
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
public readonly record struct Error<T> where T : notnull
{
    internal Error(T value)
    {
        Value = value;
    }

    internal T Value { get; }
}
