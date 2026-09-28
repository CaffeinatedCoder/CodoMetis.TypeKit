using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0004: the validation-free path called in application code. Every form that reaches it has a
/// positive control here, compiled against the real CodoMetis.TypeKit and EF Core satellite
/// assemblies, so a drifted metadata name silences them and they fail.
/// </summary>
public sealed class MaterializeOutsidePersistenceAnalyzerTests
{
    /// <summary>
    /// <c>Code</c> is a value object declared by hand, as the satellites' tests declare one, with the
    /// materializer implemented implicitly and called by its own factory, which must stay silent.
    /// <c>Foreign.IValueObjectMaterializer</c> has our interface's name in another namespace.
    /// </summary>
    private const string Subjects =
        """
        using System;
        using System.Linq.Expressions;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.EntityFrameworkCore;
        using CodoMetis.TypeKit.ValueObjects;

        namespace Foreign
        {
            public interface IValueObjectMaterializer<TSelf, T> where TSelf : IValueObjectMaterializer<TSelf, T>
            {
                static abstract TSelf Materialize(T value);
            }

            public sealed class Lookalike : IValueObjectMaterializer<Lookalike, int>
            {
                public static Lookalike Materialize(int value) => new();
            }
        }

        namespace Subjects
        {
            public sealed class Code : IValueObject<Code, string>, IValueObjectMaterializer<Code, string>
            {
                private Code(string value) => Value = value;

                public string Value { get; }

                public static Code Materialize(string value) => new(value);

                public static Result<Code, string> Create(string value) =>
                    value.Length == 3 ? Result<Code, string>.Success(Materialize(value)) : Result<Code, string>.Error("length");

                public static bool operator ==(Code? left, Code? right) => Equals(left, right);

                public static bool operator !=(Code? left, Code? right) => !Equals(left, right);

                public override bool Equals(object? obj) => obj is Code other && other.Value == Value;

                public override int GetHashCode() => Value.GetHashCode();
            }

            public static class Store
            {
                public static Code Materialize(string value) => Code.Create(value).Match(x => x, _ => throw new InvalidOperationException());
            }
        }

        """;

    private static AnalyzerTest<MaterializeOutsidePersistenceAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<MaterializeOutsidePersistenceAnalyzer>(Subjects + code);
        test.TestState.AdditionalReferences.AddRange(RealEntityFrameworkCore.References);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0004(string valueObject) =>
        new DiagnosticResult("CMTK0004", DiagnosticSeverity.Error).WithLocation(0).WithArguments(valueObject);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new MaterializeOutsidePersistenceAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0004");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    /// <summary>The generated value objects implement it explicitly, so a type parameter is the way to reach theirs.</summary>
    [Fact]
    public Task A_call_through_a_constrained_type_parameter_reports() =>
        Test(
            """
            public static class Loader
            {
                public static T Load<T>(string value) where T : IValueObjectMaterializer<T, string> => {|#0:T.Materialize(value)|};
            }
            """,
            new DiagnosticResult("CMTK0004", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("Materialize rebuilds 'T' without applying its rules. It is for values the application stored itself, and only the EF Core satellite calls it. Create the value through its factories instead."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_call_to_the_converter_reports() =>
        Test(
            """
            public static class Loader
            {
                public static Subjects.Code Load(string value) => {|#0:ValueObjectConverter<Subjects.Code, string>.Materialize(value)|};
            }
            """,
            Cmtk0004("Code"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>A hand-written conversion is the same bypass as a direct call, though it runs inside EF.</summary>
    [Fact]
    public Task A_call_inside_an_expression_tree_reports() =>
        Test(
            """
            public static class Mapping
            {
                public static readonly Expression<Func<string, Subjects.Code>> FromColumn =
                    value => {|#0:ValueObjectConverter<Subjects.Code, string>.Materialize(value)|};
            }
            """,
            Cmtk0004("Code"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_method_group_reports() =>
        Test(
            """
            public static class Loader
            {
                public static readonly Func<string, Subjects.Code> ViaConverter = {|#0:ValueObjectConverter<Subjects.Code, string>.Materialize|};

                public static Func<string, T> ViaContract<T>() where T : IValueObjectMaterializer<T, string> => {|#1:T.Materialize|};
            }
            """,
            Cmtk0004("Code"),
            new DiagnosticResult("CMTK0004", DiagnosticSeverity.Error).WithLocation(1).WithArguments("T"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_hand_written_value_objects_own_implementation_reports_outside_the_type() =>
        Test(
            """
            public static class Loader
            {
                public static Subjects.Code Load(string value) => {|#0:Subjects.Code.Materialize(value)|};
            }
            """,
            Cmtk0004("Code"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// EF's compiled model calls the converter from code it generates in the application, headed
    /// <c>// &lt;auto-generated /&gt;</c> like every generated file.
    /// </summary>
    [Fact]
    public Task Generated_code_is_left_alone()
    {
        var test = Test("");
        test.TestState.Sources.Add((
            "StoreDbModel.cs",
            """
            // <auto-generated />
            using System;
            using System.Linq.Expressions;
            using CodoMetis.TypeKit.EntityFrameworkCore;

            public static class CompiledModel
            {
                public static readonly Expression<Func<string, Subjects.Code>> FromProvider =
                    value => ValueObjectConverter<Subjects.Code, string>.Materialize(value);
            }
            """));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The subjects alone: the hand-written type's own factory, a look-alike interface, a method
    /// that is merely named <c>Materialize</c>, and <c>nameof</c>, which calls nothing.
    /// </summary>
    [Fact]
    public Task Look_alikes_the_type_itself_and_nameof_stay_silent() =>
        Test(
            """
            public static class Callers
            {
                public static Foreign.Lookalike Foreign() => global::Foreign.Lookalike.Materialize(1);

                public static T ForeignContract<T>() where T : global::Foreign.IValueObjectMaterializer<T, int> => T.Materialize(1);

                public static Subjects.Code Named() => Subjects.Store.Materialize("ABC");

                public static string Name() => nameof(IValueObjectMaterializer<Subjects.Code, string>.Materialize);
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Robustness, not a guard: without CodoMetis.TypeKit there is nothing to look at, and nothing may fail.</summary>
    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<MaterializeOutsidePersistenceAnalyzer>(
                "public sealed class S { public static S Materialize(int value) => new(); public static S Use() => Materialize(1); }",
                referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
