using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Analyzers.NoDefault;

public readonly partial record struct OrderId : IValue<Guid>;

public enum CodeFault { Blank }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result.Error(CodeFault.Blank) : new ProductCode(value);
}

public sealed class Order;

public enum OrderFault { Empty }

public static class NoDefault
{
    public static void Reported()
    {
#pragma warning disable CMTK0001 // what the sample shows the rule reporting
#pragma warning disable CS0219, CS8321 // the sample declares what the rule reports and uses none of it
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0001
        OrderId id = default;                       // CMTK0001: create it with OrderId.From
        var code = new ProductCode();               // CMTK0001: create it with Create, TryFrom or FromKnownGood
        Result<Order, OrderFault> pending = new();  // CMTK0001: use Result.Success(value) or Result.Error(error)

        T Empty<T>() where T : struct, IValue<Guid> => default;   // CMTK0001, through the constraint
        // end sample
#pragma warning restore CS0219, CS8321
#pragma warning restore CMTK0001
    }

    /// <summary>
    /// The guard checks and assertions the rule leaves alone: the analyzer runs here, and CMTK0001 is
    /// an error. Compiled, not called.
    /// </summary>
    public static void GuardChecks(OrderId id, Option<int> option)
    {
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0001-guard
        if (id == default) throw new ArgumentException("An order id is required.", nameof(id));
        if (option != default) { /* … */ }
        if (id.Equals(default(OrderId)) || EqualityComparer<OrderId>.Default.Equals(id, default)) { /* … */ }
        ArgumentOutOfRangeException.ThrowIfEqual(id, default);
        Assert.NotEqual(default, id);
        id.ShouldNotBe(default);
        // end sample
    }

    public static void Instead()
    {
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0001-instead
        OrderId id = OrderId.New();
        var code = ProductCode.FromKnownGood("ABC");
        Result<Order, OrderFault> pending = Result.Error(OrderFault.Empty);
        // end sample
    }
}

/// <summary>The Try pattern the rule leaves alone: the analyzer runs here, and CMTK0001 is an error.</summary>
public sealed class OrderLookup
{
    private readonly Dictionary<string, OrderId> _ids = [];

    // sample: CodoMetis.TypeKit.Analyzers/cmtk0001-try
    public bool TryFind(string key, out OrderId id)
    {
        if (_ids.TryGetValue(key, out var found)) { id = found; return true; }

        id = default;
        return false;
    }
    // end sample
}

// sample: CodoMetis.TypeKit.Analyzers/require-custom-initialization
[RequireCustomInitialization("Use Money.Of(amount, currency)")]
public readonly record struct Money { /* … */ }
// end sample
