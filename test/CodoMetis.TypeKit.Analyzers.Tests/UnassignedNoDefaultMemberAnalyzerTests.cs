using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>CMTK0006: a no-default member of a class that nothing sets.</summary>
public sealed class UnassignedNoDefaultMemberAnalyzerTests
{
    private const string Subjects =
        """
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        public readonly struct Plain : IValue<int>
        {
            public static Plain Make() => Plain.Make();
        }

        public sealed class ValueClass : IValue<int> { }

        """;

    private static AnalyzerTest<UnassignedNoDefaultMemberAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<UnassignedNoDefaultMemberAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0006(int location, string member, string type, string constructor) =>
        new DiagnosticResult("CMTK0006", DiagnosticSeverity.Warning).WithLocation(location).WithArguments(member, type, constructor);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new UnassignedNoDefaultMemberAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0006");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Warning);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task An_auto_property_nothing_sets_reports() =>
        Test(
            "public sealed class Order { public Plain {|#0:Id|} { get; set; } }",
            new DiagnosticResult("CMTK0006", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("'Id' starts as a default 'Plain', which passed no factory: it has no initializer, is not 'required', and the implicit constructor does not assign it. Make it 'required', initialize it, or assign it in every constructor."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Every_unset_member_reports_naming_a_constructor_that_misses_it() =>
        Test(
            """
            public sealed class Fields
            {
                private readonly Plain {|#0:_id|};
                public Option<int> {|#1:Maybe|} { get; init; }
                public Result<int, string> {|#2:Outcome|} { get; private set; }
            }

            public sealed class OneConstructorMisses
            {
                public OneConstructorMisses(Plain id) { Id = id; }
                public OneConstructorMisses(int ignored) { }
                public Plain {|#3:Id|} { get; }
            }

            public sealed record NotPositional { public Plain {|#4:Id|} { get; init; } }

            public sealed class Primary(Plain id) { public Plain {|#5:Id|} { get; set; } }

            public abstract class Base { public Plain {|#6:Id|} { get; set; } }
            """,
            Cmtk0006(0, "_id", "Plain", "the implicit constructor"),
            Cmtk0006(1, "Maybe", "Option<int>", "the implicit constructor"),
            Cmtk0006(2, "Outcome", "Result<int, string>", "the implicit constructor"),
            Cmtk0006(3, "Id", "Plain", "the constructor 'OneConstructorMisses(int)'"),
            Cmtk0006(4, "Id", "Plain", "the implicit constructor"),
            Cmtk0006(5, "Id", "Plain", "the constructor 'Primary(Plain)'"),
            Cmtk0006(6, "Id", "Plain", "the implicit constructor"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Every way a member gets a value, the materializing constructor EF and the serializers use, and
    /// the types the rule is not about.
    /// </summary>
    [Fact]
    public Task Members_that_something_sets_and_other_types_stay_silent() =>
        Test(
            """
            public sealed class Required { public required Plain Id { get; set; } }

            public sealed class Initialized
            {
                public Plain Id { get; set; } = Plain.Make();
                private readonly Plain _field = Plain.Make();
            }

            public sealed class Assigned
            {
                private Plain _id;
                private Plain _out;
                public Assigned(Plain id, Plain name)
                {
                    (Id, Name) = (id, name);
                    _id = id;
                    Init(out _out);
                }
                public Plain Id { get; }
                public Plain Name { get; }
                private static void Init(out Plain value) => value = Plain.Make();
            }

            public sealed class Chained
            {
                public Chained() : this(Plain.Make()) { }
                public Chained(Plain id) => Id = id;
                public Plain Id { get; }
            }

            public sealed class Entity
            {
                private Entity() { }
                public Entity(Plain id) => Id = id;
                public Plain Id { get; private set; }
            }

            public sealed record Positional(Plain Id);

            public sealed class Other
            {
                public Plain? Nullable { get; set; }
                public ValueClass Reference { get; set; } = null!;
                public int Number { get; set; }
                public static Plain Shared { get; set; }
                public Plain Computed => Plain.Make();
            }

            public struct InAStruct { public Plain Id { get; set; } }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<UnassignedNoDefaultMemberAnalyzer>("public sealed class S { public int N { get; set; } }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
