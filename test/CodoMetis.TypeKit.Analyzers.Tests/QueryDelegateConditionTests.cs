using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0009's query condition: <c>FirstOrNone</c>, <c>SingleOrNone</c>, <c>FirstOrError</c> and the Last
/// forms called on a query with a delegate for a condition bind to their sequence overloads, which read
/// every row and run the condition in memory.
/// </summary>
/// <remarks>
/// The oracle is the compiler's overload resolution, stated per shape in <see cref="QueryDelegateShapes"/>:
/// a condition that converts to a delegate alone, on a receiver that is a query, is reported once, at
/// the call; every other call that compiles is silent.
/// </remarks>
public sealed class QueryDelegateConditionTests
{
    private const string AsExpression = "Write the condition as a lambda at the call, or hold it as an Expression<Func<Shipment, bool>>, so that the query runs it, or call AsEnumerable() first to read every row on purpose";

    private static DiagnosticResult Cmtk0009(int location, string method, string advice = AsExpression) =>
        new DiagnosticResult("CMTK0009", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithMessage($"'{method}' on the query binds to its IEnumerable<T> overload, since the condition is a 'Func<Shipment, bool>' and not an expression: it reads every row and runs the condition in memory. {advice}.");

    private const string Shipment =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using CodoMetis.TypeKit;

        public sealed class Shipment { public string Carrier { get; set; } = ""; }

        """;

    [Fact]
    public Task Each_lookup_says_which_overload_it_fell_to_and_what_to_write_instead() =>
        Run(
            Shipment +
            """
            public static class C
            {
                private static bool IsGamma(Shipment s) => s.Carrier == "gamma";

                public static void Run(IQueryable<Shipment> shipments)
                {
                    Func<Shipment, bool> isBeta = s => s.Carrier == "beta";

                    _ = {|#0:shipments.FirstOrNone(isBeta)|};
                    _ = {|#1:shipments.SingleOrNone(IsGamma)|};
                    _ = {|#2:shipments.FirstOrError(isBeta, "none")|};
                    _ = {|#3:shipments.LastOrNone(isBeta)|};
                    _ = {|#4:shipments.LastOrError(isBeta, "none")|};
                }
            }
            """,
            Cmtk0009(0, "FirstOrNone"),
            Cmtk0009(1, "SingleOrNone"),
            Cmtk0009(2, "FirstOrError"),
            Cmtk0009(3, "LastOrNone", "A query has no last element until it is ordered: order it the other way and use FirstOrNone with the condition as a lambda, or call AsEnumerable() first to read every row on purpose"),
            Cmtk0009(4, "LastOrError", "A query has no last element until it is ordered: order it the other way and use FirstOrError with the condition as a lambda, or call AsEnumerable() first to read every row on purpose"));

    [Fact]
    public Task A_lambda_an_expression_and_a_sequence_are_silent() =>
        Run(
            Shipment +
            """
            public static class C
            {
                public static void Run(IQueryable<Shipment> shipments, List<Shipment> list)
                {
                    Expression<Func<Shipment, bool>> isAlpha = s => s.Carrier == "alpha";
                    Func<Shipment, bool> isBeta = s => s.Carrier == "beta";

                    _ = shipments.FirstOrNone(s => s.Carrier == "beta");
                    _ = shipments.SingleOrNone(isAlpha);
                    _ = shipments.FirstOrError(isAlpha, "none");
                    _ = list.FirstOrNone(isBeta);
                    _ = shipments.AsEnumerable().FirstOrNone(isBeta);
                }
            }
            """);

    /// <summary>EF Core's <c>DbSet</c>, the receiver the EF README's warning is about, and <c>Set&lt;T&gt;()</c>.</summary>
    [Fact]
    public Task A_DbSet_is_a_query()
    {
        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>(
            "using Microsoft.EntityFrameworkCore;\n" + Shipment +
            """
            public static class C
            {
                public static void Run(DbSet<Shipment> shipments, DbContext db)
                {
                    Func<Shipment, bool> isBeta = s => s.Carrier == "beta";

                    _ = {|#0:shipments.FirstOrNone(isBeta)|};
                    _ = {|#1:db.Set<Shipment>().SingleOrNone(isBeta)|};
                    _ = shipments.FirstOrNone(s => s.Carrier == "beta");
                }
            }
            """);
        test.TestState.AdditionalReferences.AddRange(RealEntityFrameworkCore.References);
        test.ExpectedDiagnostics.AddRange([Cmtk0009(0, "FirstOrNone"), Cmtk0009(1, "SingleOrNone")]);

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    public static TheoryData<string> Receivers() => [.. QueryDelegateShapes.Receivers.Select(receiver => receiver.Name)];

    /// <summary>Every receiver against every condition and lookup, over one element type.</summary>
    [Theory]
    [MemberData(nameof(Receivers))]
    public Task Every_receiver_against_every_condition(string receiver) =>
        Sweep(QueryDelegateShapes.Receivers.Single(candidate => candidate.Name == receiver), "Widget");

    public static TheoryData<string> Kinds() => [.. TypeArgumentKinds.All.Select(kind => kind.Name)];

    /// <summary>Every kind of element, on a plain query, against every condition and lookup.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public Task Every_kind_of_element(string kind)
    {
        var element = TypeArgumentKinds.All.Single(candidate => candidate.Name == kind);

        return Sweep(QueryDelegateShapes.Receivers[0], element.Argument, element.Parameters, element.Where);
    }

    private static Task Sweep(QueryReceiver receiver, string element, string parameters = "", string where = "")
    {
        var (source, expected) = QueryDelegateShapes.Use(receiver, element, parameters, where);

        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>(TypeArgumentKinds.Candidates);
        test.TestBehaviors = VariantSweep.Behaviors;
        test.TestState.Sources.Add(("Declarations.cs", QueryDelegateShapes.Declarations));
        test.TestState.Sources.Add(("Use.cs", source));
        test.ExpectedDiagnostics.AddRange(expected.Select(call => new DiagnosticResult("CMTK0009", DiagnosticSeverity.Warning).WithLocation(call.Location)));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static Task Run(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>(code);
        test.ExpectedDiagnostics.AddRange(expected);

        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
