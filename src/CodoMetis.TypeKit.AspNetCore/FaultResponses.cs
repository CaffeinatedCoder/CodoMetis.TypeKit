namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>The status a host answers each fault of one type with, and every status the type can give.</summary>
internal sealed class FaultResponses<TFault> where TFault : struct, Enum
{
    private readonly Dictionary<TFault, int> _statuses;

    private FaultResponses(Dictionary<TFault, int> statuses)
    {
        _statuses = statuses;
        Statuses = [.. statuses.Values.Distinct().Order()];
    }

    /// <summary>Every status a member is mapped to, ascending: what the document lists.</summary>
    public IReadOnlyList<int> Statuses { get; }

    /// <summary>
    /// Maps every member up front, so a mapping that throws for one, or gives one a status that is no
    /// error, fails where it is registered and not on the request that first reports that fault.
    /// </summary>
    public static FaultResponses<TFault> Map(Func<TFault, int> statusOf)
    {
        var statuses = new Dictionary<TFault, int>();
        foreach (var fault in Enum.GetValues<TFault>())
        {
            var status = statusOf(fault);
            if (status is < 400 or > 599)
                throw new ArgumentException(
                    $"{typeof(TFault).Name}.{fault} is mapped to {status}, which is not an error status (400 to 599). " +
                    "A fault is answered as problem details; a success is the value of the Result." + FaultRefusals.Mapping,
                    nameof(statusOf));

            statuses[fault] = status;
        }

        return new FaultResponses<TFault>(statuses);
    }

    /// <summary>
    /// The status of <paramref name="fault"/>. A value no member names, which only a cast makes, has
    /// none, and is refused rather than answered with a status the document does not list.
    /// </summary>
    public int StatusOf(TFault fault) =>
        _statuses.TryGetValue(fault, out var status)
            ? status
            : throw new InvalidOperationException(
                $"{typeof(TFault).Name} has no member with the value {fault:D}, " +
                "so no status is mapped for it." + FaultRefusals.Mapping);

    public static InvalidOperationException Unmapped() =>
        new($"No HTTP status is mapped for the fault type {typeof(TFault).Name}. " +
            $"Map each of its members with services.AddFaultResponses<{typeof(TFault).Name}>(fault => …)." + FaultRefusals.Mapping);
}

/// <summary>
/// Which fault type an endpoint answers with, in its metadata. Typed, so the mapping is read without
/// reflection, and abstract, so the document and the startup check read it without knowing the type.
/// </summary>
internal abstract class FaultResponsesMetadata
{
    public abstract string FaultName { get; }

    /// <summary>The statuses the host mapped the fault type to, or <see langword="null"/> where it mapped none.</summary>
    public abstract IReadOnlyList<int>? Statuses(IServiceProvider services);
}

internal sealed class FaultResponsesMetadata<TFault> : FaultResponsesMetadata where TFault : struct, Enum
{
    public static readonly FaultResponsesMetadata<TFault> Instance = new();

    private FaultResponsesMetadata() { }

    public override string FaultName => typeof(TFault).Name;

    public override IReadOnlyList<int>? Statuses(IServiceProvider services) =>
        (services.GetService(typeof(FaultResponses<TFault>)) as FaultResponses<TFault>)?.Statuses;
}
