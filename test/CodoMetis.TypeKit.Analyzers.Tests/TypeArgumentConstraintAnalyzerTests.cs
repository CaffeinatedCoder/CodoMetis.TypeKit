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

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<TypeArgumentConstraintAnalyzer>("public sealed class Box<T> { } public static class U { public static Box<int> B; }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
