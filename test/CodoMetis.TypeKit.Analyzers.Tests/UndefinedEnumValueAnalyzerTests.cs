using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0014: a <c>FaultKind</c> or <c>ConstructorAccessibility</c> that names no member. C# lets any
/// number be an enum; the package reads these two as one of their members, so a cast or a
/// <c>default</c> is reported where it is written rather than found when the host starts, or never.
/// </summary>
public sealed class UndefinedEnumValueAnalyzerTests
{
    private const string FaultKindConsequence = "a host refuses a fault set's member of no kind where it maps the set, when it starts";
    private const string AccessibilityConsequence = "CMTK0011 reads it as public";

    private static Task ShouldFlag(string source, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<UndefinedEnumValueAnalyzer>(source);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static DiagnosticResult Cmtk0014(int location, string written, string type, string consequence) =>
        new DiagnosticResult("CMTK0014", DiagnosticSeverity.Warning).WithLocation(location).WithArguments(written, type, consequence);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new UndefinedEnumValueAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0014");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Warning, "a new rule in a minor version is never an Error (decision 34)");
        rule.IsEnabledByDefault.ShouldBeTrue();
        rule.HelpLinkUri.ShouldEndWith("README.md#cmtk0014");
    }

    /// <summary>A fault set's member of no kind: a cast, <c>default</c> in both spellings, and a zero converted implicitly.</summary>
    [Fact]
    public Task A_fault_kind_that_names_no_member_reports() =>
        ShouldFlag(
            """
            using CodoMetis.TypeKit.ClosedSets;

            public static class Kinds
            {
                public static FaultKind Cast() => {|#0:(FaultKind)9|};
                public static FaultKind Default() => {|#1:default(FaultKind)|};
                public static FaultKind Literal() { FaultKind kind = {|#2:default|}; return kind; }
                public static FaultKind Zero() { FaultKind kind = {|#3:0|}; return kind; }
                public static FaultKind Folded() => {|#4:FaultKind.Malformed + 1|};
            }
            """,
            Cmtk0014(0, "(FaultKind)9", "FaultKind", FaultKindConsequence),
            Cmtk0014(1, "default(FaultKind)", "FaultKind", FaultKindConsequence),
            Cmtk0014(2, "default", "FaultKind", FaultKindConsequence),
            Cmtk0014(3, "0", "FaultKind", FaultKindConsequence),
            Cmtk0014(4, "FaultKind.Malformed + 1", "FaultKind", FaultKindConsequence));

    /// <summary>The case that read as <c>Public</c> without a word: the attribute's argument.</summary>
    [Fact]
    public Task An_accessibility_that_names_no_member_reports_in_the_attribute() =>
        ShouldFlag(
            """
            using CodoMetis.TypeKit;

            [RequireParameterlessConstructor({|#0:(ConstructorAccessibility)9|})]
            public interface IEndpoint { }
            """,
            Cmtk0014(0, "(ConstructorAccessibility)9", "ConstructorAccessibility", AccessibilityConsequence));

    /// <summary>Every member, a cast of a member's value, a member folded into another, and a <c>default</c> that is a member.</summary>
    [Fact]
    public Task A_value_that_names_a_member_is_silent() =>
        ShouldFlag(
            """
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.ClosedSets;

            [RequireParameterlessConstructor(default)]
            public interface IEndpoint { }

            [RequireParameterlessConstructor((ConstructorAccessibility)5)]
            public interface IActivated { }

            public static class Kinds
            {
                public static FaultKind[] All() => [FaultKind.NotFound, FaultKind.Conflict, FaultKind.Invalid, FaultKind.Forbidden, FaultKind.Unavailable, FaultKind.PreconditionFailed, FaultKind.Malformed];
                public static FaultKind Cast() => (FaultKind)3;
                public static FaultKind Folded() => FaultKind.NotFound + 1;
                public static FaultKind Read(int value) => (FaultKind)value;
            }
            """);

    /// <summary>A value only compared is a guard, written to find the very value the rule reports.</summary>
    [Fact]
    public Task A_value_only_compared_is_silent() =>
        ShouldFlag(
            """
            using CodoMetis.TypeKit.ClosedSets;

            public static class Guards
            {
                public static bool Unset(FaultKind kind) => kind == default;
                public static bool Other(FaultKind kind) => kind != (FaultKind)0 && kind > (FaultKind)9;
                public static bool Pattern(FaultKind kind) => kind is (FaultKind)0 or (FaultKind)9;
                public static string Name(FaultKind kind)
                {
                    switch (kind)
                    {
                        case (FaultKind)9: return "nine";
                        default: return kind switch { (FaultKind)0 => "none", _ => kind.ToString() };
                    }
                }
            }
            """);

    /// <summary>An enum of the same name that is not the package's is the application's own.</summary>
    [Fact]
    public Task An_enum_of_another_assembly_is_silent() =>
        ShouldFlag(
            """
            namespace Shop
            {
                public enum FaultKind { Gone = 1 }

                public static class Kinds
                {
                    public static FaultKind Cast() => (FaultKind)9;
                }
            }
            """);
}
