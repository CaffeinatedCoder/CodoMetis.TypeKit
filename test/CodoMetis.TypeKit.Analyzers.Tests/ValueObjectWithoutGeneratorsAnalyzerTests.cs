using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0002: a value object in a project that cannot generate it. Without this rule such a type
/// compiles with no field, no <c>Value</c> and no factory, and nothing says so.
/// </summary>
public sealed class ValueObjectWithoutGeneratorsAnalyzerTests
{
    /// <summary>
    /// Declarations that must never report, present in every test as negative controls: an interface
    /// and an abstract class that implement a marker are never generated, and a plain struct is not a
    /// value object.
    /// </summary>
    private const string Subjects =
        """
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        namespace Subjects
        {
            public enum Fault { Blank }

            public interface IIdentifier : IValue<System.Guid> { }

            public abstract class AbstractValue : IValue<int> { }

            public struct Free { }
        }

        """;

    private const string GeneratorsAssembly = "CodoMetis.TypeKit.Generators";

    private static AnalyzerTest<ValueObjectWithoutGeneratorsAnalyzer> Test(string declarations, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<ValueObjectWithoutGeneratorsAnalyzer>(Subjects + declarations);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0002(string type, string marker) =>
        new DiagnosticResult("CMTK0002", DiagnosticSeverity.Error).WithLocation(0).WithArguments(type, marker);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new ValueObjectWithoutGeneratorsAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0002");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task A_value_without_the_generators_reports_on_its_name() =>
        Test(
            "public readonly partial record struct {|#0:OrderId|} : IValue<System.Guid>;",
            new DiagnosticResult("CMTK0002", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("'OrderId' implements IValue<Guid>, but this project does not reference CodoMetis.TypeKit.Generators, so nothing is generated for it. Reference the CodoMetis.TypeKit.Generators package."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_validated_value_without_the_generators_reports() =>
        Test(
            """
            public readonly partial record struct {|#0:Email|} : IValidatedValue<Email, string, Subjects.Fault>
            {
                public static Result<Email, Subjects.Fault> Create(string value) => Result<Email, Subjects.Fault>.Error(Subjects.Fault.Blank);
            }
            """,
            Cmtk0002("Email", "IValidatedValue<Email, string, Fault>"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_marker_reached_through_another_interface_reports() =>
        Test("public readonly partial record struct {|#0:CustomerId|} : Subjects.IIdentifier;", Cmtk0002("CustomerId", "IValue<Guid>"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_partial_type_reports_once() =>
        Test(
            """
            public readonly partial record struct {|#0:Split|} : IValue<int>;
            public readonly partial record struct Split;
            """,
            Cmtk0002("Split", "IValue<int>"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>The subjects alone: nothing that is not a concrete value object reports.</summary>
    [Fact]
    public Task Interfaces_abstract_types_and_plain_structs_stay_silent() =>
        Test("").RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_reference_to_the_generators_silences_it()
    {
        var test = Test("public readonly partial record struct OrderId : IValue<System.Guid>;");
        test.TestState.AdditionalReferences.Add(RealTypeKit.EmptyAssemblyNamed(GeneratorsAssembly));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The generators are recognised by their exact assembly identity, never by a prefix or a suffix.</summary>
    [Theory]
    [InlineData("CodoMetis.TypeKit.Generators.Extensions")]
    [InlineData("Contoso.CodoMetis.TypeKit.Generators")]
    public Task A_look_alike_assembly_does_not_count_as_the_generators(string lookAlike)
    {
        var test = Test("public readonly partial record struct {|#0:OrderId|} : IValue<System.Guid>;", Cmtk0002("OrderId", "IValue<Guid>"));
        test.TestState.AdditionalReferences.Add(RealTypeKit.EmptyAssemblyNamed(lookAlike));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Robustness, not a guard: without CodoMetis.TypeKit there is nothing to look at, and nothing may fail.</summary>
    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<ValueObjectWithoutGeneratorsAnalyzer>("public struct S { }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
