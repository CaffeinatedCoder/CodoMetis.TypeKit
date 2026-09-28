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
        using System.Collections.Immutable;
        using System.Linq;
        using System.Runtime.CompilerServices;
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

        public sealed class ValueClass : IValue<int> { }

        """;

    private static AnalyzerTest<DefaultProducingCallAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    /// <summary>A compilation that references EF Core, whose <c>FirstOrDefaultAsync</c> and friends the rule reads when it is there.</summary>
    private static AnalyzerTest<DefaultProducingCallAnalyzer> TestWithEntityFrameworkCore(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<DefaultProducingCallAnalyzer>("using Microsoft.EntityFrameworkCore;" + Environment.NewLine + Subjects + code);
        test.TestState.AdditionalReferences.AddRange(RealEntityFrameworkCore.References);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private const string FirstOrNone = "Use FirstOrNone, which returns an Option, or pass a default value";

    private const string LastOrNone = "Use LastOrNone, which returns an Option, or pass a default value";

    private const string DefaultValue = "Pass a default value";

    private const string Factories = "Create it through one of its factories";

    private const string NothingFound = " when nothing is found";

    private const string PastTheEnd = " for an index past the end";

    private const string NullableFirst = "Select a nullable first, as in Select(x => (Plain?)x), so that nothing found is null";

    private const string MissingKey = " for a missing key";

    private const string GetValueOrNone = "Use GetValueOrNone, which returns an Option, or pass a default value";

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

    /// <summary><c>Find</c> and <c>FindLast</c> return <c>default(T)</c> when nothing matches, as <c>FirstOrDefault</c> does: each was silent.</summary>
    [Fact]
    public Task Find_and_FindLast_report() =>
        Test(
            """
            public static class C
            {
                public static void Run(List<Plain> ids, Plain[] array, ImmutableList<Plain> immutable, ImmutableList<Plain>.Builder builder)
                {
                    var found = {|#0:ids.Find(id => true)|};
                    var foundLast = {|#1:ids.FindLast(id => true)|};
                    var inArray = {|#2:Array.Find(array, id => true)|};
                    var lastInArray = {|#3:Array.FindLast(array, id => true)|};
                    var inImmutable = {|#4:immutable.Find(id => true)|};
                    var lastInImmutable = {|#5:immutable.FindLast(id => true)|};
                    var inBuilder = {|#6:builder.Find(id => true)|};
                    var lastInBuilder = {|#7:builder.FindLast(id => true)|};
                }
            }
            """,
            Cmtk0009(0, "Find", "Plain", NothingFound, FirstOrNonePredicate),
            Cmtk0009(1, "FindLast", "Plain", NothingFound, LastOrNonePredicate),
            Cmtk0009(2, "Find", "Plain", NothingFound, FirstOrNonePredicate),
            Cmtk0009(3, "FindLast", "Plain", NothingFound, LastOrNonePredicate),
            Cmtk0009(4, "Find", "Plain", NothingFound, FirstOrNonePredicate),
            Cmtk0009(5, "FindLast", "Plain", NothingFound, LastOrNonePredicate),
            Cmtk0009(6, "Find", "Plain", NothingFound, FirstOrNonePredicate),
            Cmtk0009(7, "FindLast", "Plain", NothingFound, LastOrNonePredicate))
            .RunAsync(TestContext.Current.CancellationToken);

    private const string FirstOrNonePredicate = "Use FirstOrNone(predicate), which returns an Option";

    private const string LastOrNonePredicate = "Use LastOrNone(predicate), which returns an Option";

    /// <summary>
    /// <c>ImmutableArray</c>'s own <c>FirstOrDefault</c> and friends (<c>ImmutableArrayExtensions</c>), which
    /// bind before <c>Enumerable</c>'s, and <c>ImmutableDictionary.GetValueOrDefault</c>, which binds before
    /// <c>CollectionExtensions</c>': each was silent.
    /// </summary>
    [Fact]
    public Task Immutable_collection_forms_report() =>
        Test(
            """
            public static class C
            {
                public static void Run(ImmutableArray<Plain> ids, ImmutableArray<Plain>.Builder arrayBuilder, ImmutableDictionary<int, Plain> byKey,
                                       IImmutableDictionary<int, Plain> contract, ImmutableDictionary<int, Plain>.Builder dictionaryBuilder,
                                       ImmutableSortedDictionary<int, Plain>.Builder sortedBuilder)
                {
                    var first = {|#0:ids.FirstOrDefault()|};
                    var firstMatch = {|#1:ids.FirstOrDefault(id => true)|};
                    var last = {|#2:ids.LastOrDefault()|};
                    var lastMatch = {|#3:ids.LastOrDefault(id => true)|};
                    var single = {|#4:ids.SingleOrDefault()|};
                    var singleMatch = {|#5:ids.SingleOrDefault(id => true)|};
                    var at = {|#6:ids.ElementAtOrDefault(3)|};
                    var builderFirst = {|#7:arrayBuilder.FirstOrDefault()|};
                    var builderLast = {|#8:arrayBuilder.LastOrDefault()|};
                    var found = {|#9:byKey.GetValueOrDefault(1)|};
                    var throughContract = {|#10:contract.GetValueOrDefault(1)|};
                    var staticForm = {|#11:ImmutableDictionary.GetValueOrDefault(byKey, 1)|};
                    var inBuilder = {|#12:dictionaryBuilder.GetValueOrDefault(1)|};
                    var inSortedBuilder = {|#13:sortedBuilder.GetValueOrDefault(1)|};
                }
            }
            """,
            Cmtk0009(0, "FirstOrDefault", "Plain", NothingFound, FirstOrNone),
            Cmtk0009(1, "FirstOrDefault", "Plain", NothingFound, FirstOrNone),
            Cmtk0009(2, "LastOrDefault", "Plain", NothingFound, LastOrNone),
            Cmtk0009(3, "LastOrDefault", "Plain", NothingFound, LastOrNone),
            Cmtk0009(4, "SingleOrDefault", "Plain", NothingFound, DefaultValue),
            Cmtk0009(5, "SingleOrDefault", "Plain", NothingFound, DefaultValue),
            Cmtk0009(6, "ElementAtOrDefault", "Plain", PastTheEnd, "Use Skip(index).FirstOrNone(), which returns an Option"),
            Cmtk0009(7, "FirstOrDefault", "Plain", NothingFound, FirstOrNone),
            Cmtk0009(8, "LastOrDefault", "Plain", NothingFound, LastOrNone),
            Cmtk0009(9, "GetValueOrDefault", "Plain", MissingKey, GetValueOrNone),
            Cmtk0009(10, "GetValueOrDefault", "Plain", MissingKey, GetValueOrNone),
            Cmtk0009(11, "GetValueOrDefault", "Plain", MissingKey, GetValueOrNone),
            Cmtk0009(12, "GetValueOrDefault", "Plain", MissingKey, GetValueOrNone),
            Cmtk0009(13, "GetValueOrDefault", "Plain", MissingKey, GetValueOrNone))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// .NET 10's <c>System.Linq.AsyncEnumerable</c>: the default comes back inside a <c>ValueTask</c>, and
    /// was silent. The compilation references no EF Core, whose absence must not switch the rule off.
    /// </summary>
    [Fact]
    public Task Async_sequence_forms_report() =>
        Test(
            """
            public static class C
            {
                public static async Task Run(IAsyncEnumerable<Plain> ids, IAsyncEnumerable<Validated> validated)
                {
                    var first = await {|#0:ids.FirstOrDefaultAsync()|};
                    var firstMatch = await {|#1:ids.FirstOrDefaultAsync(id => true)|};
                    var last = await {|#2:ids.LastOrDefaultAsync()|};
                    var lastMatch = await {|#3:ids.LastOrDefaultAsync(id => true)|};
                    var single = await {|#4:ids.SingleOrDefaultAsync()|};
                    var singleMatch = await {|#5:ids.SingleOrDefaultAsync(id => true)|};
                    var at = await {|#6:ids.ElementAtOrDefaultAsync(3)|};
                    var fromEnd = await {|#7:ids.ElementAtOrDefaultAsync(^1)|};
                    var padded = {|#8:ids.DefaultIfEmpty()|};
                    var staticForm = await {|#9:AsyncEnumerable.FirstOrDefaultAsync(validated)|};
                    var held = {|#10:ids.FirstOrDefaultAsync()|};
                }

                public static async Task<T> Constrained<T>(IAsyncEnumerable<T> items) where T : struct, IValue<int> => await {|#11:items.FirstOrDefaultAsync()|};
            }
            """,
            Cmtk0009(0, "FirstOrDefaultAsync", "Plain", NothingFound, DefaultValue),
            Cmtk0009(1, "FirstOrDefaultAsync", "Plain", NothingFound, DefaultValue),
            Cmtk0009(2, "LastOrDefaultAsync", "Plain", NothingFound, DefaultValue),
            Cmtk0009(3, "LastOrDefaultAsync", "Plain", NothingFound, DefaultValue),
            Cmtk0009(4, "SingleOrDefaultAsync", "Plain", NothingFound, DefaultValue),
            Cmtk0009(5, "SingleOrDefaultAsync", "Plain", NothingFound, DefaultValue),
            Cmtk0009(6, "ElementAtOrDefaultAsync", "Plain", PastTheEnd, NullableFirst),
            Cmtk0009(7, "ElementAtOrDefaultAsync", "Plain", PastTheEnd, NullableFirst),
            Cmtk0009(8, "DefaultIfEmpty", "Plain", " for an empty sequence", DefaultValue),
            Cmtk0009(9, "FirstOrDefaultAsync", "Validated", NothingFound, DefaultValue),
            Cmtk0009(10, "FirstOrDefaultAsync", "Plain", NothingFound, DefaultValue),
            Cmtk0009(11, "FirstOrDefaultAsync", "T", NothingFound, DefaultValue))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// EF Core's <c>FirstOrDefaultAsync</c> and friends, the way a query is usually run: the default comes
    /// back inside a <c>Task</c>, and was silent. EF Core has no overload that takes a default value, so the
    /// advice is the nullable projection, as for <c>Queryable</c>.
    /// </summary>
    [Fact]
    public Task Entity_Framework_Core_async_forms_report() =>
        TestWithEntityFrameworkCore(
            """
            public static class C
            {
                public static async Task Run(IQueryable<Plain> ids, System.Threading.CancellationToken cancellationToken)
                {
                    var first = await {|#0:ids.FirstOrDefaultAsync(cancellationToken)|};
                    var firstMatch = await {|#1:ids.FirstOrDefaultAsync(id => true, cancellationToken)|};
                    var last = await {|#2:ids.LastOrDefaultAsync()|};
                    var lastMatch = await {|#3:ids.LastOrDefaultAsync(id => true)|};
                    var single = await {|#4:ids.SingleOrDefaultAsync()|};
                    var singleMatch = await {|#5:ids.SingleOrDefaultAsync(id => true)|};
                    var at = await {|#6:ids.ElementAtOrDefaultAsync(3)|};
                    var staticForm = await {|#7:EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(ids)|};
                }
            }
            """,
            Cmtk0009(0, "FirstOrDefaultAsync", "Plain", NothingFound, NullableFirst),
            Cmtk0009(1, "FirstOrDefaultAsync", "Plain", NothingFound, NullableFirst),
            Cmtk0009(2, "LastOrDefaultAsync", "Plain", NothingFound, NullableFirst),
            Cmtk0009(3, "LastOrDefaultAsync", "Plain", NothingFound, NullableFirst),
            Cmtk0009(4, "SingleOrDefaultAsync", "Plain", NothingFound, NullableFirst),
            Cmtk0009(5, "SingleOrDefaultAsync", "Plain", NothingFound, NullableFirst),
            Cmtk0009(6, "ElementAtOrDefaultAsync", "Plain", PastTheEnd, NullableFirst),
            Cmtk0009(7, "FirstOrDefaultAsync", "Plain", NothingFound, NullableFirst))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The new forms keep the rule's line: an overload given a default value, and an element type whose
    /// default is null or an ordinary value, stay silent.
    /// </summary>
    [Fact]
    public Task The_new_forms_given_a_default_value_or_a_legitimate_element_type_stay_silent() =>
        TestWithEntityFrameworkCore(
            """
            public static class C
            {
                public static async Task Run(Plain fallback, List<int> numbers, int[] array, ImmutableList<Plain?> nullables, ImmutableArray<Free> free,
                                             ImmutableArray<Plain> ids, ImmutableDictionary<int, Plain> byKey, ImmutableDictionary<int, Plain>.Builder builder,
                                             IAsyncEnumerable<Plain> stream, IAsyncEnumerable<int> numberStream, IQueryable<int> numberQuery,
                                             IQueryable<Plain?> nullableQuery, List<ValueClass> classes)
                {
                    var number = numbers.Find(n => true);
                    var inArray = Array.FindLast(array, n => true);
                    var nullable = nullables.Find(n => true);
                    var ordinary = free.FirstOrDefault();
                    var reference = classes.Find(c => true);
                    var fallenBack = ids.FirstOrDefault(fallback);
                    var found = byKey.GetValueOrDefault(1, fallback);
                    var inBuilder = builder.GetValueOrDefault(1, fallback);
                    var streamed = await stream.FirstOrDefaultAsync(fallback);
                    var streamedMatch = await stream.LastOrDefaultAsync(id => true, fallback);
                    var streamedSingle = await stream.SingleOrDefaultAsync(fallback);
                    var streamPadded = stream.DefaultIfEmpty(fallback);
                    var streamedNumber = await numberStream.FirstOrDefaultAsync();
                    var queriedNumber = await numberQuery.FirstOrDefaultAsync();
                    var projected = await nullableQuery.SingleOrDefaultAsync();
                }
            }
            """)
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
