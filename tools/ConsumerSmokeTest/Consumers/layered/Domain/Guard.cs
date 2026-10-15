using CodoMetis.TypeKit;

namespace Shop;

public static class Guard
{
    public static OrderId Reset()
    {
        var id = OrderId.New();
        id = default;
        return id;
    }
}

// Woven, and named before the static constructor's body, which then swaps a member.
public sealed partial class Swapped : CodoMetis.TypeKit.ClosedSets.IClosedSet<Swapped>
{
    public static readonly Swapped Open = new();
    public static readonly Swapped Closed = new();

    static Swapped() { Open = Closed; }
}

[RequireParameterlessConstructor]
public interface IEndpoint;

public abstract class EndpointBase { protected EndpointBase(int port) { } }

// No parameterless constructor, and a base that base() does not reach.
public sealed class Orders : EndpointBase, IEndpoint
{
    public Orders(int port) : base(port) { }
}
