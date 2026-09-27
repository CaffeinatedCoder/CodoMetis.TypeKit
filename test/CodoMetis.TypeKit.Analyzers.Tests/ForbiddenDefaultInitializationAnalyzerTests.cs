using CodoMetis.TypeKit.ValueObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0001. Its failure mode is the bad one: an analyzer that stops reporting turns every
/// diagnostic off while every build stays green. Each registered syntax form has a positive control
/// here, so that failure turns this project red instead.
/// </summary>
/// <remarks>
/// The id and severity are asserted as literals. Both are public contract: the id is what
/// suppressions name, and the severity is what makes a violation unbuildable. An expected diagnostic
/// derived from the analyzer's own descriptor would follow a drift instead of catching it.
/// </remarks>
public sealed class ForbiddenDefaultInitializationAnalyzerTests
{
    /// <summary>
    /// The subjects every test compiles against. <c>Guarded</c> also carries two factories that use
    /// the forbidden forms inside the type itself, so every test doubles as a negative control for
    /// that exemption. <c>Other.RequireCustomInitializationAttribute</c> has our attribute's name in a
    /// foreign namespace.
    /// </summary>
    private const string Subjects =
        """
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        namespace Other
        {
            [System.AttributeUsage(System.AttributeTargets.Struct)]
            public sealed class RequireCustomInitializationAttribute : System.Attribute { }
        }

        namespace Subjects
        {
            public enum Fault { Negative }

            [RequireCustomInitialization("Use Create instead.")]
            public struct Guarded
            {
                public int N;
                public Guarded(int n) { N = n; }
                public static Guarded Create() { return new Guarded(); }
                public static Guarded Zero() { return default(Guarded); }
            }

            [RequireCustomInitialization]
            public struct Bare { public int N; }

            public struct Free { public int N; }

            public readonly struct Plain : IValue<int> { }

            public readonly struct Validated : IValidatedValue<Validated, int, Fault>
            {
                public static Result<Validated, Fault> Create(int value) { return Result<Validated, Fault>.Error(Fault.Negative); }
            }

            public interface IIdentifier : IValue<System.Guid> { }

            public readonly struct Indirect : IIdentifier { }

            [Other.RequireCustomInitialization]
            public struct Alien { public int N; }

            [Other.RequireCustomInitialization]
            public readonly struct AlienValue : IValue<int> { }

            [Other.RequireCustomInitialization]
            [RequireCustomInitialization]
            public struct DoublyMarked { public int N; }

            public sealed class ValueClass : IValue<int> { }
        }

        """;

    private static Task ShouldFlag(string consumer, string message) =>
        new AnalyzerTest<ForbiddenDefaultInitializationAnalyzer>(Subjects + consumer)
        {
            ExpectedDiagnostics =
            {
                new DiagnosticResult("CMTK0001", DiagnosticSeverity.Error).WithLocation(0).WithMessage(message),
            },
        }.RunAsync(TestContext.Current.CancellationToken);

    private static Task ShouldStaySilentOn(string consumer) =>
        new AnalyzerTest<ForbiddenDefaultInitializationAnalyzer>(Subjects + consumer).RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new ForbiddenDefaultInitializationAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0001");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    /// <summary>
    /// The verifier's compilations reference the assembly the package ships, not a copy of the
    /// contracts. The positive controls on <c>Option</c> and <c>Result</c> below depend on it too:
    /// those types exist nowhere else.
    /// </summary>
    [Fact]
    public void The_verifier_binds_to_the_real_TypeKit_assembly()
    {
        RealTypeKit.Reference.Display.ShouldBe(typeof(IValue<>).Assembly.Location);
        typeof(RequireCustomInitializationAttribute).Assembly.ShouldBeSameAs(RealTypeKit.Assembly);
        typeof(IValidatedValue<,,>).Assembly.ShouldBeSameAs(RealTypeKit.Assembly);
    }

    [Fact]
    public Task A_default_expression_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Guarded A() { return {|#0:default(Subjects.Guarded)|}; }
            }
            """,
            "Invalid initialization of 'Guarded': Use Create instead.");

    [Fact]
    public Task A_default_literal_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Guarded A() { Subjects.Guarded x = {|#0:default|}; return x; }
            }
            """,
            "Invalid initialization of 'Guarded': Use Create instead.");

    [Fact]
    public Task An_explicit_parameterless_new_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Guarded A() { return {|#0:new Subjects.Guarded()|}; }
            }
            """,
            "Invalid initialization of 'Guarded': Use Create instead.");

    [Fact]
    public Task An_implicit_parameterless_new_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Guarded A() { Subjects.Guarded x = {|#0:new()|}; return x; }
            }
            """,
            "Invalid initialization of 'Guarded': Use Create instead.");

    [Fact]
    public Task An_object_initializer_is_still_a_parameterless_construction() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Guarded A() { return {|#0:new Subjects.Guarded { N = 1 }|}; }
            }
            """,
            "Invalid initialization of 'Guarded': Use Create instead.");

    [Fact]
    public Task An_attribute_without_a_message_gets_the_fallback_wording() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Bare A() { return {|#0:default(Subjects.Bare)|}; }
            }
            """,
            "The type 'Bare' forbids default initialization");

    [Fact]
    public Task A_value_marker_alone_restricts_and_names_From() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Plain A() { return {|#0:default(Subjects.Plain)|}; }
            }
            """,
            "The value object 'Plain' must be created with 'Plain.From', not as a default instance");

    [Fact]
    public Task A_validated_marker_alone_restricts_and_names_its_factories() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Validated A() { return {|#0:default(Subjects.Validated)|}; }
            }
            """,
            "The value object 'Validated' must be created with 'Validated.Create', 'TryFrom' or 'FromKnownGood', not as a default instance");

    /// <summary>Symbol comparison over all interfaces: a marker reached through a derived interface still counts.</summary>
    [Fact]
    public Task A_marker_implemented_through_another_interface_restricts() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.Indirect A() { return {|#0:default(Subjects.Indirect)|}; }
            }
            """,
            "The value object 'Indirect' must be created with 'Indirect.From', not as a default instance");

    [Fact]
    public Task A_default_of_a_struct_constrained_type_parameter_that_is_a_value_object_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static T A<T>() where T : struct, CodoMetis.TypeKit.ValueObjects.IValue<int> { return {|#0:default(T)|}; }
            }
            """,
            "The value object 'T' must be created with 'T.From', not as a default instance");

    [Fact]
    public Task New_T_of_a_struct_constrained_type_parameter_that_is_a_value_object_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static T A<T>() where T : struct, CodoMetis.TypeKit.ValueObjects.IValue<int> { return {|#0:new T()|}; }
            }
            """,
            "The value object 'T' must be created with 'T.From', not as a default instance");

    /// <summary>
    /// A marker beside <c>new()</c> leaves only a struct value object: a generated class has no
    /// public parameterless constructor. So <c>new T()</c> is a default instance without a
    /// <c>struct</c> constraint too, and it went unreported.
    /// </summary>
    [Fact]
    public Task New_T_of_a_new_constrained_type_parameter_that_is_a_value_object_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static T A<T>() where T : CodoMetis.TypeKit.ValueObjects.IValue<int>, new() { return {|#0:new T()|}; }
            }
            """,
            "The value object 'T' must be created with 'T.From', not as a default instance");

    [Fact]
    public Task A_default_of_a_new_constrained_type_parameter_that_is_a_value_object_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static T A<T>() where T : CodoMetis.TypeKit.ValueObjects.IValue<int>, new() { return {|#0:default(T)|}; }
            }
            """,
            "The value object 'T' must be created with 'T.From', not as a default instance");

    /// <summary>
    /// Without <c>struct</c> or <c>new()</c>, or with <c>class</c>, the type parameter may be a record
    /// class value object, whose default is null rather than an instance.
    /// </summary>
    [Fact]
    public Task A_default_of_a_type_parameter_that_may_be_a_class_is_null_not_an_instance() =>
        ShouldStaySilentOn(
            """
            public static class Consumer
            {
                public static T? A<T>() where T : CodoMetis.TypeKit.ValueObjects.IValue<int> { return default(T); }
                public static T? B<T>() where T : class, CodoMetis.TypeKit.ValueObjects.IValue<int>, new() { return default(T); }
            }
            """);

    /// <summary>The package's own types, through the attribute they carry in the real assembly.</summary>
    [Fact]
    public Task A_default_option_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static CodoMetis.TypeKit.Option<int> A() { return {|#0:default(CodoMetis.TypeKit.Option<int>)|}; }
            }
            """,
            "The type 'Option' forbids default initialization");

    [Fact]
    public Task A_default_result_reports() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static CodoMetis.TypeKit.Result<int, string> A() { CodoMetis.TypeKit.Result<int, string> r = {|#0:default|}; return r; }
            }
            """,
            "The type 'Result' forbids default initialization");

    [Fact]
    public Task Parameterised_construction_is_the_sanctioned_path() =>
        ShouldStaySilentOn(
            """
            public static class Consumer
            {
                public static Subjects.Guarded A() { var x = new Subjects.Guarded(1); Subjects.Guarded y = new(2); return y.N > x.N ? y : x; }
            }
            """);

    /// <summary>
    /// The subjects alone: <c>Guarded.Create</c> and <c>Guarded.Zero</c> use the forbidden forms
    /// inside the type itself, which is where a factory has to. A rule that fired there would make
    /// the pattern impossible to implement.
    /// </summary>
    [Fact]
    public Task Creation_inside_the_restricted_type_itself_is_exempt() => ShouldStaySilentOn("");

    [Fact]
    public Task A_nullable_default_is_null_not_an_instance() =>
        ShouldStaySilentOn(
            """
            public static class Consumer
            {
                public static Subjects.Plain? A() { Subjects.Plain? x = default; return x ?? default(Subjects.Plain?); }
            }
            """);

    [Fact]
    public Task An_unrestricted_struct_is_untouched() =>
        ShouldStaySilentOn(
            """
            public static class Consumer
            {
                public static Subjects.Free A() { var x = new Subjects.Free(); Subjects.Free y = default; return y.N > x.N ? y : x; }
            }
            """);

    [Fact]
    public Task A_class_is_outside_the_rule_even_as_a_value_object() =>
        ShouldStaySilentOn(
            """
            public static class Consumer
            {
                public static object A() { return new Subjects.ValueClass(); }
            }
            """);

    /// <summary>A same-named attribute from another namespace is not ours, so it does not restrict a plain struct …</summary>
    [Fact]
    public Task A_foreign_attribute_of_the_same_name_does_not_restrict() =>
        ShouldStaySilentOn(
            """
            public static class Consumer
            {
                public static Subjects.Alien A() { var x = new Subjects.Alien(); Subjects.Alien y = default; return y.N > x.N ? y : x; }
            }
            """);

    /// <summary>… and it does not exempt a value object from the marker check either.</summary>
    [Fact]
    public Task A_foreign_attribute_cannot_exempt_a_value_object() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.AlienValue A() { return {|#0:default(Subjects.AlienValue)|}; }
            }
            """,
            "The value object 'AlienValue' must be created with 'AlienValue.From', not as a default instance");

    [Fact]
    public Task The_real_attribute_still_counts_behind_a_foreign_one() =>
        ShouldFlag(
            """
            public static class Consumer
            {
                public static Subjects.DoublyMarked A() { return {|#0:default(Subjects.DoublyMarked)|}; }
            }
            """,
            "The type 'DoublyMarked' forbids default initialization");

    /// <summary>
    /// Robustness, not a guard: without CodoMetis.TypeKit the analyzer registers nothing, and it
    /// must not fail (an exception would surface as AD0001).
    /// </summary>
    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<ForbiddenDefaultInitializationAnalyzer>(
            """
            public struct S { public int N; }
            public static class Consumer { public static S A() { S x = default; return x.N > 0 ? x : new S(); } }
            """,
            referenceTypeKit: false).RunAsync(TestContext.Current.CancellationToken);
}
