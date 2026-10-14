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

            public sealed class Logged : Handler, IHandler { }
            public class Tracked : Handler { }

            public class Pair<[Distinct] TLeft, [Distinct] TRight> { }

            public class Apart<[Unrelated] TLeft, [Unrelated] TRight> { }

            public sealed class Keyed<[Unrelated] TKey, [Distinct] TLeft, [Distinct] TRight> { }

            public static class Pairs
            {
                public static void Zip<[Distinct] TA, [Distinct] TB>(TA a, TB b) { }
            }

            public interface IContract { }
            [Flags] public enum Rights { None = 0, Read = 1, Write = 2 }
            public enum Plain { A, B }
            public readonly struct Frozen { }
            public readonly record struct FrozenRecord(int Value);
            public struct Mutable { public int Value; }
            public readonly struct Id : CodoMetis.TypeKit.ValueObjects.IValue<int> { }
            public class IdClass : CodoMetis.TypeKit.ValueObjects.IValue<int> { }
            public class Colour : CodoMetis.TypeKit.ClosedSets.IClosedSet<Colour>
            {
                public string Name => "";
                public string QualifiedName => "";
                public static System.Collections.Generic.IReadOnlyList<Colour> All => [];
                public static bool TryParse(string? name, out Colour member) { member = null!; return false; }
            }
            public sealed class Refusal : CodoMetis.TypeKit.ClosedSets.IFaultSet<Refusal>
            {
                public string Name => "";
                public string QualifiedName => "";
                public static System.Collections.Generic.IReadOnlyList<Refusal> All => [];
                public static bool TryParse(string? name, out Refusal member) { member = null!; return false; }
                public CodoMetis.TypeKit.ClosedSets.FaultKind Kind => CodoMetis.TypeKit.ClosedSets.FaultKind.NotFound;
                public string? Description => null;
            }

            public sealed class Proxy<[Interface] T> { }
            public sealed class Derivable<[Unsealed] T> { }
            public sealed class Keyed<[ValueObject] T> { }
            public sealed class Named<[ClosedSet] T> { }
            public sealed class Flagged<[FlagsEnum] T> { }
            public sealed class Pinned<[ReadOnlyStruct] T> { }
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
    /// A method the compiler binds by pattern has its type arguments inferred, and nothing names it: a
    /// <c>foreach</c>'s <c>GetEnumerator</c>, an <c>await</c>'s <c>GetAwaiter</c>, a deconstruction's
    /// <c>Deconstruct</c>, in an assignment, a <c>foreach</c> or a positional pattern, and a collection
    /// expression's <c>Add</c> or builder. Each was silent, where a call written out was reported
    /// (reported 2026-10-14); each is reported where the pattern is used.
    /// </summary>
    [Fact]
    public Task A_method_bound_by_a_pattern_is_reported_where_the_pattern_is_used() =>
        Test(
            """
            public sealed class Bag<T> { }
            public struct Cursor<T> { public T Current => default!; public bool MoveNext() => false; }
            public readonly struct Waiter<T> : System.Runtime.CompilerServices.INotifyCompletion
            {
                public bool IsCompleted => true;
                public T GetResult() => default!;
                public void OnCompleted(Action continuation) { }
            }

            public sealed class Heap<T> : System.Collections.Generic.IEnumerable<T>
            {
                public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [System.Runtime.CompilerServices.CollectionBuilder(typeof(Built), nameof(Built.Create))]
            public sealed class Built<T> : System.Collections.Generic.IEnumerable<T>
            {
                public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class Built
            {
                public static Built<T> Create<[ReadOnlyStruct] T>(ReadOnlySpan<T> items) => new();
            }

            public static class Patterns
            {
                public static Cursor<T> GetEnumerator<[ReadOnlyStruct] T>(this Bag<T> bag) => default;
                public static Waiter<T> GetAwaiter<[ReadOnlyStruct] T>(this Bag<T> bag) => default;
                public static void Deconstruct<[ReadOnlyStruct] T>(this Bag<T> bag, out int first, out int second) => (first, second) = (0, 0);
                public static void Add<[ReadOnlyStruct] T>(this Heap<T> heap, T item) { }
            }

            public sealed class Crate<T> { }

            public static class BlockPatterns
            {
                extension<[ReadOnlyStruct] T>(Crate<T> crate)
                {
                    public Cursor<T> GetEnumerator() => default;
                }
            }

            public static class Uses
            {
                public static async System.Threading.Tasks.Task Go(Bag<Handler> bag, System.Collections.Generic.List<Bag<Handler>> bags, Handler handler, Handler[] more, Crate<Handler> crate)
                {
                    foreach (var item in {|#0:bag|}) { }
                    foreach ({|#1:var (x, y)|} in bags) { }
                    var awaited = {|#2:await bag|};
                    {|#3:var (a, b) = bag|};
                    {|#4:(a, b) = bag|};
                    if (bag is {|#5:(1, 2)|}) { }
                    var label = bag switch { {|#6:(0, 0)|} => "zero", _ => "other" };
                    Heap<Handler> heap = {|#7:[handler]|};
                    Built<Handler> built = {|#8:[handler]|};
                    Heap<Handler> spread = {|#9:[..more]|};
                    Heap<Handler> twice = {|#12:[handler, handler]|};
                    {|#10:var ((p, q), r) = (bag, 1)|};
                    foreach (var item in {|#11:crate|}) { }
                }

                public static async System.Threading.Tasks.Task Fine(Bag<Frozen> bag, System.Collections.Generic.List<Bag<Frozen>> bags, Frozen handler)
                {
                    foreach (var item in bag) { }
                    foreach (var (x, y) in bags) { }
                    var awaited = await bag;
                    var (a, b) = bag;
                    (a, b) = bag;
                    if (bag is (1, 2)) { }
                    Heap<Frozen> heap = [handler];
                    Built<Frozen> built = [handler];
                }
            }
            """,
            Cmtk0010(0, "Handler", "T", "Patterns.GetEnumerator<T>", "is not a readonly struct"),
            Cmtk0010(1, "Handler", "T", "Patterns.Deconstruct<T>", "is not a readonly struct"),
            Cmtk0010(2, "Handler", "T", "Patterns.GetAwaiter<T>", "is not a readonly struct"),
            Cmtk0010(3, "Handler", "T", "Patterns.Deconstruct<T>", "is not a readonly struct"),
            Cmtk0010(4, "Handler", "T", "Patterns.Deconstruct<T>", "is not a readonly struct"),
            Cmtk0010(5, "Handler", "T", "Patterns.Deconstruct<T>", "is not a readonly struct"),
            Cmtk0010(6, "Handler", "T", "Patterns.Deconstruct<T>", "is not a readonly struct"),
            Cmtk0010(7, "Handler", "T", "Patterns.Add<T>", "is not a readonly struct"),
            Cmtk0010(8, "Handler", "T", "Built.Create<T>", "is not a readonly struct"),
            Cmtk0010(9, "Handler", "T", "Patterns.Add<T>", "is not a readonly struct"),
            Cmtk0010(10, "Handler", "T", "Patterns.Deconstruct<T>", "is not a readonly struct"),
            Cmtk0010(11, "Handler", "T", "BlockPatterns.extension<T>(Crate<T>)", "is not a readonly struct"),
            Cmtk0010(12, "Handler", "T", "Patterns.Add<T>", "is not a readonly struct"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// CMTK0010 shipped at Error in 2.0.0 with <c>[Abstract]</c>, <c>[Sealed]</c>, <c>[Record]</c> and
    /// <c>[Concrete]</c>, and left a method bound by pattern alone; an Error that reports more waits for a major
    /// version (decision 34), so at a pattern use those four stay silent until 3.0.0 (decision 89), while the
    /// same method called by name is reported as it was. A pair attribute, new in 2.1.0, is checked there.
    /// </summary>
    [Fact]
    public Task At_a_pattern_use_the_2_0_kinds_wait_for_3_0_and_a_pair_is_checked() =>
        Test(
            """
            public sealed class Bag<T> { }
            public struct Cursor<T> { public T Current => default!; public bool MoveNext() => false; }
            public sealed class Duo<TA, TB> { }

            public static class Patterns
            {
                public static Cursor<T> GetEnumerator<[Concrete] T>(this Bag<T> bag) => default;
                public static void Deconstruct<[Distinct] TA, [Distinct] TB>(this Duo<TA, TB> duo, out int first, out int second) => (first, second) = (0, 0);
            }

            public static class Uses
            {
                public static void Go(Bag<Handler> bag, Duo<Handler, Handler> same, Duo<Handler, OrderHandler> apart)
                {
                    foreach (var item in bag) { }
                    var cursor = {|#0:Patterns.GetEnumerator(bag)|};
                    {|#1:var (a, b) = same|};
                    var (c, d) = apart;
                }
            }
            """,
            Cmtk0010(0, "Handler", "T", "Patterns.GetEnumerator<T>", "is not a concrete class, record or struct"),
            Cmtk0010(1, "Handler", "TB", "Patterns.Deconstruct<TA, TB>", "is the same type as the argument for 'TA'"))
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
    /// A default interface method called through a derived interface is its own implementation there:
    /// the analyzer took it for a method it implements, and recursed until the compiler's stack
    /// overflowed. It carries its own constraint, once.
    /// </summary>
    [Fact]
    public Task A_default_interface_method_reached_through_a_derived_interface_carries_its_own_constraint() =>
        Test(
            """
            public interface IRunner { void Run<[Concrete] T>() { } }
            public interface IFastRunner : IRunner { }
            public sealed class FastRunner : IFastRunner { }
            public interface IMaker { static virtual void Make<[Concrete] T>() { } }
            public interface IFastMaker : IMaker { }

            public static class Calls
            {
                public static void Go(IFastRunner runner, IRunner plain)
                {
                    runner.Run<{|#0:Handler|}>();
                    runner.Run<OrderHandler>();
                    plain.Run<{|#1:Handler|}>();
                }

                public static void Generic<TMaker>() where TMaker : IFastMaker => TMaker.Make<{|#2:Handler|}>();
            }
            """,
            Cmtk0010(0, "Handler", "T", "IRunner.Run<T>", "is not a concrete class, record or struct"),
            Cmtk0010(1, "Handler", "T", "IRunner.Run<T>", "is not a concrete class, record or struct"),
            Cmtk0010(2, "Handler", "T", "IMaker.Make<T>", "is not a concrete class, record or struct"))
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

    /// <summary>
    /// <c>[Distinct]</c> is relational: the pair is reported once, at the later argument, naming the earlier
    /// parameter. One symbol is one type whatever its nullable annotation, and <c>dynamic</c> is <c>object</c>;
    /// a related pair, a base and a derived type, is distinct.
    /// </summary>
    [Fact]
    public Task Distinct_reports_one_type_given_twice_at_the_later_argument() =>
        Test(
            """
            public static class Uses
            {
                public static Pair<Handler, {|#0:Handler|}> Same;
                public static Pair<string, {|#1:string?|}> Annotated;
                public static Pair<object, {|#2:dynamic|}> Dynamic;
                public static Pair<(int a, int b), {|#3:(int x, int y)|}> TupleNames;
                public static Pair<Handler, OrderHandler> Related;
                public static Pair<int, int?> Nullable;
                public static Pair<int, long> Numeric;
            }
            """,
            new DiagnosticResult("CMTK0010", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("'Handler', the type argument for 'TRight' of 'Pair<TLeft, TRight>', is the same type as the argument for 'TLeft'"),
            Cmtk0010(1, "string?", "TRight", "Pair<TLeft, TRight>", "is the same type as the argument for 'TLeft'"),
            Cmtk0010(2, "dynamic", "TRight", "Pair<TLeft, TRight>", "is the same type as the argument for 'TLeft'"),
            Cmtk0010(3, "(int x, int y)", "TRight", "Pair<TLeft, TRight>", "is the same type as the argument for 'TLeft'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// <c>[Unrelated]</c> refuses an implicit identity, reference or boxing conversion either way: a base
    /// and a derived type in either order, an interface and an implementer, <c>object</c> and a struct.
    /// A numeric or a nullable conversion is no relation, nor are two classes of different base chains.
    /// </summary>
    [Fact]
    public Task Unrelated_reports_a_base_a_derived_type_an_interface_and_a_box() =>
        Test(
            """
            public static class Uses
            {
                public static Apart<Handler, {|#0:OrderHandler|}> Derived;
                public static Apart<OrderHandler, {|#1:Handler|}> Base;
                public static Apart<IHandler, {|#2:Logged|}> Implemented;
                public static Apart<object, {|#3:Point|}> Boxed;
                public static Apart<Handler, {|#4:Handler|}> Same;
                public static Apart<Point, OrderHandler> Fine;
                public static Apart<Handler, PlaceOrder> OtherChain;
                public static Apart<int, int?> Nullable;
                public static Apart<int, long> Numeric;
                public static Apart<IHandler, IComparable> TwoInterfaces;
            }
            """,
            new DiagnosticResult("CMTK0010", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("'OrderHandler', the type argument for 'TRight' of 'Apart<TLeft, TRight>', derives from, implements, boxes to or is a base of 'Handler', the type argument for 'TLeft'"),
            Cmtk0010(1, "Handler", "TRight", "Apart<TLeft, TRight>", "derives from, implements, boxes to or is a base of 'OrderHandler', the type argument for 'TLeft'"),
            Cmtk0010(2, "Logged", "TRight", "Apart<TLeft, TRight>", "derives from, implements, boxes to or is a base of 'IHandler', the type argument for 'TLeft'"),
            Cmtk0010(3, "Point", "TRight", "Apart<TLeft, TRight>", "derives from, implements, boxes to or is a base of 'object', the type argument for 'TLeft'"),
            Cmtk0010(4, "Handler", "TRight", "Apart<TLeft, TRight>", "is the same type as the argument for 'TLeft'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>A pair is checked at the stricter level either parameter declares; a parameter without either attribute is in no pair.</summary>
    [Fact]
    public Task A_pair_is_held_to_the_stricter_of_its_two_attributes() =>
        Test(
            """
            public sealed class Half<[Distinct] TLeft, TRight> { }

            public static class Uses
            {
                public static Keyed<Handler, {|#0:OrderHandler|}, Point> KeyRelatedToLeft;
                public static Keyed<Point, Handler, OrderHandler> LeftAndRightOnlyDistinct;
                public static Keyed<Point, Handler, {|#1:Handler|}> LeftAndRightSame;
                public static Half<Handler, Handler> Alone;
            }
            """,
            Cmtk0010(0, "OrderHandler", "TLeft", "Keyed<TKey, TLeft, TRight>", "derives from, implements, boxes to or is a base of 'Handler', the type argument for 'TKey'"),
            Cmtk0010(1, "Handler", "TRight", "Keyed<TKey, TLeft, TRight>", "is the same type as the argument for 'TLeft'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The pair is checked wherever a unary constraint is: an inferred call (at the call), a method group (at
    /// the group), a generic attribute (at the argument). A generic in its own body names its own parameters,
    /// which carry the attribute together, and is silent; an extension block's parameter and its method's are
    /// two generics, so they are no pair.
    /// </summary>
    [Fact]
    public Task An_inferred_pair_is_reported_at_the_call_and_a_written_one_at_the_later_argument() =>
        Test(
            """
            [AttributeUsage(AttributeTargets.All)]
            public sealed class BothAttribute<[Distinct] TA, [Distinct] TB> : Attribute { }

            public sealed class Self<[Distinct] TA, [Distinct] TB>
            {
                public Self<TA, TB>? Next;
                public Self<TB, TA>? Swapped;
            }

            public static class Ex
            {
                extension<[Distinct] T>(T value)
                {
                    public void With<[Distinct] TOther>(TOther other) { }
                }
            }

            [Both<Handler, {|#2:Handler|}>]
            public static class Calls
            {
                public static void Go(Handler handler, OrderHandler concrete)
                {
                    {|#0:Pairs.Zip(handler, handler)|};
                    Pairs.Zip<Handler, {|#1:Handler|}>(handler, handler);
                    Pairs.Zip(handler, concrete);
                    Action<Handler, Handler> group = {|#3:Pairs.Zip|};
                    Action<Handler, OrderHandler> fine = Pairs.Zip;
                    handler.With(handler);
                }
            }
            """,
            Cmtk0010(0, "Handler", "TB", "Pairs.Zip<TA, TB>", "is the same type as the argument for 'TA'"),
            Cmtk0010(1, "Handler", "TB", "Pairs.Zip<TA, TB>", "is the same type as the argument for 'TA'"),
            Cmtk0010(2, "Handler", "TB", "BothAttribute<TA, TB>", "is the same type as the argument for 'TA'"),
            Cmtk0010(3, "Handler", "TB", "Pairs.Zip<TA, TB>", "is the same type as the argument for 'TA'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A forwarding parameter is told apart from the other argument when both are parameters of one generic
    /// carrying the attribute, whose closed uses are then checked, or when their C# constraints admit no
    /// common type; the same parameter twice is the same type; otherwise the pair is a breach, or the
    /// requirement would end at the first forwarding generic. <c>struct</c> admits no <c>Nullable&lt;T&gt;</c>,
    /// and <c>unmanaged</c> no tuple holding a reference. An <c>Enum</c> bound admits every enum,
    /// a value type, so against <c>struct</c> or <c>unmanaged</c> it proves nothing:
    /// <c>DayOfWeek</c> met both, and two of it passed through (reported 2026-10-14).
    /// </summary>
    [Fact]
    public Task A_forwarding_pair_must_carry_the_attribute_or_be_proved_apart_by_its_constraints() =>
        Test(
            """
            public sealed class Leaky<TA, TB> : Pair<TA, {|#0:TB|}> { }
            public class Sound<[Distinct] TA, [Distinct] TB> : Pair<TA, TB> { }
            public sealed class Closed : Sound<Handler, {|#1:Handler|}> { }
            public sealed class Twice<T> : Pair<T, {|#2:T|}> { }
            public sealed class StructAndClass<TS, TC> : Pair<TS, TC> where TS : struct where TC : class { }
            public sealed class StructAndBound<TS, TC> : Pair<TS, TC> where TS : unmanaged where TC : Handler { }
            public sealed class TwoChains<TA, TB> : Pair<TA, TB> where TA : Handler where TB : Command { }
            public sealed class OneChain<TA, TB> : Pair<TA, {|#3:TB|}> where TA : Handler where TB : Tracked { }
            public sealed class Open<T> : Pair<T, {|#4:Handler|}> { }
            public sealed class StructAndType<T> : Pair<T, Handler> where T : struct { }
            public sealed class BoundAndType<T> : Pair<T, OrderHandler> where T : Command { }
            public sealed class BoundAndDerived<T> : Pair<T, {|#5:OrderHandler|}> where T : Handler { }
            public sealed class ClassAndStruct<T> : Pair<T, Point> where T : class { }
            public sealed class UnrelatedProvesDistinct<[Unrelated] TA, [Unrelated] TB> : Pair<TA, TB> { }
            public sealed class StructAndEnum<TS, TE> : Pair<TS, {|#6:TE|}> where TS : struct where TE : Enum { }
            public sealed class UnmanagedAndEnum<TS, TE> : Pair<TS, {|#7:TE|}> where TS : unmanaged where TE : Enum { }
            public sealed class EnumAndClass<TE, TC> : Pair<TE, TC> where TE : Enum where TC : Handler { }
            public sealed class StructAndNullable<T> : Pair<T, int?> where T : struct { }
            public sealed class UnmanagedAndManagedTuple<T> : Pair<T, (int, string)> where T : unmanaged { }
            """,
            Cmtk0010(0, "TB", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'TA', the type argument for 'TLeft'"),
            Cmtk0010(1, "Handler", "TB", "Sound<TA, TB>", "is the same type as the argument for 'TA'"),
            Cmtk0010(2, "T", "TRight", "Pair<TLeft, TRight>", "is the same type as the argument for 'TLeft'"),
            Cmtk0010(3, "TB", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'TA', the type argument for 'TLeft'"),
            Cmtk0010(4, "Handler", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T', the type argument for 'TLeft'"),
            Cmtk0010(5, "OrderHandler", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T', the type argument for 'TLeft'"),
            Cmtk0010(6, "TE", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'TS', the type argument for 'TLeft'"),
            Cmtk0010(7, "TE", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'TS', the type argument for 'TLeft'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// For <c>[Unrelated]</c> a forwarding pair is proved by both carrying <c>[Unrelated]</c> in one generic,
    /// by <c>struct</c> against a class or a delegate no value type boxes to, or by two class bounds of different chains;
    /// <c>[Distinct]</c>, <c>class</c> alone (which admits <c>object</c>) and an interface prove nothing.
    /// </summary>
    [Fact]
    public Task A_forwarding_pair_under_Unrelated_needs_a_proof_that_no_relation_is_possible() =>
        Test(
            """
            public sealed class Sound<[Unrelated] TA, [Unrelated] TB> : Apart<TA, TB> { }
            public sealed class OnlyDistinct<[Distinct] TA, [Distinct] TB> : Apart<TA, {|#0:TB|}> { }
            public sealed class StructAndClass<T> : Apart<T, Handler> where T : struct { }
            public sealed class StructAndObject<T> : Apart<T, {|#1:object|}> where T : struct { }
            public sealed class StructAndValueType<T> : Apart<T, {|#2:ValueType|}> where T : struct { }
            public sealed class StructAndInterface<T> : Apart<T, {|#3:IHandler|}> where T : struct { }
            public sealed class StructAndClassConstraint<TS, TC> : Apart<TS, {|#4:TC|}> where TS : struct where TC : class { }
            public sealed class StructAndBound<TS, TC> : Apart<TS, TC> where TS : struct where TC : Handler { }
            public sealed class TwoChains<TA, TB> : Apart<TA, TB> where TA : Handler where TB : Command { }
            public sealed class OneChain<TA, TB> : Apart<TA, {|#5:TB|}> where TA : Handler where TB : Tracked { }
            public sealed class BoundAndStruct<T> : Apart<T, Point> where T : Handler { }
            public sealed class BoundAndOtherChain<T> : Apart<T, PlaceOrder> where T : Handler { }
            public sealed class BoundAndDerived<T> : Apart<T, {|#6:OrderHandler|}> where T : Handler { }
            public sealed class BoundAndInterface<T> : Apart<T, {|#7:IHandler|}> where T : Handler { }
            public sealed class ClassAndStruct<T> : Apart<T, {|#8:Point|}> where T : class { }
            public sealed class StructAndDelegate<T> : Apart<T, Action> where T : struct { }
            """,
            Cmtk0010(0, "TB", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'TA', the type argument for 'TLeft'"),
            Cmtk0010(1, "object", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"),
            Cmtk0010(2, "ValueType", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"),
            Cmtk0010(3, "IHandler", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"),
            Cmtk0010(4, "TC", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'TS', the type argument for 'TLeft'"),
            Cmtk0010(5, "TB", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'TA', the type argument for 'TLeft'"),
            Cmtk0010(6, "OrderHandler", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"),
            Cmtk0010(7, "IHandler", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"),
            Cmtk0010(8, "Point", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A bound built from a type parameter is met by what the parameter is at each closed use, not by the
    /// bound as written: <c>int</c> meets <c>where T : IEquatable&lt;T&gt;</c>, <c>Refusal</c> meets
    /// <c>IFaultSet&lt;TSet&gt;</c>, <c>Leaf</c> meets <c>Node&lt;T&gt;</c>, so none of them proves the pair
    /// apart; two classes built from parameters are compared by their definitions, so <c>Node&lt;TA&gt;</c> and
    /// <c>Node&lt;TB&gt;</c> are one chain and <c>Node&lt;TA&gt;</c> and <c>Command</c> two (reported 2026-10-14).
    /// </summary>
    [Fact]
    public Task A_bound_built_from_a_type_parameter_is_met_by_what_the_parameter_is_at_each_use() =>
        Test(
            """
            public abstract class Node<T> where T : Node<T> { }
            public sealed class Leaf : Node<Leaf> { }

            public sealed class Respond<TSet> where TSet : class, CodoMetis.TypeKit.ClosedSets.IFaultSet<TSet>
            {
                public Fault<TSet, {|#0:Refusal|}>? Held;
                public Apart<TSet, {|#1:Refusal|}>? Kept;
            }
            public sealed class Equatable<T> : Pair<T, {|#2:int|}> where T : IEquatable<T> { }
            public sealed class Comparable<T> : Pair<T, {|#3:int|}> where T : IComparable<T> { }
            public sealed class EquatableApart<T> : Apart<T, {|#4:int|}> where T : IEquatable<T> { }
            public sealed class SelfBound<T> : Pair<T, {|#5:Leaf|}> where T : Node<T> { }
            public sealed class SelfBoundApart<T> : Apart<T, {|#6:Leaf|}> where T : Node<T> { }
            public sealed class TwoSelfBounds<TA, TB> : Pair<TA, {|#7:TB|}> where TA : Node<TA> where TB : Node<TB> { }
            public sealed class TwoSelfBoundsApart<TA, TB> : Apart<TA, {|#8:TB|}> where TA : Node<TA> where TB : Node<TB> { }
            public sealed class Sequence<TA, TB> : Pair<TB, {|#9:System.Collections.Generic.List<int>|}> where TB : System.Collections.Generic.IEnumerable<TA> { }
            public sealed class SequenceApart<TA, TB> : Apart<TB, {|#10:System.Collections.Generic.List<int>|}> where TB : System.Collections.Generic.IEnumerable<TA> { }

            public sealed class SelfBoundAndOtherChain<T> : Pair<T, PlaceOrder> where T : Node<T> { }
            public sealed class SelfBoundAndOtherChainApart<T> : Apart<T, PlaceOrder> where T : Node<T> { }
            public sealed class SelfBoundAndCommand<TA, TB> : Pair<TA, TB> where TA : Node<TA> where TB : Command { }
            public sealed class SelfBoundAndCommandApart<TA, TB> : Apart<TA, TB> where TA : Node<TA> where TB : Command { }
            """,
            Cmtk0010(0, "Refusal", "T2", "Fault<T1, T2>", "is not proved a different type from 'TSet', the type argument for 'T1'"),
            Cmtk0010(1, "Refusal", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'TSet', the type argument for 'TLeft'"),
            Cmtk0010(2, "int", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T', the type argument for 'TLeft'"),
            Cmtk0010(3, "int", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T', the type argument for 'TLeft'"),
            Cmtk0010(4, "int", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"),
            Cmtk0010(5, "Leaf", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T', the type argument for 'TLeft'"),
            Cmtk0010(6, "Leaf", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T', the type argument for 'TLeft'"),
            Cmtk0010(7, "TB", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'TA', the type argument for 'TLeft'"),
            Cmtk0010(8, "TB", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'TA', the type argument for 'TLeft'"),
            Cmtk0010(9, "List<int>", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'TB', the type argument for 'TLeft'"),
            Cmtk0010(10, "List<int>", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'TB', the type argument for 'TLeft'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A type built from a type parameter, <c>List&lt;T&gt;</c> or <c>T[]</c>, is what the parameter makes it
    /// at each closed use, so it is a breach unless proved apart, as the parameter is: compared as written,
    /// <c>List&lt;T&gt;</c> and <c>List&lt;int&gt;</c> were two types, and <c>Listed&lt;int&gt;</c> held one
    /// twice (reported 2026-10-14). Two generic types are proved different by their definitions, or by an
    /// argument no use can make one; two arrays by their ranks or elements; a parameter is never a type built
    /// from itself. For <c>[Unrelated]</c>, a value type against a class no value type boxes to, two value
    /// types never one, and two classes of different chains are proofs; two arrays, two delegates and an
    /// interface are not, being related by variance or by an implementer.
    /// </summary>
    [Fact]
    public Task A_type_built_from_a_type_parameter_is_held_as_the_parameter_is() =>
        Test(
            """
            namespace Built
            {
                using System.Collections.Generic;

                public sealed class Listed<T> : Pair<List<T>, {|#0:List<int>|}> { }
                public sealed class Arrays<T> : Pair<T[], {|#1:int[]|}> { }
                public sealed class BothOpen<TA, TB> : Pair<List<TA>, {|#2:List<TB>|}> { }
                public sealed class Sequenced<T> : Apart<IEnumerable<T>, {|#3:List<int>|}> { }
                public sealed class SameOpen<T> : Pair<List<T>, {|#4:List<T>|}> { }
                public sealed class Nested<T> : Pair<Dictionary<string, List<T>>, {|#5:Dictionary<string, List<int>>|}> { }
                public sealed class ParameterAndBuilt<T, U> : Pair<T, {|#11:List<U>|}> where T : IEnumerable<int> { }
                public sealed class UnmanagedAndBuilt<T, U> : Pair<T, {|#12:KeyValuePair<U, int>|}> where T : unmanaged { }

                public sealed class BothCarried<[Distinct] TA, [Distinct] TB> : Pair<List<TA>, List<TB>> { }
                public sealed class ClassElement<T> : Pair<List<T>, List<int>> where T : class { }
                public sealed class OtherDefinition<T> : Pair<List<T>, HashSet<int>> { }
                public sealed class ArrayAndList<T> : Pair<T[], List<int>> { }
                public sealed class Ranks<T> : Pair<T[], int[,]> { }
                public sealed class ItselfInside<T> : Pair<T, List<T>> { }
                public sealed class Outer<T> { public sealed class Inner { } }
                public sealed class OuterArguments<T> : Pair<Outer<T>.Inner, Outer<int>.Inner> where T : class { }

                public sealed class StructAndClass<T> : Apart<KeyValuePair<T, int>, List<int>> { }
                public sealed class TwoStructs<T> : Apart<KeyValuePair<T, int>, KeyValuePair<string, string>> { }
                public sealed class ClassChains<T> : Apart<List<T>, Handler> { }
                public sealed class ArrayAndClass<T> : Apart<T[], Handler> { }

                public sealed class TwoArrays<T> : Apart<T[], {|#6:object[]|}> { }
                public sealed class TwoDelegates<T> : Apart<Func<T>, {|#7:Func<object>|}> { }
                public sealed class StructAndObject<T> : Apart<KeyValuePair<T, int>, {|#8:object|}> { }
                public sealed class OneDefinition<T> : Apart<List<T>, {|#9:List<string>|}> { }
                public sealed class TwoStructsOneUse<T> : Apart<KeyValuePair<T, int>, {|#10:KeyValuePair<string, int>|}> { }
            }
            """,
            Cmtk0010(0, "List<int>", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'List<T>', the type argument for 'TLeft'"),
            Cmtk0010(1, "int[]", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T[]', the type argument for 'TLeft'"),
            Cmtk0010(2, "List<TB>", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'List<TA>', the type argument for 'TLeft'"),
            Cmtk0010(3, "List<int>", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'IEnumerable<T>', the type argument for 'TLeft'"),
            Cmtk0010(4, "List<T>", "TRight", "Pair<TLeft, TRight>", "is the same type as the argument for 'TLeft'"),
            Cmtk0010(5, "Dictionary<string, List<int>>", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'Dictionary<string, List<T>>', the type argument for 'TLeft'"),
            Cmtk0010(6, "object[]", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'T[]', the type argument for 'TLeft'"),
            Cmtk0010(7, "Func<object>", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'Func<T>', the type argument for 'TLeft'"),
            Cmtk0010(8, "object", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'KeyValuePair<T, int>', the type argument for 'TLeft'"),
            Cmtk0010(9, "List<string>", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'List<T>', the type argument for 'TLeft'"),
            Cmtk0010(10, "KeyValuePair<string, int>", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'KeyValuePair<T, int>', the type argument for 'TLeft'"),
            Cmtk0010(11, "List<U>", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T', the type argument for 'TLeft'"),
            Cmtk0010(12, "KeyValuePair<U, int>", "TRight", "Pair<TLeft, TRight>", "is not proved a different type from 'T', the type argument for 'TLeft'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A pair of one <c>[Unrelated]</c> and one <c>[Distinct]</c> is held to the stricter at its closed uses,
    /// but forwards only what both declare: into a <c>[Distinct]</c> pair it is proved, into an
    /// <c>[Unrelated]</c> one it is not, since the generic's own closed uses would not check the relation
    /// there if the stricter level were not on both. Conservative on purpose, and documented.
    /// </summary>
    [Fact]
    public Task A_mixed_pair_is_held_to_the_stricter_at_closed_uses_and_forwards_only_what_both_declare() =>
        Test(
            """
            public sealed class IntoDistinct<[Unrelated] TA, [Distinct] TB> : Pair<TA, TB> { }
            public sealed class IntoUnrelated<[Unrelated] TA, [Distinct] TB> : Apart<TA, {|#0:TB|}> { }

            public static class Uses
            {
                public static IntoDistinct<Handler, {|#1:OrderHandler|}> Related;
                public static IntoDistinct<Handler, {|#2:Handler|}> Same;
                public static IntoDistinct<Handler, Command> Fine;
            }
            """,
            Cmtk0010(0, "TB", "TRight", "Apart<TLeft, TRight>", "is not proved unrelated to 'TA', the type argument for 'TLeft'"),
            Cmtk0010(1, "OrderHandler", "TB", "IntoDistinct<TA, TB>", "derives from, implements, boxes to or is a base of 'Handler', the type argument for 'TA'"),
            Cmtk0010(2, "Handler", "TB", "IntoDistinct<TA, TB>", "is the same type as the argument for 'TA'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The base package's own use: a fault of several types is of different types, or every conversion and
    /// <c>Of</c> would be ambiguous. A generic that forwards its parameters into one is held to the same, and
    /// <c>struct</c> against <c>Enum</c> is no proof: <c>Forward&lt;DayOfWeek, DayOfWeek&gt;</c> passed (reported 2026-10-14).
    /// </summary>
    [Fact]
    public Task A_fault_of_several_types_declares_them_distinct() =>
        Test(
            """
            public static class Faults
            {
                public static Fault<DayOfWeek, {|#0:DayOfWeek|}> Twice;
                public static Fault<DayOfWeek, ConsoleColor, {|#1:DayOfWeek|}> Thrice;
                public static Fault<DayOfWeek, ConsoleColor> Fine;
                public static Fault<DayOfWeek, ConsoleColor, StringComparison> AlsoFine;
            }

            public sealed class Forward<TS, TE> where TS : struct where TE : Enum
            {
                public Fault<TS, {|#2:TE|}> Held;
            }
            """,
            Cmtk0010(0, "DayOfWeek", "T2", "Fault<T1, T2>", "is the same type as the argument for 'T1'"),
            Cmtk0010(1, "DayOfWeek", "T3", "Fault<T1, T2, T3>", "is the same type as the argument for 'T1'"),
            Cmtk0010(2, "TE", "T2", "Fault<T1, T2>", "is not proved a different type from 'TS', the type argument for 'T1'"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Six more unary constraints. An interface, not a class implementing one; a class that can be derived
    /// from, abstract included, not an interface, a sealed or a static class, nor a struct; a value object
    /// through any interface, struct or class, never an interface, even the marker or one deriving from it
    /// (one was accepted and the other refused, reported 2026-10-14); a closed set, a fault set included; an enum marked
    /// <c>[Flags]</c>, not a plain one; a readonly struct or readonly record struct, not a mutable struct, an
    /// enum or a class. The primitives are readonly structs. A static class as a type argument is the
    /// compiler's error (CS0718), never this rule's.
    /// </summary>
    [Fact]
    public Task Interface_Unsealed_ValueObject_ClosedSet_FlagsEnum_and_ReadOnlyStruct_accept_and_refuse_what_they_say() =>
        Test(
            """
            public static class Uses
            {
                public static Proxy<IContract> Contract;
                public static Proxy<{|#0:Logged|}> Implementer;
                public static Proxy<{|#1:Handler|}> AbstractClass;

                public static Derivable<Handler> Abstract;
                public static Derivable<Tracked> Open;
                public static Derivable<Command> OpenRecord;
                public static Derivable<{|#2:OrderHandler|}> Sealed;
                public static Derivable<{|#3:IContract|}> Interface;
                public static Derivable<{|#5:Point|}> Struct;

                public static Keyed<Id> ValueStruct;
                public static Keyed<IdClass> ValueClass;
                public static Keyed<{|#6:Point|}> PlainStruct;
                public static Keyed<{|#7:Colour|}> ClosedSetIsNoValueObject;
                public static Keyed<{|#15:CodoMetis.TypeKit.ValueObjects.IValue<int>|}> TheMarker;
                public static Keyed<{|#16:IDerivedValue|}> ADerivedInterface;

                public static Named<Colour> Set;
                public static Named<Refusal> FaultSet;
                public static Named<{|#8:Id|}> ValueObjectIsNoClosedSet;
                public static Named<{|#9:Plain|}> EnumIsNoClosedSet;

                public static Flagged<Rights> Flags;
                public static Flagged<{|#10:Plain|}> PlainEnum;
                public static Flagged<{|#11:int|}> NotAnEnum;

                public static Pinned<Frozen> ReadOnly;
                public static Pinned<FrozenRecord> ReadOnlyRecord;
                public static Pinned<int> Primitive;
                public static Pinned<{|#12:Mutable|}> MutableStruct;
                public static Pinned<{|#13:Plain|}> Enum;
                public static Pinned<{|#14:OrderHandler|}> Class;
            }

            public interface IDerivedValue : CodoMetis.TypeKit.ValueObjects.IValue<int> { }
            """,
            Cmtk0010(0, "Logged", "T", "Proxy<T>", "is not an interface"),
            Cmtk0010(1, "Handler", "T", "Proxy<T>", "is not an interface"),
            Cmtk0010(2, "OrderHandler", "T", "Derivable<T>", "is not a class that can be derived from"),
            Cmtk0010(3, "IContract", "T", "Derivable<T>", "is not a class that can be derived from"),
            Cmtk0010(5, "Point", "T", "Derivable<T>", "is not a class that can be derived from"),
            Cmtk0010(6, "Point", "T", "Keyed<T>", "is not a value object (IValue<T>)"),
            Cmtk0010(7, "Colour", "T", "Keyed<T>", "is not a value object (IValue<T>)"),
            Cmtk0010(15, "IValue<int>", "T", "Keyed<T>", "is not a value object (IValue<T>)"),
            Cmtk0010(16, "IDerivedValue", "T", "Keyed<T>", "is not a value object (IValue<T>)"),
            Cmtk0010(8, "Id", "T", "Named<T>", "is not a closed set (IClosedSet<TSelf>)"),
            Cmtk0010(9, "Plain", "T", "Named<T>", "is not a closed set (IClosedSet<TSelf>)"),
            Cmtk0010(10, "Plain", "T", "Flagged<T>", "is not an enum marked [Flags]"),
            Cmtk0010(11, "int", "T", "Flagged<T>", "is not an enum marked [Flags]"),
            Cmtk0010(12, "Mutable", "T", "Pinned<T>", "is not a readonly struct"),
            Cmtk0010(13, "Plain", "T", "Pinned<T>", "is not a readonly struct"),
            Cmtk0010(14, "OrderHandler", "T", "Pinned<T>", "is not a readonly struct"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A bound to the marker interface, to a type implementing it, or to a parameter so bound proves
    /// <c>[ValueObject]</c> and <c>[ClosedSet]</c>, for <c>[ValueObject]</c> only beside a constraint no interface
    /// meets, <c>struct</c>, <c>unmanaged</c>, <c>new()</c> or a class: <c>where T : IValue&lt;int&gt;</c>, and
    /// <c>class</c> beside it, admit <c>IValue&lt;int&gt;</c> itself (reported 2026-10-14). Nothing but the attribute proves the other four: an
    /// interface bound admits its implementers, <c>class</c> a sealed class, <c>Enum</c> a plain enum,
    /// <c>struct</c> a mutable one.
    /// </summary>
    [Fact]
    public Task A_marker_bound_proves_ValueObject_and_ClosedSet_and_nothing_proves_the_other_four() =>
        Test(
            """
            public static class Proven
            {
                public static Keyed<T> BoundToTheMarkerAsAStruct<T>() where T : struct, CodoMetis.TypeKit.ValueObjects.IValue<int> => new();
                public static Keyed<T> BoundToTheMarkerAsUnmanaged<T>() where T : unmanaged, CodoMetis.TypeKit.ValueObjects.IValue<int> => new();
                public static Keyed<T> BoundToTheMarkerWithAConstructor<T>() where T : CodoMetis.TypeKit.ValueObjects.IValue<int>, new() => new();
                public static Keyed<T> BoundToTheMarkerAndAClass<T>() where T : Handler, CodoMetis.TypeKit.ValueObjects.IValue<int> => new();
                public static Keyed<T> BoundToAnImplementer<T>() where T : IdClass => new();
                public static Keyed<T> BoundThroughAParameter<T, U>() where T : U where U : IdClass => new();
                public static Keyed<T> BoundThroughAClassBoundParameter<T, U>() where T : U where U : Handler, CodoMetis.TypeKit.ValueObjects.IValue<int> => new();
                public static Keyed<T> Carried<[ValueObject] T>() => new();
                public static Named<T> BoundToTheSetMarker<T>() where T : class, CodoMetis.TypeKit.ClosedSets.IClosedSet<T> => new();
                public static Named<T> BoundToAFaultSet<T>() where T : class, CodoMetis.TypeKit.ClosedSets.IFaultSet<T> => new();
                public static Named<T> BoundToASet<T>() where T : Colour => new();
                public static Proxy<T> CarriedInterface<[Interface] T>() => new();
                public static Derivable<T> CarriedUnsealed<[Unsealed] T>() => new();
                public static Flagged<T> CarriedFlags<[FlagsEnum] T>() => new();
                public static Pinned<T> CarriedReadOnly<[ReadOnlyStruct] T>() => new();
            }

            public static class Unproven
            {
                public static Keyed<{|#0:T|}> AnyStruct<T>() where T : struct => new();
                public static Named<{|#1:T|}> AnyClass<T>() where T : class => new();
                public static Proxy<{|#2:T|}> BoundToAnInterface<T>() where T : IContract => new();
                public static Derivable<{|#3:T|}> ClassAdmitsSealed<T>() where T : class => new();
                public static Derivable<{|#4:T|}> BoundToAClass<T>() where T : Handler => new();
                public static Flagged<{|#5:T|}> AnyEnum<T>() where T : struct, Enum => new();
                public static Pinned<{|#6:T|}> StructAdmitsMutable<T>() where T : struct => new();
                public static Pinned<{|#7:T|}> UnmanagedAdmitsMutable<T>() where T : unmanaged => new();
                public static Keyed<{|#8:T|}> BoundToTheMarker<T>() where T : CodoMetis.TypeKit.ValueObjects.IValue<int> => new();
                public static Keyed<{|#9:T|}> ClassAdmitsTheMarker<T>() where T : class, CodoMetis.TypeKit.ValueObjects.IValue<int> => new();
                public static Keyed<{|#10:T|}> BoundToADerivedInterface<T>() where T : IDerivedValue => new();
                public static Keyed<{|#11:T|}> BoundThroughAnInterfaceParameter<T, U>() where T : U where U : CodoMetis.TypeKit.ValueObjects.IValue<int> => new();
            }

            public interface IDerivedValue : CodoMetis.TypeKit.ValueObjects.IValue<int> { }
            """,
            Cmtk0010(0, "T", "T", "Keyed<T>", "is a type parameter that does not carry [ValueObject]"),
            Cmtk0010(1, "T", "T", "Named<T>", "is a type parameter that does not carry [ClosedSet]"),
            Cmtk0010(2, "T", "T", "Proxy<T>", "is a type parameter that does not carry [Interface]"),
            Cmtk0010(3, "T", "T", "Derivable<T>", "is a type parameter that does not carry [Unsealed]"),
            Cmtk0010(4, "T", "T", "Derivable<T>", "is a type parameter that does not carry [Unsealed]"),
            Cmtk0010(5, "T", "T", "Flagged<T>", "is a type parameter that does not carry [FlagsEnum]"),
            Cmtk0010(6, "T", "T", "Pinned<T>", "is a type parameter that does not carry [ReadOnlyStruct]"),
            Cmtk0010(7, "T", "T", "Pinned<T>", "is a type parameter that does not carry [ReadOnlyStruct]"),
            Cmtk0010(8, "T", "T", "Keyed<T>", "is a type parameter that does not carry [ValueObject]"),
            Cmtk0010(9, "T", "T", "Keyed<T>", "is a type parameter that does not carry [ValueObject]"),
            Cmtk0010(10, "T", "T", "Keyed<T>", "is a type parameter that does not carry [ValueObject]"),
            Cmtk0010(11, "T", "T", "Keyed<T>", "is a type parameter that does not carry [ValueObject]"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<TypeArgumentConstraintAnalyzer>("public sealed class Box<T> { } public static class U { public static Box<int> B; }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
