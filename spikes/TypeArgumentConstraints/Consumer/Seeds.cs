// Every line marked "// CMTK0010" must be reported, once, and no other line may be. The build output
// is the measurement: compare the reported line numbers with the marked ones (README).
using Library;
#pragma warning disable CS0169, CS0649, CS8618, CS0219

namespace Consumer;

public sealed class OrderDecider : Decider<PlaceOrder, OrderPlaced> { }                        // CMTK0010 (claim 1: base type, two arguments, two reports)

public interface IHasBox : IComparer<Box<Handler>> { }                                           // CMTK0010 (claim 2: implemented interface)

public sealed class Members
{
    private Box<Handler> _field;                                                                 // CMTK0010 (claim 2: field)
    public Box<Handler> Property { get; set; }                                                   // CMTK0010 (claim 2: property)
    public Box<Handler> Method(Box<Handler> parameter)                                           // CMTK0010 (claim 2: return and parameter, two reports)
    {
        Box<Handler> local = null!;                                                              // CMTK0010 (claim 2: local)
        var open = typeof(Box<Handler>);                                                         // CMTK0010 (claim 3: typeof)
        Runner.Run<PlaceOrder>(new PlaceOrder());                                                // CMTK0010 (claim 3: written type argument)
        Runner.Run(new PlaceOrder());                                                            // CMTK0010 (claim 4: inferred, at the call)
        Runner.Run<Command>(new PlaceOrder());                                                   // fine: Command is abstract
        return local;
    }
}

public abstract class Leaky<TCommand> : Decider<TCommand, OrderPlaced> { }                     // CMTK0010 (claim 5: forwarding without the attribute; OrderPlaced is sealed, so two)
public abstract class Sound<[CodoMetis.TypeKit.Abstract, CodoMetis.TypeKit.Record] TCommand> : Decider<TCommand, Event> { }
public sealed class Closed : Sound<PlaceOrder> { }                                               // CMTK0010 (claim 5: the forwarded requirement, at its own use)

public static class Methods
{
    public static void Call<T>(T value) where T : struct => Runner.Boxed<T>();                   // claim 6: a generic method's own parameter, satisfied by struct
    public static void Proof<T>() where T : struct => Runner.Boxed<T>();
    public static Box<T> Unproven<T>() where T : class => new();                                 // CMTK0010 (claim 6: a method's parameter without proof)
}

public static class Shapes
{
    public static Box<PlaceOrder> SealedRecord;
    public static Box<Point> Struct;
    public static Box<DayOfWeek> Enum;
    public static Factory<OrderHandler> Concrete;
    public static Factory<Point> ConcreteStruct;
    public static Box<IHandler> Interface;                                                       // CMTK0010 (claim 8: an interface is not sealed)
    public static Decider<IHandler, Event> InterfaceNotAbstract;                                 // CMTK0010 (claim 8: an interface is not abstract, and not a record: two)
    public static Factory<Handler> AbstractNotConcrete;                                          // CMTK0010 (claim 8)
    public static Decider<Handler, Event> AbstractClassNotRecord;                                // CMTK0010 (claim 9: [Abstract, Record], abstract but not a record)
    public static Decider<Command, Event> Fine;
}
