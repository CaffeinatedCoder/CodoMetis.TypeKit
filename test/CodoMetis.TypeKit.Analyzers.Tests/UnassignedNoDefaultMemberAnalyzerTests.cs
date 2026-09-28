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

    private const string RequiredAdvice = "Make it 'required', initialize it, or assign it in every constructor";

    private const string AssignAdvice = "Initialize it, or assign it in every constructor";

    /// <summary>A member that is not <c>required</c>, with the advice that fits it.</summary>
    private static DiagnosticResult Cmtk0006(int location, string member, string type, string constructor, string advice = RequiredAdvice) =>
        new DiagnosticResult("CMTK0006", DiagnosticSeverity.Warning)
            .WithLocation(location)
            .WithArguments(member, type, $"it has no initializer, is not 'required', and {constructor} does not assign it", advice);

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
            Cmtk0006(0, "_id", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(1, "Maybe", "Option<int>", "the implicit constructor"),
            Cmtk0006(2, "Outcome", "Result<int, string>", "the implicit constructor", AssignAdvice),
            Cmtk0006(3, "Id", "Plain", "the constructor 'OneConstructorMisses(int)'", AssignAdvice),
            Cmtk0006(4, "Id", "Plain", "the implicit constructor"),
            Cmtk0006(5, "Id", "Plain", "the constructor 'Primary(Plain)'"),
            Cmtk0006(6, "Id", "Plain", "the implicit constructor"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// <c>required</c> does not compile on a get-only property or a <c>readonly</c> field (CS9034), nor
    /// on a member or setter less visible than the class (CS9032), so the message names it only where
    /// it does.
    /// </summary>
    [Fact]
    public Task The_advice_names_required_only_where_it_compiles() =>
        Test(
            """
            public class Visibility
            {
                private Plain {|#0:_private|};
                public Plain {|#1:PrivateSetter|} { get; private set; }
                protected internal Plain {|#2:Wider|} { get; set; }
                internal Plain {|#3:Narrower|} { get; set; }
                public Plain {|#4:_public|};
                public readonly Plain {|#10:_readonly|};
            }

            internal sealed class Internal
            {
                internal Plain {|#5:Id|} { get; set; }
                public Plain {|#6:Explicit|} { get; init; }
            }

            public interface IHasId { Plain Id { get; set; } }

            public sealed class ExplicitImplementation : IHasId { Plain IHasId.{|#7:Id|} { get; set; } }

            public sealed class Outer { private sealed class Nested { internal Plain {|#8:Id|} { get; set; } private Plain {|#9:_hidden|}; } }

            internal sealed class InternalOuter { public sealed class Nested { internal Plain {|#11:Id|} { get; set; } } }

            public class ProtectedOuter { protected sealed class Nested { protected internal Plain {|#12:Wider|} { get; set; } public Plain {|#13:Id|} { get; set; } } }
            """,
            Cmtk0006(0, "_private", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(1, "PrivateSetter", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(2, "Wider", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(3, "Narrower", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(4, "_public", "Plain", "the implicit constructor"),
            Cmtk0006(5, "Id", "Plain", "the implicit constructor"),
            Cmtk0006(6, "Explicit", "Plain", "the implicit constructor"),
            Cmtk0006(7, "IHasId.Id", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(8, "Id", "Plain", "the implicit constructor"),
            Cmtk0006(9, "_hidden", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(10, "_readonly", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(11, "Id", "Plain", "the implicit constructor"),
            Cmtk0006(12, "Wider", "Plain", "the implicit constructor", AssignAdvice),
            Cmtk0006(13, "Id", "Plain", "the implicit constructor"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A <c>required</c> member is set by every object initializer, except behind a constructor marked
    /// <c>[SetsRequiredMembers]</c>, which promises to set it instead.
    /// </summary>
    [Fact]
    public Task A_SetsRequiredMembers_constructor_that_does_not_assign_a_required_member_reports() =>
        Test(
            """
            public sealed class Promises
            {
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public Promises() { }
                public required Plain {|#0:Id|} { get; init; }
            }

            public sealed class Keeps
            {
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public Keeps(Plain id) => Id = id;
                [System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public Keeps() : this(Plain.Make()) { }
                public Keeps(int ignored) { }
                public required Plain Id { get; init; }
            }
            """,
            new DiagnosticResult("CMTK0006", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("'Id' starts as a default 'Plain', which passed no factory: it is 'required', but the constructor 'Promises()' carries [SetsRequiredMembers] and does not assign it. Assign it in that constructor, or remove [SetsRequiredMembers] from it."))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A generated factory of the same project does not bind where Metalama runs analyzers (CS0117
    /// here), and neither does a <c>this(…)</c> given it. The one constructor that takes that many
    /// arguments is the one chained to; with none or several to choose from, the chaining constructor
    /// counts as assigning everything.
    /// </summary>
    [Fact]
    public Task An_unbound_this_initializer_chains_to_the_one_constructor_it_can_mean() =>
        Test(
            """
            public sealed class Chained
            {
                public Chained(Plain id) { Id = id; }
                public Chained() : this(Plain.{|CS0117:From|}(1)) { }
                public Plain Id { get; }
            }

            public sealed class Ambiguous
            {
                public Ambiguous(Plain id) { Id = id; }
                public Ambiguous(string text) : this(Plain.{|CS0117:From|}(1)) { }
                public Ambiguous() : this(Plain.{|CS0117:From|}(1)) { }
                public Plain Id { get; }
            }

            public sealed class ChainsToOneThatMisses
            {
                public ChainsToOneThatMisses() : this(Plain.{|CS0117:From|}(1)) { }
                public ChainsToOneThatMisses(Plain id) { }
                public Plain {|#0:Id|} { get; }
            }
            """,
            Cmtk0006(0, "Id", "Plain", "the constructor 'ChainsToOneThatMisses()'", AssignAdvice))
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
