using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0010: a type argument that breaks what its parameter declares by attribute. Each claim of
/// <c>spikes/TypeArgumentConstraints</c> is a test here.
/// </summary>
public sealed class TypeArgumentConstraintAnalyzerTests
{
    /// <summary>The generic types and the candidates, present in every test.</summary>
    private const string Subjects =
        """
        using System;
        using CodoMetis.TypeKit;
        using Subjects;

        namespace Subjects
        {
            public abstract record Command;
            public sealed record PlaceOrder : Command;
            public abstract class Handler { }
            public interface IHandler { }
            public sealed class OrderHandler : Handler { }
            public struct Point { }
            public static class Helpers { }

            public abstract class Decider<[Abstract] TCommand, [Record] TEvent> { }

            public sealed class Box<[Sealed] T> { }

            public sealed class Factory<[Concrete] T> { }

            public static class Runner
            {
                public static void Run<[Abstract] T>(T command) { }
                public static void Seal<[Sealed] T>(T value) { }
            }
        }

        """;

    private static AnalyzerTest<TypeArgumentConstraintAnalyzer> Test(string declarations, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<TypeArgumentConstraintAnalyzer>(Subjects + declarations);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0010(int location, string argument, string parameter, string owner, string breach) =>
        new DiagnosticResult("CMTK0010", DiagnosticSeverity.Error).WithLocation(location).WithArguments(argument, parameter, owner, breach);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new TypeArgumentConstraintAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0010");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task A_base_type_is_reported_at_the_argument_that_breaks_it() =>
        Test(
            "public sealed class OrderDecider : Decider<{|#0:PlaceOrder|}, PlaceOrder> { }",
            new DiagnosticResult("CMTK0010", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("'PlaceOrder', the type argument for 'TCommand' of 'Decider<TCommand, TEvent>', is not an abstract class or record"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Members_parameters_returns_locals_and_typeof_are_each_reported() =>
        Test(
            """
            public sealed class Uses
            {
                private Box<{|#0:Handler|}> _field;
                public Box<{|#1:Handler|}> Property { get; set; }
                public Box<{|#2:Handler|}> Method(Box<{|#3:Handler|}> parameter)
                {
                    Box<{|#4:Handler|}> local = null!;
                    var type = typeof(Box<{|#5:Handler|}>);
                    return local;
                }
            }
            """,
            Cmtk0010(0, "Handler", "T", "Box<T>", "is not sealed"),
            Cmtk0010(1, "Handler", "T", "Box<T>", "is not sealed"),
            Cmtk0010(2, "Handler", "T", "Box<T>", "is not sealed"),
            Cmtk0010(3, "Handler", "T", "Box<T>", "is not sealed"),
            Cmtk0010(4, "Handler", "T", "Box<T>", "is not sealed"),
            Cmtk0010(5, "Handler", "T", "Box<T>", "is not sealed"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_call_with_written_type_arguments_is_reported_at_the_argument_once() =>
        Test(
            """
            public static class Calls
            {
                public static void Go() => Runner.Run<{|#0:PlaceOrder|}>(new PlaceOrder());
            }
            """,
            Cmtk0010(0, "PlaceOrder", "T", "Runner.Run<T>", "is not an abstract class or record"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_call_with_inferred_type_arguments_is_reported_at_the_call() =>
        Test(
            """
            public static class Calls
            {
                public static void Go() => {|#0:Runner.Run(new PlaceOrder())|};
            }
            """,
            Cmtk0010(0, "PlaceOrder", "T", "Runner.Run<T>", "is not an abstract class or record"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A method group whose type arguments are inferred from the delegate it converts to has no
    /// generic name and is no call: reported at the group, wherever it converts, a local function's
    /// included. One with its arguments written is the generic name's, reported there once.
    /// </summary>
    [Fact]
    public Task A_method_group_with_inferred_type_arguments_is_reported_at_the_group() =>
        Test(
            """
            public static class Groups
            {
                public static void Register(Action<PlaceOrder> handler) { }

                public static void Go()
                {
                    Action<PlaceOrder> run = {|#0:Runner.Run|};
                    Register({|#1:Runner.Run|});
                    var cast = (Action<PlaceOrder>){|#2:Runner.Run|};
                    Action<Point> seal = Runner.Seal;
                    Action<OrderHandler> sealedHandler = Runner.Seal;
                    Action<Handler> open = {|#3:Runner.Seal|};
                    Action<PlaceOrder> local = {|#4:Local|};
                    Action<PlaceOrder> written = Runner.Run<{|#5:PlaceOrder|}>;

                    static void Local<[Abstract] T>(T command) { }
                }

                public static void Fine()
                {
                    Action<Command> run = Runner.Run;
                    Register(Runner.Run<Command>);
                    Func<Action<Command>> later = () => Runner.Run;
                }
            }
            """,
            Cmtk0010(0, "PlaceOrder", "T", "Runner.Run<T>", "is not an abstract class or record"),
            Cmtk0010(1, "PlaceOrder", "T", "Runner.Run<T>", "is not an abstract class or record"),
            Cmtk0010(2, "PlaceOrder", "T", "Runner.Run<T>", "is not an abstract class or record"),
            Cmtk0010(3, "Handler", "T", "Runner.Seal<T>", "is not sealed"),
            Cmtk0010(4, "PlaceOrder", "T", "Groups.Local<T>", "is not an abstract class or record"),
            Cmtk0010(5, "PlaceOrder", "T", "Runner.Run<T>", "is not an abstract class or record"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>A forwarding parameter carries the attribute, or the requirement ends at it.</summary>
    [Fact]
    public Task A_forwarding_type_parameter_must_carry_the_attribute() =>
        Test(
            """
            public abstract class Leaky<TCommand> : Decider<{|#0:TCommand|}, PlaceOrder> { }

            public abstract class Sound<[Abstract] TCommand> : Decider<TCommand, PlaceOrder> { }

            public sealed class Closed : Sound<{|#1:PlaceOrder|}> { }
            """,
            Cmtk0010(0, "TCommand", "TCommand", "Decider<TCommand, TEvent>", "is a type parameter that does not carry [Abstract]"),
            Cmtk0010(1, "PlaceOrder", "TCommand", "Sound<TCommand>", "is not an abstract class or record"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_struct_or_new_constraint_proves_sealed_and_concrete() =>
        Test(
            """
            public static class Proofs
            {
                public static Box<T> Boxed<T>() where T : struct => new();
                public static Factory<T> Made<T>() where T : new() => new();
                public static Factory<{|#0:T|}> Unproven<T>() where T : class => new();
            }
            """,
            Cmtk0010(0, "T", "T", "Factory<T>", "is a type parameter that does not carry [Concrete]"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// <c>[Concrete]</c> is a type that is not abstract, static or an interface, the type a host can
    /// instantiate. An enum is a value type, which <c>new()</c> and <c>struct</c> both accept, and a
    /// <c>struct</c> parameter that proves <c>[Concrete]</c> can be one; a delegate is a sealed class,
    /// made from a target and a method, as a class with only a parameterised constructor is made from
    /// its arguments; <c>dynamic</c> is <c>object</c>. Their abstract bases are not.
    /// </summary>
    [Fact]
    public Task Concrete_accepts_an_enum_a_delegate_and_dynamic_and_refuses_their_abstract_bases() =>
        Test(
            """
            public delegate void Handled(Handler handler);

            public static class Instantiable
            {
                public static Factory<DayOfWeek> Enum;
                public static Factory<Handled> Delegate;
                public static Factory<Action<int>> FrameworkDelegate;
                public static Factory<dynamic> Dynamic;
                public static Factory<object> Object;
                public static Factory<int?> NullableStruct;
                public static Factory<(int, string)> Tuple;
                public static Factory<{|#0:System.Enum|}> EnumBase;
                public static Factory<{|#1:System.Delegate|}> DelegateBase;
                public static Factory<{|#2:MulticastDelegate|}> MulticastBase;
                public static Factory<{|#3:ValueType|}> ValueTypeBase;
            }
            """,
            Cmtk0010(0, "Enum", "T", "Factory<T>", "is not a concrete class, record or struct"),
            Cmtk0010(1, "Delegate", "T", "Factory<T>", "is not a concrete class, record or struct"),
            Cmtk0010(2, "MulticastDelegate", "T", "Factory<T>", "is not a concrete class, record or struct"),
            Cmtk0010(3, "ValueType", "T", "Factory<T>", "is not a concrete class, record or struct"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A forwarding parameter whose own constraints already prove what the attribute asks needs no
    /// attribute. Only records derive from records (CS8865), so a parameter bound to a record, or to a
    /// parameter that proves <c>[Record]</c>, is a record. A sealed type has no other subtype, so a
    /// parameter bound to one that proves <c>[Sealed]</c> is that type. A sealed type is never abstract,
    /// so whatever proves <c>[Sealed]</c> proves <c>[Concrete]</c>, <c>unmanaged</c> included. Nothing
    /// but the attribute proves <c>[Abstract]</c>: C# has no constraint that excludes a concrete type.
    /// </summary>
    [Fact]
    public Task A_constraint_that_already_proves_the_attribute_is_a_proof() =>
        Test(
            """
            public sealed class Journal<[Record] T> { }

            public static class Proven
            {
                public static Journal<T> BoundToARecord<T>() where T : Command => new();
                public static Journal<T> BoundThroughAParameter<T, U>() where T : U where U : Command => new();
                public static Journal<T> BoundToARecordParameter<T, [Record] U>() where T : U => new();
                public static Box<T> BoundToASealedParameter<T, [Sealed] U>() where T : U => new();
                public static Factory<T> SealedIsConcrete<[Sealed] T>() => new();
                public static Factory<T> BoundToASealedParameterIsConcrete<T, [Sealed] U>() where T : U => new();
                public static Box<T> Unmanaged<T>() where T : unmanaged => new();
                public static Factory<T> UnmanagedIsConcrete<T>() where T : unmanaged => new();
            }

            public static class Unproven
            {
                public static Journal<{|#0:T|}> BoundToAClass<T>() where T : Handler => new();
                public static Decider<{|#1:T|}, PlaceOrder> BoundToAnAbstractRecord<T>() where T : Command => null!;
                public static Decider<{|#2:T|}, PlaceOrder> BoundToAnAbstractParameter<T, [Abstract] U>() where T : U => null!;
                public static Factory<{|#3:T|}> BoundToAConcreteParameter<T, [Concrete] U>() where T : U => new();
                public static Factory<{|#4:T|}> BoundToEnum<T>() where T : System.Enum => new();
                public static Factory<{|#5:T|}> RecordIsNotConcrete<[Record] T>() => new();
                public static Box<{|#6:T|}> ConcreteIsNotSealed<[Concrete] T>() => new();
                public static Journal<{|#7:T|}> AStructIsNoRecord<T>() where T : struct => new();
            }
            """,
            Cmtk0010(0, "T", "T", "Journal<T>", "is a type parameter that does not carry [Record]"),
            Cmtk0010(1, "T", "TCommand", "Decider<TCommand, TEvent>", "is a type parameter that does not carry [Abstract]"),
            Cmtk0010(2, "T", "TCommand", "Decider<TCommand, TEvent>", "is a type parameter that does not carry [Abstract]"),
            Cmtk0010(3, "T", "T", "Factory<T>", "is a type parameter that does not carry [Concrete]"),
            Cmtk0010(4, "T", "T", "Factory<T>", "is a type parameter that does not carry [Concrete]"),
            Cmtk0010(5, "T", "T", "Factory<T>", "is a type parameter that does not carry [Concrete]"),
            Cmtk0010(6, "T", "T", "Box<T>", "is a type parameter that does not carry [Sealed]"),
            Cmtk0010(7, "T", "T", "Journal<T>", "is a type parameter that does not carry [Record]"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Each_constraint_accepts_and_refuses_what_it_says() =>
        Test(
            """
            public sealed class Accepted : Decider<Command, PlaceOrder> { }
            public static class Also
            {
                public static Box<PlaceOrder> SealedRecord;
                public static Box<Point> Struct;
                public static Box<DayOfWeek> Enum;
                public static Factory<OrderHandler> Concrete;
                public static Factory<Point> ConcreteStruct;
                public static Box<{|#0:IHandler|}> Interface;
                public static Decider<{|#1:IHandler|}, PlaceOrder> InterfaceIsNotAbstract;
                public static Decider<Command, {|#2:OrderHandler|}> NotARecord;
                public static Factory<{|#3:Handler|}> Abstract;
                public static Factory<{|#4:IHandler|}> InterfaceNotConcrete;
            }
            """,
            Cmtk0010(0, "IHandler", "T", "Box<T>", "is not sealed"),
            Cmtk0010(1, "IHandler", "TCommand", "Decider<TCommand, TEvent>", "is not an abstract class or record"),
            Cmtk0010(2, "OrderHandler", "TEvent", "Decider<TCommand, TEvent>", "is not a record"),
            Cmtk0010(3, "Handler", "T", "Factory<T>", "is not a concrete class, record or struct"),
            Cmtk0010(4, "IHandler", "T", "Factory<T>", "is not a concrete class, record or struct"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Two_attributes_on_one_parameter_each_report() =>
        Test(
            """
            public sealed class Both<[Abstract, Record] T> { }
            public static class Uses
            {
                public static Both<{|#0:OrderHandler|}> Neither;
                public static Both<{|#1:PlaceOrder|}> RecordButSealed;
                public static Both<Command> Fine;
            }
            """,
            Cmtk0010(0, "OrderHandler", "T", "Both<T>", "is not an abstract class or record"),
            new DiagnosticResult("CMTK0010", DiagnosticSeverity.Error).WithLocation(0).WithArguments("OrderHandler", "T", "Both<T>", "is not a record"),
            Cmtk0010(1, "PlaceOrder", "T", "Both<T>", "is not an abstract class or record"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>The open generic inside its own body, and an unbound typeof, are never reported.</summary>
    [Fact]
    public Task The_open_generic_and_an_unbound_typeof_stay_silent() =>
        Test(
            """
            public abstract class Self<[Abstract] T>
            {
                public Self<T>? Next;
                public static Type Open => typeof(Self<>);
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_misplaced_attribute_is_the_compiler_s_error() =>
        new AnalyzerTest<TypeArgumentConstraintAnalyzer>(
            """
            using CodoMetis.TypeKit;

            [{|CS0592:Abstract|}]
            public sealed class NotAParameter { }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Robustness, not a guard: without CodoMetis.TypeKit there is nothing to look at, and nothing may fail.</summary>
    /// <summary>
    /// A <c>cref</c> in a documentation comment names the generic's own parameter, which carries
    /// nothing, and was reported on every documented API; an array is a sealed, concrete class and
    /// was "not sealed".
    /// </summary>
    [Fact]
    public Task A_doc_comment_cref_and_an_array_argument_are_not_breaches() =>
        Test(
            """
            /// <summary>See <see cref="Box{T}"/>, <see cref="Factory{T}"/>, <see cref="Decider{TCommand, TEvent}"/> and <see cref="Runner.Run{T}(T)"/>.</summary>
            public static class Documented
            {
                public static Box<string[]> Sealed => new();
                public static Factory<int[]> Concrete => new();
                public static Decider<{|#0:string[]|}, PlaceOrder>? Abstract => null;
            }
            """,
            Cmtk0010(0, "string[]", "TCommand", "Decider<TCommand, TEvent>", "is not an abstract class or record"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A method implementing an interface's method, or overriding a base class's, carries the
    /// constraint whether or not it repeats the attribute: a call through the class was silent.
    /// </summary>
    [Fact]
    public Task An_implementation_or_an_override_carries_the_constraint_of_what_it_implements() =>
        Test(
            """
            public interface ISender { void Send<[Concrete] T>(T handler); }
            public sealed class Sender : ISender { public void Send<T>(T handler) { } }
            public abstract class Base { public abstract void Go<[Sealed] T>(); }
            public sealed class Derived : Base { public override void Go<T>() { } }

            public static class Calls
            {
                public static void Go(Sender sender, Derived derived, IHandler handler, OrderHandler concrete)
                {
                    sender.Send<{|#0:IHandler|}>(handler);
                    {|#1:sender.Send(handler)|};
                    sender.Send(concrete);
                    derived.Go<{|#2:Handler|}>();
                    derived.Go<OrderHandler>();
                }
            }
            """,
            Cmtk0010(0, "IHandler", "T", "Sender.Send<T>", "is not a concrete class, record or struct"),
            Cmtk0010(1, "IHandler", "T", "Sender.Send<T>", "is not a concrete class, record or struct"),
            Cmtk0010(2, "Handler", "T", "Derived.Go<T>", "is not sealed"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A C# 14 extension block's type parameter is inferred from the receiver at a use: for a method,
    /// a property, a static member, a method group and an operator alike, reported at the use. Written,
    /// the block's arguments come first in the member's list (<c>handler.Take&lt;Handler, Handler&gt;(handler)</c>,
    /// measured on Roslyn 5.0.0), and each is reported where it is written. Only the lowered
    /// <c>Ex.Go(handler)</c> was reported.
    /// </summary>
    [Fact]
    public Task An_extension_block_s_type_parameter_is_checked_at_every_member_use() =>
        Test(
            """
            public static class Ex
            {
                extension<[Concrete] T>(T value)
                {
                    public void Go() { }
                    public void Take<[Sealed] TOther>(TOther other) { }
                    public int Prop => 1;
                    public static void Make() { }
                }

                extension<[Concrete] T>(T value) where T : class
                {
                    public static T operator +(T left, int right) => left;
                }
            }

            public static class ExtensionUses
            {
                public static void Go(Handler handler, OrderHandler concrete)
                {
                    {|#0:handler.Go()|};
                    _ = {|#1:handler.Prop|};
                    {|#2:Handler.Make()|};
                    Action group = {|#3:handler.Go|};
                    {|#4:Ex.Go(handler)|};
                    {|#5:handler.Take(concrete)|};
                    handler.Take<{|#6:Handler|}, {|#7:Handler|}>(handler);
                    _ = {|#8:handler + 1|};

                    concrete.Go();
                    _ = concrete.Prop;
                    OrderHandler.Make();
                    Action fine = concrete.Go;
                    concrete.Take(concrete);
                    concrete.Take<OrderHandler, OrderHandler>(concrete);
                    _ = concrete + 1;
                }
            }
            """,
            Cmtk0010(0, "Handler", "T", "Ex.extension<T>(T)", "is not a concrete class, record or struct"),
            Cmtk0010(1, "Handler", "T", "Ex.extension<T>(T)", "is not a concrete class, record or struct"),
            Cmtk0010(2, "Handler", "T", "Ex.extension<T>(T)", "is not a concrete class, record or struct"),
            Cmtk0010(3, "Handler", "T", "Ex.extension<T>(T)", "is not a concrete class, record or struct"),
            Cmtk0010(4, "Handler", "T", "Ex.Go<T>", "is not a concrete class, record or struct"),
            Cmtk0010(5, "Handler", "T", "Ex.extension<T>(T)", "is not a concrete class, record or struct"),
            Cmtk0010(6, "Handler", "T", "Ex.extension<T>(T)", "is not a concrete class, record or struct"),
            Cmtk0010(7, "Handler", "TOther", "Ex.Take<TOther>", "is not sealed"),
            Cmtk0010(8, "Handler", "T", "Ex.extension<T>(T)", "is not a concrete class, record or struct"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>A generic attribute's name binds to its constructor, and its type argument was not checked.</summary>
    [Fact]
    public Task A_generic_attribute_s_type_argument_is_checked() =>
        Test(
            """
            [AttributeUsage(AttributeTargets.All)]
            public sealed class MarkAttribute<[Concrete] T> : Attribute { }

            [Mark<{|#0:Handler|}>] public sealed class Marked { }
            [MarkAttribute<{|#1:Handler|}>] public sealed class MarkedInFull { }
            [Mark<OrderHandler>] public sealed class Fine { }
            """,
            Cmtk0010(0, "Handler", "T", "MarkAttribute<T>", "is not a concrete class, record or struct"),
            Cmtk0010(1, "Handler", "T", "MarkAttribute<T>", "is not a concrete class, record or struct"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A static method implementing a static abstract interface method, and a base class's method that
    /// implements an interface for a derived class, carry the interface's constraint; both were
    /// silent. Through the base class itself, which implements nothing, there is no constraint.
    /// </summary>
    [Fact]
    public Task A_static_abstract_or_a_base_class_implementation_carries_the_interface_s_constraint() =>
        Test(
            """
            public interface IFactory { static abstract void Make<[Concrete] T>(); }
            public sealed class Factory : IFactory { public static void Make<T>() { } }
            public class FactoryBase { public static void Make<T>() { } }
            public sealed class DerivedFactory : FactoryBase, IFactory { }

            public interface ISender { void Send<[Concrete] T>(T handler); }
            public class BaseSender { public void Send<T>(T handler) { } }
            public sealed class DerivedSender : BaseSender, ISender
            {
                public void Inside(Handler handler) => Send<{|#0:Handler|}>(handler);
                public void Based(Handler handler) => base.Send<{|#8:Handler|}>(handler);
            }

            public class OpenSender : BaseSender, ISender { }

            public static class Calls
            {
                public static void Go(DerivedSender derived, BaseSender plain, Handler handler, OrderHandler concrete)
                {
                    Factory.Make<{|#1:Handler|}>();
                    DerivedFactory.Make<{|#2:Handler|}>();
                    Factory.Make<OrderHandler>();
                    derived.Send<{|#3:Handler|}>(handler);
                    {|#4:derived.Send(handler)|};
                    Action<Handler> group = {|#5:derived.Send|};
                    derived.Send(concrete);
                    plain.Send(handler);
                    FactoryBase.Make<Handler>();
                }

                public static void Generic<TFactory>() where TFactory : IFactory => TFactory.Make<{|#6:Handler|}>();

                public static void Constrained<TSender>(TSender sender, Handler handler) where TSender : OpenSender => sender.Send<{|#7:Handler|}>(handler);
            }
            """,
            Cmtk0010(0, "Handler", "T", "BaseSender.Send<T>", "is not a concrete class, record or struct"),
            Cmtk0010(1, "Handler", "T", "Factory.Make<T>", "is not a concrete class, record or struct"),
            Cmtk0010(2, "Handler", "T", "FactoryBase.Make<T>", "is not a concrete class, record or struct"),
            Cmtk0010(3, "Handler", "T", "BaseSender.Send<T>", "is not a concrete class, record or struct"),
            Cmtk0010(4, "Handler", "T", "BaseSender.Send<T>", "is not a concrete class, record or struct"),
            Cmtk0010(5, "Handler", "T", "BaseSender.Send<T>", "is not a concrete class, record or struct"),
            Cmtk0010(6, "Handler", "T", "IFactory.Make<T>", "is not a concrete class, record or struct"),
            Cmtk0010(7, "Handler", "T", "BaseSender.Send<T>", "is not a concrete class, record or struct"),
            Cmtk0010(8, "Handler", "T", "BaseSender.Send<T>", "is not a concrete class, record or struct"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A null-conditional call with written arguments was reported twice, at the argument and at the
    /// call; a <c>nameof</c> names a type and uses no argument of it; a type that does not exist is
    /// the compiler's error alone.
    /// </summary>
    [Fact]
    public Task A_null_conditional_call_once_and_a_nameof_or_a_missing_type_not_at_all() =>
        Test(
            """
            public sealed class Holder
            {
                public void Inst<[Sealed] T>() { }
                public void Take<[Sealed] T>(T value) { }
            }

            public static class Uses
            {
                public static Box<{|CS0246:Missing|}>? Unknown;

                public static void Go(Holder? holder, Handler handler)
                {
                    holder?.Inst<{|#0:Handler|}>();
                    holder?.Inst<OrderHandler>();
                    holder?{|#1:.Take(handler)|};
                    _ = nameof(Box<Handler>);
                    _ = nameof(Box<Handler>.ToString);
                    _ = typeof(Box<{|#2:Handler|}>);
                }
            }
            """,
            Cmtk0010(0, "Handler", "T", "Holder.Inst<T>", "is not sealed"),
            Cmtk0010(1, "Handler", "T", "Holder.Take<T>", "is not sealed"),
            Cmtk0010(2, "Handler", "T", "Box<T>", "is not sealed"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<TypeArgumentConstraintAnalyzer>("public sealed class Box<T> { } public static class U { public static Box<int> B; }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
