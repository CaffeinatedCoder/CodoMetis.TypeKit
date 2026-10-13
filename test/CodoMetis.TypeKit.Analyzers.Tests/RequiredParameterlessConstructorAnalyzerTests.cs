using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0011: a concrete type under <c>[RequireParameterlessConstructor]</c> without the constructor
/// it requires. The host would find out by reflection, at run time; this rule says so at the type.
/// </summary>
public sealed class RequiredParameterlessConstructorAnalyzerTests
{
    /// <summary>
    /// The marked types, present in every test: an interface and an abstract class carry the
    /// requirement and are never reported themselves, and a derived interface forwards it.
    /// </summary>
    private const string Subjects =
        """
        using CodoMetis.TypeKit;

        namespace Subjects
        {
            [RequireParameterlessConstructor]
            public interface IEndpoint { }

            public interface IQueryEndpoint : IEndpoint { }

            [RequireParameterlessConstructor(ConstructorAccessibility.Internal)]
            public abstract class Module { }

            [RequireParameterlessConstructor(ConstructorAccessibility.Private)]
            public interface IActivated { }
        }

        """;

    private static AnalyzerTest<RequiredParameterlessConstructorAnalyzer> Test(string declarations, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<RequiredParameterlessConstructorAnalyzer>(Subjects + declarations);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0011(string type, string state, string source, string required) =>
        new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error).WithLocation(0).WithArguments(type, state, source, required);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new RequiredParameterlessConstructorAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0011");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    /// <summary>The fix lives in another assembly, so the id and the property name it reads are held to the analyzer's here.</summary>
    [Fact]
    public void The_code_fix_names_the_rule_and_the_property_the_analyzer_writes()
    {
        new RequiredParameterlessConstructorCodeFixProvider().FixableDiagnosticIds.ShouldBe(["CMTK0011"]);
        RequiredParameterlessConstructorCodeFixProvider.RequiredProperty.ShouldBe(RequiredParameterlessConstructorAnalyzer.RequiredProperty);
    }

    [Fact]
    public Task A_class_without_any_constructor_has_the_public_one_and_is_silent() =>
        Test("public sealed class Orders : Subjects.IEndpoint { }").RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_class_with_only_a_parameterised_constructor_reports_on_its_name() =>
        Test(
            """
            public sealed class {|#0:Orders|} : Subjects.IEndpoint
            {
                public Orders(int port) { }
            }
            """,
            new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("'Orders' declares no parameterless constructor, but IEndpoint requires one that is public"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_wrong_accessibility_reports_on_the_constructor() =>
        Test(
            """
            public sealed class Orders : Subjects.IEndpoint
            {
                private {|#0:Orders|}() { }
            }
            """,
            Cmtk0011("Orders", "declares a private parameterless constructor", "IEndpoint", "public"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_implicit_public_constructor_does_not_satisfy_a_non_public_requirement() =>
        Test(
            "public sealed class {|#0:Identity|} : Subjects.Module { }",
            Cmtk0011("Identity", "has the implicit public parameterless constructor", "Module", "internal"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_required_accessibility_is_the_attribute_s() =>
        Test(
            """
            public sealed class Identity : Subjects.Module
            {
                internal Identity() { }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_requirement_reaches_through_a_derived_interface_and_a_base_class() =>
        Test(
            """
            public abstract class EndpointBase : Subjects.IQueryEndpoint { }

            public sealed class {|#0:Orders|} : EndpointBase
            {
                public Orders(int port) { }
            }
            """,
            Cmtk0011("Orders", "declares no parameterless constructor", "IEndpoint", "public"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_record_with_a_primary_constructor_has_no_parameterless_one() =>
        Test(
            "public sealed record {|#0:Orders|}(int Port) : Subjects.IEndpoint;",
            Cmtk0011("Orders", "declares no parameterless constructor", "IEndpoint", "public"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_struct_has_the_public_constructor_and_is_silent_for_public() =>
        Test("public struct Ping : Subjects.IEndpoint { }").RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Two_requirements_of_different_accessibility_each_report_once() =>
        Test(
            """
            public sealed class {|#0:Both|} : Subjects.Module, Subjects.IActivated
            {
                public Both(int x) { }
            }
            """,
            Cmtk0011("Both", "declares no parameterless constructor", "Module", "internal"),
            new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error).WithLocation(0).WithArguments("Both", "declares no parameterless constructor", "IActivated", "private"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>The subjects alone: the marked interface and abstract class are never reported.</summary>
    [Fact]
    public Task The_marked_types_themselves_are_silent() =>
        Test("").RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_type_under_no_marked_type_is_left_alone() =>
        Test(
            """
            public sealed class Free
            {
                private Free() { }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Robustness, not a guard: without CodoMetis.TypeKit there is nothing to look at, and nothing may fail.</summary>
    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<RequiredParameterlessConstructorAnalyzer>("public struct S { }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_fix_adds_the_constructor_a_class_lacks() =>
        new CodeFixTest<RequiredParameterlessConstructorAnalyzer, RequiredParameterlessConstructorCodeFixProvider>(
            Subjects + """
            public sealed class {|#0:Orders|} : Subjects.IEndpoint
            {
                public Orders(int port) { }
            }
            """,
            Subjects + """
            public sealed class Orders : Subjects.IEndpoint
            {
                public Orders()
                {
                }

                public Orders(int port) { }
            }
            """)
            {
                ExpectedDiagnostics = { Cmtk0011("Orders", "declares no parameterless constructor", "IEndpoint", "public") },
            }
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_fix_changes_a_wrong_accessibility_in_place() =>
        new CodeFixTest<RequiredParameterlessConstructorAnalyzer, RequiredParameterlessConstructorCodeFixProvider>(
            Subjects + """
            public sealed class Identity : Subjects.Module
            {
                public {|#0:Identity|}() { }
            }
            """,
            Subjects + """
            public sealed class Identity : Subjects.Module
            {
                internal Identity() { }
            }
            """)
            {
                ExpectedDiagnostics = { Cmtk0011("Identity", "declares a public parameterless constructor", "Module", "internal") },
            }
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_fix_writes_the_non_public_constructor_a_class_only_has_implicitly() =>
        new CodeFixTest<RequiredParameterlessConstructorAnalyzer, RequiredParameterlessConstructorCodeFixProvider>(
            Subjects + "public sealed class {|#0:Identity|} : Subjects.Module { }",
            Subjects + """
            public sealed class Identity : Subjects.Module
            {
                internal Identity()
                {
                }
            }
            """)
            {
                ExpectedDiagnostics = { Cmtk0011("Identity", "has the implicit public parameterless constructor", "Module", "internal") },
            }
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Every other constructor of a type with a primary constructor chains to it, with arguments only the author knows.</summary>
    [Fact]
    public Task A_type_with_a_primary_constructor_gets_no_fix() =>
        new CodeFixTest<RequiredParameterlessConstructorAnalyzer, RequiredParameterlessConstructorCodeFixProvider>(
            Subjects + "public sealed record {|#0:Orders|}(int Port) : Subjects.IEndpoint;",
            Subjects + "public sealed record {|#0:Orders|}(int Port) : Subjects.IEndpoint;")
            {
                ExpectedDiagnostics = { Cmtk0011("Orders", "declares no parameterless constructor", "IEndpoint", "public") },
                FixedState = { ExpectedDiagnostics = { Cmtk0011("Orders", "declares no parameterless constructor", "IEndpoint", "public") } },
            }
            .RunAsync(TestContext.Current.CancellationToken);
}
