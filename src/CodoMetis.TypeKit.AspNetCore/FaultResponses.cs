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
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, FaultResponsesStartupCheck>());
        services.AddProblemDetails();

        return services;
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

            answers[fault] = new Answer(response.StatusCode, response.Type, response.Title, type.NameOf(fault), type.QualifiedNameOf(fault), FaultType<TFault>.DescriptionOf(fault));
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

    /// <summary>Every status an endpoint gives, with the statuses its metadata sets for some members in place of the mapped ones.</summary>
    public IReadOnlyList<int> StatusesOf(IReadOnlyList<object> endpointMetadata) =>
        [.. Type.Members.Select(fault => FaultStatuses.Override(endpointMetadata, fault, Type) ?? _answers[fault].Status).Distinct().Order()];

    /// <summary>The problem types of the members an endpoint answers with <paramref name="status"/>, for that response's description.</summary>
    public IReadOnlyList<Uri> TypesOf(int status, IReadOnlyList<object> endpointMetadata) =>
        [.. Type.Members.Where(fault => (FaultStatuses.Override(endpointMetadata, fault, Type) ?? _answers[fault].Status) == status)
                        .Select(fault => _answers[fault].Type)
                        .OfType<Uri>()
                        .Distinct()];

    public static InvalidOperationException Unmapped() =>
        new($"No HTTP status is mapped for the fault type {FaultTypes.NameOf(typeof(TFault))}. " +
            $"Map each of its members with services.AddFaultResponses<{FaultTypes.NameOf(typeof(TFault))}>(fault => …)." + FaultRefusals.Mapping);

    /// <summary>The mapped answer for one member: what the problem says, before an endpoint changes its status.</summary>
    internal sealed record Answer(int Status, Uri? Type, string? Title, string Name, string QualifiedName, string? Description);
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
    public abstract IReadOnlyList<Uri> Types(IServiceProvider services, int status, IReadOnlyList<object> endpointMetadata);

    /// <summary>What is wrong with the endpoint's status overrides for this fault type: a name no member has.</summary>
    public abstract IEnumerable<string> Problems(IServiceProvider services, IReadOnlyList<object> endpointMetadata);
}

internal sealed class FaultResponsesMetadata<TFault> : FaultResponsesMetadata where TFault : notnull
{
    public static readonly FaultResponsesMetadata<TFault> Instance = new();

    private FaultResponsesMetadata() { }

    public override Type FaultType => typeof(TFault);

    public override string FaultName { get; } = FaultTypes.NameOf(typeof(TFault));

    public override IReadOnlyList<int>? Statuses(IServiceProvider services, IReadOnlyList<object> endpointMetadata) =>
        Responses(services)?.StatusesOf(endpointMetadata);

    public override IReadOnlyList<Uri> Types(IServiceProvider services, int status, IReadOnlyList<object> endpointMetadata) =>
        Responses(services)?.TypesOf(status, endpointMetadata) ?? [];

    public override IEnumerable<string> Problems(IServiceProvider services, IReadOnlyList<object> endpointMetadata)
    {
        if (Responses(services) is not { } responses) yield break;

        foreach (var status in endpointMetadata.OfType<IFaultStatus<TFault>>())
        {
            if (status.Problem(responses.Type) is { } problem) yield return problem;
        }
    }

    private static FaultResponses<TFault>? Responses(IServiceProvider services) =>
        services.GetService(typeof(FaultResponses<TFault>)) as FaultResponses<TFault>;
}
