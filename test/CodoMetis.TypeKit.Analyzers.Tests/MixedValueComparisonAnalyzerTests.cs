using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>CMTK0008: the wrapped values of two different value objects compared.</summary>
public sealed class MixedValueComparisonAnalyzerTests
{
    /// <summary>
    /// Value objects declared by hand, with <c>Value</c> and companions as the generators give them.
    /// <c>Unwoven</c> has no <c>Value</c>, which is how a value object of the same project looks where
    /// Metalama runs analyzers.
    /// </summary>
    private const string Subjects =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using CodoMetis.TypeKit.ValueObjects;

        public readonly struct OrderId : IValue<Guid> { public Guid Value => default; }
        public readonly struct CustomerId : IValue<Guid> { public Guid Value => default; }
        public sealed class Label : IValue<string> { public string Value => ""; }
        public sealed class Title : IValue<string> { public string Value => ""; }
        public readonly struct Unwoven : IValue<Guid> { }
        public readonly struct AlsoUnwoven : IValue<Guid> { }
        public interface IIdentifier : IValue<Guid> { }

        public static class Companions
        {
            public static Guid GetValue(this OrderId? id) => id!.Value.Value;
            public static Guid? ValueOrNull(this CustomerId? id) => id?.Value;
        }

        """;

    private static AnalyzerTest<MixedValueComparisonAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<MixedValueComparisonAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    /// <summary>The values of two value objects compared.</summary>
    private static DiagnosticResult Cmtk0008(int location, string left, string right) =>
        new DiagnosticResult("CMTK0008", DiagnosticSeverity.Warning).WithLocation(location).WithArguments($"the value of a '{left}'", $"the value of a '{right}'");

    /// <summary>Two value objects themselves compared through <c>Equals(object)</c>.</summary>
    private static DiagnosticResult Cmtk0008Objects(int location, string left, string right) =>
        new DiagnosticResult("CMTK0008", DiagnosticSeverity.Warning).WithLocation(location).WithArguments($"a '{left}'", $"a '{right}'");

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new MixedValueComparisonAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0008");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Warning);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task Comparing_the_values_of_two_value_objects_reports() =>
        Test(
            "public static class C { public static bool Same(OrderId order, CustomerId customer) => {|#0:order.Value == customer.Value|}; }",
            new DiagnosticResult("CMTK0008", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("This compares the value of a 'OrderId' with the value of a 'CustomerId', two different value objects, which their types exist to keep apart. Compare two of one type, or convert one explicitly if they really share an identity."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Every_comparison_and_every_way_of_reading_the_value_reports() =>
        Test(
            """
            public static class C
            {
                public static void Run(OrderId order, CustomerId customer, Label label, Title? title, OrderId? maybeOrder, CustomerId? maybeCustomer)
                {
                    var notEqual = {|#0:order.Value != customer.Value|};
                    var ordered = {|#1:order.Value.CompareTo(customer.Value)|} < 0;
                    var equals = {|#2:order.Value.Equals(customer.Value)|};
                    var conditional = {|#3:label.Value == title?.Value|};
                    var companions = {|#4:maybeOrder.GetValue() == maybeCustomer.ValueOrNull()|};
                    var parenthesized = {|#5:(order.Value) == (customer.Value)|};
                    var strings = string.CompareOrdinal(label.Value, "") < 0 || {|#6:label.Value.CompareTo(title!.Value)|} > 0;
                }

                public static readonly Expression<Func<OrderId, CustomerId, bool>> InAQuery = (o, c) => {|#7:o.Value == c.Value|};

                public static bool Unbound(Unwoven u, AlsoUnwoven a) => {|#8:u.{|CS1061:Value|} == a.{|CS1061:Value|}|};
            }
            """,
            Cmtk0008(0, "OrderId", "CustomerId"),
            Cmtk0008(1, "OrderId", "CustomerId"),
            Cmtk0008(2, "OrderId", "CustomerId"),
            Cmtk0008(3, "Label", "Title"),
            Cmtk0008(4, "OrderId", "CustomerId"),
            Cmtk0008(5, "OrderId", "CustomerId"),
            Cmtk0008(6, "Label", "Title"),
            Cmtk0008(7, "OrderId", "CustomerId"),
            Cmtk0008(8, "Unwoven", "AlsoUnwoven"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The static and two-argument comparisons, into which analyzers such as Meziantou's MA0006 rewrite
    /// <c>==</c>: each silenced the rule.
    /// </summary>
    [Fact]
    public Task Static_and_two_argument_comparisons_report() =>
        Test(
            """
            public static class C
            {
                public static void Run(OrderId order, CustomerId customer, Label label, Title title)
                {
                    var ordinal = {|#0:string.Equals(label.Value, title.Value, StringComparison.Ordinal)|};
                    var plain = {|#1:string.Equals(label.Value, title.Value)|};
                    var ignoringCase = {|#2:label.Value.Equals(title.Value, StringComparison.OrdinalIgnoreCase)|};
                    var objects = {|#3:object.Equals(order.Value, customer.Value)|};
                    var inherited = {|#4:Equals(order.Value, customer.Value)|};
                    var comparer = {|#5:EqualityComparer<Guid>.Default.Equals(order.Value, customer.Value)|};
                    var compared = {|#6:string.Compare(label.Value, title.Value)|} < 0;
                    var ordered = {|#7:string.CompareOrdinal(label.Value, title.Value)|} < 0;
                    var cultured = {|#8:string.Compare(label.Value, title.Value, StringComparison.CurrentCulture)|} < 0;
                }
            }
            """,
            Cmtk0008(0, "Label", "Title"),
            Cmtk0008(1, "Label", "Title"),
            Cmtk0008(2, "Label", "Title"),
            Cmtk0008(3, "OrderId", "CustomerId"),
            Cmtk0008(4, "OrderId", "CustomerId"),
            Cmtk0008(5, "OrderId", "CustomerId"),
            Cmtk0008(6, "Label", "Title"),
            Cmtk0008(7, "Label", "Title"),
            Cmtk0008(8, "Label", "Title"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Two value objects of different types, not unwrapped, compile through <c>Equals(object)</c> and
    /// are never equal: the same bug.
    /// </summary>
    [Fact]
    public Task Two_different_value_objects_compared_through_Equals_report() =>
        Test(
            """
            public static class C
            {
                public static void Run(OrderId order, CustomerId customer, OrderId? maybe, Label label, Title title)
                {
                    var instance = {|#0:order.Equals(customer)|};
                    var inherited = {|#1:Equals(order, customer)|};
                    var objects = {|#2:object.Equals(order, customer)|};
                    var nullable = {|#3:maybe.Equals(customer)|};
                    var classes = {|#4:label.Equals(title)|};
                }
            }
            """,
            Cmtk0008Objects(0, "OrderId", "CustomerId"),
            Cmtk0008Objects(1, "OrderId", "CustomerId"),
            Cmtk0008Objects(2, "OrderId", "CustomerId"),
            Cmtk0008Objects(3, "OrderId", "CustomerId"),
            Cmtk0008Objects(4, "Label", "Title"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Rows that hold value objects, as entities do, for the joins.</summary>
    private const string Rows =
        """
        public sealed class OrderRow
        {
            public OrderId Id { get; set; }
            public CustomerId CustomerId { get; set; }
            public OrderId? Previous { get; set; }
            public Label? Tag { get; set; }
        }

        public sealed class CustomerRow
        {
            public CustomerId Id { get; set; }
            public CustomerId? Referrer { get; set; }
            public Title? Heading { get; set; }
        }

        """;

    /// <summary>
    /// A join is an equality of its two keys. <c>on o.CustomerId equals c.Id</c> does not compile for two
    /// value objects of different types, which is the point of the types; unwrapping both keys compiles and
    /// joins an order id to a customer id. Each form was silent.
    /// </summary>
    [Fact]
    public Task A_query_join_on_the_values_of_two_value_objects_reports() =>
        Test(
            Rows +
            """
            public static class C
            {
                public static void Run(IQueryable<OrderRow> orders, IQueryable<CustomerRow> customers, List<OrderRow> list, List<CustomerRow> people)
                {
                    var joined = from o in orders join c in customers on {|#0:o.Id.Value equals c.Id.Value|} select o;
                    var grouped = from o in list join c in people on {|#1:o.Id.Value equals c.Id.Value|} into matches select matches;
                    var typed = from o in list join CustomerRow c in people on {|#2:o.Id.Value equals c.Id.Value|} select o;
                    var companions = from o in list join c in people on {|#3:o.Previous.GetValue() equals c.Referrer.ValueOrNull()|} select o;
                    var conditional = from o in list join c in people on {|#4:o.Tag?.Value equals c.Heading?.Value|} select o;
                    var parenthesized = from o in list join c in people on {|#5:(o.Id.Value) equals (c.Id.Value)|} select o;
                }
            }
            """,
            Cmtk0008(0, "OrderId", "CustomerId"),
            Cmtk0008(1, "OrderId", "CustomerId"),
            Cmtk0008(2, "OrderId", "CustomerId"),
            Cmtk0008(3, "OrderId", "CustomerId"),
            Cmtk0008(4, "Label", "Title"),
            Cmtk0008(5, "OrderId", "CustomerId"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// <c>Join</c>, <c>GroupJoin</c> and .NET 10's <c>LeftJoin</c> and <c>RightJoin</c>, on <c>Enumerable</c>,
    /// <c>Queryable</c> and <c>AsyncEnumerable</c>, called as extensions, statically or with named arguments:
    /// the two key selectors compare what they return. Each was silent. The outer key is named first.
    /// </summary>
    [Fact]
    public Task A_Join_method_on_the_values_of_two_value_objects_reports() =>
        Test(
            Rows +
            """
            public static class C
            {
                public static void Run(IQueryable<OrderRow> orders, IQueryable<CustomerRow> customers, List<OrderRow> list, List<CustomerRow> people,
                                       IAsyncEnumerable<OrderRow> stream, IAsyncEnumerable<CustomerRow> others)
                {
                    var streamed = stream.Join(others, {|#9:o => o.Id.Value, c => c.Id.Value|}, (o, c) => o);
                    var joined = orders.Join(customers, {|#0:o => o.Id.Value, c => c.Id.Value|}, (o, c) => o);
                    var grouped = list.GroupJoin(people, {|#1:o => o.Id.Value, c => c.Id.Value|}, (o, cs) => o);
                    var left = list.LeftJoin(people, {|#2:o => o.Id.Value, c => c.Id.Value|}, (o, c) => o);
                    var right = orders.RightJoin(customers, {|#3:o => o.Id.Value, c => c.Id.Value|}, (o, c) => c);
                    var queryGrouped = orders.GroupJoin(customers, {|#4:o => o.Id.Value, c => c.Id.Value|}, (o, cs) => o);
                    var staticForm = Enumerable.Join(list, people, {|#5:o => o.Id.Value, c => c.Id.Value|}, (o, c) => o);
                    var named = list.Join(people, resultSelector: (o, c) => o, innerKeySelector: {|#6:c => c.Id.Value, outerKeySelector: o => o.Id.Value|});
                    var compared = list.Join(people, {|#7:o => o.Tag!.Value, c => c.Heading!.Value|}, (o, c) => o, StringComparer.Ordinal);
                    var companions = list.Join(people, {|#8:(OrderRow o) => o.Previous.GetValue(), (CustomerRow c) => c.Referrer.ValueOrNull()|}, (o, c) => o);
                }
            }
            """,
            Cmtk0008(0, "OrderId", "CustomerId"),
            Cmtk0008(1, "OrderId", "CustomerId"),
            Cmtk0008(2, "OrderId", "CustomerId"),
            Cmtk0008(3, "OrderId", "CustomerId"),
            Cmtk0008(4, "OrderId", "CustomerId"),
            Cmtk0008(5, "OrderId", "CustomerId"),
            Cmtk0008(6, "OrderId", "CustomerId"),
            Cmtk0008(7, "Label", "Title"),
            Cmtk0008(8, "OrderId", "CustomerId"),
            Cmtk0008(9, "OrderId", "CustomerId"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A value object of the same project has no <c>Value</c> where Metalama runs analyzers (CS1061), so a
    /// <c>Join</c> given it does not bind either; its candidates still say where the key selectors are.
    /// </summary>
    [Fact]
    public Task A_join_on_unbound_values_reports() =>
        Test(
            """
            public sealed class UnwovenRow { public Unwoven Id { get; set; } }
            public sealed class AlsoUnwovenRow { public AlsoUnwoven Id { get; set; } }

            public static class C
            {
                public static void Run(List<UnwovenRow> left, List<AlsoUnwovenRow> right)
                {
                    var joined = left.Join(right, {|#0:u => u.Id.{|CS1061:Value|}, a => a.Id.{|CS1061:Value|}|}, (u, a) => u);
                    var query = from u in left join a in right on {|#1:u.Id.{|CS1061:Value|} equals a.Id.{|CS1061:Value|}|} select u;
                }
            }
            """,
            Cmtk0008(0, "Unwoven", "AlsoUnwoven"),
            Cmtk0008(1, "Unwoven", "AlsoUnwoven"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A join of one type's values, of a value against a raw value, through a cast, or on the value objects
    /// themselves stays silent, and so does a method named <c>Join</c> that is not LINQ's.
    /// </summary>
    [Fact]
    public Task A_join_of_one_type_a_raw_value_or_a_cast_stays_silent() =>
        Test(
            Rows +
            """
            public static class Mine
            {
                public static IEnumerable<TOuter> Join<TOuter, TInner>(this IEnumerable<TOuter> outer, IEnumerable<TInner> inner, Func<TOuter, Guid> first, Func<TInner, Guid> second) => outer;
            }

            public static class C
            {
                public static void Run(List<OrderRow> list, List<CustomerRow> people, List<Guid> raw, Guid[] ids)
                {
                    var sameType = from o in list join c in people on o.CustomerId.Value equals c.Id.Value select o;
                    var typed = from o in list join c in people on o.CustomerId equals c.Id select o;
                    var rawValue = from o in list join g in raw on o.Id.Value equals g select o;
                    var cast = from o in list join c in people on (Guid)(object)o.Id.Value equals c.Id.Value select o;
                    var sameTypeMethod = list.Join(people, o => o.CustomerId.Value, c => c.Id.Value, (o, c) => o);
                    var rawMethod = list.GroupJoin(raw, o => o.Id.Value, g => g, (o, gs) => o);
                    var castMethod = list.Join(people, o => (Guid)(object)o.Id.Value, c => c.Id.Value, (o, c) => o);
                    var typedMethod = list.Join(people, o => o.CustomerId, c => c.Id, (o, c) => o);
                    var notLinq = list.Join(people, o => o.Id.Value, c => c.Id.Value);
                    var text = string.Join(",", ids);
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// One type, a raw value on one side, and a deliberate cast stay silent in the new forms too, and
    /// so does an interface, which may hold a value object of either type.
    /// </summary>
    [Fact]
    public Task Equals_of_one_type_a_raw_value_or_a_cast_stays_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(OrderId order, OrderId other, CustomerId customer, Label label, Guid raw, IIdentifier identifier, object unknown)
                {
                    var sameType = order.Equals(other) || Equals(order, other) || string.Equals(label.Value, label.Value, StringComparison.Ordinal);
                    var rawValue = order.Equals(raw) || object.Equals(order.Value, raw) || string.Compare(label.Value, "x") == 0;
                    var cast = order.Equals((object)customer) || Equals((object)order, customer) || object.Equals((Guid)(object)order.Value, customer.Value);
                    var anInterface = identifier.Equals(order) || Equals(identifier, customer);
                    var castToAValueObject = order.Equals((CustomerId)unknown) || Equals(((CustomerId)unknown), order);
                    var notAComparison = string.Concat(label.Value, label.Value);
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>One type, a raw value, a deliberate cast, a <c>Nullable</c>'s own <c>Value</c>, and a <c>Value</c> that is not a value object's.</summary>
    [Fact]
    public Task One_type_raw_values_casts_and_other_Values_stay_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(OrderId order, OrderId other, CustomerId customer, OrderId? maybe, Lazy<Guid> lazy, Lazy<Guid> lazier)
                {
                    var sameType = order.Value == other.Value;
                    var raw = order.Value == Guid.Empty;
                    var cast = (Guid)(object)order.Value == customer.Value;
                    var nullable = maybe!.Value.Value == order.Value;
                    var lazies = lazy.Value == lazier.Value;
                    var mixedWithRaw = customer.Value.Equals(lazy.Value);
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<MixedValueComparisonAnalyzer>("public static class S { public static bool F(System.Lazy<int> a, System.Lazy<int> b) => a.Value == b.Value; }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
