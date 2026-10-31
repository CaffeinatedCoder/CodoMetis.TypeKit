using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>CMTK0005: arrays and spans of a no-default struct, created with a length.</summary>
public sealed class DefaultFilledCollectionAnalyzerTests
{
    private const string Subjects =
        """
        using System;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        public readonly struct Plain : IValue<int>
        {
            public static Plain[] Slots(int n) => new Plain[n];
        }

        public sealed class ValueClass : IValue<int> { }

        [RequireCustomInitialization]
        public struct Guarded { public int N; }

        """;

    private static AnalyzerTest<DefaultFilledCollectionAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<DefaultFilledCollectionAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0005(int location, string form, string type) =>
        new DiagnosticResult("CMTK0005", DiagnosticSeverity.Warning).WithLocation(location).WithArguments(form, type);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new DefaultFilledCollectionAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0005");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Warning);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task An_array_created_with_a_length_reports() =>
        Test(
            "public static class C { public static Plain[] Slots(int n) => {|#0:new Plain[n]|}; }",
            new DiagnosticResult("CMTK0005", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("An array created with a length fills every slot it creates with a default 'Plain', which passed no factory. Create the elements from values instead, for instance with a collection expression or Select(...).ToArray()."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Every_form_that_creates_default_slots_reports() =>
        Test(
            """
            public static class C
            {
                public static void Run(int n, Plain[] existing)
                {
                    var options = {|#0:new Option<int>[n]|};
                    var results = {|#1:new Result<int, string>[n]|};
                    var guarded = {|#2:new Guarded[n]|};
                    var grid = {|#3:new Plain[n, 2]|};
                    Span<Plain> span = {|#4:stackalloc Plain[n]|};
                    var uninitialized = {|#5:GC.AllocateUninitializedArray<Plain>(n)|};
                    var allocated = {|#6:GC.AllocateArray<Plain>(n)|};
                    {|#7:Array.Resize(ref existing, n)|};
                }

                public static T[] Generic<T>(int n) where T : struct, IValue<int> => {|#8:new T[n]|};
            }
            """,
            Cmtk0005(0, "An array created with a length", "Option<int>"),
            Cmtk0005(1, "An array created with a length", "Result<int, string>"),
            Cmtk0005(2, "An array created with a length", "Guarded"),
            Cmtk0005(3, "An array created with a length", "Plain"),
            Cmtk0005(4, "A stackalloc with a length", "Plain"),
            Cmtk0005(5, "GC.AllocateUninitializedArray", "Plain"),
            Cmtk0005(6, "GC.AllocateArray", "Plain"),
            Cmtk0005(7, "Array.Resize", "Plain"),
            Cmtk0005(8, "An array created with a length", "T"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A constant length or size of zero creates no slot, as <c>new Plain[0]</c> does not: the calls
    /// were reported although the array creation was not.
    /// </summary>
    [Fact]
    public Task A_call_with_a_constant_length_of_zero_stays_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(Plain[] existing)
                {
                    Array.Resize(ref existing, 0);
                    Array.Resize(ref existing, newSize: 0);
                    var uninitialized = GC.AllocateUninitializedArray<Plain>(0);
                    var allocated = GC.AllocateArray<Plain>(0, pinned: false);
                    const int none = 0;
                    var constant = GC.AllocateArray<Plain>(none);
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A multidimensional array holds no element when any dimension is a constant zero, not only when
    /// every one is: <c>new Plain[0, 2]</c> was reported (#227). Across the input class, an array
    /// creation by rank and by which dimension is a constant zero; one with no zero dimension reports.
    /// </summary>
    [Fact]
    public Task An_array_with_any_constant_zero_dimension_stays_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(int n)
                {
                    const int none = 0;
                    var first = new Plain[0, 2];
                    var second = new Plain[2, 0];
                    var both = new Plain[0, 0];
                    var variable = new Plain[n, 0];
                    var named = new Plain[none, n];
                    var middle = new Plain[2, 0, 2];
                    var last = new Plain[n, 2, 0];
                    var jagged = new Plain[0][];
                    var grid = {|#0:new Plain[2, 2]|};
                    var cube = {|#1:new Plain[n, 1, 2]|};
                }
            }
            """,
            Cmtk0005(0, "An array created with a length", "Plain"),
            Cmtk0005(1, "An array created with a length", "Plain"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A zero of any integral type creates no slot: a dimension may be a <c>long</c>, <c>uint</c> or
    /// <c>ulong</c>, and a <c>stackalloc</c> size's constant is read before its conversion to <c>int</c>,
    /// so <c>0L</c> and <c>(byte)0</c> were reported where <c>0</c> was not.
    /// </summary>
    [Fact]
    public Task A_zero_of_any_integral_type_stays_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(int n, Plain[] existing)
                {
                    var asLong = new Plain[0L];
                    var asUnsigned = new Plain[0u, 2];
                    var asUnsignedLong = new Plain[2, 0ul];
                    var asByte = new Plain[(byte)0];
                    var asChar = new Plain['\0'];
                    Span<Plain> stackByte = stackalloc Plain[(byte)0];
                    Span<Plain> stackShort = stackalloc Plain[(short)0];
                    Span<Plain> stackChar = stackalloc Plain['\0'];
                    var allocated = GC.AllocateArray<Plain>((byte)0);
                    Array.Resize(ref existing, '\0');
                    var one = {|#0:new Plain[1L]|};
                    Span<Plain> stackOne = {|#1:stackalloc Plain[(byte)1]|};
                }
            }
            """,
            Cmtk0005(0, "An array created with a length", "Plain"),
            Cmtk0005(1, "A stackalloc with a length", "Plain"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The subjects' own <c>new Plain[1]</c> inside <c>Plain</c> doubles as the exemption for the type
    /// itself. Nothing here starts with a default element of a no-default struct.
    /// </summary>
    [Fact]
    public Task Arrays_with_values_empty_ones_and_other_element_types_stay_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(int n, Plain a, Plain b)
                {
                    var empty = new Plain[0];
                    var filled = new Plain[] { a, b };
                    var sized = new Plain[2] { a, b };
                    Plain[] expression = [a, b];
                    var numbers = new int[n];
                    var nullable = new Plain?[n];
                    var classes = new ValueClass[n];
                    var jagged = new Plain[n][];
                    Span<Plain> initialized = stackalloc Plain[] { a, b };
                    var none = GC.AllocateArray<int>(n);
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<DefaultFilledCollectionAnalyzer>("public static class S { public static int[] F(int n) => new int[n]; }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
