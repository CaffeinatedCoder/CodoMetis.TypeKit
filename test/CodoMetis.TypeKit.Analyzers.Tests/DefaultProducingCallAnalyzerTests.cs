using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>CMTK0009: a call that returns a default instance when it has nothing to return.</summary>
public sealed class DefaultProducingCallAnalyzerTests
{
    private const string Subjects =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Runtime.CompilerServices;
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

        public sealed class ValueClass : IValue<int> { }

        """;

    private static AnalyzerTest<DefaultProducingCallAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private const string FirstOrNone = "Use FirstOrNone, which returns an Option, or pass a default value";

    private const string LastOrNone = "Use LastOrNone, which returns an Option, or pass a default value";

    private const string DefaultValue = "Pass a default value";

    private const string Factories = "Create it through one of its factories";

    private const string NothingFound = " when nothing is found";

    private static DiagnosticResult Cmtk0009(int location, string method, string type, string circumstance, string advice) =>
        new DiagnosticResult("CMTK0009", DiagnosticSeverity.Warning).WithLocation(location).WithArguments(method, type, circumstance, advice);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new DefaultProducingCallAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0009");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Warning);
        rule.Category.ShouldBe("Usage");
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task OrDefault_of_a_validated_value_object_reports() =>
        Test(
            "public static class C { public static Validated Of(Option<Validated> parsed) => {|#0:parsed.OrDefault()|}; }",
            new DiagnosticResult("CMTK0009", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("'OrDefault' returns a default 'Validated' for None, an instance that passed no factory. Use Or(fallback) or Match, which say what None becomes."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Every_sequence_form_without_a_default_value_reports() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Plain> ids)
                {
                    var first = {|#0:ids.FirstOrDefault()|};
                    var firstMatch = {|#1:ids.FirstOrDefault(id => true)|};
                    var last = {|#2:ids.LastOrDefault()|};
                    var lastMatch = {|#3:ids.LastOrDefault(id => true)|};
                    var single = {|#4:ids.SingleOrDefault()|};
                    var singleMatch = {|#5:ids.SingleOrDefault(id => true)|};
                    var at = {|#6:ids.ElementAtOrDefault(3)|};
                    var fromEnd = {|#7:ids.ElementAtOrDefault(^1)|};
                    var padded = {|#8:ids.DefaultIfEmpty()|};
                    var staticForm = {|#9:Enumerable.FirstOrDefault(ids)|};
                }
            }
            """,
            Cmtk0009(0, "FirstOrDefault", "Plain", NothingFound, FirstOrNone),
            Cmtk0009(1, "FirstOrDefault", "Plain", NothingFound, FirstOrNone),
            Cmtk0009(2, "LastOrDefault", "Plain", NothingFound, LastOrNone),
            Cmtk0009(3, "LastOrDefault", "Plain", NothingFound, LastOrNone),
            Cmtk0009(4, "SingleOrDefault", "Plain", NothingFound, DefaultValue),
            Cmtk0009(5, "SingleOrDefault", "Plain", NothingFound, DefaultValue),
            Cmtk0009(6, "ElementAtOrDefault", "Plain", " for an index past the end", "Use Skip(index).FirstOrNone(), which returns an Option"),
            Cmtk0009(7, "ElementAtOrDefault", "Plain", " for an index past the end", "Use Skip(index).FirstOrNone(), which returns an Option"),
            Cmtk0009(8, "DefaultIfEmpty", "Plain", " for an empty sequence", DefaultValue),
            Cmtk0009(9, "FirstOrDefault", "Plain", NothingFound, FirstOrNone))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>In a query, <c>FirstOrNone</c> would run it on the client; a nullable projection is translated.</summary>
    [Fact]
    public Task Every_query_form_without_a_default_value_reports() =>
        Test(
            """
            public static class C
            {
                public static void Run(IQueryable<Plain> ids)
                {
                    var first = {|#0:ids.FirstOrDefault()|};
                    var last = {|#1:ids.LastOrDefault(id => true)|};
                    var single = {|#2:ids.SingleOrDefault()|};
                    var at = {|#3:ids.ElementAtOrDefault(3)|};
                    var padded = {|#4:ids.DefaultIfEmpty()|};
                }
            }
            """,
            Cmtk0009(0, "FirstOrDefault", "Plain", NothingFound, "Select a nullable first, as in Select(x => (Plain?)x), so that nothing found is null"),
            Cmtk0009(1, "LastOrDefault", "Plain", NothingFound, "Select a nullable first, as in Select(x => (Plain?)x), so that nothing found is null"),
            Cmtk0009(2, "SingleOrDefault", "Plain", NothingFound, "Select a nullable first, as in Select(x => (Plain?)x), so that nothing found is null"),
            Cmtk0009(3, "ElementAtOrDefault", "Plain", " for an index past the end", "Select a nullable first, as in Select(x => (Plain?)x), so that nothing found is null"),
            Cmtk0009(4, "DefaultIfEmpty", "Plain", " for an empty sequence", "Select a nullable first, as in Select(x => (Plain?)x), so that nothing found is null"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Lookups_creation_and_OrDefault_report() =>
        Test(
            """
            public static class C
            {
                public static void Run(Plain? maybe, Dictionary<int, Plain> byKey, IReadOnlyDictionary<int, Plain> readOnly, Option<Plain> option)
                {
                    var unwrapped = {|#0:maybe.GetValueOrDefault()|};
                    var found = {|#1:byKey.GetValueOrDefault(1)|};
                    var readOnlyFound = {|#2:readOnly.GetValueOrDefault(1)|};
                    var created = {|#3:Activator.CreateInstance<Plain>()|};
                    var byType = {|#4:Activator.CreateInstance(typeof(Plain))|};
                    var nonPublic = {|#5:Activator.CreateInstance(typeof(Plain), nonPublic: true)|};
                    var uninitialized = {|#6:RuntimeHelpers.GetUninitializedObject(typeof(Plain))|};
                    var orDefault = {|#7:option.OrDefault()|};
                    var implementation = {|#8:Option.OrDefault(option)|};
                }
            }
            """,
            Cmtk0009(0, "GetValueOrDefault", "Plain", " for null", "Pass a default value, or check HasValue first"),
            Cmtk0009(1, "GetValueOrDefault", "Plain", " for a missing key", "Use GetValueOrNone, which returns an Option, or pass a default value"),
            Cmtk0009(2, "GetValueOrDefault", "Plain", " for a missing key", "Use GetValueOrNone, which returns an Option, or pass a default value"),
            Cmtk0009(3, "CreateInstance", "Plain", "", Factories),
            Cmtk0009(4, "CreateInstance", "Plain", "", Factories),
            Cmtk0009(5, "CreateInstance", "Plain", "", Factories),
            Cmtk0009(6, "GetUninitializedObject", "Plain", "", Factories),
            Cmtk0009(7, "OrDefault", "Plain", " for None", "Use Or(fallback) or Match, which say what None becomes"),
            Cmtk0009(8, "OrDefault", "Plain", " for None", "Use Or(fallback) or Match, which say what None becomes"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Every type CMTK0001 forbids the default of, type parameters constrained to a value object included.</summary>
    [Fact]
    public Task Every_no_default_element_type_reports() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Option<int>> options, List<Result<int, Fault>> results, List<Guarded> guarded, List<Validated> validated)
                {
                    var option = {|#0:options.FirstOrDefault()|};
                    var result = {|#1:results.LastOrDefault()|};
                    var custom = {|#2:guarded.SingleOrDefault()|};
                    var checkedOne = {|#3:validated.FirstOrDefault()|};
                }

                public static T Struct<T>(IEnumerable<T> items) where T : struct, IValue<int> => {|#4:items.FirstOrDefault()|};

                public static T New<T>(IEnumerable<T> items) where T : IValue<int>, new() => {|#5:items.FirstOrDefault()|}!;
            }
            """,
            Cmtk0009(0, "FirstOrDefault", "Option<int>", NothingFound, FirstOrNone),
            Cmtk0009(1, "LastOrDefault", "Result<int, Fault>", NothingFound, LastOrNone),
            Cmtk0009(2, "SingleOrDefault", "Guarded", NothingFound, DefaultValue),
            Cmtk0009(3, "FirstOrDefault", "Validated", NothingFound, FirstOrNone),
            Cmtk0009(4, "FirstOrDefault", "T", NothingFound, FirstOrNone),
            Cmtk0009(5, "FirstOrDefault", "T", NothingFound, FirstOrNone))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A generated <c>TryFrom</c> of the same project does not bind where Metalama runs analyzers
    /// (CS0117 here), and neither does <c>OrDefault</c> on it.
    /// </summary>
    [Fact]
    public Task OrDefault_on_an_unbound_TryFrom_reports() =>
        Test(
            "public static class C { public static void Run(int input) { var code = {|#0:Validated.{|CS0117:TryFrom|}(input).OrDefault()|}; } }",
            Cmtk0009(0, "OrDefault", "Validated", " for None", "Use Or(fallback) or Match, which say what None becomes"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>A default value the caller chose, and element types whose default is null or an ordinary value.</summary>
    [Fact]
    public Task A_default_value_given_or_an_element_type_with_a_legitimate_default_stays_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Plain> ids, Plain fallback, Plain? maybe, Dictionary<int, Plain> byKey, IQueryable<Plain> query,
                                       List<Plain?> nullables, List<int> numbers, List<Free> free, List<ValueClass> classes, Option<int> number, Option<Plain> option)
                {
                    var first = ids.FirstOrDefault(fallback);
                    var firstMatch = ids.FirstOrDefault(id => true, fallback);
                    var last = ids.LastOrDefault(fallback);
                    var single = ids.SingleOrDefault(id => true, fallback);
                    var padded = ids.DefaultIfEmpty(fallback);
                    var unwrapped = maybe.GetValueOrDefault(fallback);
                    var found = byKey.GetValueOrDefault(1, fallback);
                    var queried = query.FirstOrDefault(fallback);
                    var queryPadded = query.DefaultIfEmpty(fallback);
                    var nullable = nullables.FirstOrDefault();
                    var numeral = numbers.FirstOrDefault();
                    var ordinary = free.LastOrDefault();
                    var reference = classes.FirstOrDefault();
                    var plainNumber = number.OrDefault();
                    var fallenBack = option.Or(fallback);
                    var withArguments = Activator.CreateInstance(typeof(Plain), 1);
                    var none = Activator.CreateInstance<Free>();
                }

                public static T? Unconstrained<T>(IEnumerable<T> items) where T : IValue<int> => items.FirstOrDefault();
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<DefaultProducingCallAnalyzer>(
                "public static class S { public static int F(System.Collections.Generic.List<int> l) => System.Linq.Enumerable.FirstOrDefault(l); }",
                referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
