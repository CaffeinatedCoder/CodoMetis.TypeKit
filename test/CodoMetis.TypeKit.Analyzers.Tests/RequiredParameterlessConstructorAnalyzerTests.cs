using System.Collections.Immutable;
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

            [RequireParameterlessConstructor(ConstructorAccessibility.ProtectedInternal)]
            public abstract class Widget { }

            [RequireParameterlessConstructor(ConstructorAccessibility.PrivateProtected)]
            public abstract class Gadget { }
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

    /// <summary>
    /// The state names the declared accessibility with its article, as English writes it: "an
    /// internal", "a protected internal". Every accessibility C# lets a class's constructor have.
    /// </summary>
    [Theory]
    [InlineData("internal", "declares an internal parameterless constructor")]
    [InlineData("protected", "declares a protected parameterless constructor")]
    [InlineData("protected internal", "declares a protected internal parameterless constructor")]
    [InlineData("private protected", "declares a private protected parameterless constructor")]
    [InlineData("private", "declares a private parameterless constructor")]
    public Task The_message_names_the_declared_accessibility_with_its_article(string declared, string state) =>
        Test(
            $$"""
            public class Orders : Subjects.IEndpoint
            {
                {{declared}} {|#0:Orders|}() { }
            }
            """,
            new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage($"'Orders' {state}, but IEndpoint requires one that is public"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_public_constructor_against_a_non_public_requirement_says_a_public() =>
        Test(
            """
            public class Identity : Subjects.Module
            {
                public {|#0:Identity|}() { }
            }
            """,
            new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("'Identity' declares a public parameterless constructor, but Module requires one that is internal"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The two accessibilities of two keywords, read from the attribute's enum by position (3 and 4):
    /// each is satisfied by its own constructor, in either keyword order, and not by the other's.
    /// </summary>
    [Fact]
    public Task Protected_internal_and_private_protected_are_each_required_as_written() =>
        Test(
            """
            public class WidgetOne : Subjects.Widget { protected internal WidgetOne() { } }

            public class WidgetTwo : Subjects.Widget { internal protected WidgetTwo() { } }

            public class GadgetOne : Subjects.Gadget { private protected GadgetOne() { } }

            public class GadgetTwo : Subjects.Gadget { protected private GadgetTwo() { } }

            public class WidgetWrong : Subjects.Widget { private protected {|#0:WidgetWrong|}() { } }

            public class GadgetWrong : Subjects.Gadget { protected internal {|#1:GadgetWrong|}() { } }

            public class {|#2:WidgetImplicit|} : Subjects.Widget { }

            public class {|#3:GadgetImplicit|} : Subjects.Gadget { }
            """,
            Cmtk0011("WidgetWrong", "declares a private protected parameterless constructor", "Widget", "protected internal"),
            new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error).WithLocation(1).WithArguments("GadgetWrong", "declares a protected internal parameterless constructor", "Gadget", "private protected"),
            new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error).WithLocation(2).WithArguments("WidgetImplicit", "has the implicit public parameterless constructor", "Widget", "protected internal"),
            new DiagnosticResult("CMTK0011", DiagnosticSeverity.Error).WithLocation(3).WithArguments("GadgetImplicit", "has the implicit public parameterless constructor", "Gadget", "private protected"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A struct always has a public parameterless constructor, the implicit one or one it declares, and
    /// can have no other (CS8958). It satisfies <c>Public</c> as it is, and no other accessibility can
    /// be asked of it, so no requirement is reported on it: an Error its author could answer only by a
    /// suppression would be no rule.
    /// </summary>
    [Theory]
    [InlineData("Public")]
    [InlineData("Internal")]
    [InlineData("Protected")]
    [InlineData("ProtectedInternal")]
    [InlineData("PrivateProtected")]
    [InlineData("Private")]
    public Task A_struct_or_record_struct_is_never_reported(string accessibility) =>
        Test(
            $$"""
            [RequireParameterlessConstructor(ConstructorAccessibility.{{accessibility}})]
            public interface IJob { }

            public struct Implicit : IJob { }

            public struct Declared : IJob { public Declared() { } }

            public struct Parameterised : IJob { public Parameterised(int port) { } }

            public record struct Record : IJob;

            public readonly record struct DeclaredRecord : IJob { public DeclaredRecord() { } }

            public record struct Positional(int Port) : IJob;
            """)
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

    /// <summary>The two accessibilities of two keywords, added as C# writes them.</summary>
    [Theory]
    [InlineData("Widget", "protected internal")]
    [InlineData("Gadget", "private protected")]
    public Task The_fix_adds_a_constructor_of_two_keywords(string marked, string modifiers) =>
        new CodeFixTest<RequiredParameterlessConstructorAnalyzer, RequiredParameterlessConstructorCodeFixProvider>(
            Subjects + $$"""
            public class {|#0:Orders|} : Subjects.{{marked}}
            {
                public Orders(int port) { }
            }
            """,
            Subjects + $$"""
            public class Orders : Subjects.{{marked}}
            {
                {{modifiers}} Orders()
                {
                }

                public Orders(int port) { }
            }
            """)
            {
                ExpectedDiagnostics = { Cmtk0011("Orders", "declares no parameterless constructor", marked, modifiers) },
                CodeActionVerifier  = (action, verifier) => verifier.Equal($"Add a {modifiers} parameterless constructor", action.Title),
            }
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Each keyword of the wrong accessibility goes, whichever accessibility of one or two keywords replaces it.</summary>
    [Theory]
    [InlineData("Widget", "public", "a public", "protected internal")]
    [InlineData("Widget", "private protected", "a private protected", "protected internal")]
    [InlineData("Gadget", "protected internal", "a protected internal", "private protected")]
    [InlineData("Gadget", "internal", "an internal", "private protected")]
    public Task The_fix_changes_a_constructor_to_one_of_two_keywords(string marked, string declared, string named, string required) =>
        new CodeFixTest<RequiredParameterlessConstructorAnalyzer, RequiredParameterlessConstructorCodeFixProvider>(
            Subjects + $$"""
            public class Orders : Subjects.{{marked}}
            {
                {{declared}} {|#0:Orders|}() { }
            }
            """,
            Subjects + $$"""
            public class Orders : Subjects.{{marked}}
            {
                {{required}} Orders() { }
            }
            """)
            {
                ExpectedDiagnostics = { Cmtk0011("Orders", $"declares {named} parameterless constructor", marked, required) },
                CodeActionVerifier  = (action, verifier) => verifier.Equal($"Make the constructor {required}", action.Title),
            }
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>The action's title agrees its article with the accessibility, as the message does.</summary>
    [Fact]
    public Task The_fix_s_title_says_an_internal() =>
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
                CodeActionVerifier  = (action, verifier) => verifier.Equal("Add an internal parameterless constructor", action.Title),
            }
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// The rule reports no struct, so the fix never meets one. Handed a diagnostic there all the same,
    /// as the rule gave one before 2.0.0 was tagged, it offers nothing: <c>internal S() { }</c> in a
    /// struct is CS8958. The diagnostics are made here, at each place the rule reported, with the
    /// rule's own descriptor; a class beside them gets its fix, so the property they carry is one the
    /// fix reads.
    /// </summary>
    [Fact]
    public async Task The_fix_offers_nothing_on_a_struct()
    {
        const string source =
            """
            public interface IJob { }

            public struct Implicit : IJob { }

            public struct Declared : IJob { public Declared() { } }

            public record struct Record : IJob;

            public readonly record struct DeclaredRecord : IJob { public DeclaredRecord() { } }

            public sealed class Control : IJob { public Control(int port) { } }
            """;

        var token = TestContext.Current.CancellationToken;
        var rule  = new RequiredParameterlessConstructorAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        using var workspace = new AdhocWorkspace();
        var document = workspace.AddProject("Structs", LanguageNames.CSharp).AddDocument("Structs.cs", source);
        var root     = await document.GetSyntaxRootAsync(token) ?? throw new InvalidOperationException("No syntax root.");

        // Where the rule reported: at a declared parameterless constructor, or at the type.
        var places = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                         .Where(type => type.BaseList is not null)
                         .Select(type => (type.Identifier.ValueText,
                                          Location: type.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax>()
                                                        .FirstOrDefault(constructor => constructor.ParameterList.Parameters.Count == 0)?.Identifier.GetLocation()
                                                 ?? type.Identifier.GetLocation()))
                         .ToList();

        places.Count.ShouldBe(5, "The source's types were not found, so this test checks nothing.");

        foreach (var (name, location) in places)
        {
            var diagnostic = Diagnostic.Create(
                rule,
                location,
                ImmutableDictionary<string, string?>.Empty.Add(RequiredParameterlessConstructorAnalyzer.RequiredProperty, nameof(Accessibility.Internal)),
                name,
                "declares no parameterless constructor",
                "IJob",
                "internal");

            var actions = new List<string>();
            await new RequiredParameterlessConstructorCodeFixProvider().RegisterCodeFixesAsync(
                new Microsoft.CodeAnalysis.CodeFixes.CodeFixContext(document, diagnostic, (action, _) => actions.Add(action.Title), token));

            if (name == "Control")
                actions.ShouldBe(["Add an internal parameterless constructor"], "The class beside the structs got no fix, so the diagnostics here carry nothing the fix reads.");
            else
                actions.ShouldBeEmpty($"The fix offers '{string.Join("', '", actions)}' on the struct {name}, which can have no parameterless constructor but a public one (CS8958).");
        }
    }

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
