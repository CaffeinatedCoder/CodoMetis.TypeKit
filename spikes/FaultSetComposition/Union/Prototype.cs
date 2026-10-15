using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FaultSetComposition.Union;

// NOT counted as the consumer's: shape 3's candidate, which would ship in the base package
// (Fault<T1, T2>, Fault<T1, T2, T3>) and the ASP.NET Core satellite (the ToHttpResult overloads).

/// <summary>A fault of one of two sets: what a C# 15 union of the two faults would be, today.</summary>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[RequireCustomInitialization("A fault of one of several sets converts from Fault.Of(reason).")]
public readonly record struct Fault<T1, T2> where T1 : notnull where T2 : notnull
{
    private readonly Fault<T1> _first;
    private readonly Fault<T2> _second;
    private readonly byte _case;

    private Fault(Fault<T1> fault) => (_first, _case) = (fault, 1);

    private Fault(Fault<T2> fault) => (_second, _case) = (fault, 2);

    public static implicit operator Fault<T1, T2>(Fault<T1> fault) => new(fault);

    public static implicit operator Fault<T1, T2>(Fault<T2> fault) => new(fault);

    public static Fault<T1, T2> Of(T1 reason, string? detail = null) => Fault.Of(reason, detail);

    public static Fault<T1, T2> Of(T2 reason, string? detail = null) => Fault.Of(reason, detail);

    public TResult Match<TResult>(Func<Fault<T1>, TResult> first, Func<Fault<T2>, TResult> second) =>
        _case switch
        {
            1 => first(_first),
            2 => second(_second),
            _ => throw new InvalidOperationException("An uninitialized Fault<T1, T2>."),
        };
}

/// <summary>A fault of one of three sets.</summary>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[RequireCustomInitialization("A fault of one of several sets converts from Fault.Of(reason).")]
public readonly record struct Fault<T1, T2, T3> where T1 : notnull where T2 : notnull where T3 : notnull
{
    private readonly Fault<T1> _first;
    private readonly Fault<T2> _second;
    private readonly Fault<T3> _third;
    private readonly byte _case;

    private Fault(Fault<T1> fault) => (_first, _case) = (fault, 1);

    private Fault(Fault<T2> fault) => (_second, _case) = (fault, 2);

    private Fault(Fault<T3> fault) => (_third, _case) = (fault, 3);

    public static implicit operator Fault<T1, T2, T3>(Fault<T1> fault) => new(fault);

    public static implicit operator Fault<T1, T2, T3>(Fault<T2> fault) => new(fault);

    public static implicit operator Fault<T1, T2, T3>(Fault<T3> fault) => new(fault);

    public static Fault<T1, T2, T3> Of(T1 reason, string? detail = null) => Fault.Of(reason, detail);

    public TResult Match<TResult>(Func<Fault<T1>, TResult> first, Func<Fault<T2>, TResult> second, Func<Fault<T3>, TResult> third) =>
        _case switch
        {
            1 => first(_first),
            2 => second(_second),
            3 => third(_third),
            _ => throw new InvalidOperationException("An uninitialized Fault<T1, T2, T3>."),
        };
}

public static class UnionHttpResults
{
    public static Results<NoContent, FaultProblem<T1>, FaultProblem<T2>> ToHttpResult<T1, T2>(this Result<Fault<T1, T2>> result)
        where T1 : notnull where T2 : notnull =>
        result.Match<Results<NoContent, FaultProblem<T1>, FaultProblem<T2>>>(
            static () => TypedResults.NoContent(),
            static fault => fault.Match<Results<NoContent, FaultProblem<T1>, FaultProblem<T2>>>(f => Problem(f), f => Problem(f)));

    public static Results<NoContent, FaultProblem<T1>, FaultProblem<T2>, FaultProblem<T3>> ToHttpResult<T1, T2, T3>(this Result<Fault<T1, T2, T3>> result)
        where T1 : notnull where T2 : notnull where T3 : notnull =>
        result.Match<Results<NoContent, FaultProblem<T1>, FaultProblem<T2>, FaultProblem<T3>>>(
            static () => TypedResults.NoContent(),
            static fault => fault.Match<Results<NoContent, FaultProblem<T1>, FaultProblem<T2>, FaultProblem<T3>>>(f => Problem(f), f => Problem(f), f => Problem(f)));

    public static Results<TSuccess, FaultProblem<T1>, FaultProblem<T2>> ToHttpResult<T, T1, T2, TSuccess>(this Result<T, Fault<T1, T2>> result, Func<T, TSuccess> success)
        where T : notnull where T1 : notnull where T2 : notnull where TSuccess : IResult =>
        result.Match<Results<TSuccess, FaultProblem<T1>, FaultProblem<T2>>>(
            value => success(value),
            static fault => fault.Match<Results<TSuccess, FaultProblem<T1>, FaultProblem<T2>>>(f => Problem(f), f => Problem(f)));

    // FaultProblem's constructor is internal; in the satellite this is `new FaultProblem<T>(...)`.
    private static FaultProblem<T> Problem<T>(Fault<T> fault) where T : notnull =>
        (FaultProblem<T>)Result<Fault<T>>.Error(fault).ToHttpResult().Result;
}
