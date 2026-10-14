using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodoMetis.TypeKit.Testing.Tests;

/// <summary>
/// <c>ShouldBeError(expected)</c> on a <c>Result&lt;T, Fault&lt;TFault&gt;&gt;</c> takes a fault. A bare
/// reason converted to a fault without a detail, so <c>ShouldBeError(OrderFault.NotFound)</c> compiled
/// and failed on every fault that carried one, reporting two faults with the same reason.
/// </summary>
public sealed class FaultAssertionTests
{
    public enum OrderFault { NotFound, AlreadyShipped }

    private static Result<int, Fault<OrderFault>> Gone => Result.Error(Fault.Of(OrderFault.NotFound, "Order 7 is gone."));

    [Fact]
    public void The_expected_fault_passes() => Gone.ShouldBeError(Fault.Of(OrderFault.NotFound, "Order 7 is gone."));

    [Fact]
    public void The_reason_is_asserted_on_the_fault_handed_back() => Gone.ShouldBeError().Reason.ShouldBe(OrderFault.NotFound);

    [Fact]
    public void A_fault_with_another_detail_is_another_error() =>
        Should.Throw<TypeKitAssertionException>(() => Gone.ShouldBeError(Fault.Of(OrderFault.NotFound)))
              .Message.ShouldStartWith("Expected the error Fault { Reason = NotFound, Detail =  }, but");

    [Theory]
    [InlineData("Result<int, Fault<OrderFault>> result) => result.ShouldBeError(OrderFault.NotFound);")]
    [InlineData("Result<Fault<OrderFault>> result) => result.ShouldBeError(OrderFault.NotFound);")]
    public void A_bare_reason_is_not_the_expected_fault(string signatureEnd)
    {
        var problems = Compile($$"""
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.Testing;

            public enum OrderFault { NotFound }

            public static class Checks
            {
                public static void Assert({{signatureEnd}}
            }
            """);

        problems.ShouldNotBeEmpty();
        problems.ShouldAllBe(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void The_compilation_is_sound() =>
        Compile("""
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.Testing;

            public enum OrderFault { NotFound }

            public static class Checks
            {
                public static void Assert(Result<int, Fault<OrderFault>> result) => result.ShouldBeError(Fault.Of(OrderFault.NotFound));
            }
            """).ShouldBeEmpty();

    private static IReadOnlyList<Diagnostic> Compile(string source) =>
    [
        .. CSharpCompilation.Create(
                                "Consumer",
                                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14))],
                                References.Value,
                                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
                            .GetDiagnostics()
                            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning),
    ];

    private static readonly Lazy<MetadataReference[]> References = new(() =>
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                                                                       .Append(typeof(Option<>).Assembly.Location)
                                                                       .Append(typeof(ResultAssertions).Assembly.Location)
                                                                       .Distinct(StringComparer.Ordinal)
                                                                       .Select(path => MetadataReference.CreateFromFile(path)),
    ]);
}
