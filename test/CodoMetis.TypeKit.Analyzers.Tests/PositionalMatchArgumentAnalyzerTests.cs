using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0012: a closed set's <c>Match</c> given an argument by position, which reordering the members
/// swaps silently.
/// </summary>
/// <remarks>
/// The verifier never weaves, so the sets below declare by hand what the generators would: the
/// woven one as it binds from another project, and one without <c>Match</c> as the project declaring
/// the set sees it, where Metalama runs analyzers before weaving and the call does not bind.
/// BuildOutcomeTests holds the second form to a real woven build.
/// </remarks>
public sealed class PositionalMatchArgumentAnalyzerTests
{
    private const string Sets =
        """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using CodoMetis.TypeKit.ClosedSets;

        namespace Subjects
        {
            public sealed class Status : IClosedSet<Status>
            {
                public static readonly Status Open = new();
                public static readonly Status Shipped = new();
                public static readonly Status New = new();

                private Status() { }

                public string Name => "";
                public string QualifiedName => "";
                public static IReadOnlyList<Status> All => [Open, Shipped, New];
                public static bool TryParse([NotNullWhen(true)] string? name, [MaybeNullWhen(false)] out Status member) { member = null; return false; }

                public TResult Match<TResult>(Func<TResult> open, Func<TResult> shipped, Func<TResult> @new) => open();

                // An overload of another signature, the set's own.
                public string Match(Func<Status, string> describe) => describe(this);
            }

            // As the declaring project sees a set before weaving: no Match yet.
            public sealed class Unwoven : IClosedSet<Unwoven>
            {
                public static readonly Unwoven Low = new();
                public static readonly Unwoven High = new();

                private Unwoven() { }

                public string Name => "";
                public string QualifiedName => "";
                public static IReadOnlyList<Unwoven> All => [Low, High];
                public static bool TryParse([NotNullWhen(true)] string? name, [MaybeNullWhen(false)] out Unwoven member) { member = null; return false; }
            }

            // Not a closed set: a Match of the same shape is nobody's concern here.
            public sealed class Other
            {
                public TResult Match<TResult>(Func<TResult> a, Func<TResult> b) => a();
            }
        }

        """;

    private static AnalyzerTest<PositionalMatchArgumentAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<PositionalMatchArgumentAnalyzer>(Sets + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0012(string set, string names) => Cmtk0012(0, set, names);

    private static DiagnosticResult Cmtk0012(int location, string set, string names) =>
        new DiagnosticResult("CMTK0012", DiagnosticSeverity.Error).WithLocation(location).WithArguments(set, names);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new PositionalMatchArgumentAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0012");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    /// <summary>The fix lives in another assembly, so the id and the property name it reads are held to the analyzer's here.</summary>
    [Fact]
    public void The_code_fix_names_the_rule_and_the_property_the_analyzer_writes()
    {
        new PositionalMatchArgumentCodeFixProvider().FixableDiagnosticIds.ShouldBe(["CMTK0012"]);
        PositionalMatchArgumentCodeFixProvider.NamesProperty.ShouldBe(PositionalMatchArgumentAnalyzer.NamesProperty);
    }

    [Fact]
    public Task Every_argument_named_is_silent() =>
        Test(
            """
            static class Use
            {
                static int Pick(Subjects.Status s) => s.Match(shipped: () => 2, open: () => 1, @new: () => 3);
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task An_argument_by_position_is_reported_with_every_name_a_keyword_escaped() =>
        Test(
            """
            static class Use
            {
                static int Pick(Subjects.Status s) => s.Match({|#0:() => 1|}, () => 2, () => 3);
            }
            """,
            new DiagnosticResult("CMTK0012", DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("'Status.Match' is given an argument by position, which binds to whichever member is declared at that place: reorder the members and the branches swap without a word. Name every argument (open:, shipped:, @new:)."))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>C# 7.2 lets a named argument in its own position precede one by position; the one by position is still reported.</summary>
    [Fact]
    public Task One_argument_by_position_among_named_ones_is_reported_at_it() =>
        Test(
            """
            static class Use
            {
                static int Pick(Subjects.Status s) => s.Match(open: () => 1, {|#0:() => 2|}, @new: () => 3);
            }
            """,
            Cmtk0012("Status", "open:, shipped:, @new:"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_conditional_call_is_reported() =>
        Test(
            """
            static class Use
            {
                static int? Pick(Subjects.Status? s) => s?.Match({|#0:() => 1|}, () => 2, () => 3);
            }
            """,
            Cmtk0012("Status", "open:, shipped:, @new:"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A conditional call followed by another conditional access, <c>s?.Match(…)?.ToUpper()</c>: the
    /// nearest <c>?.</c> around the call is the one after it, whose receiver is the call's own result.
    /// The receiver is the one whose <c>?.</c> binds the call, bound or not.
    /// </summary>
    [Fact]
    public Task A_conditional_call_followed_by_a_conditional_access_is_reported() =>
        Test(
            """
            sealed class Holder
            {
                public Subjects.Status Status { get; set; } = Subjects.Status.Open;
            }

            static class Use
            {
                static string Upper(Subjects.Status? s) => s?.Match({|#0:() => "a"|}, () => "b", () => "c")?.ToUpper();

                static string Trimmed(Holder h) => h.Status?.Match({|#1:() => " a"|}, () => "b", () => "c")?.Trim();

                static string Awaited(Subjects.Status? s) => s?.Match({|#2:() => System.Threading.Tasks.Task.FromResult("a")|}, () => System.Threading.Tasks.Task.FromResult("b"), () => System.Threading.Tasks.Task.FromResult("c"))?.Result;

                static string Unbound(Subjects.Unwoven? u) => u?{|CS1061:.Match|}({|#3:() => 1|}, () => 2)?.ToString();

                static string Nested(Holder? h) => h?.Status?.Match({|#4:() => "a"|}, () => "b", () => "c")?.Trim()?.ToUpper();

                static int? Length(Subjects.Status? s) => s?.Match({|#5:() => "a"|}, () => "b", () => "c").Length;
            }
            """,
            Cmtk0012(0, "Status", "open:, shipped:, @new:"),
            Cmtk0012(1, "Status", "open:, shipped:, @new:"),
            Cmtk0012(2, "Status", "open:, shipped:, @new:"),
            Cmtk0012(3, "Unwoven", "low:, high:"),
            Cmtk0012(4, "Status", "open:, shipped:, @new:"),
            Cmtk0012(5, "Status", "open:, shipped:, @new:"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Before weaving, in the project that declares the set, the call does not bind; one argument per
    /// member on a closed set is the generated <c>Match</c>, and the names are the generators' rule.
    /// </summary>
    [Fact]
    public Task An_unbound_call_with_one_argument_per_member_is_reported_with_the_generators_names() =>
        Test(
            """
            static class Use
            {
                static int Pick(Subjects.Unwoven u) => u.{|CS1061:Match|}({|#0:() => 1|}, () => 2);
            }
            """,
            Cmtk0012("Unwoven", "low:, high:"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task An_unbound_call_with_another_count_is_not_taken_for_the_generated_Match() =>
        Test(
            """
            static class Use
            {
                static int Pick(Subjects.Unwoven u) => u.{|CS1061:Match|}(() => 1);
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task An_overload_the_set_declares_and_a_Match_of_another_type_are_silent() =>
        Test(
            """
            static class Use
            {
                static string Describe(Subjects.Status s) => s.Match(status => status.Name);

                static int Pick(Subjects.Other o) => o.Match(() => 1, () => 2);
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_fix_names_each_argument_by_position_after_the_parameter_it_binds_to_now() =>
        new CodeFixTest<PositionalMatchArgumentAnalyzer, PositionalMatchArgumentCodeFixProvider>(
            Sets + """
            static class Use
            {
                static int Pick(Subjects.Status s) => s.Match(
                    {|#0:() => 1|},
                    shipped: () => 2,
                    () => 3);
            }
            """,
            Sets + """
            static class Use
            {
                static int Pick(Subjects.Status s) => s.Match(
                    open: () => 1,
                    shipped: () => 2,
                    @new: () => 3);
            }
            """)
            {
                ExpectedDiagnostics = { Cmtk0012("Status", "open:, shipped:, @new:") },
            }
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task The_fix_names_the_arguments_of_a_conditional_call_followed_by_a_conditional_access() =>
        new CodeFixTest<PositionalMatchArgumentAnalyzer, PositionalMatchArgumentCodeFixProvider>(
            Sets + """
            sealed class Holder
            {
                public Subjects.Status Status { get; set; } = Subjects.Status.Open;
            }

            static class Use
            {
                static string Upper(Subjects.Status? s) => s?.Match({|#0:() => "a"|}, () => "b", () => "c")?.ToUpper();

                static string Trimmed(Holder h) => h.Status?.Match({|#1:() => " a"|}, () => "b", () => "c")?.Trim();

                static int? Unbound(Subjects.Unwoven? u) => u?{|CS1061:.Match|}({|#2:() => 1|}, () => 2)?.GetHashCode();
            }
            """,
            Sets + """
            sealed class Holder
            {
                public Subjects.Status Status { get; set; } = Subjects.Status.Open;
            }

            static class Use
            {
                static string Upper(Subjects.Status? s) => s?.Match(open: () => "a", shipped: () => "b", @new: () => "c")?.ToUpper();

                static string Trimmed(Holder h) => h.Status?.Match(open: () => " a", shipped: () => "b", @new: () => "c")?.Trim();

                static int? Unbound(Subjects.Unwoven? u) => u?{|CS1061:.Match|}(low: () => 1, high: () => 2)?.GetHashCode();
            }
            """)
            {
                ExpectedDiagnostics =
                {
                    Cmtk0012(0, "Status", "open:, shipped:, @new:"),
                    Cmtk0012(1, "Status", "open:, shipped:, @new:"),
                    Cmtk0012(2, "Unwoven", "low:, high:"),
                },
            }
            .RunAsync(TestContext.Current.CancellationToken);
}
