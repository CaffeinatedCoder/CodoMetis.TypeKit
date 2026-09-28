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
        using System.Linq.Expressions;
        using CodoMetis.TypeKit.ValueObjects;

        public readonly struct OrderId : IValue<Guid> { public Guid Value => default; }
        public readonly struct CustomerId : IValue<Guid> { public Guid Value => default; }
        public sealed class Label : IValue<string> { public string Value => ""; }
        public sealed class Title : IValue<string> { public string Value => ""; }
        public readonly struct Unwoven : IValue<Guid> { }
        public readonly struct AlsoUnwoven : IValue<Guid> { }

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

    private static DiagnosticResult Cmtk0008(int location, string left, string right) =>
        new DiagnosticResult("CMTK0008", DiagnosticSeverity.Warning).WithLocation(location).WithArguments(left, right);

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
