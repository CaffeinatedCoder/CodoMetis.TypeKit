using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>CMTK0005: arrays and spans of a no-default struct, created with a length.</summary>
public sealed class DefaultFilledCollectionAnalyzerTests
{
    private const string Subjects =
        """
        using System;
        using System.Buffers;
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
    /// The non-generic array factories and the pool create default slots as <c>new Plain[n]</c> does, and
    /// none was reported (#228). Across the input class, an expression that creates an array of a value
    /// object, by the API that creates it: each overload of <c>Array.CreateInstance</c> and
    /// <c>CreateInstanceFromArrayType</c>, <c>Rent</c> on the shared pool, a created one and a derived one.
    /// </summary>
    [Fact]
    public Task The_array_factories_and_the_pool_report() =>
        Test(
            """
            public sealed class Pool : ArrayPool<Plain>
            {
                public override Plain[] Rent(int minimumLength) => [];
                public override void Return(Plain[] array, bool clearArray = false) { }
            }

            public static class C
            {
                public static void Run(int n, int[] lengths, Pool pool)
                {
                    var one = {|#0:Array.CreateInstance(typeof(Plain), n)|};
                    var two = {|#1:Array.CreateInstance(typeof(Plain), 2, 2)|};
                    var three = {|#2:Array.CreateInstance(typeof(Plain), 2, n, 2)|};
                    var four = {|#3:Array.CreateInstance(typeof(Plain), 2, 2, 2, 2)|};
                    var held = {|#4:Array.CreateInstance(typeof(Plain), lengths)|};
                    var bounded = {|#5:Array.CreateInstance(typeof(Plain), new[] { 2 }, new[] { 0 })|};
                    var wide = {|#6:Array.CreateInstance(typeof(Plain), 2L, 2L)|};
                    var fromArray = {|#7:Array.CreateInstanceFromArrayType(typeof(Plain[]), n)|};
                    var fromGrid = {|#8:Array.CreateInstanceFromArrayType(typeof(Plain[,]), 2, 2)|};
                    var fromBounded = {|#9:Array.CreateInstanceFromArrayType(typeof(Plain[]), [2], [1])|};
                    var shared = {|#10:ArrayPool<Plain>.Shared.Rent(n)|};
                    var created = {|#11:ArrayPool<Plain>.Create().Rent(3)|};
                    var derived = {|#12:pool.Rent(3)|};
                    var options = {|#13:Array.CreateInstance(typeof(Option<int>), n)|};
                }

                public static Array Generic<T>(int n) where T : struct, IValue<int> => {|#14:Array.CreateInstance(typeof(T), n)|};
            }
            """,
            Cmtk0005(0, "Array.CreateInstance", "Plain"),
            Cmtk0005(1, "Array.CreateInstance", "Plain"),
            Cmtk0005(2, "Array.CreateInstance", "Plain"),
            Cmtk0005(3, "Array.CreateInstance", "Plain"),
            Cmtk0005(4, "Array.CreateInstance", "Plain"),
            Cmtk0005(5, "Array.CreateInstance", "Plain"),
            Cmtk0005(6, "Array.CreateInstance", "Plain"),
            Cmtk0005(7, "Array.CreateInstanceFromArrayType", "Plain"),
            Cmtk0005(8, "Array.CreateInstanceFromArrayType", "Plain"),
            Cmtk0005(9, "Array.CreateInstanceFromArrayType", "Plain"),
            Cmtk0005(10, "ArrayPool.Rent", "Plain"),
            Cmtk0005(11, "ArrayPool.Rent", "Plain"),
            Cmtk0005(12, "ArrayPool.Rent", "Plain"),
            Cmtk0005(13, "Array.CreateInstance", "Option<int>"),
            Cmtk0005(14, "Array.CreateInstance", "T"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A constant zero length in any dimension creates no slot, and a <c>Type</c> the rule cannot read names
    /// no element: a variable, <c>GetType()</c>, a nullable value object, an array of another type.
    /// </summary>
    [Fact]
    public Task The_array_factories_with_a_zero_length_or_an_unknown_type_stay_silent() =>
        Test(
            """
            public static class C
            {
                public static void Run(int n, Type type, Plain a)
                {
                    var one = Array.CreateInstance(typeof(Plain), 0);
                    var first = Array.CreateInstance(typeof(Plain), 0, n);
                    var second = Array.CreateInstance(typeof(Plain), n, 0);
                    var third = Array.CreateInstance(typeof(Plain), n, 2, 0);
                    var listed = Array.CreateInstance(typeof(Plain), 2, 0, 2, 2);
                    var bounded = Array.CreateInstance(typeof(Plain), new[] { 0 }, new[] { 5 });
                    var wide = Array.CreateInstance(typeof(Plain), 2L, 0L);
                    var fromArray = Array.CreateInstanceFromArrayType(typeof(Plain[]), 0);
                    var fromGrid = Array.CreateInstanceFromArrayType(typeof(Plain[,]), n, 0);
                    var shared = ArrayPool<Plain>.Shared.Rent(0);
                    var created = ArrayPool<Plain>.Create().Rent(minimumLength: 0);

                    var variable = Array.CreateInstance(type, n);
                    var runtime = Array.CreateInstance(a.GetType(), n);
                    var nullable = Array.CreateInstance(typeof(Plain?), n);
                    var numbers = Array.CreateInstance(typeof(int), n);
                    var jagged = Array.CreateInstance(typeof(Plain[]), n);
                    var other = Array.CreateInstanceFromArrayType(typeof(int[]), n);
                    var unknown = Array.CreateInstanceFromArrayType(type, n);
                    var pooled = ArrayPool<int>.Shared.Rent(n);
                    var instance = Activator.CreateInstance(typeof(Plain));
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// <c>ArrayPool&lt;T&gt;.Rent</c> and the <c>typeof</c> factories judge their element as
    /// <c>GC.AllocateArray&lt;T&gt;</c> does, across every kind of <see cref="TypeArgumentKinds"/>: each
    /// reports exactly where the generic allocator does. <c>typeof</c> takes neither <c>dynamic</c> (CS1962) nor a nullable reference type
    /// (CS8639), so those two kinds run <c>Rent</c> alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_factories_judge_each_kind_as_the_generic_allocator_does(string kindName)
    {
        var kind    = TypeArgumentKinds.All.Single(candidate => candidate.Name == kindName);
        var typeOf  = kind.Argument is not ("dynamic" or "string?");
        var source  =
            $$"""
              #nullable enable
              using System.Buffers;

              public static class Use{{kind.Parameters}} {{kind.Where}}
              {
                  public static void M(int n)
                  {
                      _ = GC.AllocateArray<{{kind.Argument}}>(n);
                      _ = ArrayPool<{{kind.Argument}}>.Shared.Rent(n);
                      {{(typeOf ? $"_ = Array.CreateInstance(typeof({kind.Argument}), n);" : "")}}
                      {{(typeOf ? $"_ = Array.CreateInstance(typeof({kind.Argument}), n, n);" : "")}}
                      {{(typeOf ? $"_ = Array.CreateInstanceFromArrayType(typeof({kind.Argument}[]), n);" : "")}}
                  }
              }
              """;

        var forms = await ReportedForms(source);

        var reported = forms.Contains("GC.AllocateArray");
        if (kind is KindOfType { Is: var type } && type.HasFlag(Is.Struct) && type.HasFlag(Is.ValueObjectMarker)) reported.ShouldBeTrue("a value object struct is the positive control");

        string[] factories = typeOf ? ["ArrayPool.Rent", "Array.CreateInstance", "Array.CreateInstance", "Array.CreateInstanceFromArrayType"] : ["ArrayPool.Rent"];
        forms.Where(form => form != "GC.AllocateArray").Order().ShouldBe(reported ? factories.Order() : []);
    }

    public static TheoryData<string> Kinds() => [.. TypeArgumentKinds.All.Where(kind => kind.Mentions.All(Declared)).Select(kind => kind.Name)];

    private static bool Declared(string attribute) => RealTypeKit.Assembly.GetType($"CodoMetis.TypeKit.{attribute}Attribute") is not null;

    /// <summary>The form of each CMTK0005 the analyzer reports in <paramref name="source"/>, which must compile.</summary>
    private static async Task<string[]> ReportedForms(string source)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var references   = await ReferenceAssemblies.Net.Net100.ResolveAsync(LanguageNames.CSharp, cancellation);
        var compilation  = CSharpCompilation.Create(
            "Kinds",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: cancellation), CSharpSyntaxTree.ParseText(TypeArgumentKinds.Candidates, cancellationToken: cancellation)],
            [.. references, RealTypeKit.Reference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        compilation.GetDiagnostics(cancellation).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();

        var diagnostics = await compilation.WithAnalyzers([new DefaultFilledCollectionAnalyzer()]).GetAnalyzerDiagnosticsAsync(cancellation);
        return [.. diagnostics.Select(diagnostic => diagnostic.GetMessage().Split(" fills ")[0])];
    }

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
