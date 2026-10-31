using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0009's outer joins: <c>LeftJoin</c> hands its result selector the <c>default</c> of the inner element
/// for an outer element with no match, <c>RightJoin</c> the outer element's, and .NET 11's <c>FullJoin</c>
/// either, on <c>Enumerable</c>, <c>Queryable</c> and <c>AsyncEnumerable</c>.
/// </summary>
/// <remarks>
/// .NET 10's reference assemblies have no <c>FullJoin</c> and no join that returns tuples, so the tests of those
/// compile against .NET 11's (<see cref="Net110"/>), and the rest against .NET 10's, as every analyzer test does.
/// The element is judged as CMTK0001 judges a <c>default</c>, except an <c>Option</c>, whose default is
/// <c>None</c>: the way out the message names.
/// </remarks>
public sealed class OuterJoinDefaultTests
{
    private const string Subjects =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        public enum Fault { Refused }

        public readonly struct Plain : IValue<int> { }

        public readonly struct Validated : IValidatedValue<Validated, int, Fault>
        {
            public static Result<Validated, Fault> Create(int value) => Result<Validated, Fault>.Error(Fault.Refused);
        }

        [RequireCustomInitialization]
        public struct Guarded { public int N; }

        public struct Free { public int N; }

        public sealed class Row { public int Key { get; set; } }

        """;

    /// <summary>
    /// .NET 11's reference assemblies, 11.0.0-rc.3, which declare <c>FullJoin</c> and the joins that return
    /// tuples, resolved from NuGet as <see cref="ReferenceAssemblies.Net.Net100"/> is.
    /// </summary>
    private static readonly ReferenceAssemblies Net110 =
        new("net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0-rc.3.26522.148"), Path.Combine("ref", "net11.0"));

    private static AnalyzerTest<DefaultProducingCallAnalyzer> Test(string code, bool net11 = false, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>(Subjects + code);
        if (net11) test.ReferenceAssemblies = Net110;
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static string Advice(string side, string type, bool query) =>
        query
            ? $"Select a nullable first, as in {side}.Select(x => ({type}?)x), so that no match is null"
            : $"Wrap each {side} element in an Option before the join, as in {side}.Select(Option.Some), so that no match is None";

    /// <summary>The inner element, <c>default</c> for an outer element with no match.</summary>
    private static DiagnosticResult Inner(int location, string method, string type, bool query = false) =>
        new DiagnosticResult("CMTK0009", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithMessage($"'{method}' returns a default '{type}' as the inner element of an outer element with no match, an instance that passed no factory. {Advice("inner", type, query)}.");

    /// <summary>The outer element, <c>default</c> for an inner element with no match.</summary>
    private static DiagnosticResult Outer(int location, string method, string type, bool query = false) =>
        new DiagnosticResult("CMTK0009", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithMessage($"'{method}' returns a default '{type}' as the outer element of an inner element with no match, an instance that passed no factory. {Advice("outer", type, query)}.");

    /// <summary>
    /// .NET 10's <c>LeftJoin</c> and <c>RightJoin</c> on each host, with and without a comparer, with
    /// asynchronous key selectors, statically and with named arguments: the side the join fills is reported.
    /// </summary>
    [Fact]
    public Task Each_outer_join_reports_the_side_it_fills_with_default() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Plain> plains, List<Row> rows, IQueryable<Plain> queryPlains, IQueryable<Row> queryRows,
                                       IAsyncEnumerable<Plain> streamPlains, IAsyncEnumerable<Row> streamRows)
                {
                    var left = {|#0:rows.LeftJoin(plains, r => r.Key, p => 1, (r, p) => p)|};
                    var leftCompared = {|#1:rows.LeftJoin(plains, r => r.Key, p => 1, (r, p) => p, EqualityComparer<int>.Default)|};
                    var right = {|#2:plains.RightJoin(rows, p => 1, r => r.Key, (p, r) => p)|};
                    var rightCompared = {|#3:plains.RightJoin(rows, p => 1, r => r.Key, (p, r) => p, EqualityComparer<int>.Default)|};
                    var queryLeft = {|#4:queryRows.LeftJoin(plains, r => r.Key, p => 1, (r, p) => p)|};
                    var queryRight = {|#5:queryPlains.RightJoin(rows, p => 1, r => r.Key, (p, r) => p)|};
                    var streamLeft = {|#6:streamRows.LeftJoin(streamPlains, r => r.Key, p => 1, (r, p) => p)|};
                    var streamRight = {|#7:streamPlains.RightJoin(streamRows, p => 1, r => r.Key, (p, r) => p)|};
                    var streamAwaited = {|#8:streamRows.LeftJoin(streamPlains, (r, ct) => ValueTask.FromResult(r.Key), (p, ct) => ValueTask.FromResult(1), (r, p, ct) => ValueTask.FromResult(p))|};
                    var staticForm = {|#9:Enumerable.RightJoin(plains, rows, p => 1, r => r.Key, (p, r) => p)|};
                    var named = {|#10:rows.LeftJoin(resultSelector: (r, p) => p, inner: plains, innerKeySelector: p => 1, outerKeySelector: r => r.Key)|};
                    var conditional = plains?{|#11:.RightJoin(rows, p => 1, r => r.Key, (p, r) => p)|};
                }
            }
            """,
            expected:
            [
                Inner(0, "LeftJoin", "Plain"),
                Inner(1, "LeftJoin", "Plain"),
                Outer(2, "RightJoin", "Plain"),
                Outer(3, "RightJoin", "Plain"),
                Inner(4, "LeftJoin", "Plain", query: true),
                Outer(5, "RightJoin", "Plain", query: true),
                Inner(6, "LeftJoin", "Plain"),
                Outer(7, "RightJoin", "Plain"),
                Inner(8, "LeftJoin", "Plain"),
                Outer(9, "RightJoin", "Plain"),
                Inner(10, "LeftJoin", "Plain"),
                Outer(11, "RightJoin", "Plain"),
            ])
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// .NET 11's <c>FullJoin</c> fills either side, each a diagnostic of its own, and the joins that return
    /// tuples fill them as the ones with a result selector do.
    /// </summary>
    [Fact]
    public Task FullJoin_reports_each_side_and_the_tuple_forms_report_as_the_others() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Plain> plains, List<Guarded> guarded, List<Row> rows, IQueryable<Plain> queryPlains, IQueryable<Row> queryRows,
                                       IAsyncEnumerable<Plain> streamPlains, IAsyncEnumerable<Row> streamRows)
                {
                    var both = {|#0:plains.FullJoin(guarded, p => 1, g => 1, (p, g) => 0)|};
                    var outerOnly = {|#1:plains.FullJoin(rows, p => 1, r => r.Key, (p, r) => 0)|};
                    var innerOnly = {|#2:rows.FullJoin(plains, r => r.Key, p => 1, (r, p) => 0, EqualityComparer<int>.Default)|};
                    var tuples = {|#3:rows.FullJoin(plains, r => r.Key, p => 1)|};
                    var leftTuples = {|#4:rows.LeftJoin(plains, r => r.Key, p => 1)|};
                    var rightTuples = {|#5:plains.RightJoin(rows, p => 1, r => r.Key)|};
                    var query = {|#6:queryPlains.FullJoin(rows, p => 1, r => r.Key, (p, r) => 0)|};
                    var queryTuples = {|#7:queryRows.FullJoin(plains, r => r.Key, p => 1)|};
                    var queryLeftTuples = {|#8:queryRows.LeftJoin(plains, r => r.Key, p => 1)|};
                    var stream = {|#9:streamPlains.FullJoin(streamRows, p => 1, r => r.Key, (p, r) => 0)|};
                    var streamAwaited = {|#10:streamRows.FullJoin(streamPlains, (r, ct) => ValueTask.FromResult(r.Key), (p, ct) => ValueTask.FromResult(1), (r, p, ct) => ValueTask.FromResult(0))|};
                    var streamTuples = {|#11:streamPlains.RightJoin(streamRows, (p, ct) => ValueTask.FromResult(1), (r, ct) => ValueTask.FromResult(r.Key))|};
                }
            }
            """,
            net11: true,
            expected:
            [
                Outer(0, "FullJoin", "Plain"),
                Inner(0, "FullJoin", "Guarded"),
                Outer(1, "FullJoin", "Plain"),
                Inner(2, "FullJoin", "Plain"),
                Inner(3, "FullJoin", "Plain"),
                Inner(4, "LeftJoin", "Plain"),
                Outer(5, "RightJoin", "Plain"),
                Outer(6, "FullJoin", "Plain", query: true),
                Inner(7, "FullJoin", "Plain", query: true),
                Inner(8, "LeftJoin", "Plain", query: true),
                Outer(9, "FullJoin", "Plain"),
                Inner(10, "FullJoin", "Plain"),
                Outer(11, "RightJoin", "Plain"),
            ])
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Every type CMTK0001 forbids the default of, a type parameter constrained to a value object included.</summary>
    [Fact]
    public Task Every_no_default_element_type_reports() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Row> rows, List<Validated> validated, List<Guarded> guarded, List<Result<int, Fault>> results)
                {
                    var checkedOne = {|#0:rows.LeftJoin(validated, r => r.Key, v => 1, (r, v) => 0)|};
                    var custom = {|#1:guarded.RightJoin(rows, g => 1, r => r.Key, (g, r) => 0)|};
                    var result = {|#2:rows.LeftJoin(results, r => r.Key, x => 1, (r, x) => 0)|};
                }

                public static void Struct<T>(List<Row> rows, IEnumerable<T> items) where T : struct, IValue<int> => _ = {|#3:rows.LeftJoin(items, r => r.Key, i => 1, (r, i) => 0)|};

                public static void New<T>(List<Row> rows, IEnumerable<T> items) where T : IValue<int>, new() => _ = {|#4:items.RightJoin(rows, i => 1, r => r.Key, (i, r) => 0)|};
            }
            """,
            expected:
            [
                Inner(0, "LeftJoin", "Validated"),
                Outer(1, "RightJoin", "Guarded"),
                Inner(2, "LeftJoin", "Result<int, Fault>"),
                Inner(3, "LeftJoin", "T"),
                Outer(4, "RightJoin", "T"),
            ])
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The side a join never fills, an element whose default is null or an ordinary value, an <c>Option</c>
    /// (the way out in memory, whose default is <c>None</c>), the nullable projection (the way out in a query),
    /// and the joins with no unmatched side stay silent.
    /// </summary>
    [Fact]
    public Task The_side_never_filled_a_legitimate_default_the_ways_out_and_inner_joins_stay_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Plain> plains, List<Row> rows, List<Plain?> nullables, List<Free> free, List<Option<int>> options,
                                       IQueryable<Row> queryRows, IAsyncEnumerable<Plain> streamPlains, IAsyncEnumerable<Row> streamRows)
                {
                    var outerOfLeft = plains.LeftJoin(rows, p => 1, r => r.Key, (p, r) => p);
                    var innerOfRight = rows.RightJoin(plains, r => r.Key, p => 1, (r, p) => p);
                    var outerOfLeftTuples = plains.LeftJoin(rows, p => 1, r => r.Key);
                    var classes = rows.LeftJoin(rows, r => r.Key, s => s.Key, (r, s) => s);
                    var classesFull = rows.FullJoin(rows, r => r.Key, s => s.Key, (r, s) => s);
                    var nullable = rows.LeftJoin(nullables, r => r.Key, p => 1, (r, p) => p);
                    var ordinary = free.FullJoin(free, f => f.N, g => g.N, (f, g) => g);
                    var option = rows.LeftJoin(options, r => r.Key, o => 1, (r, o) => o);
                    var wayOut = rows.LeftJoin(plains.Select(Option.Some), r => r.Key, p => 1, (r, p) => p);
                    var wayOutFull = plains.Select(Option.Some).FullJoin(plains.Select(Option.Some), p => 1, q => 1, (p, q) => p);
                    var wayOutStream = streamRows.LeftJoin(streamPlains.Select(Option.Some), r => r.Key, p => 1, (r, p) => p);
                    var wayOutQuery = queryRows.LeftJoin(plains.Select(p => (Plain?)p), r => r.Key, p => 1, (r, p) => p);
                    var inner = plains.Join(plains, p => 1, q => 1, (p, q) => p);
                    var grouped = rows.GroupJoin(plains, r => r.Key, p => 1, (r, ps) => ps);
                }
            }
            """,
            net11: true)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// In a project that declares value objects, Metalama runs analyzers before weaving, where a lambda that
    /// reads the generated <c>.Value</c> does not bind (CS1061 here), and neither does the join that takes it.
    /// Its candidates are LINQ's joins, and the element types are read from the sequences passed: a list, an
    /// array, a query, a type parameter constrained to a sequence, after <c>?.</c>, in any argument form.
    /// </summary>
    [Fact]
    public Task A_join_that_does_not_bind_is_read_from_its_candidates() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Plain> plains, Plain[] array, List<Row> rows, IQueryable<Row> queryRows)
                {
                    var left = {|#0:rows.LeftJoin(plains, r => r.Key, p => p.{|#10:Value|}, (r, p) => p)|};
                    var right = {|#1:plains.RightJoin(rows, p => p.{|#11:Value|}, r => r.Key, (p, r) => p)|};
                    var fromArray = {|#2:rows.LeftJoin(array, r => r.Key, p => p.{|#12:Value|}, (r, p) => p)|};
                    var query = {|#3:queryRows.LeftJoin(plains, r => r.Key, p => p.{|#13:Value|}, (r, p) => p)|};
                    var staticForm = {|#4:Enumerable.LeftJoin(rows, plains, r => r.Key, p => p.{|#14:Value|}, (r, p) => p)|};
                    var named = {|#5:rows.LeftJoin(innerKeySelector: p => p.{|#15:Value|}, inner: array, outerKeySelector: r => r.Key, resultSelector: (r, p) => p)|};
                    var outerOfLeft = plains.LeftJoin(rows, p => p.{|#16:Value|}, r => r.Key, (p, r) => p);
                    var conditional = plains?{|#7:.RightJoin(rows, p => p.{|#18:Value|}, r => r.Key, (p, r) => p)|};
                }

                public static void Constrained<TSequence>(List<Row> rows, TSequence plains) where TSequence : IEnumerable<Plain> =>
                    _ = {|#6:rows.LeftJoin(plains, r => r.Key, p => p.{|#17:Value|}, (r, p) => p)|};
            }
            """,
            expected:
            [
                Inner(0, "LeftJoin", "Plain"), Unbound(10),
                Outer(1, "RightJoin", "Plain"), Unbound(11),
                Inner(2, "LeftJoin", "Plain"), Unbound(12),
                Inner(3, "LeftJoin", "Plain", query: true), Unbound(13),
                Inner(4, "LeftJoin", "Plain"), Unbound(14),
                Inner(5, "LeftJoin", "Plain"), Unbound(15),
                Unbound(16),
                Outer(7, "RightJoin", "Plain"), Unbound(18),
                Inner(6, "LeftJoin", "Plain"), Unbound(17),
            ])
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>The generated <c>.Value</c> that does not bind before weaving, listed in source order with the rule's results.</summary>
    private static DiagnosticResult Unbound(int location) => DiagnosticResult.CompilerError("CS1061").WithLocation(location);

    public static TheoryData<string> Kinds() => [.. TypeArgumentKinds.All.Where(kind => kind.Mentions.All(Declared)).Select(kind => kind.Name)];

    private static bool Declared(string attribute) => RealTypeKit.Assembly.GetType($"CodoMetis.TypeKit.{attribute}Attribute") is not null;

    /// <summary>
    /// Across the variant catalogue of what a type is (<see cref="TypeArgumentKinds"/>), each join reports the
    /// side it fills exactly where CMTK0001 reports a <c>default</c> of the kind (<see cref="NoDefaultKindTests.HasNoDefault"/>),
    /// and never the side it does not fill.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public Task Each_kind_is_reported_on_the_side_a_join_fills_as_CMTK0001_judges_it(string kindName)
    {
        var kind      = TypeArgumentKinds.All.Single(candidate => candidate.Name == kindName);
        var reporting = NoDefaultKindTests.HasNoDefault(kind);

        string At(int location, string code) => reporting ? $"{{|#{location}:{code}|}}" : code;

        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>(
            $$"""
              #nullable enable
              using System.Linq;

              public static class Use{{kind.Parameters}} {{kind.Where}}
              {
                  public static void M(IEnumerable<string> keys, IEnumerable<{{kind.Argument}}> items)
                  {
                      _ = {{At(0, "keys.LeftJoin(items, k => 0, i => 0, (k, i) => 0)")}};
                      _ = {{At(1, "items.RightJoin(keys, i => 0, k => 0, (i, k) => 0)")}};
                      _ = {{At(2, "items.FullJoin(keys, i => 0, k => 0, (i, k) => 0)")}};
                      _ = {{At(3, "keys.FullJoin(items, k => 0, i => 0)")}};
                      _ = items.LeftJoin(keys, i => 0, k => 0, (i, k) => 0);
                      _ = keys.RightJoin(items, k => 0, i => 0, (k, i) => 0);
                  }
              }
              """);

        test.TestState.Sources.Add(("Candidates.cs", TypeArgumentKinds.Candidates));
        test.ReferenceAssemblies = Net110;
        test.TestBehaviors = VariantSweep.Behaviors;

        if (reporting)
        {
            test.ExpectedDiagnostics.AddRange(
                from location in Enumerable.Range(0, 4)
                select new DiagnosticResult("CMTK0009", DiagnosticSeverity.Warning).WithLocation(location));
        }

        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
