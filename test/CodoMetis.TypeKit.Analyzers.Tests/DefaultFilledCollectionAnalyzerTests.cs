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
