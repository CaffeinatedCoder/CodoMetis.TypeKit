using System.Collections.Concurrent;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>What every <c>AddFaultResponses</c> overload does, once it has the fault type's members and a response for each.</summary>
internal static class FaultRegistration
{
    public static IServiceCollection Add<TFault>(IServiceCollection services, FaultType<TFault> type, Func<TFault, FaultResponse> responseOf, string parameter)
        where TFault : notnull
    {
        // Two mappings for one fault type: whichever was registered last would answer, silently.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FaultResponses<TFault>)))
            throw new InvalidOperationException(
                $"The fault type {type.TypeName} is mapped already. Map it once, with a status for each member.");

        services.AddSingleton(FaultResponses<TFault>.Map(type, responseOf, parameter));

        return Checked(services);
    }

    /// <summary>The mapping of every fault set that has none of its own; <paramref name="responseOf"/> null maps each member by its kind.</summary>
    public static IServiceCollection AddDefault(IServiceCollection services, Func<IFault, FaultResponse>? responseOf, string parameter)
    {
        // Two defaults: whichever was registered last would answer, silently.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(DefaultFaultResponses)))
            throw new InvalidOperationException("A mapping for every fault set is given already. Give it once, and map a set that answers otherwise on its own.");

        services.AddSingleton(new DefaultFaultResponses(responseOf, parameter));

        return Checked(services);
    }

    /// <summary>A member by its kind, the refusal of a member of no kind naming it.</summary>
    public static FaultResponse ByKind(IFault fault, string qualifiedName, string parameter)
    {
        try { return FaultResponse.For(fault.Kind); }
        catch (ArgumentOutOfRangeException refused) { throw new ArgumentException($"{qualifiedName}: {refused.Message}", parameter, refused); }
    }

    private static IServiceCollection Checked(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, FaultResponsesStartupCheck>());
        services.AddProblemDetails();

        return services;
    }
}

/// <summary>
/// How a host answers a fault set it mapped no response for: each member through <see cref="IFault"/>, the
/// set mapped once, the first time it is asked for.
/// </summary>
/// <remarks>
/// A fault type is known here only as a type argument without the closed set's constraint, so the set's
/// members are reached through the attribute the generators put on it
/// (<see cref="GeneratedClosedSetAttribute.Accept{TResult}"/>), never by <c>MakeGenericType</c>. A type that
/// is no generated closed set, or no fault set, has no default, and is unmapped as before.
/// </remarks>
internal sealed class DefaultFaultResponses(Func<IFault, FaultResponse>? responseOf, string parameter)
{
    private readonly ConcurrentDictionary<Type, object?> _mapped = new();

    public FaultResponses<TFault>? For<TFault>() where TFault : notnull =>
        // A mapping that throws is not cached: every reader that asks again is refused again.
        (FaultResponses<TFault>?)_mapped.GetOrAdd(typeof(TFault), static (type, defaults) =>
            typeof(IFault).IsAssignableFrom(type) && ClosedSetSchemaTransformer.Describe(type) is { } set ? set.Accept(new Mapper(defaults)) : null, this);

    /// <summary>The caller's parameter, which a refusal names.</summary>
    private string Parameter => parameter;

    private FaultResponse ResponseOf(IFault fault, string qualifiedName) =>
        responseOf is not null ? responseOf(fault) : FaultRegistration.ByKind(fault, qualifiedName, parameter);

    private sealed class Mapper(DefaultFaultResponses defaults) : IClosedSetVisitor<object>
    {
        public object Visit<TSet>() where TSet : class, IClosedSet<TSet>
        {
            var type = FaultTypes.Set<TSet>();
            return FaultResponses<TSet>.Map(type, fault => defaults.ResponseOf((IFault)fault, type.QualifiedNameOf(fault)), defaults.Parameter);
        }
    }
}

/// <summary>How a host answers each fault of one type, and every status the type can give.</summary>
internal sealed class FaultResponses<TFault> where TFault : notnull
{
    private readonly Dictionary<TFault, Answer> _answers;

    private FaultResponses(FaultType<TFault> type, Dictionary<TFault, Answer> answers)
    {
        Type = type;
        _answers = answers;
        Statuses = [.. answers.Values.Select(answer => answer.Status).Distinct().Order()];
    }

    public FaultType<TFault> Type { get; }

    /// <summary>Every status a member is mapped to, ascending: what the document lists where no endpoint changes one.</summary>
    public IReadOnlyList<int> Statuses { get; }

    /// <summary>
    /// Maps every member up front, so a mapping that throws for one, or gives one a status that is no
    /// error, fails where it is registered and not on the request that first reports that fault.
    /// </summary>
    /// <param name="type">The fault type's members.</param>
    /// <param name="responseOf">The response of each member.</param>
    /// <param name="parameter">The caller's parameter, which a refusal names: the mapping as the host wrote it.</param>
    public static FaultResponses<TFault> Map(FaultType<TFault> type, Func<TFault, FaultResponse> responseOf, string parameter)
    {
        var answers = new Dictionary<TFault, Answer>();
        foreach (var fault in type.Members)
        {
            var response = responseOf(fault)
                        ?? throw new ArgumentException($"{type.QualifiedNameOf(fault)} is mapped to no response." + FaultRefusals.Mapping, parameter);

            if (response.StatusCode is < 400 or > 599)
                throw new ArgumentException(
                    $"{type.QualifiedNameOf(fault)} is mapped to {response.StatusCode}, which is not an error status (400 to 599). " +
                    "A fault is answered as problem details; a success is the value of the Result." + FaultRefusals.Mapping,
                    parameter);

            answers[fault] = new Answer(response.StatusCode, response.Type is { } problemType ? FaultResponse.Written(problemType) : null, response.Title, type.NameOf(fault), type.QualifiedNameOf(fault), FaultType<TFault>.DescriptionOf(fault));
        }

        return new FaultResponses<TFault>(type, answers);
    }

    /// <summary>
    /// How <paramref name="fault"/> is answered. A value no member names, which only a cast makes, has
    /// no answer, and is refused rather than answered with a status the document does not list.
    /// </summary>
    public Answer Of(TFault fault) =>
        _answers.TryGetValue(fault, out var answer)
            ? answer
            : throw new InvalidOperationException(
                $"{Type.TypeName} has no member {(fault is Enum ? $"with the value {fault:D}" : "that is this instance")}, " +
                "so no status is mapped for it." + FaultRefusals.Mapping);

    /// <summary>
    /// Every status an endpoint gives, with the statuses its metadata sets for some members in place of the
    /// mapped ones, and none for the members it leaves out.
    /// </summary>
    public IReadOnlyList<int> StatusesOf(IReadOnlyList<object> endpointMetadata) =>
        [.. AnsweredBy(endpointMetadata).Select(fault => StatusOf(fault, endpointMetadata)).Distinct().Order()];

    /// <summary>The problem types of the members an endpoint answers with <paramref name="status"/>, for that response's description.</summary>
    public IReadOnlyList<string> TypesOf(int status, IReadOnlyList<object> endpointMetadata) =>
        [.. AnsweredBy(endpointMetadata).Where(fault => StatusOf(fault, endpointMetadata) == status)
                                        .Select(fault => _answers[fault].Type)
                                        .OfType<string>()
                                        .Distinct()];

    /// <summary>The members an endpoint answers with: every member, but those its metadata leaves out.</summary>
    public IEnumerable<TFault> AnsweredBy(IReadOnlyList<object> endpointMetadata) =>
        Type.Members.Where(fault => FaultExclusions.Excluding(endpointMetadata, fault, Type) is null);

    private int StatusOf(TFault fault, IReadOnlyList<object> endpointMetadata) =>
        FaultStatuses.Override(endpointMetadata, fault, Type) ?? _answers[fault].Status;

    /// <summary>
    /// How the host answers <typeparamref name="TFault"/>: its own mapping, or else the default for every
    /// fault set, or <see langword="null"/> where neither answers it.
    /// </summary>
    public static FaultResponses<TFault>? Of(IServiceProvider services) =>
        services.GetService(typeof(FaultResponses<TFault>)) as FaultResponses<TFault>
     ?? (services.GetService(typeof(DefaultFaultResponses)) as DefaultFaultResponses)?.For<TFault>();

    public static InvalidOperationException Unmapped() =>
        new($"No HTTP status is mapped for the fault type {FaultTypes.NameOf(typeof(TFault))}. " +
            $"Map each of its members with services.AddFaultResponses<{FaultTypes.NameOf(typeof(TFault))}>(fault => …)." + FaultRefusals.Mapping);

    /// <summary>
    /// The mapped answer for one member: what the problem says, before an endpoint changes its status. Its
    /// <c>Type</c> is the problem's <c>type</c> as the response and the document write it (<see cref="FaultResponse.Written"/>).
    /// </summary>
    internal sealed record Answer(int Status, string? Type, string? Title, string Name, string QualifiedName, string? Description);
}

/// <summary>
/// Which fault type an endpoint answers with, in its metadata. Typed, so the mapping is read without
/// reflection, and abstract, so the document and the startup check read it without knowing the type.
/// </summary>
internal abstract class FaultResponsesMetadata
{
    public abstract Type FaultType { get; }

    public abstract string FaultName { get; }

    /// <summary>The statuses the endpoint answers the fault type with, or <see langword="null"/> where the host mapped none.</summary>
    public abstract IReadOnlyList<int>? Statuses(IServiceProvider services, IReadOnlyList<object> endpointMetadata);

    /// <summary>The problem types listed for <paramref name="status"/>.</summary>
    public abstract IReadOnlyList<string> Types(IServiceProvider services, int status, IReadOnlyList<object> endpointMetadata);

    /// <summary>
    /// What is wrong with the endpoint's declarations for this fault type: a status or a member left out that
    /// names no member, and a member given a status and left out at once.
    /// </summary>
    public abstract IEnumerable<string> Problems(IServiceProvider services, IReadOnlyList<object> endpointMetadata);

    /// <summary>
    /// The name and the qualified name of each member the endpoint answers with, those it leaves out aside, or
    /// none where the host mapped no status.
    /// </summary>
    public abstract IEnumerable<(string Name, string QualifiedName)> Names(IServiceProvider services, IReadOnlyList<object> endpointMetadata);
}

internal sealed class FaultResponsesMetadata<TFault> : FaultResponsesMetadata where TFault : notnull
{
    public static readonly FaultResponsesMetadata<TFault> Instance = new();

    private FaultResponsesMetadata() { }

    public override Type FaultType => typeof(TFault);

    public override string FaultName { get; } = FaultTypes.NameOf(typeof(TFault));

    public override IReadOnlyList<int>? Statuses(IServiceProvider services, IReadOnlyList<object> endpointMetadata) =>
        Responses(services)?.StatusesOf(endpointMetadata);

    public override IReadOnlyList<string> Types(IServiceProvider services, int status, IReadOnlyList<object> endpointMetadata) =>
        Responses(services)?.TypesOf(status, endpointMetadata) ?? [];

    public override IEnumerable<string> Problems(IServiceProvider services, IReadOnlyList<object> endpointMetadata)
    {
        if (Responses(services) is not { } responses) yield break;

        foreach (var status in endpointMetadata.OfType<IFaultStatus<TFault>>())
        {
            if (status.Problem(responses.Type) is { } problem) yield return problem;
        }

        foreach (var exclusion in endpointMetadata.OfType<IFaultExclusion<TFault>>())
        {
            if (exclusion.Problem(responses.Type) is { } problem) yield return problem;
        }

        // A status for a member left out says the endpoint answers it, and the exclusion that it never does.
        foreach (var fault in responses.Type.Members)
        {
            if (FaultExclusions.Excluding(endpointMetadata, fault, responses.Type) is not { } exclusion) continue;

            foreach (var status in endpointMetadata.OfType<IFaultStatus<TFault>>().Where(status => status.Matches(fault, responses.Type)))
                yield return $"{status.Describe()} gives a status to {responses.Type.QualifiedNameOf(fault)}, which {exclusion.Describe()} leaves out";
        }
    }

    public override IEnumerable<(string Name, string QualifiedName)> Names(IServiceProvider services, IReadOnlyList<object> endpointMetadata) =>
        Responses(services) is { } responses
            ? responses.AnsweredBy(endpointMetadata).Select(fault => (responses.Type.NameOf(fault), responses.Type.QualifiedNameOf(fault)))
            : [];

    private static FaultResponses<TFault>? Responses(IServiceProvider services) => FaultResponses<TFault>.Of(services);
}
